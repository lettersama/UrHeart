using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using MrTGame.Core;
using MrTGame.Systems;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// Play Mode integration check; the authored timing/art stay intact during the first pass.
public static class Chapter01SlidesQA
{
    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    static readonly List<string> checks = new List<string>();
    static GameSetup setup;
    static OSChapter01EndingDirector ending;
    static OSChapter01EndingData originalConfiguration, configuration;
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
            || SceneLoadManager.Instance.IsBusy || GameTest.Instance.IsCheckpointJumpRunning)
            return "Wait for active save/scene operation";
        setup = typeof(GameSetup).GetField("activeSetup", BindingFlags.Static | BindingFlags.NonPublic)?.GetValue(null) as GameSetup;
        if (setup == null) return "Active GameSetup required";
        checks.Clear();ending = null;originalConfiguration = configuration = null;
        OutputDirectory = Path.GetFullPath(Path.Combine("Temp", "Chapter01SlidesQA", Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(OutputDirectory);Status = "running";
        GameDataManager.Instance.StartCoroutine(Guarded());return Status;
    }

    static IEnumerator Guarded()
    {
        var data = GameDataManager.Instance;var saves = GameAutosaveService.Instance;
        var original = data.SaveData;var language = data.CurrentLanguageState;
        bool demo = Field<bool>(setup, "isDemo"), background = Application.runInBackground;
        var resources = GameResourceSystem.Instance;bool resourcesEnabled = resources != null && resources.enabled;
        string[] paths = { saves.SavePath, saves.SavePath + ".bak", saves.SavePath + ".tmp" };
        string[] hashes = null;string configurationJson = null;
        var work = new Stack<IEnumerator>();
        try
        {
            saves.EnterTestSession();hashes = paths.Select(Fingerprint).ToArray();
            data.ReplaceSaveData(JsonUtility.FromJson<GameSaveData>(JsonUtility.ToJson(original)));
            Application.runInBackground = true;if (resources != null) resources.enabled = false;
            Set(setup, "isDemo", true);work.Push(Run());
            while (work.Count > 0)
            {
                object next;
                try
                {
                    if (configurationJson == null && originalConfiguration != null) configurationJson = JsonUtility.ToJson(originalConfiguration);
                    if (!work.Peek().MoveNext()) { (work.Pop() as IDisposable)?.Dispose();continue; }
                    next = work.Peek().Current;
                }
                catch (Exception error) { Status = "FAILED: " + error;Debug.LogError("[Chapter 01 Slides QA] " + Status);yield break; }
                if (next is IEnumerator nested) work.Push(nested);else yield return next;
            }
            Check(paths.Select(Fingerprint).SequenceEqual(hashes), "normal save files unchanged");
            Check(saves.IsTestSession && !saves.IsWriting, "test session continues to block writes");
            Check(JsonUtility.ToJson(originalConfiguration) == configurationJson, "authored configuration unchanged");
            Status = "PASS: " + checks.Count + " slide integration checks";
            Debug.Log("[Chapter 01 Slides QA] " + Status + " | " + OutputDirectory);
        }
        finally
        {
            while (work.Count > 0) (work.Pop() as IDisposable)?.Dispose();
            saves.EnterTestSession();saves.EndSession();
            if (ending != null) { Invoke("CancelPresentation");Set(ending, "configuration", originalConfiguration); }
            if (configuration != null) UnityEngine.Object.Destroy(configuration);
            Set(setup, "isDemo", demo);GameManager.Instance?.OpenMainMenuWithoutSave();
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
        Check(GameTest.Instance.JumpToCheckpoint(GameTestCheckpoint.Chapter01EndingAnimation), "isolated ending checkpoint accepted");
        yield return Wait(() => !GameTest.Instance.IsCheckpointJumpRunning, "ending checkpoint loaded", 65);
        Check(GameTest.Instance.JumpSucceeded, "ending checkpoint succeeded");
        ending = OS_Handle.Instance?.Chapter01Ending;Check(ending != null && ending.Configuration != null, "ending director configured");
        originalConfiguration = ending.Configuration;configuration = UnityEngine.Object.Instantiate(originalConfiguration);
        configuration.hideFlags = HideFlags.DontSave;Set(ending, "configuration", configuration);
        Check(configuration.slides?.Length == 6 && configuration.slides.All(s => s != null)
            && configuration.slides.Take(5).All(s => s.image != null), "six authored shots with five still sprites");
        var frames = configuration.slides[5].frames;
        Check(frames?.Length == 4 && frames.All(s => s != null) && frames.Distinct().Count() == 4, "sixth shot has four distinct authored frames");
        float duration = configuration.slides.Sum(s => s.holdSeconds);
        Check(duration >= 14f && duration <= 17f, "authored six-shot duration is 14-17 seconds: " + duration.ToString("F2"));
        Check(configuration.doorSound != null, "door knock audio configured");
        Resume(OSChapter01EndingPhase.Slides, 0);
        var image = Field<Image>(ending, "slideImage");
        yield return Wait(() => ending.Phase == OSChapter01EndingPhase.Slides && image.IsActive()
            && image.sprite == configuration.slides[0].image, "first real slide visible");
        var entered = Enumerable.Repeat(-1f, 6).ToArray();var shots = new bool[6];var seenFrames = new List<int>();
        float started = Time.realtimeSinceStartup, deadline = started + duration + 5;
        while (ending.Phase == OSChapter01EndingPhase.Slides && Time.realtimeSinceStartup < deadline)
        {
            int index = Saved.slideIndex;CheckRange(index >= 0 && index < 6, "slide index stays in authored range");
            var door = Field<Button>(ending, "doorButton");
            CheckRange(OS_Handle.Instance.IsSystemInputLocked && (!door.IsActive() || !door.IsInteractable()), "slides block door and OS input");
            CheckRange(image.IsActive() && (index < 5 ? image.sprite == configuration.slides[index].image : frames.Contains(image.sprite)), "authored sprite matches current shot");
            if (entered[index] < 0) { entered[index] = Time.realtimeSinceStartup;checks.Add("shot " + (index + 1) + " appeared: " + image.sprite.name); }
            if (index == 5)
            {
                int frame = Array.IndexOf(frames, image.sprite);
                if (seenFrames.Count == 0 || seenFrames[seenFrames.Count - 1] != frame) seenFrames.Add(frame);
            }
            if (!shots[index] && Time.realtimeSinceStartup - entered[index] >= configuration.slides[index].holdSeconds * .45f)
            { shots[index] = true;yield return Shot("0" + (index + 1) + "-slide"); }
            yield return null;
        }
        Check(shots.All(s => s) && entered.All(t => t >= 0), "all six shots captured near their midpoint");
        Check(seenFrames.SequenceEqual(new[] { 0, 1, 2, 3 }), "sixth-shot frames play 0, 1, 2, 3 once in order");
        yield return WaitDoor();checks.Add("real slideshow elapsed: " + (Time.realtimeSinceStartup - started).ToString("F2") + " seconds");
        var treatment = Field<Material>(ending, "slideTreatment");float shaderClock = treatment != null ? treatment.GetFloat("_Phase") : 0;
        float heldUntil = Time.realtimeSinceStartup + 1.1f;
        while (Time.realtimeSinceStartup < heldUntil)
        {
            CheckRange(ending.Phase == OSChapter01EndingPhase.Door && image.sprite == frames[3] && !Saved.completed, "door waits for input over frozen final frame");
            CheckRange(treatment == null || treatment.GetFloat("_Phase") == shaderClock, "held shader clock stays frozen");yield return null;
        }
        Check(!CursorHandle.Instance.IsScriptControlledBy(ending), "door releases cursor for player");
        yield return Shot("07-final-frame-door");ClickDoor();float clicked = Time.realtimeSinceStartup;bool knocked = false;
        deadline = clicked + configuration.doorKnockSeconds + configuration.demoWipeSeconds + 5;
        while (ending.Phase != OSChapter01EndingPhase.Completed && Time.realtimeSinceStartup < deadline)
        {
            CheckRange(ending.Phase == OSChapter01EndingPhase.Door || ending.Phase == OSChapter01EndingPhase.DoorKnocked, "Demo never enters later-chapter animation");
            knocked |= ending.Phase == OSChapter01EndingPhase.DoorKnocked;yield return null;
        }
        Check(knocked && ending.Phase == OSChapter01EndingPhase.Completed && Saved.completed, "real click passes DoorKnocked and completes Demo");
        Check(Time.realtimeSinceStartup - clicked >= configuration.doorKnockSeconds, "Demo preserves the knock wait before completion");
        yield return null;CheckClean();yield return Shot("08-demo-completed");
        configuration.initialBlackSeconds = .02f;foreach (var slide in configuration.slides) slide.holdSeconds = .35f;
        foreach (int index in new[] { 3, 5 })
        {
            Resume(OSChapter01EndingPhase.Slides, index);Sprite first = index == 5 ? frames[0] : configuration.slides[index].image;
            yield return Wait(() => ending.Phase == OSChapter01EndingPhase.Slides && Saved.slideIndex == index
                && image.sprite == first, "resume begins at requested shot " + (index + 1));
            yield return WaitDoor();Check(image.sprite == frames[3], "resumed shot " + (index + 1) + " reaches final held frame");
        }
        Resume(OSChapter01EndingPhase.Door, 5);yield return WaitDoor();
        Check(image.sprite == frames[3], "restored Door uses last animation frame");
        yield return Shot("09-restored-door");Set(setup, "isDemo", false);
        Resume(OSChapter01EndingPhase.Door, 5);yield return WaitDoor();ClickDoor();
        yield return Wait(() => ending.Phase == OSChapter01EndingPhase.Animation, "full-game click continues to Animation", 8);
        Check(!Field<GameObject>(ending, "endCard").activeInHierarchy, "full-game branch does not show Demo card");
        Invoke("CancelPresentation");GameDataManager.Instance.OSProcessData.chapter01Ending = new OSChapter01EndingSaveData();
        yield return null;CheckClean();Check(!ending.IsPresenting && !CursorHandle.Instance.IsScriptControlledBy(ending), "cancel releases presentation and cursor");
        GameManager.Instance.OpenMainMenuWithoutSave();yield return Wait(() => GameManager.Instance.Phase == GamePhase.Boot && !GameManager.Instance.IsTransitioning, "QA exits to menu");
    }

    static OSChapter01EndingSaveData Saved => GameDataManager.Instance.OSProcessData.chapter01Ending;
    static void Resume(OSChapter01EndingPhase phase, int index) => GameDataManager.Instance.OSProcessData.chapter01Ending =
        new OSChapter01EndingSaveData { phase = phase, slideIndex = index };
    static IEnumerator WaitDoor() => Wait(() => ReferenceEquals(Field<OSChapter01EndingSaveData>(ending, "state"), Saved)
        && ending.Phase == OSChapter01EndingPhase.Door && Field<bool>(ending, "waitingDoor")
        && Field<Button>(ending, "doorButton").IsInteractable(), "door is ready for real input");
    static void CheckClean()
    {
        Check(Field<Material>(ending, "slideTreatment") == null, "runtime slide material released");
        Check(ending.transform.Find("Chapter01 room tone") == null && ending.transform.Find("Chapter01 footsteps and tools") == null,
            "temporary slide audio objects destroyed");
        Check(Field<RenderTexture>(ending, "videoTexture") == null && Field<Texture2D>(ending, "capturedFrame") == null
            && Field<Material>(ending, "runtimeFreeze") == null, "movie and freeze media released");
    }
    static void ClickDoor()
    {
        var button = Field<Button>(ending, "doorButton");var rect = (RectTransform)button.transform;
        var pointer = new PointerEventData(EventSystem.current) { button = PointerEventData.InputButton.Left,
            position = RectTransformUtility.WorldToScreenPoint(button.GetComponentInParent<Canvas>().worldCamera, rect.TransformPoint(rect.rect.center)) };
        var hits = new List<RaycastResult>();EventSystem.current.RaycastAll(pointer, hits);
        Check(hits.Count > 0 && hits[0].gameObject.GetComponentInParent<Button>() == button, "wooden door is frontmost clickable target");
        ExecuteEvents.Execute(button.gameObject, pointer, ExecuteEvents.pointerClickHandler);
    }
    static IEnumerator Wait(Func<bool> condition, string label, float seconds = 8)
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
    static void CheckRange(bool ok, string label) { if (!ok) throw new InvalidOperationException(label); }
    static void Check(bool ok, string label) { CheckRange(ok, label);checks.Add(label); }
    static T Field<T>(object owner, string name) => (T)owner.GetType().GetField(name, Private).GetValue(owner);
    static void Set(object owner, string name, object value) => owner.GetType().GetField(name, Private).SetValue(owner, value);
    static void Invoke(string method) => typeof(OSChapter01EndingDirector).GetMethod(method, Private).Invoke(ending, null);
    static string Fingerprint(string path)
    {
        if (!File.Exists(path)) return "missing";
        using (var sha = SHA256.Create()) return Convert.ToBase64String(sha.ComputeHash(File.ReadAllBytes(path)));
    }
}
