using System;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Jobs;

namespace GatorDragonGames.JigglePhysics {

[Serializable]
public struct JiggleColliderSerializable {
    public Transform transform;

    [Tooltip("Capsule only: when set, the capsule runs from this collider's placement (start) to this " +
        "Transform (end), tracking it every frame, and Height is ignored. End Radius / End Offset still " +
        "apply at that end.")]
    public Transform endTransform;
    public JiggleCollider collider;

    // overrideTransform lets a caller pass the transform it will actually place this collider by, so the gizmo
    // cannot disagree with the collision data when the explicit transform slot is left empty.
    public void OnDrawGizmosSelected(Transform overrideTransform = null) {
        var placement = transform != null ? transform : overrideTransform;
        if (placement == null) {
            return;
        }
        collider.Read(placement);
        if (endTransform != null && collider.type == JiggleCollider.JiggleColliderType.Capsule) {
            collider.hasEndTransform = true;
            collider.worldEndPosition = endTransform.position;
        } else {
            // This struct lives on the component between gizmo calls, so clear the flag rather than let a
            // stale endpoint linger after the field is emptied in the inspector.
            collider.hasEndTransform = false;
        }
        var position = (Vector3)collider.localToWorldMatrix.c3.xyz;
        Gizmos.color = new Color(0.1254902f, 0.7607843f, 0.7215686f, 1f);
        switch (collider.type) {
            case JiggleCollider.JiggleColliderType.Sphere: {
                var r = collider.worldRadius;
                // Three orthogonal rings, VRC PhysBone-style wireframe.
                JiggleGizmoDraw.DrawEllipseArc(position, Vector3.right, Vector3.up, r, r, 0f, Mathf.PI * 2f, JiggleGizmoDraw.RingSegments);
                JiggleGizmoDraw.DrawEllipseArc(position, Vector3.up, Vector3.forward, r, r, 0f, Mathf.PI * 2f, JiggleGizmoDraw.RingSegments);
                JiggleGizmoDraw.DrawEllipseArc(position, Vector3.forward, Vector3.right, r, r, 0f, Mathf.PI * 2f, JiggleGizmoDraw.RingSegments);
            }
            break;
            case JiggleCollider.JiggleColliderType.Capsule: {
                collider.GetWorldCapsuleSegment(out var segA, out var segB);
                var a = (Vector3)segA;
                var b = (Vector3)segB;
                var segVec = b - a;
                // Follow the actual (possibly offset-tilted) segment direction rather than capsuleAxis, so the
                // gizmo tracks startOffset/endOffset. Falls back to capsuleAxis when the segment degenerates.
                var axisDir = segVec.sqrMagnitude > 1e-12f ? segVec.normalized : (Vector3)collider.GetWorldAxis();
                collider.GetWorldAxes(out var xAxis, out var yAxis, out var zAxis);
                float3 uAxis, vAxis;
                // Per-axis radii at each end, picked out by which local axis the capsule runs along.
                var startR = collider.worldStartRadius;
                var endR = collider.worldEndRadius;
                float ruStart, rvStart, rAxisStart;
                float ruEnd, rvEnd, rAxisEnd;
                switch (collider.capsuleAxis) {
                    case JiggleCollider.CapsuleAxis.X:
                        uAxis = yAxis; vAxis = zAxis;
                        ruStart = startR.y; rvStart = startR.z; rAxisStart = startR.x;
                        ruEnd = endR.y; rvEnd = endR.z; rAxisEnd = endR.x;
                        break;
                    case JiggleCollider.CapsuleAxis.Y:
                        uAxis = xAxis; vAxis = zAxis;
                        ruStart = startR.x; rvStart = startR.z; rAxisStart = startR.y;
                        ruEnd = endR.x; rvEnd = endR.z; rAxisEnd = endR.y;
                        break;
                    default: // Z
                        uAxis = xAxis; vAxis = yAxis;
                        ruStart = startR.x; rvStart = startR.y; rAxisStart = startR.z;
                        ruEnd = endR.x; rvEnd = endR.y; rAxisEnd = endR.z;
                        break;
                }
                var u = (Vector3)uAxis;
                var v = (Vector3)vAxis;
                // Equator rings at the cylinder/hemisphere junction, each at its own end's radii
                JiggleGizmoDraw.DrawEllipseArc(a, u, v, ruStart, rvStart, 0f, Mathf.PI * 2f, JiggleGizmoDraw.RingSegments);
                JiggleGizmoDraw.DrawEllipseArc(b, u, v, ruEnd, rvEnd, 0f, Mathf.PI * 2f, JiggleGizmoDraw.RingSegments);
                // Side silhouette lines: connect matching angular points on the two (possibly differently sized) rings.
                Gizmos.DrawLine(a + u * ruStart, b + u * ruEnd);
                Gizmos.DrawLine(a - u * ruStart, b - u * ruEnd);
                Gizmos.DrawLine(a + v * rvStart, b + v * rvEnd);
                Gizmos.DrawLine(a - v * rvStart, b - v * rvEnd);
                // Hemisphere arcs, bulging away from the cylinder body. Both arcs at a given cap share the same
                // axial radius, so they meet exactly at the pole (a - axisDir * rAxisStart / b + axisDir * rAxisEnd).
                JiggleGizmoDraw.DrawEllipseArc(a, u, -axisDir, ruStart, rAxisStart, 0f, Mathf.PI, JiggleGizmoDraw.ArcSegments);
                JiggleGizmoDraw.DrawEllipseArc(a, v, -axisDir, rvStart, rAxisStart, 0f, Mathf.PI, JiggleGizmoDraw.ArcSegments);
                JiggleGizmoDraw.DrawEllipseArc(b, u, axisDir, ruEnd, rAxisEnd, 0f, Mathf.PI, JiggleGizmoDraw.ArcSegments);
                JiggleGizmoDraw.DrawEllipseArc(b, v, axisDir, rvEnd, rAxisEnd, 0f, Mathf.PI, JiggleGizmoDraw.ArcSegments);
                // Cross marker on the start (a) cap. Start and End are defined by the direction Axis points, and
                // large offsets can even swap the two ends, so mark which one the Start fields drive.
                var markerSize = Mathf.Max(ruStart, rvStart) * 0.4f;
                if (markerSize > 0f) {
                    Gizmos.DrawLine(a - u * markerSize, a + u * markerSize);
                    Gizmos.DrawLine(a - v * markerSize, a + v * markerSize);
                }
            }
            break;
            case JiggleCollider.JiggleColliderType.Plane: {
                var up = ((Vector4)collider.localToWorldMatrix.c1).normalized;
                var upDir = new Vector3(up.x, up.y, up.z);
                var right = Vector3.Cross(upDir, Vector3.forward).normalized;
                if (right.magnitude < 0.01f) right = Vector3.Cross(upDir, Vector3.right).normalized;
                var forward = Vector3.Cross(upDir, right).normalized;
                var size = 2f;
                var p1 = position + (right + forward) * size;
                var p2 = position + (right - forward) * size;
                var p3 = position + (-right - forward) * size;
                var p4 = position + (-right + forward) * size;
                Gizmos.DrawLine(p1, p2);
                Gizmos.DrawLine(p2, p3);
                Gizmos.DrawLine(p3, p4);
                Gizmos.DrawLine(p4, p1);
                Gizmos.DrawLine(p1, p3);
                Gizmos.DrawLine(p2, p4);
                // Draw normal arrow
                Gizmos.DrawLine(position, position + upDir * size * 0.5f);
            }
            break;
        }
    }
}

[Serializable]
public struct JiggleCollider {
    public enum JiggleColliderType {
        Sphere,
        Capsule,
        Plane
    }

