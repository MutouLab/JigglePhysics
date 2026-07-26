using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace GatorDragonGames.JigglePhysics {

public class JiggleRig : MonoBehaviour, IJiggleParameterProvider {
    [SerializeField] private JiggleRigData jiggleRigData;
    [SerializeField, Tooltip("Whether to check if parameters have been changed each frame.")] private bool animatedParameters = false;
    
    [NonSerialized] private JiggleTreeSegment segment;
    private bool addedToJiggleTreeSegments = false;

    public JiggleRigData GetJiggleRigData() => jiggleRigData;

    public JiggleTreeInputParameters GetInputParameters() => jiggleRigData.jiggleTreeInputParameters;
    
    
    /// <summary>
    /// Sets the jiggle tree input parameters, but only locally, to send it to jobs either make sure animatedParameters is true, or call UpdateParameters after you're done making changes.
    /// </summary>
    /// <param name="newParameters">The parameters that the jiggles should use to determine their motion.</param>
    public void SetInputParameters(JiggleTreeInputParameters newParameters) {
        jiggleRigData.jiggleTreeInputParameters = newParameters;
    }

    #if !JIGGLEPHYSICS_DISABLE_ON_ENABLE
    private void OnEnable() {
        OnInitialize();
    }
    #endif
    
    #if !JIGGLEPHYSICS_DISABLE_ON_DISABLE
    private void OnDisable() {
        OnRemove();
    }
    #endif

    public void OnInitialize() {
        if (jiggleRigData.rootBone == null) {
            throw new UnityException("Jiggle Rig enabled without a root bone assigned!");
        }

        jiggleRigData.RegenerateCacheLookup();

        segment ??= new JiggleTreeSegment(this);
        segment.SetDirty();
        if (!addedToJiggleTreeSegments) {
            JigglePhysics.AddJiggleTreeSegment(segment);
            addedToJiggleTreeSegments = true;
        }
    }

    public void OnRemove() {
        if (segment != null && addedToJiggleTreeSegments) {
            JigglePhysics.RemoveJiggleTreeSegment(segment);
            addedToJiggleTreeSegments = false;
        }
    }

    /// <summary>
    /// Immediately resamples the rest pose of the bones in the tree. This can be useful if you have modified the bones' transforms on initialization and want to control when the rest pose is sampled.
    /// </summary>
    public void ResampleRestPose() {
        jiggleRigData.ResampleRestPose();
        if (segment != null && segment.jiggleTree != null) {
            segment.jiggleTree.SetDirty();
        }
    }

    public void SnapToRestPose() {
        jiggleRigData.SnapToRestPose();
    }
    
    /// <summary>
    /// Sends updated parameters to the jiggle tree on the jobs side.
    /// </summary>
    public void UpdateParameters() {
        if (segment == null) {
            return;
        }
        segment.UpdateParameters();
    }

    public bool HasAnimatedParameters {
        get => animatedParameters;
        set => animatedParameters = value;
    }

    // Collider set as of the last inspector change, to notice registration edits while playing. Not
    // serialized: it describes the state the live tree was built from, which a domain reload rebuilds anyway.
    [NonSerialized] private int lastColliderSignature;
    [NonSerialized] private bool hasColliderSignature;

    private void OnValidate() {
        if (!jiggleRigData.hasSerializedData) {
            jiggleRigData = JiggleRigData.Default();
        }
        jiggleRigData.OnValidate();
        if (Application.isPlaying) {
            UpdateParameters();
            // Parameters are pushed to the running tree, but the collider set is only read when the tree is
            // built, so editing Jiggle Colliders while playing would otherwise appear to do nothing until the
            // rig was toggled off and on. Rebuilding on every OnValidate instead would restart the simulation
            // on each slider drag, hence comparing the set rather than always dirtying.
            var colliderSignature = jiggleRigData.GetColliderSignature();
            if (hasColliderSignature && colliderSignature != lastColliderSignature) {
                segment?.SetDirty();
            }
            lastColliderSignature = colliderSignature;
            hasColliderSignature = true;
        }
    }

    private void OnDrawGizmosSelected() {
        if (!isActiveAndEnabled) {
            return;
        }
        jiggleRigData.OnDrawGizmosSelected();
    }

}

}
