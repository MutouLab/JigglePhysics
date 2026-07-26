using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.UIElements;
#endif

namespace GatorDragonGames.JigglePhysics {

[Serializable]
public struct JiggleTransformCachedData {
    public Transform bone;
    public float normalizedDistanceFromRoot;
    public float lossyScale;
    public Vector3 restLocalPosition;
    public Vector4 restLocalRotation;
    // Authored local scale, sampled the same way as restLocalPosition/restLocalRotation (never read live from
    // the bone at simulation time). This matters specifically for scale: squash writes localScale every frame,
    // so reading it live (as JiggleMemoryBus.AddTreeToSlice used to) would capture squash's own output as the
    // "rest" value the next time the tree rebuilds, corrupting it further on every subsequent rebuild.
    public Vector3 restLocalScale;
}

[Serializable]
public struct JiggleRigData {
    [SerializeField] public bool hasSerializedData;
    [SerializeField] public string serializedVersion;
    [SerializeField] public Transform rootBone;
    [SerializeField] public bool excludeRoot;
    [SerializeField] public JiggleTreeInputParameters jiggleTreeInputParameters;
    [SerializeField] public Transform[] excludedTransforms;
    [SerializeField, HideInInspector] public JiggleTransformCachedData[] transformCachedData;
    // References to GameObjects carrying JiggleColliderExample components (VRC PhysBone-Collider-style),
    // rather than inline collider definitions. This makes personal collisions opt-in per rig: a rig only
    // collides against colliders it explicitly references here, instead of every rig colliding against every
    // scene collider (which caused self-collision "explosions" for colliders shared between mirrored rigs,
    // e.g. two breasts colliding with each other's own rig). See JiggleColliderExample.affectsAllRigs for the
    // opposite, global-collider behavior.
    // The reference is per-GameObject, not per-component: every collider component on a registered object
    // takes part. A concave surface has to be approximated by several convex shapes (e.g. a back's groove as
    // stacked mounds), and per-component references made that proxy register one drag at a time and silently
    // fall out of sync when its shapes were later refined - every referencing rig had to be revisited.
    // Children are searched only for objects marked with JiggleColliderGroup, so that a proxy split across
    // several child objects can also be registered once, without a plain bone registration dragging in every
    // collider further down the skeleton.
    [SerializeField, Tooltip("GameObjects whose Jiggle Collider Example components this rig collides " +
        "against. Every collider component on a registered object takes part; children are searched only if " +
        "the object has a Jiggle Collider Group component. Maximum of 32 colliders total per rig.")]
    public GameObject[] jiggleColliderObjects;

    // Pre-v0.0.4 storage: references were per-component rather than per-GameObject. Kept only as the
    // migration source for TryUpdateSerialization, which empties it into jiggleColliderObjects.
    [SerializeField, HideInInspector] public JiggleColliderExample[] jiggleColliders;
    
    [NonSerialized]
    private Dictionary<Transform, JiggleTransformCachedData> transformToCachedDataMap;

