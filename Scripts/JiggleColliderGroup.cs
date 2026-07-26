using UnityEngine;

namespace GatorDragonGames.JigglePhysics {

/// <summary>
/// Marks a GameObject as a collection of Jiggle Colliders: a rig that registers this object picks up every
/// JiggleColliderExample beneath it, instead of only the ones on the object itself.
/// </summary>
/// <remarks>
/// Registration is normally per-object and does not search children, because colliders live on bones and
/// bones nest - searching children from a bone would silently drag in every collider further down the
/// skeleton. This component is the explicit opt-out of that rule, so it belongs on a node that exists purely
/// to group colliders (a "Chest" or "Spine" holder under a physics root), never on a bone.
///
/// Grouping is a property of the collection, not of any one rig: every rig registering this object sees the
/// same set. Colliders reachable through several registrations (the group and one of its children, or two
/// nested groups) are still only counted once.
/// </remarks>
[AddComponentMenu("Physics/Jiggle Collider Group")]
public class JiggleColliderGroup : MonoBehaviour {
}

}