    public enum CapsuleAxis {
        X,
        Y,
        Z
    }

    [NonSerialized] public bool enabled;

    public JiggleColliderType type;

    // Local-space offset from the collider Transform, applied before localToWorldMatrix is derived.
    public float3 positionOffset;

    public float radius;
    [NonSerialized] public float worldRadius;

    // Capsule-only: the radius at each end of the segment, given per local X/Y/Z axis so each end can be an
    // ellipsoid rather than a sphere. The component matching capsuleAxis is the cap's axial radius; the other
    // two are the cross-section. Any component <= 0 falls back to `radius`, which keeps colliders authored
    // before these fields existed (and any end you simply do not want to shape) perfectly round.
    // Interpolating between the two ends approximates a tapered capsule: a true frustum SDF's contact point
    // does not generally coincide with the closest point on the axis, which matches this package's
    // "authorable rather than physically accurate" collision model.
    public float3 startRadius;
    public float3 endRadius;
    [NonSerialized] public float3 worldStartRadius;
    [NonSerialized] public float3 worldEndRadius;

    public float height;
    [NonSerialized] public float worldHeight;

    public CapsuleAxis capsuleAxis;

    // Capsule-only: local-space offsets applied to the start (a) and end (b) endpoints of the capsule segment,
    // on top of the capsuleAxis/height-derived endpoints. See GetWorldCapsuleSegment.
    public float3 startOffset;
    public float3 endOffset;