    private bool TryUpdateSerialization() {
        switch (serializedVersion) {
            case "v0.0.0": // Collision radius local space -> world space
                if (rootBone == null) {
                    return false;
                }
                var cachedScale = GetCache(rootBone);
                var scale = rootBone.lossyScale;
                var scaleSample = (scale.x + scale.y + scale.z)/3f;
                var scaleCorrection = cachedScale.lossyScale*(1f/(scaleSample*scaleSample));
                jiggleTreeInputParameters.collisionRadius.value *= scaleCorrection;
                serializedVersion = "v0.0.1";
                return true;
            case "v0.0.1": // rest pose is now serialized on author, generate if missing.
                var length = transformCachedData.Length;
                for (int i = 0; i < length; i++) {
                    var cachedData = transformCachedData[i];
                    var t = cachedData.bone;
                    if (!t) continue;
                    t.GetLocalPositionAndRotation(out var localPosition, out var localRotation);
                    cachedData.restLocalPosition = localPosition;
                    cachedData.restLocalRotation = new Vector4(localRotation.x, localRotation.y, localRotation.z, localRotation.w);
                    transformCachedData[i] = cachedData;
                }
                serializedVersion = "v0.0.2";
                return true;
            case "v0.0.2":
                // Rest scale is now serialized on author too (see JiggleTransformCachedData.restLocalScale):
                // previously it was read live from the bone at simulate time, which squash can overwrite, so
                // sample it now while this data predates squash and can't yet be corrupted by it. Runs in the
                // editor (OnValidate), so this always sees an authored value, never a mid-squash one.
                var scaleLength = transformCachedData.Length;
                for (int i = 0; i < scaleLength; i++) {
                    var cachedData = transformCachedData[i];
                    var t = cachedData.bone;
                    if (!t) continue;
                    cachedData.restLocalScale = t.localScale;
                    transformCachedData[i] = cachedData;
                }
                serializedVersion = "v0.0.3";
                return true;
            case "v0.0.3":
                // Collider references become per-GameObject (see jiggleColliderObjects). Distinct components
                // on the same object collapse to one entry: without the dedup, an object whose shapes were
                // referenced individually would register once per old reference and push twice as hard.
                if (jiggleColliders is { Length: > 0 }) {
                    var migratedObjects = new List<GameObject>(jiggleColliders.Length);
                    for (int i = 0; i < jiggleColliders.Length; i++) {
                        var reference = jiggleColliders[i];
                        if (reference == null) continue;
                        if (!migratedObjects.Contains(reference.gameObject)) {
                            migratedObjects.Add(reference.gameObject);
                        }
                    }
                    jiggleColliderObjects = migratedObjects.ToArray();
                    jiggleColliders = Array.Empty<JiggleColliderExample>();
                }
                serializedVersion = "v0.0.4";
                return true;
            default:
                return false;
        }
    }

    public void ResampleRestPose() {
        var length = transformCachedData.Length;
        for (int i = 0; i < length; i++) {
            var cachedData = transformCachedData[i];
            var t = cachedData.bone;
            if (!t) continue;
            t.GetLocalPositionAndRotation(out var localPosition, out var localRotation);
            cachedData.restLocalPosition = localPosition;
            cachedData.restLocalRotation = new Vector4(localRotation.x, localRotation.y, localRotation.z, localRotation.w);
            cachedData.restLocalScale = t.localScale;
            transformCachedData[i] = cachedData;
        }
        RegenerateCacheLookup();
    }

    public void SnapToRestPose() {
        var length = transformCachedData.Length;
        for (int i = 0; i < length; i++) {
            var cachedData = transformCachedData[i];
            var t = cachedData.bone;
            if (!t || t == rootBone) continue;
            t.SetLocalPositionAndRotation(cachedData.restLocalPosition, new Quaternion(cachedData.restLocalRotation.x, cachedData.restLocalRotation.y, cachedData.restLocalRotation.z, cachedData.restLocalRotation.w));
        }
    }

    public void RegenerateCacheLookup() {
        transformToCachedDataMap = new Dictionary<Transform, JiggleTransformCachedData>();
        var count = transformCachedData.Length;
        for (int i = 0; i < count; i++) {
            var cachedData = transformCachedData[i];
            transformToCachedDataMap[cachedData.bone] = cachedData;
        }
    }

    public bool GetIsExcluded(Transform t) {
        var count = excludedTransforms.Length;
        for (int i = 0; i < count; i++) {
            if (excludedTransforms[i] == t) {
                return true;
            }
        }
        return false;
    }
    
    // Scratch space for expanding registered GameObjects into their collider components without per-rebuild
    // allocations. Main-thread only, like every other authoring entry point here.
    private static readonly List<JiggleColliderExample> tempColliderComponents = new();
    // Deduplication is per-component, not per-registered-object: a group and one of its children can both be
    // registered, which would otherwise add the same shape twice and push twice as hard.
    private static readonly HashSet<JiggleColliderExample> tempSeenColliders = new();

