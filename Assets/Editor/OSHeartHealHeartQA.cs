using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using MrTGame.Core;
using MrTGame.Systems;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// Opt-in integration check: drives the existing treatment chart and real pointer
// handlers. It runs on a disposable in-memory save and never writes production data.
public static class OSHeartHealHeartQA
{
    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    static readonly List<string> checks = new List<string>();
    public static string Status { get; private set; } = "idle";
    public static string OutputDirectory { get; private set; }

    public static string Start()
    {
        if (Status == "running") return Status;
        var saves = GameAutosaveService.Instance;
        if (!Application.isPlaying || saves == null || GameDataManager.Instance == null
            || GameManager.Instance == null || GameTest.Instance == null || SceneLoadManager.Instance == null)
            return "Play Mode initialization required";
        if (saves.IsBusy || saves.IsWriting || GameManager.Instance.IsTransitioning
            || GameTest.Instance.IsCheckpointJumpRunning || SceneLoadManager.Instance.IsBusy)
            return "Wait for active save/scene operation";
        OutputDirectory = Path.GetFullPath(Path.Combine("Temp", "HeartHealHeartQA", Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(OutputDirectory);
        checks.Clear(); Status = "running";
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
        bool resourceEnabled = resources != null && resources.enabled;
        bool background = Application.runInBackground;
        string[] paths = { saves.SavePath, saves.SavePath + ".bak", saves.SavePath + ".tmp" };
        string[] hashes = paths.Select(Fingerprint).ToArray();
        var stack = new Stack<IEnumerator>();
        try
        {
            saves.EnterTestSession();
            data.ReplaceSaveData(JsonUtility.FromJson<GameSaveData>(JsonUtility.ToJson(original)));
            data.SetCurrentLanguage(LanguageState.zh_CN);
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
                    Status = "FAILED: " + error;
                    Debug.LogError("[Heart Monitor QA] " + Status);
                    yield break;
                }
                if (next is IEnumerator nested) stack.Push(nested); else yield return next;
            }
            Check(paths.Select(Fingerprint).SequenceEqual(hashes), "production save files unchanged");
            Check(saves.IsTestSession && !saves.IsWriting, "test autosave isolation remains active");
            Status = "PASS: " + checks.Count + " heart presentation checks";
            Debug.Log("[Heart Monitor QA] " + Status + " | " + OutputDirectory);
        }
        finally
        {
            while (stack.Count > 0) (stack.Pop() as IDisposable)?.Dispose();
            OS_Handle.Instance?.HeartHealToolWindowController?.StopTreatment();
            GameTest.Instance?.GetComponent<GameTestConsole>()?.SetOpen(false);
            saves.EnterTestSession(); saves.EndSession();
            GameManager.Instance?.OpenMainMenuWithoutSave();
            data.ReplaceSaveData(original); data.SetCurrentLanguage(language);
            if (resources != null) resources.enabled = resourceEnabled;
            Application.runInBackground = background;
            if (!paths.Select(Fingerprint).SequenceEqual(hashes)) Status = "FAILED: production save changed\n" + Status;
            if (Status == "running") Status = "FAILED: QA interrupted";
            File.WriteAllText(Path.Combine(OutputDirectory, "results.txt"), Status + "\n" + string.Join("\n", checks));
        }
    }

