using System;
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

    // The drawer only receives the JiggleCollider child, but two-bone mode is authored on the wrapping
    // JiggleColliderSerializable's endTransform. Resolve the sibling by rewriting the property path; if the
    // collider is embedded some other way (no ".collider" tail) fall back to "no end transform", so the
    // drawer degrades to showing every field rather than hiding one that is actually read.
    private static bool GetHasEndTransform(SerializedProperty colliderProp) {
        var path = colliderProp.propertyPath;
        if (!path.EndsWith(".collider", StringComparison.Ordinal)) {
            return false;
        }
        var endTransformPath = path.Substring(0, path.Length - ".collider".Length) + ".endTransform";
        var endTransformProp = colliderProp.serializedObject.FindProperty(endTransformPath);
        return endTransformProp != null && endTransformProp.objectReferenceValue != null;
    }

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
                // No Height row in two-bone mode either: the end Transform defines the segment, so Height is
                // not read (see GetWorldCapsuleSegment), same principle as hiding Radius.
                var lineCount = GetHasEndTransform(property) ? 1f : 2f; // (height,) capsuleAxis
                height += (EditorGUIUtility.singleLineHeight + spacing) * lineCount;
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
                if (!GetHasEndTransform(property)) {
                    DrawClampedFloat(ref rect, heightProp, "Height");
                }
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

// Draws the wrapper so endTransform only appears for capsules: on a sphere or plane the field is never read,
// and the precedent here is to hide controls that nothing reads (see the Radius/Height handling above).
[CustomPropertyDrawer(typeof(JiggleColliderSerializable))]
public class JiggleColliderSerializablePropertyDrawer : PropertyDrawer {
    private static readonly GUIContent EndTransformLabel = new GUIContent("End Transform",
        "Capsule only: when set, the capsule runs from this collider's placement (start) to this Transform " +
        "(end), tracking it every frame, and Height is ignored. End Radius / End Offset still apply at that end.");

    private static bool GetIsCapsule(SerializedProperty property) {
        var typeProp = property.FindPropertyRelative("collider").FindPropertyRelative("type");
        return (JiggleCollider.JiggleColliderType)typeProp.enumValueIndex == JiggleCollider.JiggleColliderType.Capsule;
    }

    public override float GetPropertyHeight(SerializedProperty property, GUIContent label) {
        var height = EditorGUIUtility.singleLineHeight;
        if (!property.isExpanded) {
            return height;
        }
        var spacing = EditorGUIUtility.standardVerticalSpacing;
        height += spacing + EditorGUI.GetPropertyHeight(property.FindPropertyRelative("transform"));
        if (GetIsCapsule(property)) {
            height += spacing + EditorGUI.GetPropertyHeight(property.FindPropertyRelative("endTransform"));
        }
        height += spacing + EditorGUI.GetPropertyHeight(property.FindPropertyRelative("collider"));
        return height;
    }

    public override void OnGUI(Rect position, SerializedProperty property, GUIContent label) {
        EditorGUI.BeginProperty(position, label, property);
        var rect = new Rect(position.x, position.y, position.width, EditorGUIUtility.singleLineHeight);
        property.isExpanded = EditorGUI.Foldout(rect, property.isExpanded, label, true);
        if (property.isExpanded) {
            var spacing = EditorGUIUtility.standardVerticalSpacing;
            rect.y += rect.height + spacing;
            EditorGUI.indentLevel++;
            var transformProp = property.FindPropertyRelative("transform");
            rect.height = EditorGUI.GetPropertyHeight(transformProp);
            EditorGUI.PropertyField(rect, transformProp);
            rect.y += rect.height + spacing;
            if (GetIsCapsule(property)) {
                var endTransformProp = property.FindPropertyRelative("endTransform");
                rect.height = EditorGUI.GetPropertyHeight(endTransformProp);
                EditorGUI.PropertyField(rect, endTransformProp, EndTransformLabel);
                rect.y += rect.height + spacing;
            }
            var colliderProp = property.FindPropertyRelative("collider");
            rect.height = EditorGUI.GetPropertyHeight(colliderProp);
            EditorGUI.PropertyField(rect, colliderProp);
            EditorGUI.indentLevel--;
        }
        EditorGUI.EndProperty();
    }
}

}