    // JiggleTree pairs personalColliders[i] with personalColliderTransforms[i] and
    // personalColliderEndTransforms[i] (see JiggleMemoryBus's TransformAccessArray population). One method
    // fills all three lists in a single pass so they cannot fall out of index-for-index step - previously
    // this was a comment contract across three separate methods.
    // Returns the total collider count found before the 32-per-tree cap truncates the lists, so OnValidate
    // can warn about the overflow without duplicating the expansion logic. The truncation itself is silent
    // because this also runs per-frame from the selected-rig gizmo, which would spam a warning.
    public int GetJiggleColliders(List<JiggleCollider> colliders, List<Transform> colliderTransforms, List<Transform> colliderEndTransforms)
        => GetJiggleColliders(colliders, colliderTransforms, colliderEndTransforms, out _);

    // The signature overload additionally reports an identity hash of the expanded set, so a caller can tell
    // a registration edit (which needs a tree rebuild) from a parameter edit. See JiggleRig.OnValidate.
    public int GetJiggleColliders(List<JiggleCollider> colliders, List<Transform> colliderTransforms, List<Transform> colliderEndTransforms, out int signature) {
        signature = 17;
        colliders.Clear();
        colliderTransforms.Clear();
        colliderEndTransforms.Clear();
        if (jiggleColliderObjects == null) {
            return 0;
        }
        tempSeenColliders.Clear();
        var count = jiggleColliderObjects.Length;
        for (int i = 0; i < count; i++) {
            var obj = jiggleColliderObjects[i];
            if (obj == null) continue;
            // Children are searched only for objects explicitly marked as a collection (see
            // JiggleColliderGroup); a plain registration stays at the object itself, so registering a bone
            // can never drag in the colliders of every bone below it. Inactive objects are included either
            // way, matching how a direct registration ignores the active state.
            if (obj.GetComponent<JiggleColliderGroup>() != null) {
                obj.GetComponentsInChildren(true, tempColliderComponents);
            } else {
                obj.GetComponents(tempColliderComponents);
            }
            var componentCount = tempColliderComponents.Count;
            for (int o = 0; o < componentCount; o++) {
                var reference = tempColliderComponents[o];
                if (!tempSeenColliders.Add(reference)) continue;
                signature = signature * 31 + reference.GetInstanceID();
                colliders.Add(reference.Collider.collider);
                colliderTransforms.Add(reference.ResolvedTransform);
                colliderEndTransforms.Add(reference.Collider.endTransform);
            }
        }
        var total = colliders.Count;
        if (total > 32) {
            colliders.RemoveRange(32, total - 32);
            colliderTransforms.RemoveRange(32, total - 32);
            colliderEndTransforms.RemoveRange(32, total - 32);
        }
        return total;
    }

    void ValidateCurve(ref AnimationCurve animationCurve) {
        if (animationCurve == null || animationCurve.length == 0) {
            animationCurve = AnimationCurve.Linear(0f, 1f, 1f, 1f);
        }
    }

    // Scratch lists for the OnValidate overflow check and the signature below; only the returned count and
    // signature are read, never the list contents.
    private static readonly List<JiggleCollider> tempValidationColliders = new();
    private static readonly List<Transform> tempValidationTransforms = new();
    private static readonly List<Transform> tempValidationEndTransforms = new();

    // Identity of the currently registered collider set, cheap enough to sample on every inspector change.
    public int GetColliderSignature() {
        GetJiggleColliders(tempValidationColliders, tempValidationTransforms, tempValidationEndTransforms, out var signature);
        return signature;
    }

