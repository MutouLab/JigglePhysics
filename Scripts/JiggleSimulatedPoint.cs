using System;
using Unity.Mathematics;

namespace GatorDragonGames.JigglePhysics {

public unsafe struct JiggleSimulatedPoint {
    public const int MAX_CHILDREN = 32;

    // Generated at runtime
    public float3 lastPosition;
    public float3 position;
    public float3 workingPosition;
    public float3 parentPose;
    public float3 pose;
    public float desiredLengthToParent;
    public bool animated;
    public float worldRadius;
    // World-space offset (rotation + averagePointScale already applied) added to workingPosition to form the
    // collision proxy position used in DoDepenetration. Computed once per Cache(), see JiggleJobSimulate.Cache().
    public float3 collisionOffset;
    // World-space push applied to this point by collision depenetration this frame (see DepenetrateCollider);
    // reset to zero every Cache(). Feeds JiggleJobSimulate.GetNormalizedPush (contact-driven elasticity
    // softening); no longer used for squash itself, which is now driven by bone length instead of push direction.
    public float3 contactPush;
    // Net world-space depenetration applied to this point during the current step (see DepenetrateCollider).
    // Unlike contactPush, which keeps only the single largest push as a "how hard is this pressed" signal,
    // this is the signed sum, because it stands in for the displacement that FinishStep has to keep out of the
    // implied Verlet velocity. Consumed and cleared by FinishStep.
    public float3 stepDepenetration;
    // Whether ApplyPose last wrote a squash-driven scale to this bone. Squash itself needs no persisted state
    // (it's recomputed fresh from the current bone length every frame), but this lets ApplyPose notice when
    // squash drops back to 0 and write the rest scale back exactly once, instead of leaving the bone stuck at
    // its last squashed scale forever. Stays false (and the bone's scale is never touched) for any rig that
    // never enables squash.
    public bool hasWrittenScale;
    //public float3 debug;

    // Set at initialization
    public float distanceFromRoot;
    public int parentIndex;
    public fixed int childrenIndices[MAX_CHILDREN];
    public int childrenCount;
    public bool hasTransform;

    private static bool GetIsValid(float3 vector) {
        return !float.IsNaN(vector.x) && !float.IsNaN(vector.y) && !float.IsNaN(vector.z);
    }
    private static bool GetIsValid(float value) {
        return !float.IsNaN(value);
    }

    public bool GetIsValid(int pointCount, out string failReason) {
        if (!GetIsValid(lastPosition)) {
            failReason = "lastPosition is NaN";
            return false;
        }
        if (!GetIsValid(position)) {
            failReason = "position is NaN";
            return false;
        }
        if (!GetIsValid(workingPosition)) {
            failReason = "workingPosition is NaN";
            return false;
        }
        if (!GetIsValid(pose)) {
            failReason = "pose is NaN";
            return false;
        }
        if (!GetIsValid(parentPose)) {
            failReason = "parentPose is NaN";
            return false;
        }
        if (!GetIsValid(desiredLengthToParent)) {
            failReason = "desiredLengthToParent is NaN";
            return false;
        }
        if (!GetIsValid(worldRadius)) {
            failReason = "worldRadius is NaN";
            return false;
        }
        if (!GetIsValid(collisionOffset)) {
            failReason = "collisionOffset is NaN";
            return false;
        }
        if (!GetIsValid(contactPush)) {
            failReason = "contactPush is NaN";
            return false;
        }
        if (!GetIsValid(stepDepenetration)) {
            failReason = "stepDepenetration is NaN";
            return false;
        }
        if (!GetIsValid(distanceFromRoot)) {
            failReason = "distanceFromRoot is NaN";
            return false;
        }
        if (childrenCount < 0 || childrenCount > MAX_CHILDREN) {
            failReason = "childrenCount is outside range";
            return false;
        }
        if (parentIndex < -1 || parentIndex >= pointCount) {
            failReason = "parentIndex is outside range";
            return false;
        }
        for (int i = 0; i < childrenCount; i++) {
            int childIndex = childrenIndices[i];
            if (childIndex < 0 || childIndex >= pointCount) {
                failReason = "childrenIndices is outside range";
                return false;
            }
        }
        failReason = "All good!";
        return true;
    }

    public override string ToString() {
        return $"(position: {position},\nlastPosition: {lastPosition},\n" +
               $"workingPosition: {workingPosition},\n" +
               $"parentPose: {parentPose},\npose: {pose},\ndesiredLengthToParent:{desiredLengthToParent},\n" +
               $"animated: {animated},\n parentIndex: {parentIndex},\n " +
               $"children: [{childrenIndices[0]}, ...],\n childrenCount: {childrenCount},\n hasTransform: {hasTransform})";
    }

    public void Sanitize() {
        if (!GetIsValid(lastPosition)) {
            lastPosition = float3.zero;
        }
        if (!GetIsValid(position)) {
            position = float3.zero;
        }
        if (!GetIsValid(workingPosition)) {
            workingPosition = float3.zero;
        }
        if (!GetIsValid(pose)) {
            pose = float3.zero;
        }
        if (!GetIsValid(parentPose)) {
            parentPose = float3.zero;
        }
        if (!GetIsValid(desiredLengthToParent)) {
            desiredLengthToParent = 0.1f;
        }
        if (!GetIsValid(worldRadius)) {
            worldRadius = 0.1f;
        }
        if (!GetIsValid(collisionOffset)) {
            collisionOffset = float3.zero;
        }
        if (!GetIsValid(contactPush)) {
            contactPush = float3.zero;
        }
        if (!GetIsValid(stepDepenetration)) {
            stepDepenetration = float3.zero;
        }
        if (!GetIsValid(distanceFromRoot)) {
            distanceFromRoot = 0.1f;
        }
    }
}

}
