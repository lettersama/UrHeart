using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using MrTGame.Core;
using MrTGame.Systems;
using UnityEngine;
using UnityEngine.EventSystems;

// Focused regression for the bundled materials and downstairs picture report.
// Uses a cloned save and real scene/hotspot actions; never writes a player's save.
public static class Chapter01HomeReportQA
{
    static readonly List<string> checks = new List<string>();
    static OSMemoryPicController controller;
    static OSMemoryPicHotspot hovered;
    public static string Status { get; private set; } = "idle";
    public static string OutputDirectory { get; private set; }

    public static string Start()
    {
        if (Status == "running") return Status;
        var saves = GameAutosaveService.Instance;
        if (!Application.isPlaying || saves == null || GameDataManager.Instance == null
            || GameTest.Instance == null || GameManager.Instance == null || SceneLoadManager.Instance == null)
            return "Play Mode initialization required";
        if (saves.IsBusy || saves.IsWriting || GameManager.Instance.IsTransitioning
            || SceneLoadManager.Instance.IsBusy || GameSessionFlow.Instance?.IsBusy == true
            || GameTest.Instance.IsCheckpointJumpRunning || GameTest.Instance.IsMemoryJumpRunning)
            return "Wait for active save/scene operation";
        checks.Clear();controller = null;hovered = null;
        OutputDirectory = Path.GetFullPath(Path.Combine("Temp", "Chapter01HomeReportQA", Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(OutputDirectory);Status = "running";
        GameDataManager.Instance.StartCoroutine(Guarded());return Status;
    }

    static IEnumerator Guarded()
    {
        var data = GameDataManager.Instance;var saves = GameAutosaveService.Instance;
        var original = data.SaveData;var language = data.CurrentLanguageState;
        bool background = Application.runInBackground;
        var resources = GameResourceSystem.Instance;bool resourcesEnabled = resources != null && resources.enabled;
        string[] paths = { saves.SavePath, saves.SavePath + ".bak", saves.SavePath + ".tmp" };
        string[] hashes = null;
        var work = new Stack<IEnumerator>();
        try
        {
            saves.EnterTestSession();hashes = paths.Select(Fingerprint).ToArray();
            data.ReplaceSaveData(JsonUtility.FromJson<GameSaveData>(JsonUtility.ToJson(original)));
            data.SetCurrentLanguage(LanguageState.zh_CN);
            Application.runInBackground = true;if (resources != null) resources.enabled = false;
            work.Push(Run());
            while (work.Count > 0)
            {
                object next;
                try
                {
                    if (!work.Peek().MoveNext()) { (work.Pop() as IDisposable)?.Dispose();continue; }
                    next = work.Peek().Current;
                }
                catch (Exception error)
                {
                    Status = "FAILED: " + error;
                    Debug.LogError("[Chapter 01 Home Report QA] " + Status);yield break;
                }
                if (next is IEnumerator nested) work.Push(nested);else yield return next;
            }
            Check(paths.Select(Fingerprint).SequenceEqual(hashes), "normal save files unchanged");
            Check(saves.IsTestSession && !saves.IsWriting, "test session blocks writes");
            Status = "PASS: " + checks.Count + " focused home integration checks";
            Debug.Log("[Chapter 01 Home Report QA] " + Status + " | " + OutputDirectory);
        }
        finally
        {
            while (work.Count > 0) (work.Pop() as IDisposable)?.Dispose();
            ClearHover();saves.EnterTestSession();saves.EndSession();
            GameManager.Instance?.OpenMainMenuWithoutSave();
            data.ReplaceSaveData(original);data.SetCurrentLanguage(language);
            if (resources != null) resources.enabled = resourcesEnabled;
            Application.runInBackground = background;
            if (hashes != null && !paths.Select(Fingerprint).SequenceEqual(hashes)) Status = "FAILED: normal save files changed\n" + Status;
            if (Status == "running") Status = "FAILED: QA interrupted before completion";
            File.WriteAllText(Path.Combine(OutputDirectory, "results.txt"), Status + "\n" + string.Join("\n", checks));
        }
    }

    static IEnumerator Run()
    {
        GameTest.Instance.GetComponent<GameTestConsole>()?.SetOpen(false);
        Check(GameTest.Instance.JumpToCheckpoint(GameTestCheckpoint.Chapter01BeforeInteractivePicture), "isolated home checkpoint accepted");
        yield return Wait(() => !GameTest.Instance.IsCheckpointJumpRunning, "home checkpoint finished", 65f);
        Check(GameTest.Instance.JumpSucceeded, "home checkpoint succeeded");
        yield return Wait(() =>
        {
            controller = UnityEngine.Object.FindObjectsByType<OSMemoryPicController>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                .FirstOrDefault(p => p.memory == OS_Handle.Instance.MemoryWindowController && p.State != null);
            return controller != null;
        }, "authored picture initialized");
        yield return Ready();
        Check(controller.CurrentScene.sceneId == "entrance" && controller.State.initialized
            && controller.State.scenarioId == "chapter01_home", "checkpoint entered real home picture from entrance");
        var memoryWindow = controller.memory.GetComponent<OSWindowUnit>();
        foreach (var window in UnityEngine.Object.FindObjectsByType<OSWindowUnit>(FindObjectsSortMode.None))
            if (window.IsOpen && window != controller.pictureWindow && window != memoryWindow)
                window.CloseSilently();
        controller.FocusPicture();yield return null;

        yield return Click("enter");
        Check(controller.CurrentScene.sceneId == "hall", "front door entered downstairs hall");
        var picture = Hover("hall_picture");
        Check(controller.tooltip.gameObject.activeInHierarchy
            && controller.tooltipLabel.text == GameDataManager.Instance.GetLocalizedText("os.memory.pic.hall_picture"),
            "before straightening, real pointer hover says straighten picture");
        Check(!controller.State.Has("picture_fixed"), "picture begins unstraightened");
        Check(controller.TryInteract("hall_picture", picture.customEffect), "straightening action accepted");
        Check(controller.State.Has("picture_fixed") && controller.memory.CurrentData.DataIndex == 2910,
            "straightening sets flag and speaks its own action text");
        Check(controller.tooltip.gameObject.activeInHierarchy
            && controller.tooltipLabel.text == GameDataManager.Instance.GetLocalizedText("os.memory.pic.hall_picture_inspect"),
            "tooltip changes immediately to inspect without moving pointer");
        Check(LayerVisible("picture_straight") && !LayerVisible("picture"), "straightened artwork replaces tilted artwork");
        Check(!controller.TryInteract("hall_picture"), "same-frame picture duplicate rejected");
        yield return null;yield return Ready();
        Check(controller.TryInteract("hall_picture", picture.customEffect), "straightened picture remains inspectable");
        Check(controller.memory.CurrentData.DataIndex == 3780
            && controller.memory.CurrentData.Text == GameDataManager.Instance.GetLocalizedText("os.memory.3780.text"),
            "downstairs repeat inspection reads separate localized landscape text 3780");
        CompleteCurrentText();yield return Shot("01-hall-picture-straight-inspect");ClearHover();

        yield return Click("upstairs");
        yield return Click("landing_photo");
        Check(controller.memory.CurrentData.DataIndex == 2920
            && controller.memory.CurrentData.Text == GameDataManager.Instance.GetLocalizedText("os.memory.2920.text"),
            "upstairs photo retains original localized text 2920");
        yield return Click("to_workshop");
        Check(!controller.State.Has("window_inspected") && !controller.State.Has("wood_taken"), "material pickup still requires the broken-window clue");
        yield return Click("planks");
        Check(controller.memory.CurrentData.DataIndex == 3650 && !controller.State.inventory.Contains("wood")
            && !controller.State.inventory.Contains("nails"), "early plank inspection grants neither material");
        yield return Click("robot");
        yield return Wait(() => controller.State.Has("glass_broken") && !controller.IsBusy, "real robot placement completes glass event");
        yield return Click("back.workshop");yield return Click("back.upstairs");
        yield return Click("to_living");yield return Click("window");
        Check(controller.State.Has("window_inspected"), "real broken-window inspection unlocks material pickup");
        yield return Click("back.window");yield return Click("back.living");
        yield return Click("upstairs");yield return Click("to_workshop");
        yield return Click("robot");
        Check(controller.memory.CurrentData.DataIndex == 2980, "robot requests materials before either pickup");
        int beforeCount = controller.State.inventory.Count;
        var planks = Hover("planks");
        Check(controller.TryInteract("planks", planks.customEffect), "single bundled plank pickup accepted");
        Check(controller.State.inventory.Count == beforeCount + 2
            && controller.State.inventory.Count(i => i == "wood") == 1
            && controller.State.inventory.Count(i => i == "nails") == 1,
            "one real pickup adds exactly one wood and one nails item");
        Check(controller.State.Has("wood_taken") && controller.State.Has("nails_taken"), "one pickup commits both collection flags");
        Check(controller.memory.CurrentData.DataIndex == 2990
            && controller.memory.CurrentData.Text == GameDataManager.Instance.GetLocalizedText("os.memory.2990.text"),
            "bundled pickup speaks localized materials text 2990");
        Check(!planks.gameObject.activeInHierarchy && !LayerVisible("planks"), "collected plank art and hotspot disappear");
        var afterPickup = controller.State.inventory.ToArray();
        Check(!controller.TryInteract("planks"), "same-frame repeat pickup rejected");
        yield return null;yield return Ready();
        Check(!controller.TryInteract("planks") && controller.State.inventory.SequenceEqual(afterPickup), "later repeat pickup also rejected without duplicate inventory");
        Check(controller.inventory.slots.Any(s => s.ItemId == "wood" && s.icon.gameObject.activeInHierarchy)
            && controller.inventory.slots.Any(s => s.ItemId == "nails" && s.icon.gameObject.activeInHierarchy),
            "both collected materials have visible inventory icons");
        CompleteCurrentText();yield return Shot("02-planks-and-nails-collected");ClearHover();

        yield return Click("robot");
        Check(controller.memory.CurrentData.DataIndex == 3800, "robot requests only hammer after bundled materials pickup");

        yield return Click("toolbox");
        Check(controller.CurrentScene.FindHotspot("take_nails") == null, "toolbox no longer defines a separate nail pickup");
        var oldNailViews = controller.sceneRoot.GetComponentsInChildren<OSMemoryPicHotspot>(true)
            .Where(h => h.hotspotId == "take_nails").ToArray();
        Check(oldNailViews.All(h => !h.gameObject.activeInHierarchy), "any retired prefab nail hotspot is hidden");
        Check(!LayerVisible("nails"), "toolbox nail artwork disappears after bundled pickup");
        Check(!controller.TryInteract("take_nails") && controller.State.inventory.SequenceEqual(afterPickup), "retired nail action cannot grant extra materials");
        yield return Click("take_hammer");yield return Click("back.toolbox");yield return Click("robot");
        Check(controller.memory.CurrentData.DataIndex == 3790 && controller.CurrentScene.sceneId == "workshop",
            "with all materials robot directs player to window without opening repair early");
        Check(controller.memory.CurrentData.Text == GameDataManager.Instance.GetLocalizedText("os.memory.3790.text"),
            "ready-material hint resolves its localized text");
        CompleteCurrentText();yield return Shot("03-robot-materials-ready");
        // Collection flags survive consumption: using the boards must not make
        // this hint ask for another set of already-collected materials.
        yield return Click("back.workshop");yield return Click("back.upstairs");
        yield return Click("to_living");yield return Click("window");
        yield return Click("place_board");yield return Click("place_board");yield return Click("place_board");
        Check(controller.State.Has("boards_placed") && !controller.State.inventory.Contains("wood"), "real window placement consumes the wood");
        yield return Click("back.window");yield return Click("back.living");yield return Click("upstairs");
        yield return Click("to_workshop");yield return Click("robot");
        Check(controller.memory.CurrentData.DataIndex == 3790, "robot still points to unfinished window after boards are consumed");
        var robot = controller.CurrentScene.FindHotspot("robot");
        var restored = JsonUtility.FromJson<OSMemoryPicSaveData>(JsonUtility.ToJson(controller.State));
        Check(robot.FindOutcome(restored).memoryIndex == 3790, "serialized in-progress state retains ready-material hint");
        restored.flags.Remove("wood_taken");restored.flags.Remove("nails_taken");
        Check(robot.FindOutcome(restored).memoryIndex == 3760, "hammer-first pickup only requests wood and nails");
        restored.flags.Add("window_fixed");
        Check(robot.FindOutcome(restored).memoryIndex == 3720, "fixed window still requires glass cleanup before robot repair");
        restored.flags.Add("glass_swept");
        Check(robot.FindOutcome(restored).targetScene == "wiring", "robot repair opens after both window and cleanup gates");
        ClearHover();GameManager.Instance.OpenMainMenuWithoutSave();
        yield return Wait(() => GameManager.Instance.Phase == GamePhase.Boot && !GameManager.Instance.IsTransitioning, "QA exits to menu", 25f);
    }

    static IEnumerator Click(string id)
    {
        // Screenshots resume at end-of-frame; the next action must happen on a
        // fresh frame to respect the production duplicate-click guard.
        yield return null;
        yield return Ready();var hotspot = Hover(id);
        Check(controller.TryInteract(id, hotspot.customEffect), "actual hotspot action accepted: " + id);
        Check(!controller.TryInteract(id), "same-frame duplicate blocked: " + id);
        yield return null;yield return Ready();ClearHover();
    }
    static OSMemoryPicHotspot Hover(string id)
    {
        ClearHover();controller.FocusPicture();Canvas.ForceUpdateCanvases();
        var hotspot = controller.sceneRoot.GetComponentsInChildren<OSMemoryPicHotspot>()
            .Where(h => h.hotspotId == id).OrderBy(h => h.navigationOnly).FirstOrDefault();
        Check(hotspot != null, "active hotspot exists: " + id);
        var rect = (RectTransform)hotspot.transform;
        var pointer = new PointerEventData(EventSystem.current) { button = PointerEventData.InputButton.Left,
            position = RectTransformUtility.WorldToScreenPoint(hotspot.GetComponentInParent<Canvas>().worldCamera,
                rect.TransformPoint(rect.rect.center)) };
        var hits = new List<RaycastResult>();EventSystem.current.RaycastAll(pointer, hits);
        Check(hits.Count > 0 && hits[0].gameObject.GetComponentInParent<OSMemoryPicHotspot>() == hotspot,
            "hotspot is frontmost at its click point: " + id);
        ExecuteEvents.Execute(hotspot.gameObject, pointer, ExecuteEvents.pointerEnterHandler);hovered = hotspot;
        return hotspot;
    }
    static void ClearHover()
    {
        if (hovered != null && EventSystem.current != null)
            ExecuteEvents.Execute(hovered.gameObject, new PointerEventData(EventSystem.current), ExecuteEvents.pointerExitHandler);
        hovered = null;
    }
    static bool LayerVisible(string id)
    {
        var layer = controller.sceneRoot.GetComponentInChildren<OSMemoryPicSceneView>().layers.Single(l => l.id == id);
        return layer.graphic != null && layer.graphic.gameObject.activeInHierarchy;
    }
    static void CompleteCurrentText()
    {
        foreach (var unit in controller.memory.TextUnits)
            if (unit.BoundData != null && unit.DataIndex == controller.memory.CurrentData.DataIndex)
                unit.CompleteImmediately();
    }
    static IEnumerator Ready() => Wait(() => controller != null && controller.CanInteract, "picture interaction ready", 15f);
    static IEnumerator Wait(Func<bool> condition, string label, float seconds = 8f)
    {
        float deadline = Time.realtimeSinceStartup + seconds;
        while (!condition() && Time.realtimeSinceStartup < deadline) yield return null;
        Check(condition(), label);
    }
    static IEnumerator Shot(string name)
    {
        yield return new WaitForEndOfFrame();var texture = ScreenCapture.CaptureScreenshotAsTexture();
        try { File.WriteAllBytes(Path.Combine(OutputDirectory, name + ".png"), texture.EncodeToPNG()); }
        finally { UnityEngine.Object.Destroy(texture); }
    }
    static void Check(bool ok, string label)
    {
        if (!ok) throw new InvalidOperationException(label);
        checks.Add(label);
    }
    static string Fingerprint(string path)
    {
        if (!File.Exists(path)) return "missing";
        using (var sha = SHA256.Create()) return Convert.ToBase64String(sha.ComputeHash(File.ReadAllBytes(path)));
    }
}