    public void OnValidate() {
        jiggleTreeInputParameters.OnValidate();
        excludedTransforms ??= Array.Empty<Transform>();
        jiggleColliderObjects ??= Array.Empty<GameObject>();
        ValidateCurve(ref jiggleTreeInputParameters.stiffness.curve);
        ValidateCurve(ref jiggleTreeInputParameters.angleLimit.curve);
        ValidateCurve(ref jiggleTreeInputParameters.stretch.curve);
        ValidateCurve(ref jiggleTreeInputParameters.drag.curve);
        ValidateCurve(ref jiggleTreeInputParameters.airDrag.curve);
        ValidateCurve(ref jiggleTreeInputParameters.gravity.curve);
        ValidateCurve(ref jiggleTreeInputParameters.collisionRadius.curve);
        ValidateCurve(ref jiggleTreeInputParameters.squash.curve);
        ValidateCurve(ref jiggleTreeInputParameters.contactSoftness.curve);
        BuildNormalizedDistanceFromRootList();
        for (int i = 0; i < 100; i++) {
            if (!TryUpdateSerialization()) {
                break;
            }
        }
        // The cap counts expanded colliders (a registered object contributes every component it carries), so
        // the registration array's length alone proves nothing - run the real expansion and read its total.
        // Truncation happens inside GetJiggleColliders; this only surfaces the warning at authoring time.
        if (GetJiggleColliders(tempValidationColliders, tempValidationTransforms, tempValidationEndTransforms) > 32) {
            Debug.LogWarning("JigglePhysics: Maximum of 32 personal Jiggle Colliders are supported per tree. Extra colliders will be dropped.");
        }
    }
    public void BuildNormalizedDistanceFromRootList() {
        if (!rootBone) {
            return;
        }
        JigglePhysics.VisitForLength(rootBone, this, rootBone.position, 0f, out var totalLength);
        var data = new List<JiggleTransformCachedData>();
        VisitAndSetCacheData(data, rootBone, rootBone.position, 0f, totalLength);
        transformCachedData = data.ToArray();
        RegenerateCacheLookup();
    }
    
    private void VisitAndSetCacheData(List<JiggleTransformCachedData> data, Transform t, Vector3 lastPosition, float currentLength, float totalLength) {
        if (t == null || GetIsExcluded(t)) {
            return;
        }
        var validChildrenCount = GetValidChildrenCount(t);
        var scale = t.lossyScale;
        currentLength += Vector3.Distance(lastPosition, t.position);
        t.GetLocalPositionAndRotation(out var localPosition, out var localRotation);
        var position = t.position;
        data.Add(new JiggleTransformCachedData() {
            bone = t,
            restLocalPosition = localPosition,
            restLocalRotation = new Vector4(localRotation.x, localRotation.y, localRotation.z, localRotation.w),
            restLocalScale = t.localScale,
            normalizedDistanceFromRoot = currentLength / totalLength,
            lossyScale = (scale.x + scale.y + scale.z)/3f,
        });
        for (int i = 0; i < validChildrenCount; i++) {
            var child = GetValidChild(t, i);
            VisitAndSetCacheData(data, child, position, currentLength, totalLength);
        }
    }

    public int GetValidChildrenCount(Transform t) {
        if (t == null) return 0;
        int count = 0;
        var childCount = t.childCount;
        for(int i=0;i<childCount;i++) {
            var child = t.GetChild(i);
            if (child == null || GetIsExcluded(child)) continue;
            count++;
        }
        return count;
    }

    public Transform GetValidChild(Transform t, int index) {
        if (t == null) return null;
        int count = 0;
        var childCount = t.childCount;
        for(int i=0;i<childCount;i++) {
            var child = t.GetChild(i);
            if (child == null || GetIsExcluded(child)) continue;
            if (count == index) {
                return child;
            }
            count++;
        }
        return null;
    }
    
    public bool GetHasRootTransformError() => !rootBone;
    public bool GetCacheIsValid() {
        if (transformCachedData is not { Length: > 0 } || transformToCachedDataMap == null || transformToCachedDataMap.Count != transformCachedData.Length) {
            return false;
        }
        var count = transformCachedData.Length;
        for (int i = 0; i < count; i++) {
            if (!transformCachedData[i].bone) return false;
        }
        return true;
    }
    public JiggleTransformCachedData GetCache(Transform t) {
#if UNITY_EDITOR
        if (transformToCachedDataMap == null) {
            throw new InvalidOperationException("JiggleRigData: Cache lookup not initialized. Call RegenerateCacheLookup() first.");
        }
        if (!transformToCachedDataMap.TryGetValue(t, out var cachedData)) {
            throw new KeyNotFoundException($"JiggleRigData: Transform '{t.name}' not found in cache. Ensure it is a child of the root bone and not excluded.");
        }
        return cachedData;
#else
        return transformToCachedDataMap[t];
#endif
    }

