using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using MrTGame.Core;
using UnityEngine;
using UnityEngine.EventSystems;

namespace MrTGame.EditorTools
{
    // Run in Play Mode. Uses an isolated SaveData; never writes a player save file.
    public static class OSMemoryWindowLayoutQA
    {
        const string Output = "Temp/MemoryWindowLayout";
        static readonly FieldInfo SaveField = typeof(GameDataManager).GetField("saveData", BindingFlags.Instance | BindingFlags.NonPublic);
        static readonly List<string> Checks = new List<string>();
        static OSMemoryWindowController memory;
        static OSWindowUnit window;
        static OSMemoryPicController picture;
        public static string Status { get; private set; } = "idle";

        public static string Start()
        {
            if (!Application.isPlaying || GameDataManager.Instance == null) throw new InvalidOperationException("Play Mode required.");
            if (Status == "running") return Status;
            GameAutosaveService.Instance?.EnterTestSession();
            Status = "running";
            Checks.Clear();
            Directory.CreateDirectory(Output);
            GameDataManager.Instance.StartCoroutine(Run());
            return Status;
        }

        static void Check(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException("[Memory Layout QA] " + message);
            Checks.Add(message);
        }

        static bool Near(Vector2 first, Vector2 second) => (first - second).sqrMagnitude < .01f;
        static bool Matches(OSWindowLayoutSaveData layout) => Near(window.WindowRect.anchoredPosition, layout.anchoredPosition)
            && Near(window.WindowRect.rect.size, layout.size);

        static IEnumerator Wait(Func<bool> condition, string message)
        {
            float deadline = Time.realtimeSinceStartup + 25f;
            while (!condition() && Time.realtimeSinceStartup < deadline) yield return null;
            Check(condition(), message);
        }

        static IEnumerator Open(bool interactive)
        {
            Check(OS_Handle.Instance.OpenMemoryWindow(), "Memory opens");
            yield return Wait(() => window.IsOpen && !window.IsOpening && !window.IsClosing, "opening animation completes");
            if (interactive) yield return Wait(() => picture.CanInteract, "interactive picture ready");
        }

        static void CheckSaved(OSWindowLayoutSaveData expected, string context)
        {
            OS_Handle.Instance.CaptureWindowLayouts();
            OSWindowLayoutSaveData actual;
            Check(GameDataManager.Instance.TryGetOSWindowLayout(window.WindowId, out actual)
                && Near(actual.anchoredPosition, expected.anchoredPosition) && Near(actual.size, expected.size), context);
        }

