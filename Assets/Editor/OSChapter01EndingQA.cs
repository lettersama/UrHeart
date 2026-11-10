using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using MrTGame.Core;
using MrTGame.Systems;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// Runs on a disposable in-memory save. No player save is written to disk.
public static class OSChapter01EndingQA
{
    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    static readonly FieldInfo Save = typeof(GameDataManager).GetField("saveData", Private);
    static readonly List<string> results = new List<string>();
    public static string Status { get; private set; } = "idle";
    public static string Start()
    {
        if (!Application.isPlaying || GameDataManager.Instance == null) return "Play Mode initialization required";
        if (Status == "running") return Status;
        GameAutosaveService.Instance?.EnterTestSession();
        results.Clear();Status = "running";
        GameDataManager.Instance.StartCoroutine(Guarded());return Status;
    }
    static void Check(bool ok, string label)
    {
        if (!ok) throw new InvalidOperationException(label);
        results.Add(label);
    }
    static IEnumerator Guarded()
    {
        var manager = GameDataManager.Instance;
        var original = manager.SaveData;
        var language = manager.CurrentLanguageState;
        bool background = Application.runInBackground;
        Application.runInBackground = true;
        var stack = new Stack<IEnumerator>();stack.Push(Run());
        Save.SetValue(manager, JsonUtility.FromJson<GameSaveData>(JsonUtility.ToJson(original)));
        try
        {
            while (stack.Count > 0)
            {
                object value;
                try
                {
                    if (!stack.Peek().MoveNext()) { (stack.Pop() as IDisposable)?.Dispose();continue; }
                    value = stack.Peek().Current;
                }
                catch (Exception error) { Status = "FAILED: " + error;Debug.LogError(Status);yield break; }
                if (value is IEnumerator nested) stack.Push(nested);else yield return value;
            }
            Status = "PASS: " + results.Count + " checks";
        }
        finally
        {
            while (stack.Count > 0) (stack.Pop() as IDisposable)?.Dispose();
            Save.SetValue(manager, original);manager.SetCurrentLanguage(language);
            Application.runInBackground = background;
            Directory.CreateDirectory("Temp/Chapter01EndingQA");
            File.WriteAllText("Temp/Chapter01EndingQA/results.txt", Status + "\n" + string.Join("\n", results));
        }
    }
    static IEnumerator Wait(Func<bool> condition, string label, float seconds = 20f)
    {
        float deadline = Time.realtimeSinceStartup + seconds;
        while (!condition() && Time.realtimeSinceStartup < deadline) yield return null;
        Check(condition(), label);
    }
    static IEnumerator Shot(string name)
    {
        yield return new WaitForEndOfFrame();
        var image = ScreenCapture.CaptureScreenshotAsTexture();
        Directory.CreateDirectory("Temp/Chapter01EndingQA");
        File.WriteAllBytes("Temp/Chapter01EndingQA/" + name + ".png", image.EncodeToPNG());
        UnityEngine.Object.Destroy(image);
    }
    static T Field<T>(object owner, string name) => (T)owner.GetType().GetField(name, Private).GetValue(owner);
    static void Click(Button button)
    {
        var rect = (RectTransform)button.transform;var canvas = button.GetComponentInParent<Canvas>();
        var pointer = new PointerEventData(EventSystem.current)
        { button = PointerEventData.InputButton.Left, position = RectTransformUtility.WorldToScreenPoint(canvas.worldCamera, rect.TransformPoint(rect.rect.center)) };
        var hits = new List<RaycastResult>();EventSystem.current.RaycastAll(pointer, hits);
        Check(hits.Count > 0 && hits[0].gameObject.GetComponentInParent<Button>() == button, "frontmost clickable: " + button.name);
        ExecuteEvents.Execute(button.gameObject, pointer, ExecuteEvents.pointerClickHandler);
    }
    static IEnumerator Run()
    {
        var data = GameDataManager.Instance;
        Check(GameTest.Instance.JumpToCheckpoint(GameTestCheckpoint.Chapter01BeforeInteractivePicture), "start isolated chapter checkpoint");
        yield return Wait(() => !GameTest.Instance.IsCheckpointJumpRunning && OS_Handle.Instance != null, "checkpoint ready", 40f);
        var os = OS_Handle.Instance;
        var ending = os.Chapter01Ending;
        Check(ending != null && ending.Configuration != null, "authored ending configuration");
        os.LinkMessageController.SetGlobalMessagesSuppressed(true);
        os.LinkMessageController.NotifyOSHidden();
        os.MemoryWindowController.DebugJumpToData(2680);
        var picture = UnityEngine.Object.FindAnyObjectByType<OSMemoryPicController>(FindObjectsInactive.Include);
        yield return Wait(() => picture != null && picture.CanInteract, "picture ready");

        var legacy = new OSEnergyLoopSaveData { isCharging = true, chargeDurationSeconds = 90, remainingChargeSeconds = 45 };
        legacy.EnsureData();Check(legacy.chargeDurationSeconds == 300 && legacy.remainingChargeSeconds == 150, "legacy charge retains 50 percent progress");
        legacy.EnsureData();Check(legacy.remainingChargeSeconds == 150, "migration is idempotent");
        data.OSProcessData.installedToolProgramIds.Add(OSToolProgramIds.EnergyLoop);
        data.OSProcessData.energyLoop = new OSEnergyLoopSaveData();
        Check(data.TryBeginEnergyCharge() && data.OSEnergyLoopState.remainingChargeSeconds == 300, "charge starts at 05:00");
        data.TickEnergyCharge(299);Check(!data.OSEnergyLoopState.chargeReadyToClaim, "299 seconds is not ready");
        data.TickEnergyCharge(1);Check(data.OSEnergyLoopState.chargeReadyToClaim, "300 seconds requires claim");
        data.SetOSResourceValue(ResourceType.Battery, 50);
        int gained;Check(data.TryClaimEnergyCharge(out gained) && gained == 25 && !data.TryClaimEnergyCharge(out gained), "claim adds 25 exactly once");

        picture.State.currentSceneId = "workshop";
        yield return null;yield return null;
        Check(!ending.TryBeginFromRoomExit(picture, "back.workshop"), "unrepaired robot does not trigger ending");
        Check(picture.TryNavigate("back.workshop"), "unrepaired workshop exit retains navigation");
        yield return Wait(() => picture.CanInteract && picture.CurrentScene.sceneId == "upstairs", "ordinary exit reaches upstairs");
        picture.State.Set("robot_repaired");picture.State.Set("knock_heard");picture.State.flags.Remove("knock_pending");
        picture.State.currentSceneId = "wiring";
        yield return null;yield return null;
        Check(picture.TryNavigate("back.wiring"), "leave repaired wiring normally");
        yield return Wait(() => picture.CanInteract && picture.CurrentScene.sceneId == "workshop", "wiring returns to workshop");
        Check(!ending.IsPresenting && picture.memory.CurrentData.DataIndex == 3770, "repair return prompt remains before ending");
        var arrows = picture.GetComponentsInChildren<OSMemoryPicHotspot>().Where(h => h.navigationOnly && h.hotspotId == "back.workshop").ToArray();
        Check(arrows.Length == 2, "both workshop arrows share the ending action");
        Check(picture.TryNavigate("back.workshop"), "repaired workshop exit starts ending");
        Check(ending.IsPresenting && os.IsSystemInputLocked && os.TryCloseFrontmostNormalWindow(), "ending consumes OS input and Escape");
        yield return new WaitForSecondsRealtime(1);
        yield return Shot("01-storyboard");
        yield return Wait(() => ending.Phase == OSChapter01EndingPhase.Door, "storyboard holds at clickable door");
        yield return null;
        Check(!CursorHandle.Instance.IsScriptControlledBy(ending), "door releases cursor for player");
        Check(os.GetComponent<OSPaperPushMusic>()?.IsPlaying != true, "paper music stopped during ending");
        yield return Shot("02-door");

        // Simulate a restored snapshot at the door; do not skip the required click.
        data.OSProcessData.chapter01Ending = JsonUtility.FromJson<OSChapter01EndingSaveData>(JsonUtility.ToJson(data.OSProcessData.chapter01Ending));
        yield return new WaitForSecondsRealtime(1);
        Check(ending.Phase == OSChapter01EndingPhase.Door, "door checkpoint resumes without replaying slides");
        Click(Field<Button>(ending, "doorButton"));
        yield return Wait(() => ending.Phase == OSChapter01EndingPhase.Animation, "door click starts animation");
        yield return new WaitForSecondsRealtime(1);yield return Shot("03-animation-placeholder");
        yield return Wait(() => ending.Phase == OSChapter01EndingPhase.Glitch, "animation ends in freeze");
        yield return new WaitForSecondsRealtime(.95f);yield return Shot("04-freeze");
        yield return Wait(() => ending.Phase == OSChapter01EndingPhase.Completed, "fade reaches DEMO end");
        Check(data.OSProcessData.chapter01Ending.completed, "ending completion recorded in SaveData");
        yield return Shot("05-demo-zh");
        foreach (LanguageState language in Enum.GetValues(typeof(LanguageState)))
        {
            data.SetCurrentLanguage(language);yield return null;
            string value;
            Check(data.TryGetLocalizedText("ui.os.chapter01_ending.return_menu", out value) && !string.IsNullOrEmpty(value), "end translation: " + language);
            foreach (var label in Field<GameObject>(ending, "endCard").GetComponentsInChildren<TMP_Text>())
            { label.ForceMeshUpdate();Check(!label.isTextOverflowing, "end card fits: " + language + " / " + label.name); }
        }
        data.SetCurrentLanguage(LanguageState.en);yield return null;yield return Shot("06-demo-en");
        data.OSProcessData.chapter01Ending = JsonUtility.FromJson<OSChapter01EndingSaveData>(JsonUtility.ToJson(data.OSProcessData.chapter01Ending));
        yield return null;yield return null;
        Check(ending.Phase == OSChapter01EndingPhase.Completed && Field<GameObject>(ending, "endCard").activeInHierarchy, "completed checkpoint resumes end card");
        Click(Field<Button>(ending, "returnButton"));
        yield return Wait(() => GameManager.Instance.Phase == GamePhase.Boot && !ending.IsPresenting, "return button reaches main menu");
        Check(!CursorHandle.Instance.IsScriptControlledBy(ending), "return menu releases cursor ownership");
        yield return new WaitForSecondsRealtime(.5f);yield return Shot("07-main-menu");
    }
}