    /// <summary>
    /// Sends updated parameters to the jiggle tree on the jobs side. Uses the provided list to prevent allocations.
    /// </summary>
    /// <param name="tree">Tree to update</param>
    /// <param name="parameters">empty list purely used to prevent allocations</param>
    public void UpdateParameters(JiggleTree tree, List<JigglePointParameters> parameters) {
        parameters.Clear();
        var bones = tree.bones;
        if (bones == null) {
            return;
        }
        var boneCount = bones.Length;
        for (int i = 0; i < boneCount; i++) {
            var bone = bones[i];
            var cache = GetCache(bone);
            parameters.Add(GetJiggleBoneParameter(cache.normalizedDistanceFromRoot));
        }
        tree.SetParameters(parameters);
    }
    
    public JigglePointParameters GetJiggleBoneParameter(float normalizedDistanceFromRoot) {
        return jiggleTreeInputParameters.ToJigglePointParameters(normalizedDistanceFromRoot);
    }
    
    public Transform[] GetJiggleBoneTransforms() {
        return rootBone.GetComponentsInChildren<Transform>();
    }
    
    public bool IsValid(Transform root) => (rootBone && rootBone.IsChildOf(root));
    public static JiggleRigData Default() {
        return new JiggleRigData {
            rootBone = null,
            serializedVersion = "v0.0.4",
            hasSerializedData = true,
            excludeRoot = false,
            jiggleTreeInputParameters = JiggleTreeInputParameters.Default(),
            excludedTransforms = Array.Empty<Transform>(),
            transformCachedData = Array.Empty<JiggleTransformCachedData>(),
            jiggleColliderObjects = Array.Empty<GameObject>(),
            jiggleColliders = Array.Empty<JiggleColliderExample>()
        };
    }

    public void OnDrawGizmosSelected() {
        // Referenced colliders are no longer drawn from here: JiggleColliderExample.OnDrawGizmos() already draws
        // its own shape unconditionally (not selection-gated), so looping over jiggleColliderObjects here as well would
        // just double them up whenever this rig happens to be selected.

        if (!rootBone) return;
        Gizmos.color = new Color(0.9607844f, 0.9607844f, 0.9607844f, 1f);
        var jiggleTree = JigglePhysics.CreateJiggleTree(this, null);
        var points = jiggleTree.points;
        var parameters = jiggleTree.parameters;
        var bones = jiggleTree.bones;
        var pointCount = points.Length;
        for (var index = 0; index < pointCount; index++) {
            var simulatedPoint = points[index];
            if (simulatedPoint.parentIndex == -1) continue;
            var parentIndex = simulatedPoint.parentIndex;
            var parentPoint = points[parentIndex];
            // Only segments between two real bones collide: DoDepenetration bails out as soon as either end is
            // a virtual point (the projected root and leaf tips), so drawing those would show collision volume
            // that does not exist - a leaf tip would taper the capsule to a point. The last real bone still
            // appears, as the tail of the segment coming from its parent.
            if (!parentPoint.hasTransform || !simulatedPoint.hasTransform) continue;

            // The editor never runs Cache(), so JiggleSimulatedPoint.collisionOffset is never populated here;
            // resolve the same bone-local-space -> world-space transform Cache() does, directly from the
            // (always-valid, see JigglePhysics.Visit) Transform each point is associated with.
            var headBone = bones[parentIndex];
            var headParameters = parameters[parentIndex];
            var headAverageScale = AverageScale(headBone.lossyScale);
            var headOffset = headBone.rotation * (Vector3)(headParameters.collisionOffset * headAverageScale);
            var headRadius = headParameters.collisionRadius * headAverageScale;

            var tailBone = bones[index];
            var tailParameters = parameters[index];
            var tailAverageScale = AverageScale(tailBone.lossyScale);
            var tailOffset = tailBone.rotation * (Vector3)(tailParameters.collisionOffset * tailAverageScale);
            var tailRadius = tailParameters.collisionRadius * tailAverageScale;

            DrawBone(parentPoint.position, simulatedPoint.position, headOffset, tailOffset, headRadius, tailRadius, headParameters);
        }
    }

    // Mirrors the averaging JiggleJobSimulate.Cache() applies at runtime, including the abs: a mirrored bone
    // carries a negative lossyScale component, and without it the gizmo would disagree with the real radius.
    private static float AverageScale(Vector3 lossyScale) {
        return (Mathf.Abs(lossyScale.x) + Mathf.Abs(lossyScale.y) + Mathf.Abs(lossyScale.z)) / 3f;
    }

