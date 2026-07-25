using UnityEngine;

namespace GatorDragonGames.JigglePhysics {

// Shared wireframe-gizmo drawing helpers, so the collider gizmos (JiggleCollider.cs) and the bone collision
// gizmos (JiggleRigData.cs) render in the same VRC PhysBone-style contour-line look.
internal static class JiggleGizmoDraw {
    public const int RingSegments = 32;
    public const int ArcSegments = 16;

    // Draws an ellipse (or arc thereof) in the plane spanned by axisA/axisB, centered at `center`.
    public static void DrawEllipseArc(Vector3 center, Vector3 axisA, Vector3 axisB, float radiusA, float radiusB, float startAngle, float endAngle, int segments) {
        var prevPoint = center + axisA * (Mathf.Cos(startAngle) * radiusA) + axisB * (Mathf.Sin(startAngle) * radiusB);
        for (int i = 1; i <= segments; i++) {
            var t = startAngle + (endAngle - startAngle) * i / segments;
            var point = center + axisA * (Mathf.Cos(t) * radiusA) + axisB * (Mathf.Sin(t) * radiusB);
            Gizmos.DrawLine(prevPoint, point);
            prevPoint = point;
        }
    }
}

}
