using UnityEditor;
using UnityEngine;

namespace GatorDragonGames.JigglePhysics {

[CustomPropertyDrawer(typeof(JiggleCollider))]
public class JiggleColliderPropertyDrawer : PropertyDrawer {
    private static readonly GUIContent CenterLabel = new GUIContent("Center", "Offset from the collider transform, in its local space.");
    private static readonly GUIContent StartRadiusLabel = new GUIContent("Start Radius", "Radius at the start endpoint (the one Start Offset moves, marked with a cross gizmo), per local X/Y/Z axis. The component matching Axis is the cap length; the other two shape the cross-section ellipse. Any component left at 0 falls back to Radius.");
    private static readonly GUIContent EndRadiusLabel = new GUIContent("End Radius", "Radius at the end endpoint (the one End Offset moves), per local X/Y/Z axis. Interpolating from Start Radius approximates a tapered capsule. Any component left at 0 falls back to Radius.");
    private static readonly GUIContent StartOffsetLabel = new GUIContent("Start Offset", "Local-space offset applied to the capsule's start point, independent of Height/Axis.");
    private static readonly GUIContent EndOffsetLabel = new GUIContent("End Offset", "Local-space offset applied to the capsule's end point, independent of Height/Axis.");

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
                // No Radius row: a capsule's size lives entirely in the per-end radii below.
                height += (EditorGUIUtility.singleLineHeight + spacing) * 2f; // height, capsuleAxis
                height += EditorGUI.GetPropertyHeight(property.FindPropertyRelative("startRadius")) + spacing;
                height += EditorGUI.GetPropertyHeight(property.FindPropertyRelative("endRadius")) + spacing;
                height += EditorGUI.GetPropertyHeight(property.FindPropertyRelative("startOffset")) + spacing;
                height += EditorGUI.GetPropertyHeight(property.FindPropertyRelative("endOffset")) + spacing;
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
        var startRadiusProp = property.FindPropertyRelative("startRadius");
        var endRadiusProp = property.FindPropertyRelative("endRadius");
        var startOffsetProp = property.FindPropertyRelative("startOffset");
        var endOffsetProp = property.FindPropertyRelative("endOffset");
        var rect = new Rect(position.x, position.y, position.width, EditorGUIUtility.singleLineHeight);

        DrawSingleLine(ref rect, typeProp, null);

        var type = (JiggleCollider.JiggleColliderType)typeProp.enumValueIndex;

        switch (type) {
            case JiggleCollider.JiggleColliderType.Sphere:
                DrawClampedFloat(ref rect, radiusProp, "Radius");
                break;
            case JiggleCollider.JiggleColliderType.Capsule:
                // Radius is not drawn: it only seeds the per-end radii below (and covers colliders serialized
                // before they existed), so showing it would imply a size control that nothing reads.
                DrawClampedFloat(ref rect, heightProp, "Height");
                DrawSingleLine(ref rect, capsuleAxisProp, new GUIContent("Axis"));
                // Start* and End* are kept adjacent so it is obvious which endpoint each field drives.
                SeedUnsetRadius(startRadiusProp, radiusProp);
                DrawMeasured(ref rect, startRadiusProp, StartRadiusLabel);
                DrawMeasured(ref rect, startOffsetProp, StartOffsetLabel);
                SeedUnsetRadius(endRadiusProp, radiusProp);
                DrawMeasured(ref rect, endRadiusProp, EndRadiusLabel);
                DrawMeasured(ref rect, endOffsetProp, EndOffsetLabel);
                break;
            case JiggleCollider.JiggleColliderType.Plane:
                break;
        }

        DrawMeasured(ref rect, positionOffsetProp, CenterLabel);

        EditorGUI.EndProperty();
    }

    // Unity cannot give a serialized struct field a non-zero default, so a capsule authored before the per-end
    // radii existed (or one just added to the array) deserializes as all-zero. The runtime reads that as "use
    // Radius", so show those effective values instead of a misleading zero. Only an all-zero vector is seeded,
    // leaving a deliberately zeroed single component alone.
    private static void SeedUnsetRadius(SerializedProperty prop, SerializedProperty radiusProp) {
        var x = prop.FindPropertyRelative("x");
        var y = prop.FindPropertyRelative("y");
        var z = prop.FindPropertyRelative("z");
        if (x == null || y == null || z == null) {
            return;
        }
        if (x.floatValue != 0f || y.floatValue != 0f || z.floatValue != 0f) {
            return;
        }
        var radius = radiusProp.floatValue;
        x.floatValue = radius;
        y.floatValue = radius;
        z.floatValue = radius;
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
