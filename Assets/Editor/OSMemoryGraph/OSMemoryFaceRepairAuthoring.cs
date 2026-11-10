using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using MrTGame.EditorTools.OSMemoryGraph;
using MrTGame.Narrative;
using UnityEditor;
using UnityEditor.Events;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace MrTGame.EditorTools
{
    // Explicit Edit Mode authoring only. No InitializeOnLoad or scene mutation.
    public static class OSMemoryFaceRepairAuthoring
    {
        const string Art = "Assets/Art/MemoryUpgrade03/FaceRepair/Runtime/Animation/";
        public const string PrefabPath = "Assets/Prefab/UI/OSMemory/Chapter02Makeup.prefab";
        public const string SequencePath = "Assets/Data/OSMemorySubwindows/Chapter02Makeup.asset";
        const string DataPath = "Assets/Data/OSMemoryData.asset";
        const string CsvPath = "Tools/OSMemoryData.csv";

        [Serializable] sealed class Manifest { public State[] states; public Action[] actions; }
        [Serializable] sealed class Frame { public string delta; public int durationMs; }
        [Serializable] sealed class State { public string id; public string patch; public Frame[] frames; }
        [Serializable] sealed class Action { public string id; public string from; public string to; public Frame[] frames; }

        [MenuItem("Tools/MrTGame/OS Memory/Build Makeup Illustration")]
        public static void Build()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Build the makeup illustration in Edit Mode.");
            var data = AssetDatabase.LoadAssetAtPath<OSMemoryData>(DataPath);
            var template = AssetDatabase.LoadAssetAtPath<OSMemorySubwindowSequence>(
                "Assets/Data/OSMemorySubwindows/Chapter02Breakfast_Offer.asset");
            if (data == null || template == null || !data.TryGetData(6960, out var entry))
                throw new InvalidOperationException("Memory 6960 or the breakfast subwindow template is missing.");
            if (entry.SubwindowSequence != null && AssetDatabase.GetAssetPath(entry.SubwindowSequence) != SequencePath)
                throw new InvalidOperationException("Memory 6960 already owns another illustration; preserve it for review.");
            string originalCsv = File.ReadAllText(CsvPath);
            if (!Regex.IsMatch(originalCsv, @"(?m)^1,node,,\d+,6970,"))
                throw new InvalidOperationException("CSV node 6970 is missing; cannot insert the 6960 action safely.");

            Manifest manifest = JsonUtility.FromJson<Manifest>(File.ReadAllText(Art + "manifest.json"));
            if (manifest?.states == null || manifest.actions == null)
                throw new InvalidOperationException("FaceRepair manifest has no states or actions.");
            var sprites = new Dictionary<string, Sprite>(StringComparer.Ordinal);
            Sprite baseSprite = LoadSprite("../repair_bust.png", sprites);
            foreach (State state in manifest.states)
            {
                LoadSprite(state.patch, sprites);
                ValidateFrames(state.frames, sprites);
            }
            foreach (Action action in manifest.actions)
            {
                if (!manifest.states.Any(state => state.id == action.from)
                    || !manifest.states.Any(state => state.id == action.to))
                    throw new InvalidOperationException("Unknown source or destination for " + action.id);
                ValidateFrames(action.frames, sprites);
            }

            GameObject root = new GameObject("Chapter02Makeup", typeof(RectTransform), typeof(CanvasGroup));
            OSMemoryShotContent prefabContent;
            try
            {
                root.layer = 8;
                RectTransform rect = (RectTransform)root.transform;
                rect.anchorMin = Vector2.zero;
                rect.anchorMax = Vector2.one;
                rect.anchoredPosition = new Vector2(0f, -7.5f);
                rect.sizeDelta = new Vector2(-2f, -17f);
                root.GetComponent<CanvasGroup>().blocksRaycasts = false;
                var content = root.AddComponent<OSMemoryShotContent>();
                Image background = MakeLayer(root.transform, "Base");
                background.sprite = baseSprite;
                Image stateImage = MakeLayer(root.transform, "StatePatch");
                Image frameImage = MakeLayer(root.transform, "FrameDelta");
                var presentation = root.AddComponent<OSMemoryFaceRepairPresentation>();
                var serialized = new SerializedObject(presentation);
                serialized.FindProperty("stateImage").objectReferenceValue = stateImage;
                serialized.FindProperty("frameImage").objectReferenceValue = frameImage;
                SerializedProperty states = serialized.FindProperty("states");
                states.arraySize = manifest.states.Length;
                for (int i = 0; i < manifest.states.Length; i++)
                {
                    State source = manifest.states[i];
                    SerializedProperty target = states.GetArrayElementAtIndex(i);
                    target.FindPropertyRelative("id").stringValue = source.id;
                    target.FindPropertyRelative("patch").objectReferenceValue = sprites[source.patch];
                    SetFrames(target.FindPropertyRelative("frames"), source.frames, sprites);
                }
                SerializedProperty actions = serialized.FindProperty("actions");
                actions.arraySize = manifest.actions.Length;
                for (int i = 0; i < manifest.actions.Length; i++)
                {
                    Action source = manifest.actions[i];
                    SerializedProperty target = actions.GetArrayElementAtIndex(i);
                    target.FindPropertyRelative("id").stringValue = source.id;
                    target.FindPropertyRelative("from").stringValue = source.from;
                    target.FindPropertyRelative("to").stringValue = source.to;
                    SetFrames(target.FindPropertyRelative("frames"), source.frames, sprites);
                }
                serialized.ApplyModifiedPropertiesWithoutUndo();
                UnityEventTools.AddPersistentListener(Event(content, "onWindowOpened"), presentation.PlayOpened);
                UnityEventTools.AddPersistentListener(Event(content, "onReleased"), presentation.Release);
                prefabContent = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath).GetComponent<OSMemoryShotContent>();
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }

            var sequence = AssetDatabase.LoadAssetAtPath<OSMemorySubwindowSequence>(SequencePath);
            if (sequence == null)
            {
                sequence = ScriptableObject.CreateInstance<OSMemorySubwindowSequence>();
                AssetDatabase.CreateAsset(sequence, SequencePath);
            }
            // Copy the established native-size shell, layer placement and close timing.
            EditorUtility.CopySerialized(template, sequence);
            sequence.name = "Chapter02Makeup";
            var sequenceData = new SerializedObject(sequence);
            sequenceData.FindProperty("sequenceId").stringValue = OSMemoryFaceRepairPresentation.SequenceId;
            sequenceData.FindProperty("requiredToolProgram").stringValue = OSToolProgramIds.MemoryRepair03;
            sequenceData.FindProperty("shots").arraySize = 1;
            sequenceData.FindProperty("shots").GetArrayElementAtIndex(0)
                .FindPropertyRelative("contentPrefab").objectReferenceValue = prefabContent;
            sequenceData.FindProperty("continueAtProgress").floatValue = 0f;
            sequenceData.FindProperty("autoAdvance").boolValue = false;
            sequenceData.FindProperty("keepOpenUntilCancelled").boolValue = true;
            sequenceData.ApplyModifiedPropertiesWithoutUndo();
            if (!sequence.Validate(10, out string error)) throw new InvalidOperationException(error);
            AssetDatabase.SaveAssetIfDirty(sequence);

            // Only add/replace the presentation action; prose, options, resources,
            // localization and every other node retain their serialized values.
            var memoryData = new SerializedObject(data);
            SerializedProperty nodes = memoryData.FindProperty("dataList");
            for (int i = 0; i < nodes.arraySize; i++)
            {
                SerializedProperty node = nodes.GetArrayElementAtIndex(i);
                if (node.FindPropertyRelative("dataIndex").intValue != 6960) continue;
                SerializedProperty actions = node.FindPropertyRelative("onEnterActions");
                SerializedProperty target = null;
                for (int j = 0; j < actions.arraySize; j++)
                    if (actions.GetArrayElementAtIndex(j).FindPropertyRelative("actionId").stringValue
                        == OSMemoryActionIds.PlaySubwindowsAfterText) target = actions.GetArrayElementAtIndex(j);
                if (target == null)
                {
                    actions.arraySize++;
                    target = actions.GetArrayElementAtIndex(actions.arraySize - 1);
                }
                target.FindPropertyRelative("actionId").stringValue = OSMemoryActionIds.PlaySubwindowsAfterText;
                target.FindPropertyRelative("parameter").stringValue = string.Empty;
                target.FindPropertyRelative("asset").objectReferenceValue = sequence;
                break;
            }
            memoryData.ApplyModifiedPropertiesWithoutUndo();
            AssetDatabase.SaveAssetIfDirty(data);

            // Keep the source CSV in sync without rewriting unrelated quoted prose.
            string actionRow = OSMemoryCsvUtility.Serialize(data).Split('\n').Single(line =>
                line.StartsWith("1,action,,,6960,", StringComparison.Ordinal)
                && line.Contains("," + OSMemoryActionIds.PlaySubwindowsAfterText + ",")).TrimEnd('\r');
            string updatedCsv = Regex.Replace(originalCsv,
                @"(?m)^1,action,,,6960,[^\r\n]*,memory\.play_subwindows_after_text,[^\r\n]*(?:\r?\n|$)", string.Empty);
            Match insert = Regex.Match(updatedCsv, @"(?m)^1,node,,\d+,6970,");
            string newline = originalCsv.Contains("\r\n") ? "\r\n" : "\n";
            updatedCsv = updatedCsv.Insert(insert.Index, actionRow + newline);
            byte[] originalBytes = File.ReadAllBytes(CsvPath);
            bool hasBom = originalBytes.Length >= 3 && originalBytes[0] == 0xef && originalBytes[1] == 0xbb && originalBytes[2] == 0xbf;
            File.WriteAllText(CsvPath, updatedCsv, new UTF8Encoding(hasBom));
            Debug.Log("[Memory Makeup] Bound native animation to 6960–7240; record 7010 → 7040 for brows and tilt.");
        }

        static Sprite LoadSprite(string relative, Dictionary<string, Sprite> sprites)
        {
            if (sprites.TryGetValue(relative, out Sprite existing)) return existing;
            string path = Path.GetFullPath(Art + relative).Replace('\\', '/');
            string project = Path.GetFullPath(".").Replace('\\', '/') + "/";
            path = path.Substring(project.Length);
            Sprite sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
            if (sprite == null || sprite.rect.width != 216f || sprite.rect.height != 162f)
                throw new InvalidOperationException("Missing native 216×162 sprite: " + path);
            sprites.Add(relative, sprite);
            return sprite;
        }

        static void ValidateFrames(Frame[] frames, Dictionary<string, Sprite> sprites)
        {
            if (frames == null || frames.Length == 0) throw new InvalidOperationException("Empty FaceRepair clip.");
            foreach (Frame frame in frames)
            {
                if (frame.durationMs <= 0) throw new InvalidOperationException("Non-positive FaceRepair duration.");
                LoadSprite(frame.delta, sprites);
            }
        }

        static void SetFrames(SerializedProperty target, Frame[] frames, Dictionary<string, Sprite> sprites)
        {
            target.arraySize = frames.Length;
            for (int i = 0; i < frames.Length; i++)
            {
                SerializedProperty frame = target.GetArrayElementAtIndex(i);
                frame.FindPropertyRelative("delta").objectReferenceValue = sprites[frames[i].delta];
                frame.FindPropertyRelative("seconds").floatValue = frames[i].durationMs / 1000f;
            }
        }

        static Image MakeLayer(Transform parent, string name)
        {
            var gameObject = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            gameObject.layer = 8;
            gameObject.transform.SetParent(parent, false);
            ((RectTransform)gameObject.transform).sizeDelta = new Vector2(216f, 162f);
            Image image = gameObject.GetComponent<Image>();
            image.raycastTarget = false;
            image.enabled = name == "Base";
            return image;
        }

        static UnityEvent Event(OSMemoryShotContent content, string name) =>
            (UnityEvent)typeof(OSMemoryShotContent).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(content);
    }
}
