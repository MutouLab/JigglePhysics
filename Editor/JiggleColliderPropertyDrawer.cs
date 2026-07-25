using UnityEditor;
using UnityEngine;

namespace GatorDragonGames.JigglePhysics {

[CustomPropertyDrawer(typeof(JiggleCollider))]
public class JiggleColliderPropertyDrawer : PropertyDrawer {
    private static readonly GUIContent CenterLabel = new GUIContent("Center", "Offset from the collider transform, in its local space.");
    private static readonly GUIContent RadiusScaleLabel = new GUIContent("Radius Scale", "Per-axis scale of the radius along the collider's local X/Y/Z axes. The component matching Axis scales the cap length; the other two shape the cross-section ellipse. A value of 0 (or negative) on any component is treated as 1 (round).");

    public override float GetPropertyHeight(SerializedProperty property, GUIContent label) {
        var typeProp = property.FindPropertyRelative("type");
        var type = (JiggleCollider.JiggleColliderType)typeProp.enumValueIndex;
        var spacing = EditorGUIUtility.standardVerticalSpacing;
        // type + positionOffset are shown for every collider type. Vector properties are measured rather
        // than assumed to be one line: float2/float3 occupy two lines when the inspector is not in wide mode.
        var height = EditorGUIUtility.singleLineHeight + spacing;
        height += EditorGUI.GetPropertyHeight(property.FindPropertyRelative("positionOffset")) + spacing;
        switch (type) {
            case JiggleCollider.JiggleColliderType.Sphere:
                height += EditorGUIUtility.singleLineHeight + spacing; // radius
                break;
            case JiggleCollider.JiggleColliderType.Capsule:
                height += (EditorGUIUtility.singleLineHeight + spacing) * 3f; // radius, height, capsuleAxis
                height += EditorGUI.GetPropertyHeight(property.FindPropertyRelative("radiusScale")) + spacing;
                break;
            case JiggleCollider.JiggleColliderType.Plane:
                break;
        }
        return height;
    }

    public override void OnGUI(Rect position, SerializedProperty property, GUIContent label) {
        EditorGUI.BeginProperty(position, label, property);

        var typeProp = property.FindPropertyRelative("type");
        var positionOffsetProp = property.FindPropertyRelative("positionOffset");
        var radiusProp = property.FindPropertyRelative("radius");
        var heightProp = property.FindPropertyRelative("height");
        var capsuleAxisProp = property.FindPropertyRelative("capsuleAxis");
        var radiusScaleProp = property.FindPropertyRelative("radiusScale");
        var rect = new Rect(position.x, position.y, position.width, EditorGUIUtility.singleLineHeight);

        DrawSingleLine(ref rect, typeProp, null);

        var type = (JiggleCollider.JiggleColliderType)typeProp.enumValueIndex;

        switch (type) {
            case JiggleCollider.JiggleColliderType.Sphere:
                DrawClampedFloat(ref rect, radiusProp, "Radius");
                break;
            case JiggleCollider.JiggleColliderType.Capsule:
                DrawClampedFloat(ref rect, radiusProp, "Radius");
                DrawClampedFloat(ref rect, heightProp, "Height");
                DrawSingleLine(ref rect, capsuleAxisProp, new GUIContent("Axis"));
                NormalizeUnsetRadiusScale(radiusScaleProp);
                DrawMeasured(ref rect, radiusScaleProp, RadiusScaleLabel);
                break;
            case JiggleCollider.JiggleColliderType.Plane:
                break;
        }

        DrawMeasured(ref rect, positionOffsetProp, CenterLabel);

        EditorGUI.EndProperty();
    }

    // Unity cannot give a serialized struct field a non-zero default, so a collider that predates radiusScale
    // (or one just added to the array) deserializes as (0,0,0). The runtime already reads non-positive
    // components as 1, but showing zeros reads as "no size", so surface the effective value instead.
    // Only an all-zero vector is rewritten, leaving a deliberately zeroed single component alone.
    private static void NormalizeUnsetRadiusScale(SerializedProperty prop) {
        var x = prop.FindPropertyRelative("x");
        var y = prop.FindPropertyRelative("y");
        var z = prop.FindPropertyRelative("z");
        if (x == null || y == null || z == null) {
            return;
        }
        if (x.floatValue != 0f || y.floatValue != 0f || z.floatValue != 0f) {
            return;
        }
        x.floatValue = 1f;
        y.floatValue = 1f;
        z.floatValue = 1f;
    }

    // Draws a property that is known to occupy exactly one line, then advances past it.
    private static void DrawSingleLine(ref Rect rect, SerializedProperty prop, GUIContent label) {
        rect.height = EditorGUIUtility.singleLineHeight;
        if (label == null) {
            EditorGUI.PropertyField(rect, prop);
        } else {
            EditorGUI.PropertyField(rect, prop, label);
        }
        rect.y += rect.height + EditorGUIUtility.standardVerticalSpacing;
    }

    // Draws a property whose height must be queried (float2/float3 grow to two lines in narrow inspectors).
    private static void DrawMeasured(ref Rect rect, SerializedProperty prop, GUIContent label) {
        rect.height = EditorGUI.GetPropertyHeight(prop);
        EditorGUI.PropertyField(rect, prop, label);
        rect.y += rect.height + EditorGUIUtility.standardVerticalSpacing;
    }

    private static void DrawClampedFloat(ref Rect rect, SerializedProperty prop, string label) {
        rect.height = EditorGUIUtility.singleLineHeight;
        EditorGUI.PropertyField(rect, prop, new GUIContent(label));
        if (prop.floatValue < 0f) {
            prop.floatValue = 0f;
        }
        rect.y += rect.height + EditorGUIUtility.standardVerticalSpacing;
    }

}

}
