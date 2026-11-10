using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using DG.Tweening;
using MrTGame.Core;
using MrTGame.Systems;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Focused regression for the night light field. Uses real scene windows and
// production Paint; it does not replay the chapter or change formal saves.
public static class OSChapter02NightfallOcclusionQA
{
    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    static readonly List<string> checks = new List<string>();
    static readonly List<object> samples = new List<object>();
    static OS_Handle os;
    static OSChapter02FinalTalkDirector talk;
    static OSChapter02NightfallPresentation night;
    static OSLinkMessageController link;
    static OSWindowUnit live, folder;
    static GameObject cursorOwner, nestedRoot, redPointer;
    static bool redWasActive;
    static string context;
    public static string Status { get; private set; } = "idle";
    public static string OutputDirectory { get; private set; }

    public static string Start()
    {
        var saves = GameAutosaveService.Instance;
        if (Status == "running") return Status;
        if (!Application.isPlaying || saves == null || GameDataManager.Instance == null
            || GameManager.Instance == null || GameSessionFlow.Instance == null
            || SceneLoadManager.Instance == null || GameTest.Instance == null)
            return "Play Mode initialization required";
        if (saves.IsBusy || saves.IsWriting || GameManager.Instance.IsTransitioning
            || SceneLoadManager.Instance.IsBusy || GameTest.Instance.IsCheckpointJumpRunning)
            return "Wait for active save/scene operation";
        checks.Clear(); samples.Clear(); context = "initialization";
        OutputDirectory = Path.GetFullPath(Path.Combine("Temp", "OSChapter02NightfallOcclusionQA", Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(OutputDirectory);
        Status = "running";
        GameDataManager.Instance.StartCoroutine(Guarded());
        return Status;
    }

    static IEnumerator Guarded()
    {
        var data = GameDataManager.Instance;
        var saves = GameAutosaveService.Instance;
        var original = data.SaveData;
        var language = data.CurrentLanguageState;
        var resources = GameResourceSystem.Instance;
        bool resourcesEnabled = resources != null && resources.enabled;
        bool background = Application.runInBackground;
        string[] paths = { saves.SavePath, saves.SavePath + ".bak", saves.SavePath + ".tmp" };
        string[] hashes = null;
        var stack = new Stack<IEnumerator>();
        try
        {
            saves.EnterTestSession(); hashes = paths.Select(Hash).ToArray();
            saves.UseIsolatedStorageForTests(OutputDirectory); saves.EnterTestSession();
            data.ReplaceSaveData(Clone(original));
            Application.runInBackground = true;
            if (resources != null) resources.enabled = false;
            stack.Push(Run());
            while (stack.Count > 0)
            {
                object next;
                try
                {
                    if (!stack.Peek().MoveNext()) { (stack.Pop() as IDisposable)?.Dispose(); continue; }
                    next = stack.Peek().Current;
                }
                catch (Exception error)
                {
                    Status = "FAILED at " + context + "\n" + error;
                    Debug.LogError("[Nightfall Occlusion QA] " + Status);
                    yield break;
                }
                if (next is IEnumerator child) stack.Push(child); else yield return next;
            }
            Check(paths.Select(Hash).SequenceEqual(hashes), "formal save hashes remain unchanged");
            Status = "PASS: " + checks.Count + " checks, " + samples.Count + " light-field samples";
        }
        finally
        {
            while (stack.Count > 0) (stack.Pop() as IDisposable)?.Dispose();
            try
            {
                if (folder != null && night != null) night.RemoveGlow(folder.WindowRect);
                if (cursorOwner != null) CursorHandle.Instance?.ReleaseScriptControlWithoutWarp(cursorOwner);
                if (redPointer != null) redPointer.SetActive(redWasActive);
                if (nestedRoot != null) { nestedRoot.SetActive(false); UnityEngine.Object.Destroy(nestedRoot); }
                if (cursorOwner != null) UnityEngine.Object.Destroy(cursorOwner);
                talk?.ResetPresentation(); night?.ResetPresentation();
                GameTest.Instance?.GetComponent<GameTestConsole>()?.SetOpen(false);
                saves.EnterTestSession(); saves.EndSession();
                GameManager.Instance?.OpenMainMenuWithoutSave();
                data.ReplaceSaveData(original); data.SetCurrentLanguage(language);
                if (resources != null) resources.enabled = resourcesEnabled;
                Application.runInBackground = background;
                if (!ReferenceEquals(data.SaveData, original) || data.CurrentLanguageState != language
                    || hashes != null && !paths.Select(Hash).SequenceEqual(hashes))
                    throw new InvalidOperationException("Original memory/language/save files were not restored");
                checks.Add("cleanup restored original memory/language/resources and left test-session writes disabled");
            }
            catch (Exception error) { Status = "FAILED during cleanup: " + error + "\n" + Status; }
            if (Status == "running") Status = "FAILED: interrupted at " + context;
            File.WriteAllText(Path.Combine(OutputDirectory, "results.json"), Newtonsoft.Json.JsonConvert.SerializeObject(new {
                status = Status, context, checks, samples, originalFiles = paths, originalHashes = hashes,
                finalHashes = paths.Select(Hash).ToArray(), testSession = saves.IsTestSession,
                note = "Samples use production Paint and real window geometry/sibling order. Screenshot review is separate."
            }, Newtonsoft.Json.Formatting.Indented));
            File.WriteAllText(Path.Combine(OutputDirectory, "results.txt"), Status + "\n" + string.Join("\n", checks));
            Debug.Log("[Nightfall Occlusion QA] " + Status + " | " + OutputDirectory);
            os = null; talk = null; night = null; link = null; live = folder = null;
            cursorOwner = nestedRoot = redPointer = null;
        }
    }

    static IEnumerator Run()
    {
        context = "night checkpoint";
        Check(GameTest.Instance.JumpToCheckpoint(GameTestCheckpoint.Chapter02FinalTalk), "checkpoint accepted");
        yield return Wait(() => !GameTest.Instance.IsCheckpointJumpRunning && !GameManager.Instance.IsTransitioning
            && !SceneLoadManager.Instance.IsBusy && OS_Handle.Instance != null, "checkpoint ready", 80f);
        Check(GameTest.Instance.JumpSucceeded && GameAutosaveService.Instance.IsTestSession, "checkpoint stays isolated");
        os = OS_Handle.Instance; talk = os.Chapter02FinalTalk; night = talk.Nightfall;
        link = os.LinkMessageController; live = os.CharacterWindowController.GetComponent<OSWindowUnit>();
        folder = os.transform.Find("Windows/WindowUnit_Folder")?.GetComponent<OSWindowUnit>();
        Check(folder != null && folder.transform.parent == live.transform.parent, "real Folder and Live windows resolve");
        GameTest.Instance.GetComponent<GameTestConsole>()?.SetOpen(false);
        talk.ResetPresentation(); link.NotifyOSHidden();
        var fixture = Clone(GameDataManager.Instance.SaveData);
        OSChapter02FinalTalkDirector.PrepareDebugCheckpoint(fixture.osProcess.memory, fixture.osProcess.energyLoop, 1);
        fixture.osProcess.memory.chapter02FinalTalkStep = (int)OSChapter02FinalTalkDirector.FinalTalkStep.Night;
        fixture.osProcess.characterInteraction = new OSCharacterInteractionSaveData();
        GameDataManager.Instance.ReplaceSaveData(fixture);
        night.ResetPresentation(); Focus();
        foreach (var window in os.GetComponentsInChildren<OSWindowUnit>(true))
            if (window != live && window != link.LinkWindow) window.CloseFromScript();
        os.OpenCharacterWindowForReconnect(0f); Settle();
        link.SetGlobalMessagesSuppressed(false); link.NotifyOSBecameVisible();
        for (int i = 0; i < 6; i++) { talk.DebugStep(); Settle(); }
        yield return Wait(() => link.LinkWindow.IsOpen && link.DisplayedEntryId == "chapter02_final_talk_night"
            && night.DebugDark >= 16f, "night and its real message are visible", 20f);
        Focus();
        link.TryCompleteDisplayedTextImmediately("chapter02_final_talk_night"); Settle();

        cursorOwner = new GameObject("NightfallOcclusionQA_CursorOwner");
        Check(CursorHandle.Instance != null && CursorHandle.Instance.AcquireScriptControlWithoutWarp(cursorOwner, false),
            "fixture owns only the displayed cursor without warping the physical pointer");
        CursorHandle.Instance.SetScriptScreenPosition(cursorOwner, new Vector2(-10000f, -10000f));
        IsolateRedPointer();
        Check(redPointer == null || !redPointer.activeSelf,
            "any existing red pointer is hidden for light-source isolation (not-yet-created is valid)");

        var portrait = Get(os.CharacterWindowController, "characterImage") as Image;
        Check(portrait != null && portrait.gameObject.activeInHierarchy, "the authored Live portrait is visible");
        Vector3 livePoint = portrait.rectTransform.TransformPoint(portrait.rectTransform.rect.center);
        Vector3 textPoint = TextPoint(link.DisplayedTextLabel);
        // Only the two requested screenshots yield a rendered frame. Geometry
        // assertions otherwise run synchronously with isolated pointer lights.
        yield return CheckOrdinaryOccluder("Live portrait", livePoint);
        yield return CheckOrdinaryOccluder("message glyph", textPoint);
        CheckGlowTitleAndPointer(livePoint);
        CheckNestedUnregisteredWindow(textPoint);
        CheckHeartbeatOcclusion();
    }

    static IEnumerator CheckOrdinaryOccluder(string source, Vector3 point)
    {
        context = source;
        folder.CloseFromScript(); Settle(); Sample(point, 4, source + " unoccluded baseline");
        folder.OpenStandalone(0f); Settle();
        folder.SetAlwaysOnTop(true);
        Align(folder.WindowRect, folder.WindowRect.rect.center, point);
        folder.transform.SetSiblingIndex(folder.transform.parent.childCount - 1);
        Sample(point, 0, source + " does not shine through the upper Folder window");
        ScreenCapture.CaptureScreenshot(Path.Combine(OutputDirectory,
            source == "Live portrait" ? "live-covered.png" : "message-covered.png"));
        yield return null;
        // The story may have re-shown her pointer while the frame rendered.
        // Restore light-source isolation before the remaining geometry checks.
        IsolateRedPointer();
        CursorHandle.Instance.SetScriptScreenPosition(cursorOwner, new Vector2(-10000f, -10000f));
        Focus();
        var overlap = folder.WindowRect.position;
        folder.WindowRect.position += folder.WindowRect.TransformVector(new Vector3(folder.WindowRect.rect.width + 1000f, 0f, 0f));
        Sample(point, 4, source + " returns when the actual occluder moves away");
        folder.WindowRect.position = overlap;
        folder.transform.SetSiblingIndex(0);
        Sample(point, 4, source + " remains bright when Folder sorts below Live's hierarchy");
        folder.transform.SetSiblingIndex(folder.transform.parent.childCount - 1);
        folder.Minimize();
        Check(folder.IsMinimized, source + " uses the real minimize operation");
        Sample(point, 4, source + " is not occluded by a minimized window");
        folder.RestoreAndBringToFront(); Settle();
        Sample(point, 0, source + " is occluded again after restoring Folder");
        folder.CloseFromScript(); Settle();
        Sample(point, 4, source + " is not occluded by a closed window");
    }

    static void CheckGlowTitleAndPointer(Vector3 point)
    {
        context = "upper self-lit window and pointer";
        folder.OpenStandalone(0f); Settle();
        folder.SetAlwaysOnTop(true);
        var rect = folder.WindowRect;
        float title = (float)Invoke(night, "TitleHeight", folder);
        Align(rect, new Vector2(rect.rect.center.x, rect.rect.yMax - title * .5f), point);
        folder.transform.SetSiblingIndex(folder.transform.parent.childCount - 1);
        night.AddGlowWindow(folder);
        Sample(point, 0, "an upper self-lit window's title does not inherit the lower portrait glow");
        var body = rect.TransformPoint(new Vector2(rect.rect.center.x, rect.rect.yMax - title - 10f));
        Sample(body, 4, "the same upper self-lit window retains its own body glow");
        night.RemoveGlow(rect);
        Sample(point, 0, "ordinary upper window title stays black without a nearby pointer");
        var camera = Invoke(night, "OverlayCamera") as Camera;
        CursorHandle.Instance.SetScriptScreenPosition(cursorOwner, RectTransformUtility.WorldToScreenPoint(camera, point));
        Sample(point, 4, "player pointer light illuminates the upper occluder after self-glow masking");
        CursorHandle.Instance.SetScriptScreenPosition(cursorOwner, new Vector2(-10000f, -10000f));
        folder.CloseFromScript(); Settle();
    }

    static void CheckNestedUnregisteredWindow(Vector3 point)
    {
        context = "nested runtime window";
        nestedRoot = new GameObject("NightfallOcclusionQA_NestedContainer", typeof(RectTransform));
        nestedRoot.layer = os.gameObject.layer;
        var parent = (RectTransform)nestedRoot.transform;
        parent.SetParent(live.transform.parent, false);
        parent.anchorMin = Vector2.zero; parent.anchorMax = Vector2.one; parent.offsetMin = parent.offsetMax = Vector2.zero;
        var node = new GameObject("NightfallOcclusionQA_UnregisteredWindow", typeof(RectTransform), typeof(Image), typeof(OSWindowUnit));
        node.layer = os.gameObject.layer;
        node.transform.SetParent(parent, false); node.GetComponent<Image>().color = Color.gray;
        var window = node.GetComponent<OSWindowUnit>();
        window.OpenStandalone(0f); Complete(window, "openSequence");
        Check(!os.WindowUnits.Contains(window), "runtime occluder is intentionally absent from the serialized window list");
        Align(window.WindowRect, window.WindowRect.rect.center, point);
        parent.SetSiblingIndex(parent.parent.childCount - 1);
        Sample(point, 0, "an unregistered window under an upper parent container still occludes");
        parent.SetSiblingIndex(0);
        Sample(point, 4, "moving the parent container below Live restores the nested message glow");
        window.CloseFromScript(); nestedRoot.SetActive(false);
    }

    static void CheckHeartbeatOcclusion()
    {
        context = "heartbeat occlusion";
        // This is a separate emission baseline: the preceding real message can
        // overlap the heartbeat, and animated resource rows can glow at level 4.
        link.LinkWindow.CloseFromScript(); live.CloseFromScript(); Settle();
        var heart = os.HeartbeatWaveform;
        Check(heart != null && heart.isActiveAndEnabled, "the actual heartbeat view is active");
        var rect = (RectTransform)heart.transform;
        var waveform = Get(heart, "samples") as Color32[];
        Check(waveform != null && waveform.Length > 0, "heartbeat provides its current visible waveform samples");
        int column = waveform.Length / 2, rows = (int)Get(heart, "virtualHeight");
        float row = Mathf.Round(waveform[column].r / 255f * (rows - 1)) + .5f;
        Vector3 point = rect.TransformPoint(new Vector2(rect.rect.xMin + (column + .5f) / waveform.Length * rect.rect.width,
            rect.rect.yMin + row / rows * rect.rect.height));
        var originalViews = Get(night, "resourceViews");
        Set(night, "resourceViews", Array.Empty<OSResourceStateView>());
        (Get(night, "resourceGlow") as IDictionary)?.Clear();
        try
        {
            Sample(point, 2, "uncovered heartbeat keeps its authored half-strength glow");
            folder.OpenStandalone(0f); Settle(); folder.SetAlwaysOnTop(true);
            Align(folder.WindowRect, folder.WindowRect.rect.center, point);
            folder.transform.SetSiblingIndex(folder.transform.parent.childCount - 1);
            Sample(point, 0, "an upper ordinary window also masks the non-window heartbeat glow");
            folder.CloseFromScript(); Settle();
        }
        finally { Set(night, "resourceViews", originalViews); }
    }

    static Vector3 TextPoint(TMP_Text label)
    {
        Check(label != null && label.gameObject.activeInHierarchy, "real message text is visible");
        label.ForceMeshUpdate();
        for (int i = 0; i < label.textInfo.characterCount; i++)
        {
            var character = label.textInfo.characterInfo[i];
            if (character.isVisible) return label.transform.TransformPoint((character.bottomLeft + character.topRight) * .5f);
        }
        throw new InvalidOperationException("No visible message glyph for the sample");
    }
    static void Sample(Vector3 world, int expected, string label)
    {
        IsolateRedPointer();
        Canvas.ForceUpdateCanvases(); Invoke(night, "Paint", 16f, 0f);
        var overlay = (RectTransform)Get(night, "overlay");
        var local = (Vector2)overlay.InverseTransformPoint(world);
        int width = (int)Get(night, "width"), height = (int)Get(night, "height");
        int x = Mathf.FloorToInt((local.x - overlay.rect.xMin) / overlay.rect.width * width);
        int y = Mathf.FloorToInt((local.y - overlay.rect.yMin) / overlay.rect.height * height);
        Check(x >= 0 && x < width && y >= 0 && y < height, label + " sample is on screen");
        int level = ((byte[])Get(night, "light"))[y * width + x];
        int alpha = ((byte[])Get(night, "alpha"))[y * width + x];
        samples.Add(new { label, x, y, level, alpha, expected, folderIndex = folder.transform.GetSiblingIndex(),
            liveIndex = live.transform.GetSiblingIndex(), folderOpen = folder.IsOpen, folderMinimized = folder.IsMinimized });
        Check(level == expected && (expected == 0 ? alpha == 255 : expected != 4 || alpha == 0),
            label + " (light=" + level + ", alpha=" + alpha + ", texel=" + x + "," + y + ")");
    }
    static void IsolateRedPointer()
    {
        // Night can precede the scripted actor's first creation. Re-resolve
        // after rendered frames so a newly-created pointer cannot contaminate
        // subsequent self-emission samples, and restore its original state.
        var next = live?.transform.parent?.Find("CharacterPermanentRedPointer")?.gameObject;
        if (next != redPointer)
        {
            if (redPointer != null) redPointer.SetActive(redWasActive);
            redPointer = next;
            if (redPointer != null) redWasActive = redPointer.activeSelf;
        }
        if (redPointer != null) redPointer.SetActive(false);
    }
    static void Align(RectTransform rect, Vector2 local, Vector3 world) => rect.position += world - rect.TransformPoint(local);
    static void Focus() { talk.DebugSetFocus(true); Set(night, "hasFocus", true); }
    static void Settle()
    {
        foreach (var window in os.GetComponentsInChildren<OSWindowUnit>(true))
        { Complete(window, "openSequence"); Complete(window, "closeSequence"); }
        Complete(link, "reopenTween"); Complete(link?.WindowLayout, "heightTween"); Canvas.ForceUpdateCanvases();
    }
    static IEnumerator Wait(Func<bool> ready, string label, float seconds)
    {
        float until = Time.realtimeSinceStartup + seconds;
        while (!ready() && Time.realtimeSinceStartup < until) { if (talk != null) Focus(); yield return null; }
        Check(ready(), label);
    }
    static GameSaveData Clone(GameSaveData value) => JsonUtility.FromJson<GameSaveData>(JsonUtility.ToJson(value));
    static void Check(bool value, string label) { if (!value) throw new InvalidOperationException(label); checks.Add(label); }
    static object Get(object target, string name) => target?.GetType().GetField(name, Private)?.GetValue(target);
    static void Set(object target, string name, object value) => target.GetType().GetField(name, Private).SetValue(target, value);
    static object Invoke(object target, string name, params object[] args) => target.GetType().GetMethod(name, Private).Invoke(target, args);
    static void Complete(object target, string field) { var tween = Get(target, field) as Tween; if (tween != null && tween.IsActive()) tween.Complete(true); }
    static string Hash(string path)
    {
        if (string.IsNullOrEmpty(path) || !File.Exists(path)) return "absent";
        using (var hash = SHA256.Create()) return Convert.ToBase64String(hash.ComputeHash(File.ReadAllBytes(path)));
    }
}
