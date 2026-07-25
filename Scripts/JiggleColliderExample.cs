using UnityEngine;

namespace GatorDragonGames.JigglePhysics {
public class JiggleColliderExample : MonoBehaviour {
    [SerializeField] private JiggleColliderSerializable jiggleCollider;

    [SerializeField, Tooltip("If enabled, this collider affects every Jiggle Rig in the scene, like a global " +
        "scene collider (the old behavior). If disabled (default), it only affects Jiggle Rigs that reference " +
        "this component directly in their Jiggle Colliders list, so unrelated rigs (or this collider's own rig) " +
        "don't collide against it.")]
    private bool affectsAllRigs = false;

    // Lets a Jiggle Rig that references this component read its shape, regardless of affectsAllRigs.
    public JiggleColliderSerializable Collider => jiggleCollider;

    // The transform the collider is actually placed by. Authors usually just put the component on the object
    // they want to collide with and leave the explicit slot empty, so fall back to this GameObject. Both the
    // collision data and the gizmo resolve it here, so a blank slot can never draw one shape and collide as
    // another.
    public Transform ResolvedTransform => jiggleCollider.transform != null ? jiggleCollider.transform : transform;

    // Tracks what was actually handed to JigglePhysics, so a toggle can never double-register or try to remove
    // something that was never added. Deliberately not serialized: it describes runtime state, and OnEnable
    // re-establishes it after a domain reload.
    private bool registeredGlobally;

    private void OnEnable() {
        SyncGlobalRegistration();
    }

    private void OnDisable() {
        if (registeredGlobally) {
            JigglePhysics.RemoveJiggleCollider(jiggleCollider);
            registeredGlobally = false;
        }
    }

    private void OnValidate() {
        // Registration is otherwise a snapshot taken at enable time, so flipping the toggle in the inspector
        // would appear to do nothing until the object was disabled and re-enabled. Outside play mode there is
        // no job system to register with, and AddJiggleCollider would dereference it.
        if (Application.isPlaying && isActiveAndEnabled) {
            SyncGlobalRegistration();
        }
    }

    private void SyncGlobalRegistration() {
        if (affectsAllRigs == registeredGlobally) {
            return;
        }
        if (affectsAllRigs) {
            JigglePhysics.AddJiggleCollider(jiggleCollider);
        } else {
            JigglePhysics.RemoveJiggleCollider(jiggleCollider);
        }
        registeredGlobally = affectsAllRigs;
    }

    private void OnDrawGizmos() {
        jiggleCollider.OnDrawGizmosSelected(ResolvedTransform);
    }
}
}
