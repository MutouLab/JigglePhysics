using System;
using System.Runtime.CompilerServices;
using Unity.Mathematics;
using UnityEngine;

namespace GatorDragonGames.JigglePhysics
{

[Serializable]
public struct JigglePointParameters
{
    public float rootElasticity;
    public float angleElasticity;
    public bool angleLimited;
    public float angleLimit;
    public float angleLimitSoften;
    public float lengthElasticity;
    public float elasticitySoften;
    public float gravityMultiplier;
    public float blend;
    public float airDrag;
    public float drag;
    public float ignoreRootMotion;
    public float collisionRadius;
    // Bone-local-space offset for the collision proxy position (see JiggleSimulatedPoint.collisionOffset for
    // the resolved world-space value used during depenetration).
    public float3 collisionOffset;
    // 0..1 strength of classic squash & stretch driven by this bone's current length vs. its rest length (see
    // JiggleJobSimulate.ApplyPose); 0 never touches the bone's scale at all.
    public float squash;
    // How far the cross-section bulges for a given amount of length compression, as an exponent: 1 is exact
    // volume preservation, 0 compresses without widening, above 1 exaggerates. See JiggleJobSimulate.ApplyPose.
    public float squashBulge;
    // 0..1 strength of contact-driven elasticity softening, and the normalizedPush (see
    // JiggleJobSimulate.GetNormalizedPush) above which it starts ramping in. See JiggleJobSimulate.GetContactSoftening.
    public float contactSoftness;
    public float contactSoftnessThreshold;
}

[Serializable]
public struct JiggleTreeCurvedFloat
{
    // Base multiplier (0..1 unless otherwise noted by the caller)
    public float value;

    public bool curveEnabled;
    public AnimationCurve curve;

    private static readonly AnimationCurve kUnitCurve = AnimationCurve.Constant(0f, 1f, 1f);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public float Evaluate(float t01) {
        // t01 is assumed clamped by caller
        return curveEnabled ? value * curve.Evaluate(t01) : value;
    }

    public JiggleTreeCurvedFloat(float value) {
        this.value = value;
        curveEnabled = false;
        curve = kUnitCurve;
    }

    // Validation helpers let the runtime path stay branch-light.
    public void Ensure01() {
        value = Mathf.Clamp01(value);
    }

    public void EnsureNonNegative() {
        if (value < 0f) value = 0f;
    }
}

[Serializable]
public struct JiggleTreeInputParameters {
    public bool advancedToggle;
    public bool collisionToggle;
    public bool angleLimitToggle;