    static IEnumerator Run()
    {
        var data = GameDataManager.Instance;
        Check(GameTest.Instance.JumpToCheckpoint(GameTestCheckpoint.PrologueHeartHeal), "prepare real treatment checkpoint");
        yield return Wait(() => !GameTest.Instance.IsCheckpointJumpRunning && OS_Handle.Instance != null
            && !GameManager.Instance.IsTransitioning, "checkpoint loaded", 60f);
        var os = OS_Handle.Instance;
        data.OSCharacterInteractionState.ActivateSequence(null);
        os.LinkMessageController.SetGlobalMessagesSuppressed(true);
        os.LinkMessageController.NotifyOSHidden();
        GameTest.Instance.GetComponent<GameTestConsole>()?.SetOpen(false);
        Check(os.OpenHeartHealTool(1), "open first treatment window");
        var treatment = os.HeartHealToolWindowController;
        var window = treatment.GetComponent<OSWindowUnit>();
        yield return Wait(() => !window.IsOpening && !treatment.IsFirstTreatmentTutorialLocked, "treatment UI ready", 8f);
        var presentation = treatment.GetComponentInChildren<OSHeartHealHeartPresentation>(true);
        Check(presentation != null && presentation.IsConfigured && presentation.isActiveAndEnabled,
            "heart replaces placeholder in actual visible window");
        var heart = presentation.GetComponent<Image>();
        var animator = presentation.GetComponent<Animator>();
        var symbols = Field<Image[]>(presentation, "feedbackImages");
        var heal = Field<Sprite>(presentation, "healingSymbol");
        var miss = Field<Sprite>(presentation, "missSymbol");
        Check(heart.sprite != null && heart.preserveAspect && !heart.raycastTarget,
            "sprite retains aspect and does not intercept treatment input");
        Check(animator.runtimeAnimatorController != null && animator.updateMode == AnimatorUpdateMode.UnscaledTime,
            "heart uses configured unscaled Animator");
        Check(Field<RuntimeAnimatorController[]>(treatment, "heartAnimatorControllers").Length == 4
            && Field<RuntimeAnimatorController[]>(treatment, "heartAnimatorControllers")
                .All(controller => controller == animator.runtimeAnimatorController), "all four treatment stages have heart animation");
        var clips = animator.runtimeAnimatorController.animationClips;
        Check(clips.All(clip => Mathf.Approximately(clip.frameRate, 10f)), "all heart clips sample at 10 fps");
        var idle = clips.Single(clip => clip.name == "Heart_Idle");
        Check(AnimationUtility.GetAnimationClipSettings(idle).loopTime
            && clips.Where(clip => clip != idle).All(clip => !AnimationUtility.GetAnimationClipSettings(clip).loopTime),
            "Idle loops and reaction clips exit");
        Check(symbols.Length == 6 && symbols.All(image => image != null && !image.raycastTarget)
            && symbols[0].GetComponentInParent<RectMask2D>() != null, "six reusable symbols are clipped inside monitor");
        var observed = new HashSet<Sprite>();
        float until = Time.unscaledTime + 1.15f;
        while (Time.unscaledTime < until) { observed.Add(heart.sprite); yield return null; }
        Check(observed.Count >= 4 && animator.GetCurrentAnimatorStateInfo(0).IsName("Idle"),
            "visible idle heartbeat actually changes sprite frames");
        Check(!symbols.Any(image => image.gameObject.activeSelf), "idle has no success or failure symbols");
        yield return Shot("01-heart-idle");

        Check(treatment.BeginTreatment(), "start actual first-treatment chart");
        yield return Wait(() => treatment.CanTriggerShock, "shock input ready", 2f);
        int energy = data.GetOSResourceValue(ResourceType.Battery);
        float state = RawState(treatment);
        bool didHit;
        Check(treatment.TryTriggerShock(out didHit) && !didHit, "empty shock is accepted without a hit");
        yield return null;
        Check(Mathf.Approximately(state, RawState(treatment)) && !symbols.Any(image => image.gameObject.activeSelf),
            "empty press emits no healing symbol or healing state");
        Check(data.GetOSResourceValue(ResourceType.Battery) == energy - 1, "empty shock preserves existing energy cost");
        var beats = treatment.GetComponentsInChildren<OSHeartHealBeatUnit>(true);
        yield return Wait(() => beats.Any(beat => beat.BeatType == OSHeartHealBeatType.Tap && beat.CanBeHit),
            "real tap reaches judgement window", 12f);
        state = RawState(treatment);
        int resolved = Field<int>(treatment, "resolvedBeatCount");
        Check(treatment.TryTriggerShock(out didHit) && didHit, "actual tap calls hit judgement");
        Check(RawState(treatment) > state && Field<int>(treatment, "resolvedBeatCount") == resolved + 1,
            "ReportHit changes existing health and resolution count once");
        yield return null;
        Check(symbols.Any(image => image.gameObject.activeSelf && image.sprite == heal), "tap hit emits treatment crosses");
        Check(animator.GetCurrentAnimatorStateInfo(0).IsName("Hit"), "resolved tap plays Hit animation");
        var rising = symbols.First(image => image.gameObject.activeSelf && image.sprite == heal);
        float riseY = rising.rectTransform.anchoredPosition.y;
        yield return new WaitForSecondsRealtime(.15f);
        Check(rising.rectTransform.anchoredPosition.y > riseY, "healing cross moves upward");
        yield return Shot("02-heart-hit");

        yield return Wait(() => symbols.Any(image => image.gameObject.activeSelf && image.sprite == miss),
            "missed real chart note emits downward marker", 8f);
        var falling = symbols.First(image => image.gameObject.activeSelf && image.sprite == miss);
        float fallY = falling.rectTransform.anchoredPosition.y;
        yield return new WaitForSecondsRealtime(.12f);
        Check(falling.gameObject.activeSelf && falling.rectTransform.anchoredPosition.y < fallY,
            "miss arrow visibly descends");
        yield return Shot("03-heart-miss");

        // The prior tap's window overlaps the hold's early window. The real
        // controller selects the nearest target; merely finding a hittable
        // hold could press that tap instead and never start the hold at all.
        OSHeartHealBeatUnit holdTarget = null;
        yield return Wait(() =>
        {
            holdTarget = beats.Where(beat => beat.CanBeHit).OrderBy(beat => beat.TimingErrorSeconds).FirstOrDefault();
            return holdTarget != null && holdTarget.IsHoldBeat;
        }, "real hold reaches judgement window as the closest target", 15f);
        var click = treatment.GetComponentInChildren<OSHeartHealClickButton>(true);
        Check(click != null && EventSystem.current != null, "actual hold pointer handler exists");
        var pointer = new PointerEventData(EventSystem.current) { button = PointerEventData.InputButton.Left };
        state = RawState(treatment);
        click.OnPointerDown(pointer);
        Check(treatment.IsShockHeld && Field<bool>(holdTarget, "isHolding") && RawState(treatment) > state,
            "hold pointer starts real ReportHoldTick healing");
        float firstTickState = RawState(treatment);
        int resolvedAtHoldStart = Field<int>(treatment, "resolvedBeatCount");
        var pattern = Field<OSHeartHealPuzzleData>(treatment, "currentPattern");
        yield return new WaitForSecondsRealtime(.34f);
        // The preceding tap at 8.25 s misses at 8.34 s, after this hold can
        // start at 8.285 s. Its -1 damage masks three +0.25 hold ticks in
        // the net health value. No other input is sent during this interval,
        // so these newly resolved notes are misses, not successful hits.
        int concurrentMisses = Field<int>(treatment, "resolvedBeatCount") - resolvedAtHoldStart;
        float sustainedHealing = RawState(treatment) - firstTickState + concurrentMisses * pattern.MissDamage;
        Check(treatment.IsShockHeld && Field<bool>(holdTarget, "isHolding")
            && sustainedHealing >= pattern.HoldTickHeal * 2f - .001f,
            "sustained hold continues awarding chart ticks");
        Debug.Log("[Heart Monitor QA] hold sample: healing=" + sustainedHealing.ToString("0.###")
            + ", concurrent misses=" + concurrentMisses + ", raw delta="
            + (RawState(treatment) - firstTickState).ToString("0.###"));
        Check(symbols.Any(image => image.gameObject.activeSelf && image.sprite == heal), "hold ticks also show treatment crosses");
        Check(symbols.Count(image => image.gameObject.activeSelf) <= 6, "rapid hold ticks stay inside fixed feedback pool");
        yield return Shot("04-heart-hold");
        click.OnPointerUp(pointer);
        treatment.StopTreatment();
        yield return null;
        Check(!symbols.Any(image => image.gameObject.activeSelf) && animator.GetCurrentAnimatorStateInfo(0).IsName("Idle"),
            "stopping clears transient symbols and restores Idle");

        int[] poolIds = symbols.Select(image => image.GetInstanceID()).ToArray();
        presentation.ShowTreatmentFeedback(true);
        Check(symbols.Any(image => image.gameObject.activeSelf), "lifecycle fixture has active transient feedback");
        window.Minimize();
        Check(!symbols.Any(image => image.gameObject.activeSelf), "minimizing cleans up active feedback");
        window.RestoreAndBringToFront();
        yield return null;
        Check(!symbols.Any(image => image.gameObject.activeSelf) && animator.GetCurrentAnimatorStateInfo(0).IsName("Idle"),
            "restoring starts with natural idle and no stale symbols");
        presentation.ShowTreatmentFeedback(false);
        yield return new WaitForSecondsRealtime(1f);
        Check(!symbols.Any(image => image.gameObject.activeSelf), "feedback expires without treatment or external cleanup");
        Check(Field<Image[]>(presentation, "feedbackImages").Select(image => image.GetInstanceID()).SequenceEqual(poolIds),
            "feedback lifecycle reuses the same six objects");
        window.Close();
        Check(!presentation.isActiveAndEnabled && !symbols.Any(image => image.gameObject.activeSelf),
            "closing leaves no active monitor feedback");
    }

    static float RawState(OSHeartHealToolWindowController treatment) => Field<float>(treatment, "rawTreatmentState");
    static T Field<T>(object owner, string field) => (T)owner.GetType().GetField(field, Private).GetValue(owner);
    static IEnumerator Wait(Func<bool> condition, string label, float seconds)
    {
        float deadline = Time.unscaledTime + seconds;
        while (!condition() && Time.unscaledTime < deadline) yield return null;
        Check(condition(), label);
    }
    static IEnumerator Shot(string name)
    {
        yield return new WaitForEndOfFrame();
        var image = ScreenCapture.CaptureScreenshotAsTexture();
        try { File.WriteAllBytes(Path.Combine(OutputDirectory, name + ".png"), image.EncodeToPNG()); }
        finally { UnityEngine.Object.Destroy(image); }
        yield return null;
    }
    static void Check(bool value, string label)
    {
        if (!value) throw new InvalidOperationException(label);
        checks.Add(label);
    }
    static string Fingerprint(string path)
    {
        if (!File.Exists(path)) return "absent";
        using (var hash = SHA256.Create()) return Convert.ToBase64String(hash.ComputeHash(File.ReadAllBytes(path)));
    }
}
