using System.Collections.Generic;
using System.Linq;
using Unity.Editor;
using Unity.Editor.Bridge;
using Unity.Entities;
using UnityEditor;
using UnityEngine;

namespace Unity.DOTS.Editor
{
    [InitializeOnLoad]
    class EntityConversionHeader
    {
        static EntityConversionHeader()
        {
            UnityEditor.Editor.finishedDefaultHeaderGUI += DisplayConvertToEntityHeaderCallBack;
        }

        static class ConvertToEntityHeaderTextStrings
        {
            public const string ConvertToEntity = "ConvertToEntity";
            public const string ConvertByAncestor = "(by ancestor)";
            public const string ConvertByScene = "(by scene)";
            public const string StopConvertToEntityInHierarchy = "(" + nameof(StopConvertToEntity) + " in hierarchy)";
            public const string ConvertAndInjectInParents = "(ConvertAndInject mode in parents)";
        }

        static void DisplayConvertToEntityHeaderCallBack(UnityEditor.Editor editor)
        {
            var selectedGameObject = editor.target as GameObject;

            if (selectedGameObject == null)
                return;

            using (new EditorGUILayout.HorizontalScope(EditorStyles.largeLabel))
            {
                EditorGUILayout.LabelField(EditorGUIUtility.TrTextContentWithIcon(ConvertToEntityHeaderTextStrings.ConvertToEntity, Icons.Convert), EditorStyles.label, GUILayout.MaxWidth(130));

                // Multi-selection
                List<GameObject> TargetsList = new List<GameObject>();
                TargetsList.Clear();
                TargetsList.AddRange(editor.targets.OfType<GameObject>());

                if (TargetsList.Count > 1)
                {
                    using (new EditorGUI.DisabledGroupScope(true))
                    {
                        EditorGUILayout.ToggleLeft(EditorGUIBridge.mixedValueContent, false);
                    }
                    return;
                }

                var conversionStatus = GameObjectConversionEditorUtility.GetGameObjectConversionResultStatus(selectedGameObject);
                using (new EditorGUI.DisabledGroupScope(true))
                {
                    switch (conversionStatus)
                    {
                        case GameObjectConversionResultStatus.ConvertedBySubScene:
                            {
                                EditorGUILayout.ToggleLeft(EditorGUIUtility.TrTempContent(ConvertToEntityHeaderTextStrings.ConvertByScene), true);
                            }
                            return;

                        case GameObjectConversionResultStatus.NotConvertedByStopConvertToEntityComponent:
                            {
                                EditorGUILayout.ToggleLeft(EditorGUIUtility.TrTempContent(ConvertToEntityHeaderTextStrings.StopConvertToEntityInHierarchy), false);
                            }
                            return;

                        case GameObjectConversionResultStatus.NotConvertedByConvertAndInjectMode:
                            {
                                EditorGUILayout.ToggleLeft(EditorGUIUtility.TrTempContent(ConvertToEntityHeaderTextStrings.ConvertAndInjectInParents), false);
                            }
                            return;

                        case GameObjectConversionResultStatus.ConvertedByAncestor:
                            {
                                EditorGUILayout.ToggleLeft(EditorGUIUtility.TrTempContent(ConvertToEntityHeaderTextStrings.ConvertByAncestor), true);
                            }
                            return;
                    }
                }

                // Converted by ConvertToEntity.
                using (var changeScope = new EditorGUI.ChangeCheckScope())
                {
                    EditorGUILayout.ToggleLeft(EditorGUIUtility.TrTempContent(""), GameObjectConversionEditorUtility.IsConverted(GameObjectConversionEditorUtility.GetGameObjectConversionResultStatus(selectedGameObject)));
                    if (changeScope.changed)
                    {
                        if (selectedGameObject.GetComponent<ConvertToEntity>() == null)
                        {
                            Undo.AddComponent<ConvertToEntity>(selectedGameObject);
                        }
                        else
                        {
                            Undo.DestroyObjectImmediate(selectedGameObject.GetComponent<ConvertToEntity>());
                        }
                    }
                }
            }
        }
    }
}