    public JiggleTreeCurvedFloat stiffness;       // 0..1
    public float soften;                          // 0..1
    public JiggleTreeCurvedFloat angleLimit;      // 0..1
    public float angleLimitSoften;                // 0..1
    public float rootStretch;                     // 0..1
    public float ignoreRootMotion;                // 0..1
    public JiggleTreeCurvedFloat stretch;         // 0..1
    public JiggleTreeCurvedFloat drag;            // 0..1
    public JiggleTreeCurvedFloat airDrag;         // 0..1
    public JiggleTreeCurvedFloat gravity;         // arbitrary
    public JiggleTreeCurvedFloat collisionRadius; // >= 0
    public float3 collisionOffsetStart;           // bone-local space, root end
    public float3 collisionOffsetEnd;             // bone-local space, tip end
    public JiggleTreeCurvedFloat squash;          // 0..1, default 0 (no squash, see JigglePointParameters.squash)
    public float squashBulge;                     // >= 0, default 1 (exact volume preservation)
    public JiggleTreeCurvedFloat contactSoftness; // 0..1, default 0 (no softening, see JigglePointParameters.contactSoftness)
    public float contactSoftnessThreshold;        // 0..1, normalizedPush below which contactSoftness has no effect
    public float blend;                           // 0..1

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public JigglePointParameters ToJigglePointParameters(float normalizedDistanceFromRoot) {
        // Clamp once up front to keep curves happy and avoid repeated clamps.
        float t = Mathf.Clamp01(normalizedDistanceFromRoot);

        bool adv = advancedToggle;

        float stiff = stiffness.Evaluate(t);
        float dragVal = drag.Evaluate(t);
        float airVal = airDrag.Evaluate(t);
        float gravVal = gravity.Evaluate(t);

        float stretchVal = adv ? stretch.Evaluate(t) : 0f;
        float angleLimitVal = angleLimitToggle ? angleLimit.Evaluate(t) : 0f;
        float collisionVal = (collisionToggle && adv) ? collisionRadius.Evaluate(t) : 0f;
        float3 collisionOffsetVal = (collisionToggle && adv) ? math.lerp(collisionOffsetStart, collisionOffsetEnd, t) : float3.zero;
        // Gated on advanced alone, matching where it sits in the inspector (next to stretch, its counterpart).
        // It needs contact to do anything, but there is no need to also gate on collisionToggle: with collision
        // off nothing ever pushes the point, so the squash target stays at identity by itself.
        float squashVal = adv ? squash.Evaluate(t) : 0f;
        // Gated the same way as squash (advanced only): contactSoftness/Threshold only do anything through the
        // softening formula in JiggleJobSimulate.GetContactSoftening, which itself only matters where collisions
        // push the point in the first place.
        float contactSoftnessVal = adv ? contactSoftness.Evaluate(t) : 0f;

        float stiffSq = stiff * stiff;
        float oneMinusStr = 1f - stretchVal;
        float lengthElast = adv ? (oneMinusStr * oneMinusStr) : 1f;
        float softenSq = adv ? (soften * soften) : 0f;

        return new JigglePointParameters
        {
            rootElasticity = adv ? (1f - rootStretch) : 1f,
            angleElasticity = stiffSq,
            lengthElasticity = lengthElast,
            elasticitySoften = softenSq,
            ignoreRootMotion = adv ? ignoreRootMotion : 0f,
            gravityMultiplier = gravVal,
            angleLimited = angleLimitToggle,
            angleLimit = angleLimitVal,
            angleLimitSoften = angleLimitSoften,
            blend = 1f,
            drag = dragVal,
            airDrag = airVal,
            collisionRadius = collisionVal,
            collisionOffset = collisionOffsetVal,
            squash = squashVal,
            // Passed through rather than gated: it only shapes squash's result, which squash == 0 already makes
            // inert, the same way contactSoftnessThreshold rides along with contactSoftness.
            squashBulge = squashBulge,
            contactSoftness = contactSoftnessVal,
            // Not gated on adv/curve-evaluated: it's just a pivot point for contactSoftness, and contactSoftness
            // being 0 already makes it inert, same as how angleLimitSoften passes through unconditionally.
            contactSoftnessThreshold = contactSoftnessThreshold
        };
    }

    public static JiggleTreeInputParameters Default() {
        return new JiggleTreeInputParameters {
            stiffness = new JiggleTreeCurvedFloat(0.8f),
            angleLimit = new JiggleTreeCurvedFloat(0.5f),
            stretch = new JiggleTreeCurvedFloat(0.1f),
            rootStretch = 0f,
            drag = new JiggleTreeCurvedFloat(0.1f),
            airDrag = new JiggleTreeCurvedFloat(0f),
            ignoreRootMotion = 0f,
            gravity = new JiggleTreeCurvedFloat(1f),
            collisionRadius = new JiggleTreeCurvedFloat(0.1f),
            squash = new JiggleTreeCurvedFloat(0f),
            squashBulge = 1f,
            contactSoftness = new JiggleTreeCurvedFloat(0f),
            contactSoftnessThreshold = 0.2f,
            soften = 0f,
            angleLimitSoften = 0f,
            blend = 1f
        };
    }

    // Editor-time clamping so the runtime path can assume valid ranges.
    public void OnValidate() {
        collisionRadius.EnsureNonNegative();

        stiffness.Ensure01();
        angleLimit.Ensure01();
        drag.Ensure01();
        airDrag.Ensure01();
        stretch.Ensure01();
        squash.Ensure01();
        // No upper bound: past 1 the cross-section gains more than the length lost, which is a legitimate look
        // when the "volume" is a stand-in rather than anything measured off the mesh.
        squashBulge = Mathf.Max(0f, squashBulge);
        contactSoftness.Ensure01();

        rootStretch = Mathf.Clamp01(rootStretch);
        ignoreRootMotion = Mathf.Clamp01(ignoreRootMotion);
        soften = Mathf.Clamp01(soften);
        angleLimitSoften = Mathf.Clamp01(angleLimitSoften);
        contactSoftnessThreshold = Mathf.Clamp01(contactSoftnessThreshold);
        blend = Mathf.Clamp01(blend);
    }
}

}