    // Capsule-only runtime state: when hasEndTransform is set (authored via JiggleColliderSerializable's
    // endTransform), the segment's far endpoint is worldEndPosition instead of the Height/Axis-derived one.
    // Fed once per Simulate from the main thread (JiggleMemoryBus.WriteColliderEndpointPositions); Read()
    // deliberately leaves both fields untouched so the collider read job's read-modify-write cannot lose them.
    [NonSerialized] public bool hasEndTransform;
    [NonSerialized] public float3 worldEndPosition;

    [NonSerialized] public float4x4 localToWorldMatrix;
    private float AverageScale(float4x4 matrix) {
        float sx = math.length(matrix.c0.xyz);
        float sy = math.length(matrix.c1.xyz);
        float sz = math.length(matrix.c2.xyz);
        return (sx + sy + sz) / 3f;
    }

    public float3 GetWorldAxis() {
        float3 col = capsuleAxis switch {
            CapsuleAxis.X => localToWorldMatrix.c0.xyz,
            CapsuleAxis.Y => localToWorldMatrix.c1.xyz,
            CapsuleAxis.Z => localToWorldMatrix.c2.xyz,
            _ => throw new ArgumentOutOfRangeException()
        };
        return math.normalizesafe(col, new float3(0f, 1f, 0f));
    }

    // Returns the three world-space local axes (normalized), in X/Y/Z order, used for the ellipsoidal radius.
    public void GetWorldAxes(out float3 x, out float3 y, out float3 z) {
        x = math.normalizesafe(localToWorldMatrix.c0.xyz, new float3(1f, 0f, 0f));
        y = math.normalizesafe(localToWorldMatrix.c1.xyz, new float3(0f, 1f, 0f));
        z = math.normalizesafe(localToWorldMatrix.c2.xyz, new float3(0f, 0f, 1f));
    }

    // Single source of truth for the capsule's world-space segment endpoints. startOffset/endOffset are
    // transformed as directions (no translation), so they move the endpoints in the collider's local space
    // regardless of where positionOffset/localToWorldMatrix places the collider's center. With both offsets
    // zero this reduces exactly to center +/- GetWorldAxis() * (worldHeight * 0.5f). When hasEndTransform is
    // set, this Height/Axis-derived formula is bypassed entirely in favor of the two-bone branch below.
    public void GetWorldCapsuleSegment(out float3 a, out float3 b) {
        var center = localToWorldMatrix.c3.xyz;
        if (hasEndTransform) {
            // Two-bone mode: the start cap sits at the collider's own placement and the end cap follows the
            // end Transform, so Height/Axis no longer define the segment. Both offsets stay in the start
            // transform's local space, consistent with the cross-section axes (a known limitation).
            a = center + math.mul(localToWorldMatrix, new float4(startOffset, 0f)).xyz;
            b = worldEndPosition + math.mul(localToWorldMatrix, new float4(endOffset, 0f)).xyz;
            return;
        }
        var axisDir = GetWorldAxis();
        var halfHeight = worldHeight * 0.5f;
        a = center - axisDir * halfHeight + math.mul(localToWorldMatrix, new float4(startOffset, 0f)).xyz;
        b = center + axisDir * halfHeight + math.mul(localToWorldMatrix, new float4(endOffset, 0f)).xyz;
    }

    public void Read(Transform transform) {
        Read(transform.localToWorldMatrix);
    }
    
    public void Read(TransformAccess transform) {
        Read(transform.localToWorldMatrix);
    }
    
    public void Read(float4x4 matrix) {
        localToWorldMatrix = math.mul(matrix, float4x4.Translate(positionOffset));
        var averageScale = AverageScale(localToWorldMatrix);
        worldRadius = math.max(0f, radius) * averageScale;
        worldHeight = math.max(0f, height) * averageScale;
        worldStartRadius = ResolveEndRadius(startRadius, radius) * averageScale;
        worldEndRadius = ResolveEndRadius(endRadius, radius) * averageScale;
    }

    // An all-zero vector means "not authored" and falls back to the uniform `radius`, which is what colliders
    // serialized before these fields existed look like. Once any component is set the vector is taken as-is, so
    // a deliberately zeroed component reads as "flat" rather than silently reverting to `radius`. The lower
    // clamp keeps an extreme aspect from producing a radius whose square underflows, which would divide by zero
    // in the ellipsoid solve.
    private static float3 ResolveEndRadius(float3 perAxisRadius, float uniformRadius) {
        if (math.all(perAxisRadius <= 0f)) {
            return new float3(math.max(0f, uniformRadius));
        }
        return math.max(perAxisRadius, new float3(1e-6f));
    }
}

}