        static void Drag(bool resize, Vector2 delta)
        {
            var rect = window.WindowRect;
            var canvas = window.GetComponentInParent<Canvas>();
            var raycaster = canvas.GetComponent<UnityEngine.UI.GraphicRaycaster>();
            var dragArea = (RectTransform)typeof(OSWindowUnit).GetField("dragArea", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(window);
            Vector3 world = resize ? rect.TransformPoint(new Vector2(rect.rect.xMax - 1f, rect.rect.center.y))
                : dragArea.TransformPoint(dragArea.rect.center);
            Vector2 start = RectTransformUtility.WorldToScreenPoint(canvas.worldCamera, world);
            Vector2 end = RectTransformUtility.WorldToScreenPoint(canvas.worldCamera,
                world + rect.parent.TransformVector(delta));
            var hit = new RaycastResult { gameObject = resize ? window.gameObject : dragArea.gameObject, module = raycaster };
            var pointer = new PointerEventData(EventSystem.current)
            {
                button = PointerEventData.InputButton.Left, position = start, pressPosition = start,
                pointerCurrentRaycast = hit, pointerPressRaycast = hit, rawPointerPress = hit.gameObject
            };
            window.OnPointerDown(pointer);
            window.OnBeginDrag(pointer);
            pointer.position = end;
            pointer.delta = end - start;
            window.OnDrag(pointer);
            window.OnEndDrag(pointer);
            window.OnPointerUp(pointer);
        }

        static IEnumerator Shot(string name)
        {
            yield return new WaitForEndOfFrame();
            var image = ScreenCapture.CaptureScreenshotAsTexture();
            File.WriteAllBytes(Output + "/" + name + ".png", image.EncodeToPNG());
            UnityEngine.Object.Destroy(image);
        }

        static IEnumerator Run()
        {
            var manager = GameDataManager.Instance;
            GameSaveData original = manager.SaveData;
            bool background = Application.runInBackground;
            bool passed = false;
            Application.runInBackground = true;
            try
            {
                SaveField.SetValue(manager, JsonUtility.FromJson<GameSaveData>(JsonUtility.ToJson(original)));
                var process = manager.OSProcessData;
                process.memory = new OSMemoryProcessSaveData { currentDataIndex = 2660 };
                process.currentChapterIndex = 1;
                process.toolUnlocked = true;
                process.chapter01KeyXAcquired = true;
                process.installedToolProgramIds.Remove("memory-repair-2");
                if (!process.installedToolProgramIds.Contains("memory-repair-1")) process.installedToolProgramIds.Add("memory-repair-1");
                manager.SetOSResourceValue(ResourceType.Battery, 100);
                GameTest.Instance.JumpToMemoryIndex(2660);
                yield return Wait(() => OS_Handle.Instance != null && OS_Handle.Instance.MemoryWindowController?.CurrentData?.DataIndex == 2660,
                    "ordinary Memory is available");
                memory = OS_Handle.Instance.MemoryWindowController;
                window = memory.GetComponent<OSWindowUnit>();
                picture = memory.GetComponentInChildren<OSMemoryPicController>(true);
                yield return Wait(() => !window.IsOpening, "ordinary opening finishes");
                foreach (var other in UnityEngine.Object.FindObjectsByType<OSWindowUnit>())
                    if (other.IsOpen && other != window) other.CloseSilently();
                window.ApplyLayout(new OSWindowLayoutSaveData
                {
                    windowId = window.WindowId, anchoredPosition = new Vector2(-63, 17), size = new Vector2(310, 247), siblingIndex = window.transform.GetSiblingIndex()
                });
                var ordinary = window.CaptureLayout();
                Check(!window.HasTemporaryOpeningLayout && Matches(ordinary), "ordinary player layout applied");
                CheckSaved(ordinary, "ordinary layout recorded before upgrade");
                yield return Shot("01-ordinary");

                memory.ShowData(2670);
                Check(manager.TryInstallToolProgram("memory-repair-2") == OSToolInstallResult.Success, "second Memory upgrade installs through real installation path");
                Check(!window.IsOpen, "upgrade closes Memory before its new layout");
                CheckSaved(ordinary, "installation does not replace the ordinary layout");
                yield return Open(false);
                foreach (var text in memory.TextUnits) if (text.IsPlaying) text.CompleteImmediately();
                yield return Wait(() => picture.CanInteract, "upgrade automatically enters the picture scenario");
                Check(window.HasTemporaryOpeningLayout && Near(window.WindowRect.rect.size, new Vector2(175, 280))
                    && Near(window.WindowRect.anchoredPosition, new Vector2(-222, 12)), "first entry uses fixed picture composition");
                Vector2 lockedSize = window.WindowRect.rect.size;
                Drag(true, new Vector2(31, 0));
                Check(Near(window.WindowRect.rect.size, lockedSize), "picture mode blocks edge resize");
                Vector2 beforeMove = window.WindowRect.anchoredPosition;
                Drag(false, new Vector2(21, -9));
                Vector2 moved = window.WindowRect.anchoredPosition;
                Check(!Near(beforeMove, moved) && Near(window.WindowRect.rect.size, lockedSize), "picture mode allows moving with fixed size");
                CheckSaved(ordinary, "moving picture mode preserves ordinary saved geometry");
                yield return Shot("02-interactive-moved");

                window.Minimize();
                window.RestoreAndBringToFront();
                yield return null;
                Check(Near(window.WindowRect.anchoredPosition, moved), "minimize/restore preserves temporary position");
                window.CloseSilently();
                yield return Open(true);
                Check(Near(window.WindowRect.anchoredPosition, moved), "close/reopen preserves temporary position");
                Check(window.IsAlwaysOnTop == manager.OSProcessData.memory.interactiveWindowPinned, "reopen preserves the picture pin preference");
                CheckSaved(ordinary, "reopening picture mode keeps ordinary saved geometry");

                // Save while playing, then simulate rebuilding the window after loading that save.
                string saved = JsonUtility.ToJson(manager.SaveData);
                window.CloseSilently();
                memory.PrepareRuntime();
                SaveField.SetValue(manager, JsonUtility.FromJson<GameSaveData>(saved));
                OS_Handle.Instance.RestoreWindowLayouts();
                yield return Open(true);
                CheckSaved(ordinary, "save/load during picture mode retains the player's previous layout");
                Check(picture.ExitScenario(2660), "picture scenario exits to ordinary Memory");
                Check(!window.HasTemporaryOpeningLayout && Matches(ordinary) && !picture.pictureWindow.IsOpen,
                    "exit restores ordinary geometry immediately and closes the picture");
                yield return Shot("03-restored");
                window.CloseSilently();
                manager.SetCurrentChapterIndex(2);
                yield return Open(false);
                Check(Matches(ordinary) && !window.HasTemporaryOpeningLayout, "later chapter reopens the original player layout");
                Vector2 beforeResize = window.WindowRect.rect.size;
                Drag(true, new Vector2(23, 0));
                Check(window.WindowRect.rect.width > beforeResize.x + 20f, "ordinary resize is restored");
                ordinary = window.CaptureLayout();
                CheckSaved(ordinary, "new ordinary resize continues saving normally");

                // A chapter transition can occur before a new Memory node is assigned.
                manager.SetCurrentChapterIndex(1);
                memory.ShowData(2680);
                yield return Wait(() => picture.CanInteract, "picture mode can be entered again for chapter-boundary coverage");
                manager.SetCurrentChapterIndex(2);
                yield return null;
                Check(!manager.IsMemoryInteractiveSceneActive && !window.HasTemporaryOpeningLayout && Matches(ordinary),
                    "chapter advance restores layout even with the old picture cursor");
                manager.SetCurrentChapterIndex(1);
                memory.ShowData(2680);
                yield return Wait(() => picture.CanInteract, "picture ready before closed chapter change");
                window.CloseSilently();
                manager.SetCurrentChapterIndex(2);
                yield return Open(false);
                Check(Matches(ordinary) && !manager.IsMemoryInteractiveSceneActive, "chapter advance while closed restores before reopening");

                // Leaving the story during an open tween must not let that tween overwrite restoration.
                manager.SetCurrentChapterIndex(1);
                memory.ShowData(2680);
                yield return Wait(() => picture.CanInteract, "picture ready before interrupted opening");
                window.CloseSilently();
                OS_Handle.Instance.OpenMemoryWindow();
                Check(window.IsOpening, "opening tween is active for transition coverage");
                memory.ShowData(2660);
                yield return Wait(() => !window.IsOpening, "interrupted story opening finishes");
                yield return null;
                Check(Matches(ordinary) && !window.HasTemporaryOpeningLayout, "mode exit during opening restores the full original rect");
                CheckSaved(ordinary, "all lifecycle transitions preserve the latest ordinary layout");
                passed = true;
            }
            finally
            {
                // Restore runtime UI while still bound to the isolated save, then the real save reference.
                if (window != null) window.CloseSilently();
                if (memory != null) memory.PrepareRuntime();
                SaveField.SetValue(manager, original);
                OS_Handle.Instance?.RestoreWindowLayouts();
                Application.runInBackground = background;
                Status = passed ? "passed" : "failed";
                File.WriteAllText(Output + "/result.json", Newtonsoft.Json.JsonConvert.SerializeObject(new
                { status = Status, checks = Checks, saveRestored = ReferenceEquals(manager.SaveData, original) }, Newtonsoft.Json.Formatting.Indented));
            }
        }
    }
}
