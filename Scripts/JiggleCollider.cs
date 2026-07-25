using System;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Jobs;

namespace GatorDragonGames.JigglePhysics {

[Serializable]
public struct JiggleColliderSerializable {
    public Transform transform;
    public JiggleCollider collider;

    private const int RingSegments = 32;
    private const int ArcSegments = 16;

    public void OnDrawGizmosSelected() {
        if (transform == null) {
            return;
        }
        collider.Read(transform);
        var position = (Vector3)collider.localToWorldMatrix.c3.xyz;
        Gizmos.color = new Color(0.854902f, 0.6470588f, 0.1254902f, 1f);
        switch (collider.type) {
            case JiggleCollider.JiggleColliderType.Sphere: {
                var r = collider.worldRadius;
                // Three orthogonal rings, VRC PhysBone-style wireframe.
                DrawEllipseArc(position, Vector3.right, Vector3.up, r, r, 0f, Mathf.PI * 2f, RingSegments);
                DrawEllipseArc(position, Vector3.up, Vector3.forward, r, r, 0f, Mathf.PI * 2f, RingSegments);
                DrawEllipseArc(position, Vector3.forward, Vector3.right, r, r, 0f, Mathf.PI * 2f, RingSegments);
            }
            break;
            case JiggleCollider.JiggleColliderType.Capsule: {
                var axisDir = (Vector3)collider.GetWorldAxis();
                collider.GetWorldAxes(out var xAxis, out var yAxis, out var zAxis);
                float3 uAxis, vAxis;
                float ruScale, rvScale, axisScale;
                switch (collider.capsuleAxis) {
                    case JiggleCollider.CapsuleAxis.X:
                        uAxis = yAxis; vAxis = zAxis;
                        ruScale = collider.worldRadiusScale.y; rvScale = collider.worldRadiusScale.z; axisScale = collider.worldRadiusScale.x;
                        break;
                    case JiggleCollider.CapsuleAxis.Y:
                        uAxis = xAxis; vAxis = zAxis;
                        ruScale = collider.worldRadiusScale.x; rvScale = collider.worldRadiusScale.z; axisScale = collider.worldRadiusScale.y;
                        break;
                    default: // Z
                        uAxis = xAxis; vAxis = yAxis;
                        ruScale = collider.worldRadiusScale.x; rvScale = collider.worldRadiusScale.y; axisScale = collider.worldRadiusScale.z;
                        break;
                }
                var u = (Vector3)uAxis;
                var v = (Vector3)vAxis;
                var halfHeight = collider.worldHeight * 0.5f;
                var ru = collider.worldRadius * ruScale;
                var rv = collider.worldRadius * rvScale;
                var rAxis = collider.worldRadius * axisScale;
                var top = position + axisDir * halfHeight;
                var bottom = position - axisDir * halfHeight;
                // Equator rings at the cylinder/hemisphere junction (reflects radiusScale)
                DrawEllipseArc(top, u, v, ru, rv, 0f, Mathf.PI * 2f, RingSegments);
                DrawEllipseArc(bottom, u, v, ru, rv, 0f, Mathf.PI * 2f, RingSegments);
                // Side silhouette lines
                Gizmos.DrawLine(top + u * ru, bottom + u * ru);
                Gizmos.DrawLine(top - u * ru, bottom - u * ru);
                Gizmos.DrawLine(top + v * rv, bottom + v * rv);
                Gizmos.DrawLine(top - v * rv, bottom - v * rv);
                // Hemisphere arcs, bulging away from the cylinder body. Both arcs at a given cap share the same
                // axial radius (rAxis), so they meet exactly at the pole (top/bottom + axisDir * rAxis).
                DrawEllipseArc(top, u, axisDir, ru, rAxis, 0f, Mathf.PI, ArcSegments);
                DrawEllipseArc(top, v, axisDir, rv, rAxis, 0f, Mathf.PI, ArcSegments);
                DrawEllipseArc(bottom, u, -axisDir, ru, rAxis, 0f, Mathf.PI, ArcSegments);
                DrawEllipseArc(bottom, v, -axisDir, rv, rAxis, 0f, Mathf.PI, ArcSegments);
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

    // Draws an ellipse (or arc thereof) in the plane spanned by axisA/axisB, centered at `center`.
    private static void DrawEllipseArc(Vector3 center, Vector3 axisA, Vector3 axisB, float radiusA, float radiusB, float startAngle, float endAngle, int segments) {
        var prevPoint = center + axisA * (Mathf.Cos(startAngle) * radiusA) + axisB * (Mathf.Sin(startAngle) * radiusB);
        for (int i = 1; i <= segments; i++) {
            var t = startAngle + (endAngle - startAngle) * i / segments;
            var point = center + axisA * (Mathf.Cos(t) * radiusA) + axisB * (Mathf.Sin(t) * radiusB);
            Gizmos.DrawLine(prevPoint, point);
            prevPoint = point;
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

    // Per-axis scale of the capsule's radius, expressed along the collider's local X/Y/Z axes (not reordered
    // by capsuleAxis). The component matching capsuleAxis scales the cap's axial radius; the other two scale
    // the cross-section ellipse. Values <= 0 are treated as 1 so that pre-existing serialized data (which
    // defaults to (0,0,0)) behaves exactly like a circular capsule.
    public float3 radiusScale;
    [NonSerialized] public float3 worldRadiusScale;

    public float height;
    [NonSerialized] public float worldHeight;

    public CapsuleAxis capsuleAxis;

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
        // Non-positive components mean "circular"/"round". The lower clamp keeps an extreme aspect from producing a
        // radius small enough that its square underflows, which would divide by zero in the ellipsoid solve.
        worldRadiusScale = math.max(math.select(radiusScale, new float3(1f), radiusScale <= 0f), new float3(1e-3f));
    }
}

}