    private static void DrawWireDisc(Vector3 center, Vector3 normal, float radius, int segmentCount = 32) {
        normal.Normalize();
        Vector3 up = normal;
        Vector3 forward = Vector3.Slerp(up, -up, 0.5f);
        Vector3 right = Vector3.Cross(up, forward).normalized * radius;

        float angleStep = 360f / segmentCount;
        Vector3 prevPoint = center + right;
        for (int i = 1; i <= segmentCount; i++) {
            float angle = angleStep * i;
            Quaternion rot = Quaternion.AngleAxis(angle, up);
            Vector3 nextPoint = center + rot * right;
            Gizmos.DrawLine(prevPoint, nextPoint);
            prevPoint = nextPoint;
        }
    }
    
    // boneHead/boneTail are the animated bone positions; headOffset/tailOffset shift the collision-capsule
    // ends away from them (mirrors JiggleSimulatedPoint.collisionOffset), and headRadius/tailRadius are the
    // (already bone-scaled) collisionRadius at each end, since the curve can taper it along the chain.
    private static void DrawBone(Vector3 boneHead, Vector3 boneTail, Vector3 headOffset, Vector3 tailOffset, float headRadius, float tailRadius, JigglePointParameters headParameters) {
        var a = boneHead + headOffset;
        var b = boneTail + tailOffset;
        var segVec = b - a;
        var axisDir = segVec.sqrMagnitude > 1e-12f ? segVec.normalized : Vector3.up;
        var u = Vector3.Cross(axisDir, Vector3.forward).normalized;
        if (u.magnitude < 0.01f) {
            u = Vector3.Cross(axisDir, Vector3.right).normalized;
        }
        var v = Vector3.Cross(axisDir, u).normalized;

        // Equator rings at each end (the collisionRadius curve can taper, so the two ends can differ)
        JiggleGizmoDraw.DrawEllipseArc(a, u, v, headRadius, headRadius, 0f, Mathf.PI * 2f, JiggleGizmoDraw.RingSegments);
        JiggleGizmoDraw.DrawEllipseArc(b, u, v, tailRadius, tailRadius, 0f, Mathf.PI * 2f, JiggleGizmoDraw.RingSegments);
        // Side silhouette lines: connect matching angular points on the two (possibly differently sized) rings.
        Gizmos.DrawLine(a + u * headRadius, b + u * tailRadius);
        Gizmos.DrawLine(a - u * headRadius, b - u * tailRadius);
        Gizmos.DrawLine(a + v * headRadius, b + v * tailRadius);
        Gizmos.DrawLine(a - v * headRadius, b - v * tailRadius);
        // Cap arcs, bulging away from the segment body.
        JiggleGizmoDraw.DrawEllipseArc(a, u, -axisDir, headRadius, headRadius, 0f, Mathf.PI, JiggleGizmoDraw.ArcSegments);
        JiggleGizmoDraw.DrawEllipseArc(a, v, -axisDir, headRadius, headRadius, 0f, Mathf.PI, JiggleGizmoDraw.ArcSegments);
        JiggleGizmoDraw.DrawEllipseArc(b, u, axisDir, tailRadius, tailRadius, 0f, Mathf.PI, JiggleGizmoDraw.ArcSegments);
        JiggleGizmoDraw.DrawEllipseArc(b, v, axisDir, tailRadius, tailRadius, 0f, Mathf.PI, JiggleGizmoDraw.ArcSegments);

        // Angle limit disc: kept as-is, drawn from the un-offset bone positions since it represents a joint
        // constraint rather than the collision shape.
        var boneDirection = (boneTail - boneHead).normalized;
        var angleLimitScale = 0.05f;
        DrawWireDisc(boneHead + boneDirection * (angleLimitScale * Mathf.Cos(headParameters.angleLimit * Mathf.Deg2Rad)),
            boneDirection,
            angleLimitScale * Mathf.Sin(headParameters.angleLimit * Mathf.Deg2Rad));
    }
#if UNITY_EDITOR
#endif
}
}
