#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace GatorDragonGames.JigglePhysics {

[CustomPropertyDrawer(typeof(JiggleRigData))]
public class JiggleRigDataPropertyDrawer : PropertyDrawer {
    public override VisualElement CreatePropertyGUI(SerializedProperty property) {
        var visualTreeAsset =
            AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(
                AssetDatabase.GUIDToAssetPath("3b91b5cf6b975bd4d83d8a940258c420"));
        var visualElement = new VisualElement();
        visualTreeAsset.CloneTree(visualElement);

        var rootElement = visualElement.Q<ObjectField>("RootField");
        rootElement.objectType = typeof(Transform);
        var rootProp = property.FindPropertyRelative(nameof(JiggleRigData.rootBone));
        rootElement.BindProperty(rootProp);
        rootElement.Q<Label>().text = "Root Transform";

        var excludeRootToggleElement = visualElement.Q<Toggle>("ExcludeRootToggle");
        excludeRootToggleElement.BindProperty(property.FindPropertyRelative(nameof(JiggleRigData.excludeRoot)));
        excludeRootToggleElement.Q<Label>().text = "Motionless Root";
        excludeRootToggleElement.tooltip =
            "Exclude the root from the jiggle simulation. Use this to coalesce many branching jiggles.";

        var excludedTransformsElement = visualElement.Q<PropertyField>("IgnoredTransformsField");
        excludedTransformsElement.BindProperty(property.FindPropertyRelative(nameof(JiggleRigData.excludedTransforms)));

        var personalCollidersElement = visualElement.Q<PropertyField>("PersonalCollidersField");
        personalCollidersElement.BindProperty(property.FindPropertyRelative(nameof(JiggleRigData.jiggleColliderObjects)));
        // Keep the label the docs and muscle memory know, rather than the field name's "Jiggle Collider Objects".
        personalCollidersElement.label = "Jiggle Colliders";

        var container = visualElement.Q<VisualElement>("Contents");
        //var rig = (JiggleRigData)property.boxedValue;

        var inputParams = visualElement.Q<PropertyField>("JiggleTreeInputParameters");
        inputParams.BindProperty(property.FindPropertyRelative(nameof(JiggleRigData.jiggleTreeInputParameters)));
        container.Add(inputParams);

        var rootSection = visualElement.Q<VisualElement>("RootSection");
        excludeRootToggleElement.RegisterValueChangedCallback(evt => {
            if (evt == null || rootSection == null) {
                return;
            }
            rootSection.style.display = evt.newValue ? DisplayStyle.None : DisplayStyle.Flex;
        });

        var recursiveWarning = visualElement.Q<VisualElement>("PersonalColliderWarning");
        if (!property.serializedObject.isEditingMultipleObjects && Application.isPlaying) {
            var rootBone = (Transform)rootProp.objectReferenceValue;
            var isRecursiveRig = false;
#if UNITY_2023_1_OR_NEWER
            var otherRigs = Object.FindObjectsByType<JiggleRig>(FindObjectsInactive.Exclude,
                                                               FindObjectsSortMode.None);
#else
            // FindObjectsByType は Unity 2023.1 で追加された。2022.3 でも使えるよう旧APIへ分岐する
            // (FindObjectsOfType(false) は非アクティブを除外＝FindObjectsInactive.Exclude と同義。
            //  ソート順は全件走査して条件一致を探すだけなので依存しない)。
            var otherRigs = Object.FindObjectsOfType<JiggleRig>(false);
#endif
            foreach (JiggleRig otherRig in otherRigs) {
                var otherRoot = otherRig.GetJiggleRigData().rootBone;
                if (rootBone && rootBone != otherRoot && rootBone.IsChildOf(otherRoot)) {
                    isRecursiveRig = true;
                    break;
                }
            }

            if (isRecursiveRig) {
                recursiveWarning.style.display = DisplayStyle.Flex;
                var warningText =
                    "Recursive rigs ignore colliders as they get merged with their parent rig. If colliders are needed, add them to the parent rig instead.";
                HelpBox helpBox = new HelpBox(warningText, HelpBoxMessageType.Warning);
                recursiveWarning.Add(helpBox);
            }
            else {
                recursiveWarning.style.display = DisplayStyle.None;
            }
        }
        else {
            recursiveWarning.style.display = DisplayStyle.None;
        }

        return visualElement;
    }

    public override void OnGUI(Rect position, SerializedProperty property, GUIContent label) {
        EditorGUI.LabelField(position, "Jiggle Physics doesn't support IMGUI inspectors, sorry!");
    }
}

}

#endif