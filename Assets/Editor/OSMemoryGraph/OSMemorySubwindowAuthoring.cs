using System;
using System.Collections.Generic;
using System.IO;
using MrTGame.Narrative;
using TMPro;
using UnityEditor;
using UnityEngine;

namespace MrTGame.EditorTools
{
    // Capture only authoring state. Playback never reads scene preview objects.
    public static class OSMemorySubwindowAuthoring
    {
        public static OSMemorySubwindowSequence Capture(
            RectTransform preview, string sequenceId, string sequencePath, string contentFolder)
        {
            if (Application.isPlaying || preview == null)
                throw new InvalidOperationException("Capture needs a preview group in Edit Mode.");
            var sources = new List<RectTransform>();
            foreach (Transform child in preview)
            {
                if (child.gameObject.activeSelf && child.GetComponent<OSWindowUnit>() != null)
                    sources.Add((RectTransform)child);
            }
            sources.Sort((left, right) => string.CompareOrdinal(left.name, right.name));
            if (sources.Count == 0 || sources.Count > 10)
                throw new InvalidOperationException("Select between 1 and 10 active preview windows.");
            foreach (RectTransform source in sources)
                if (source.Find("ImageRectObj") == null
                    || source.localScale != Vector3.one || source.localRotation != Quaternion.identity)
                    throw new InvalidOperationException(source.name + " needs ImageRectObj and an unscaled/unrotated window root.");

            EnsureFolder(Path.GetDirectoryName(sequencePath).Replace('\\', '/'));
            EnsureFolder(contentFolder);
            var sequence = AssetDatabase.LoadAssetAtPath<OSMemorySubwindowSequence>(sequencePath);
            if (sequence == null)
            {
                sequence = ScriptableObject.CreateInstance<OSMemorySubwindowSequence>();
                AssetDatabase.CreateAsset(sequence, sequencePath);
            }
            Undo.RecordObject(sequence, "Capture Memory Subwindows");
            var serialized = new SerializedObject(sequence);
            serialized.FindProperty("sequenceId").stringValue = sequenceId;
            serialized.FindProperty("referenceSize").vector2Value = preview.rect.size;
            SerializedProperty shots = serialized.FindProperty("shots");
            shots.arraySize = sources.Count;
            for (int i = 0; i < sources.Count; i++)
            {
                RectTransform source = sources[i];
                Transform sourceContent = source.Find("ImageRectObj");
                string keyPrefix = "os.memory.subwindows." + sequenceId + ".shot."
                    + (i + 1).ToString("00");
                int textIndex = 0;
                foreach (TMP_Text text in sourceContent.GetComponentsInChildren<TMP_Text>(true))
                {
                    OSLocalizedText localized = text.GetComponent<OSLocalizedText>();
                    if (localized == null)
                        localized = Undo.AddComponent<OSLocalizedText>(text.gameObject);
                    if (string.IsNullOrEmpty(localized.LocalizationKey) && !string.IsNullOrEmpty(text.text))
                    {
                        var textData = new SerializedObject(localized);
                        textData.FindProperty("localizationKey").stringValue = keyPrefix + ".text." + textIndex.ToString("00");
                        textData.FindProperty("fallbackText").stringValue = text.text;
                        textData.FindProperty("targetText").objectReferenceValue = text;
                        textData.ApplyModifiedProperties();
                    }
                    textIndex++;
                }

                GameObject temporary = UnityEngine.Object.Instantiate(sourceContent.gameObject);
                temporary.name = "ImageRectObj";
                try
                {
                    // Cloning with no parent changes RectTransform geometry; copy it explicitly.
                    var originalRect = (RectTransform)sourceContent;
                    var contentRect = temporary.GetComponent<RectTransform>();
                    contentRect.anchorMin = originalRect.anchorMin;
                    contentRect.anchorMax = originalRect.anchorMax;
                    contentRect.pivot = originalRect.pivot;
                    contentRect.sizeDelta = originalRect.sizeDelta;
                    contentRect.anchoredPosition = originalRect.anchoredPosition;
                    contentRect.localScale = originalRect.localScale;
                    contentRect.localRotation = originalRect.localRotation;
                    temporary.SetActive(true);
                    var group = temporary.GetComponent<CanvasGroup>();
                    if (group != null) group.alpha = 1f;
                    var content = temporary.GetComponent<OSMemoryShotContent>();
                    if (content == null) content = temporary.AddComponent<OSMemoryShotContent>();
                    var contentData = new SerializedObject(content);
                    contentData.FindProperty("floatingCaption").objectReferenceValue =
                        temporary.transform.Find("TMP")?.GetComponent<TMP_Text>();
                    contentData.ApplyModifiedPropertiesWithoutUndo();
                    string prefabPath = contentFolder + "/Subwindows_" + sequenceId + "_Shot"
                        + (i + 1).ToString("00") + ".prefab";
                    GameObject prefab = PrefabUtility.SaveAsPrefabAsset(temporary, prefabPath);
                    SerializedProperty shot = shots.GetArrayElementAtIndex(i);
                    shot.FindPropertyRelative("contentPrefab").objectReferenceValue = prefab.GetComponent<OSMemoryShotContent>();
                    Vector3 center = preview.InverseTransformPoint(source.TransformPoint(source.rect.center));
                    shot.FindPropertyRelative("position").vector2Value = (Vector2)center - preview.rect.center;
                    shot.FindPropertyRelative("size").vector2Value = source.rect.size;
                }
                finally { UnityEngine.Object.DestroyImmediate(temporary); }
            }
            serialized.ApplyModifiedProperties();
            EditorUtility.SetDirty(sequence);
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(preview.gameObject.scene);
            AssetDatabase.SaveAssets();
            return sequence;
        }

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }
    }
}
