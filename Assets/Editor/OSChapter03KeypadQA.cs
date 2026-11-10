using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using MrTGame.Core;
using MrTGame.Systems;
using Newtonsoft.Json;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.Rendering;
using UnityEngine.UI;

// Opt-in isolated Play Mode QA. Uses the real laboratory checkpoint, Home door,
// modal raycasts, callbacks, audio events and rendered frames. Never focuses the Editor.
public static class OSChapter03KeypadQA
{
    const BindingFlags Fields = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    const float ShakeSeconds = .32f;
    static readonly List<string> checks = new List<string>(), screenshots = new List<string>();
    static readonly List<object> observations = new List<object>();
    static readonly List<SoundSample> sounds = new List<SoundSample>();
    static readonly List<ShakeSample> shakeFrames = new List<ShakeSample>();
    static GameDataManager data;
    static GameSetup setup;
    static OS_Handle os;
    static OSMemoryWindowController memory;
    static OSChapter03HomeController home;
    static OSChapter03StoryDirector story;
    static OSWindowUnit memoryWindow;
    static Mouse testMouse;
    static Camera renderCamera;
    static string context, shakeScreenshot;
    static bool cancel, recordingShake, shakeShotRequested;
    static int shakeStartFrame, lastRenderFrame;
    static Vector2 displayHome, frameHome;
    static Exception samplingError;

    sealed class SoundSample
    {
        public string context, key;
        public int frame;
        public float time, gain, pitch;
    }

    sealed class ShakeSample
    {
        public int frame;
        public float time, dt, remaining, x, y, frameX, frameY;
        public bool paused;
    }

    static OSChapter03StoryPresentation P => story?.Presentation;
    static OSChapter03StoryState S => story?.State;
    static OSHomeView V => home?.View;
    static RectTransform Frame => Get(P, "modalFrame") as RectTransform;
    static RectTransform Display => Get(P, "keypadDisplay") as RectTransform;
    static RectTransform Slots => Get(P, "keypadSlots") as RectTransform;
    static TMP_Text Feedback => Get(P, "keypadFeedback") as TMP_Text;
    static string Value => Get(P, "keypadValue") as string;
    public static string Status { get; private set; } = "idle";
    public static string OutputDirectory { get; private set; }
    public static void Cancel() => cancel = true;

    public static string Start()
    {
        if (Status == "running") return Status;
        var saves = GameAutosaveService.Instance;
        if (!Application.isPlaying || saves == null || GameDataManager.Instance == null || GameManager.Instance == null
            || SceneLoadManager.Instance == null || GameSessionFlow.Instance == null || GameTest.Instance == null)
            return "Play Mode initialization required";
        if (saves.IsBusy || saves.IsWriting || GameManager.Instance.IsTransitioning || SceneLoadManager.Instance.IsBusy
            || GameSessionFlow.Instance.IsBusy || GameTest.Instance.IsCheckpointJumpRunning)
            return "Wait for active save/scene operation";
        if (saves.IsTestSession) return "Fresh Play Mode required to hash the original Editor save path";
        setup = typeof(GameSetup).GetField("activeSetup", BindingFlags.Static | BindingFlags.NonPublic)?.GetValue(null) as GameSetup;
        if (setup == null) return "Active GameSetup is required";
        data = GameDataManager.Instance; context = "initialization"; cancel = recordingShake = shakeShotRequested = false;
        samplingError = null; renderCamera = null; lastRenderFrame = -1; shakeScreenshot = null;
        checks.Clear(); screenshots.Clear(); observations.Clear(); sounds.Clear(); shakeFrames.Clear();
        OutputDirectory = Path.GetFullPath(Path.Combine("Temp", "OSChapter03KeypadQA", Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(OutputDirectory); Status = "running";
        data.StartCoroutine(Guarded()); return Status;
    }

    static IEnumerator Guarded()
    {
        var saves = GameAutosaveService.Instance;
        var original = data.SaveData;
        var language = data.CurrentLanguageState;
        var resources = GameResourceSystem.Instance;
        bool resourcesEnabled = resources != null && resources.enabled;
        bool background = Application.runInBackground, demo = (bool)Get(setup, "isDemo");
        float volume = AudioListener.volume;
        var random = UnityEngine.Random.state;
        var previousMouse = Mouse.current;
        string[] files = new[] { saves.SavePath, saves.SavePath + ".bak", saves.SavePath + ".tmp" }
            .Concat(Directory.GetFiles("Assets/Scenes", "*.unity", SearchOption.AllDirectories)).ToArray();
        string[] hashes = null;
        var work = new Stack<IEnumerator>();
        Exception failure = null;
        try
        {
            saves.EnterTestSession(); hashes = files.Select(Hash).ToArray();
            saves.UseIsolatedStorageForTests(OutputDirectory); saves.EnterTestSession();
            data.ReplaceSaveData(Clone(original)); Set(setup, "isDemo", false);
            Application.runInBackground = true; AudioListener.volume = 0f;
            if (resources != null) resources.enabled = false;
            testMouse = InputSystem.AddDevice<Mouse>("Chapter03KeypadQAMouse");
            MoveMouse(new Vector2(-10000f, -10000f));
            work.Push(Run());
            while (work.Count > 0)
            {
                object next = null;
                try
                {
                    testMouse?.MakeCurrent(); story?.DebugSetFocus(true); os?.Chapter03Entry?.DebugSetFocus(true);
                    if (samplingError != null) throw new InvalidOperationException("Keypad frame sampling failed", samplingError);
                    if (cancel) throw new OperationCanceledException("Keypad QA cancelled");
                    if (!work.Peek().MoveNext()) { (work.Pop() as IDisposable)?.Dispose(); continue; }
                    next = work.Peek().Current;
                }
                catch (Exception error) { failure = error; }
                if (failure != null)
                {
                    Status = "FAILED at " + context + "\n" + failure;
                    string path = Path.Combine(OutputDirectory, "failure.png"); screenshots.Add(path);
                    ScreenCapture.CaptureScreenshot(path);
                    yield return null; yield return new WaitForSecondsRealtime(.25f);
                    Debug.LogError("[Chapter03 Keypad QA] " + Status); break;
                }
                if (next is IEnumerator nested) work.Push(nested); else yield return next;
            }
            if (failure == null)
            {
                Check(files.Select(Hash).SequenceEqual(hashes), "original save files and all scene files remain unchanged");
                Status = "PASS";
            }
        }
        finally
        {
            recordingShake = false; RenderPipelineManager.endCameraRendering -= SampleShake;
            if (story != null) story.DebugSfxPlayed -= ObserveSound;
            while (work.Count > 0) (work.Pop() as IDisposable)?.Dispose();
            try
            {
                if (GameTest.Instance?.IsCheckpointJumpRunning == true) GameTest.Instance.StopAllCoroutines();
                os?.LinkMessageController?.NotifyOSHidden(); story?.ResetPresentation(); home?.Suspend();
                story?.DebugSetFocus(false); os?.Chapter03Entry?.DebugSetFocus(false);
                os?.Chapter03Entry?.ResetPresentation(); os?.Chapter02FinalTalk?.ResetPresentation();
                GameTest.Instance?.GetComponent<GameTestConsole>()?.SetOpen(false);
                saves.EnterTestSession(); saves.EndSession(); GameManager.Instance?.OpenMainMenuWithoutSave();
                data.ReplaceSaveData(original); data.SetCurrentLanguage(language);
                if (setup != null) Set(setup, "isDemo", demo);
                if (resources != null) resources.enabled = resourcesEnabled;
                Application.runInBackground = background; AudioListener.volume = volume;
                if (testMouse != null && testMouse.added) InputSystem.RemoveDevice(testMouse);
                if (previousMouse != null && previousMouse.added) previousMouse.MakeCurrent();
                UnityEngine.Random.state = random;
                if (!ReferenceEquals(data.SaveData, original) || data.CurrentLanguageState != language || GameSetup.IsDemo != demo
                    || Application.runInBackground != background || AudioListener.volume != volume
                    || resources != null && resources.enabled != resourcesEnabled
                    || previousMouse != null && previousMouse.added && Mouse.current != previousMouse
                    || !UnityEngine.Random.state.Equals(random)) throw new InvalidOperationException("Original save/settings were not restored");
                if (hashes != null && !files.Select(Hash).SequenceEqual(hashes)) throw new IOException("Original save or scene files changed");
                checks.Add("cleanup restores original save, language, Demo mode, listener, resources, background, mouse and random state");
            }
            catch (Exception error) { Status = "FAILED during cleanup: " + error + "\n" + Status; }
            if (Status == "running") Status = "FAILED: interrupted at " + context;
            if (Status == "PASS") Status = "PASS: " + checks.Count + " checks";
            File.WriteAllText(Path.Combine(OutputDirectory, "results.json"), JsonConvert.SerializeObject(new {
                status = Status, context, checks, observations, screenshots, sounds, shakeFrames,
                originalFiles = files, originalHashes = hashes, finalHashes = files.Select(Hash).ToArray(),
                mode = "muted isolated background keypad QA",
                limitations = "No Editor focus and no production animation ticking or parameter changes. Modal buttons use actual screen-coordinate GraphicRaycaster dispatch. Home door uses its ordinary tile-click path. Error movement is sampled on actual OS camera renders; only Memory typing is accelerated while draining narration. Audio is verified through actual new-source DebugSfxPlayed events, not audible listening. Exit Play Mode after this isolated run."
            }, Formatting.Indented));
            File.WriteAllText(Path.Combine(OutputDirectory, "results.txt"), Status + "\n" + string.Join("\n", checks));
            Debug.Log("[Chapter03 Keypad QA] " + Status + " | " + OutputDirectory);
            data = null; setup = null; os = null; memory = null; home = null; story = null;
            memoryWindow = null; testMouse = null; renderCamera = null;
        }
    }

    static IEnumerator Run()
    {
        data.SetCurrentLanguage(LanguageState.zh_CN);
        yield return EnterLaboratoryCheckpoint();
        story.DebugSfxPlayed += ObserveSound;
        RenderPipelineManager.endCameraRendering += SampleShake;
        yield return OpenKeypad();
        context = "Chinese keypad structure and input";
        CheckLayout(); CheckDigits(""); CheckText(Feedback, "initial hint", true);
        yield return Capture("00-zh_CN-keypad-empty.png");

        yield return PressDigits("0");
        int before = sounds.Count;
        yield return ClickButton("Submit");
        Check(Value == "0" && P.IsModalOpen && !S.Has("lab_open") && Near((float)Get(P, "keypadShake"), 0f)
            && sounds.Count == before, "incomplete password does not submit or shake");
        yield return PressDigits("123"); CheckDigits("0123");
        yield return ClickButton("Digit4"); CheckDigits("0123");
        before = sounds.Count;
        yield return ClickButton("Backspace"); CheckDigits("012");
        Check(sounds.Skip(before).Count(sound => sound.key.StartsWith("Chapter03_KeypadPress_", StringComparison.Ordinal)) == 1,
            "the real Backspace click plays exactly one keypad press sound");
        yield return ClearDigits();
        before = sounds.Count; yield return ClickButton("Backspace");
        Check(Value == string.Empty && sounds.Count == before, "empty Backspace leaves the display unchanged");

        yield return PressDigits("1111");
        yield return WrongPassword(true);
        CheckDigits(""); CheckText(Feedback, "Chinese wrong-code feedback", true);
        yield return Capture("01-zh_CN-keypad-wrong-rest.png");

        yield return PressDigits("07");
        data.SetCurrentLanguage(LanguageState.en);
        yield return Wait(() => Feedback.text == OSChapter03StoryContent.Instance.Text("ui.password.format"),
            "open keypad refreshes its prompt after a real language change");
        yield return Settled(); CheckDigits("07");
        Check(Frame.Find("Title").GetComponent<TMP_Text>().text == OSChapter03StoryContent.Instance.Text("ui.password.title"),
            "open keypad title changes language without resetting entered digits");
        yield return CloseKeypad();

        var languages = (LanguageState[])Enum.GetValues(typeof(LanguageState));
        Check(languages.Length == 10, "the localization sweep includes all ten supported languages");
        foreach (var language in languages)
        {
            context = "localized keypad " + language;
            data.SetCurrentLanguage(language);
            yield return OpenKeypad();
            yield return PressDigits("07");
            CheckDigits("07"); CheckLayout();
            Check(Feedback.text == OSChapter03StoryContent.Instance.Text("ui.password.format"), "fresh keypad uses the current language prompt");
            CheckText(Feedback, "format prompt", true);
            CheckText(Frame.Find("Title").GetComponent<TMP_Text>(), "title", false);
            yield return Capture(language + "-keypad-format.png");
            yield return PressDigits("99");
            yield return WrongPassword(false);
            Check(Feedback.text == OSChapter03StoryContent.Instance.Text("ui.password.error"), "wrong password uses the current language error text");
            CheckText(Feedback, "error prompt", true); CheckDigits("");
            yield return Capture(language + "-keypad-error.png");
            yield return CloseKeypad();
        }

        context = "correct laboratory password";
        data.SetCurrentLanguage(LanguageState.zh_CN); yield return OpenKeypad();
        yield return PressDigits("0710"); CheckDigits("0710");
        yield return Capture("02-zh_CN-keypad-correct-ready.png");
        before = sounds.Count; yield return ClickButton("Submit");
        Check(S.Has("lab_open") && story.CurrentPhase == OSChapter03StoryDirector.Phase.Lab && !P.IsModalOpen,
            "real 0710 submission unlocks the laboratory and closes the keypad");
        Check(sounds.Skip(before).Count(sound => sound.key == "Chapter03_KeypadAccept") == 1,
            "correct submission plays one accept sound");
        yield return Drive(() => V.CurrentRoom?.id == "lab" && !V.IsTransitioning, "production story enters the laboratory after 0710");
        Check(!V.Layout.FindRoom("east").FindDoor("lab_door").locked, "laboratory door remains unlocked after entering");
        yield return Capture("03-laboratory-after-0710.png");
    }

    static IEnumerator EnterLaboratoryCheckpoint()
    {
        context = "laboratory entrance checkpoint";
        var console = GameTest.Instance.GetComponent<GameTestConsole>();
        Check(console != null, "real test console exists");
        console.SetOpen(true); yield return Settled();
        var button = console.GetComponentsInChildren<Button>(true).FirstOrDefault(item => item.name == "第三章_实验室门口");
        Check(button != null && button.IsInteractable(), "new laboratory entrance console row is available");
        button.onClick.Invoke();
        yield return Wait(() => !GameTest.Instance.IsCheckpointJumpRunning && !GameManager.Instance.IsTransitioning
            && !SceneLoadManager.Instance.IsBusy && OS_Handle.Instance != null, "laboratory checkpoint reload finishes", 80f);
        Check(GameTest.Instance.JumpSucceeded && GameAutosaveService.Instance.IsTestSession, "checkpoint succeeds inside the isolated test session");
        os = OS_Handle.Instance; memory = os.MemoryWindowController;
        home = memory.GetComponent<OSChapter03HomeController>(); story = os.Chapter03Story;
        memoryWindow = memory.GetComponent<OSWindowUnit>(); renderCamera = memoryWindow.GetComponentInParent<Canvas>().rootCanvas.worldCamera;
        Check(story != null && home != null && renderCamera != null, "story, Home and the real OS render camera are available");
        yield return Wait(() => !console.IsOpen && story.IsAvailable && Ready(home.HomeWindow) && V.CurrentRoom?.id == "east"
            && (bool)Call(V, "AcceptsInput"), "east corridor is ready for normal input");
        Check(memory.CurrentData?.DataIndex == 9210 && S.Has("password_ready") && !S.Has("lab_open")
            && S.memoryQueue.Count == 0 && S.liveQueue.Count == 0, "checkpoint starts beside the locked lab with no pre-opened keypad or queued dialogue");
    }

    static IEnumerator OpenKeypad()
    {
        yield return Drive(() => S.memoryQueue.Count == 0 && !P.IsModalOpen && story.IsAvailable && !story.BlocksHomeInput,
            "previous password interaction has finished");
        var door = V.CurrentRoom.FindDoor("lab_door");
        Check(door != null && door.locked && (bool)Call(V, "AcceptsInput"), "the real laboratory door accepts input");
        Call(V, "Click", door.tile);
        yield return Drive(() => P.IsModalOpen && S.memoryQueue.Count > 0 && S.memoryQueue[0].type == "keypad",
            "ordinary Home door click opens the real keypad");
        yield return Wait(() => (bool)Get(P, "CanInteract"), "keypad input guard finishes");
        yield return Settled();
    }

    static IEnumerator CloseKeypad()
    {
        yield return ClickButton("Close");
        yield return Drive(() => !P.IsModalOpen && S.memoryQueue.Count == 0, "closing the keypad drains the queued password hint");
        Check(!S.Has("lab_open"), "dismissing a rejected code leaves the laboratory locked");
    }

    static IEnumerator PressDigits(string digits)
    {
        foreach (char digit in digits)
        {
            string expected = Value.Length < 4 ? Value + digit : Value;
            yield return ClickButton("Digit" + digit);
            CheckDigits(expected);
        }
    }

    static IEnumerator ClearDigits()
    {
        while (!string.IsNullOrEmpty(Value)) yield return ClickButton("Backspace");
        CheckDigits("");
    }

    static IEnumerator WrongPassword(bool record)
    {
        Check(Value?.Length == 4 && Value != "0710", "a complete incorrect code is ready");
        int before = sounds.Count;
        displayHome = (Vector2)Get(P, "keypadDisplayHome"); frameHome = Frame.anchoredPosition;
        yield return ClickButton("Submit", record ? (Action)(() => {
            shakeFrames.Clear(); shakeStartFrame = Time.frameCount; lastRenderFrame = -1;
            recordingShake = true; shakeShotRequested = false;
            shakeScreenshot = Path.Combine(OutputDirectory, "zh_CN-keypad-wrong-shake.png");
        }) : null);
        Check(!S.Has("lab_open") && P.IsModalOpen && Value == string.Empty, "wrong submission clears digits and keeps the door locked");
        Check(sounds.Skip(before).Count(sound => sound.key == "Chapter03_KeypadError") == 1, "wrong submission plays one error sound");
        yield return Wait(() => Near((float)Get(P, "keypadShake"), 0f) && Vector2.Distance(Display.anchoredPosition, displayHome) < .001f,
            "error display returns exactly to its resting position", 3f);
        yield return null;
        if (record)
        {
            yield return Wait(() => shakeFrames.Any(frame => frame.remaining <= 0f), "a rendered resting frame completes the shake trace", 3f);
            recordingShake = false;
            Check(shakeFrames.Count >= 4 && shakeFrames.Any(frame => frame.x > .5f) && shakeFrames.Any(frame => frame.x < -.5f),
                "rendered error display moves both left and right");
            Check(shakeFrames.All(frame => Mathf.Abs(frame.x) <= 3.001f && Mathf.Abs(frame.y) < .001f
                && Mathf.Abs(frame.frameX) < .001f && Mathf.Abs(frame.frameY) < .001f), "only the display shakes horizontally while the frame stays fixed");
            var moving = shakeFrames.Where(frame => frame.frame >= shakeStartFrame).ToArray();
            int end = Array.FindIndex(moving, frame => frame.remaining <= 0f);
            Check(end > 0, "shake trace includes movement before its first resting frame");
            float elapsed = ShakeSeconds - moving[0].remaining;
            for (int i = 1; i <= end; i++) if (!moving[i].paused) elapsed += Mathf.Min(moving[i].dt, .1f);
            float tolerance = Mathf.Max(.025f, moving.Take(end + 1).Max(frame => Mathf.Min(frame.dt, .1f))) + .005f;
            Check(Mathf.Abs(elapsed - ShakeSeconds) <= tolerance, "display shake lasts 0.32 seconds of the existing unpaused animation clock");
            observations.Add(new { context, type = "error shake timing", expectedSeconds = ShakeSeconds, sampledActiveSeconds = elapsed,
                tolerance, firstFrame = moving[0].frame, endFrame = moving[end].frame, renderedSamples = moving.Length });
            Check(shakeShotRequested, "Chinese error screenshot is requested during visible display displacement");
            yield return Wait(() => File.Exists(shakeScreenshot) && new FileInfo(shakeScreenshot).Length > 100,
                "Chinese moving-error screenshot is written", 10f);
        }
    }

    static void SampleShake(ScriptableRenderContext renderContext, Camera camera)
    {
        if (!recordingShake || camera != renderCamera || Time.frameCount == lastRenderFrame || Display == null || Frame == null) return;
        try
        {
            lastRenderFrame = Time.frameCount;
            Vector2 delta = Display.anchoredPosition - displayHome, frameDelta = Frame.anchoredPosition - frameHome;
            shakeFrames.Add(new ShakeSample { frame = Time.frameCount, time = Time.realtimeSinceStartup, dt = Time.unscaledDeltaTime,
                remaining = (float)Get(P, "keypadShake"), x = delta.x, y = delta.y, frameX = frameDelta.x, frameY = frameDelta.y, paused = P.Paused });
            if (!shakeShotRequested && Mathf.Abs(delta.x) >= 2f)
            {
                shakeShotRequested = true; screenshots.Add(shakeScreenshot); ScreenCapture.CaptureScreenshot(shakeScreenshot);
                observations.Add(new { context, type = "rendered wrong-code screenshot", frame = Time.frameCount, displacement = delta.x, path = shakeScreenshot });
            }
        }
        catch (Exception error) { samplingError = error; recordingShake = false; }
    }

    static void CheckDigits(string expected)
    {
        Check(Value == expected, "keypadValue matches input: " + expected);
        Check(Frame.Find("Digits") == null && !Frame.GetComponentsInChildren<TMP_Text>(true).Any(label => label.name == "Digits"),
            "the old Digits TMP is absent");
        var lines = Slots.GetComponentsInChildren<Image>().Where(image => image.name == "Slot").OrderBy(image => image.rectTransform.anchoredPosition.x).ToArray();
        var figures = Slots.GetComponentsInChildren<RectTransform>().Where(rect => rect.name == "Figure").OrderBy(rect => rect.anchoredPosition.x).ToArray();
        Check(lines.Length == 4 && figures.Length == expected.Length, "four slot underlines and one Figure per entered digit are visible");
        Check(lines.Select((line, index) => Near(line.rectTransform.rect.width, index == expected.Length ? 15f : 11f)
            && Near(line.rectTransform.rect.height, 1f)).All(value => value), "only the next unfilled underline is longer");
        Check(figures.All(figure => {
            var pixels = figure.GetComponentsInChildren<Image>();
            return pixels.Length > 0 && pixels.All(pixel => pixel.name == "DigitPixel"
                && Near(pixel.rectTransform.rect.width, 3f) && Near(pixel.rectTransform.rect.height, 3f));
        }), "display figures use three-pixel bitmap cells");
    }

    static void CheckLayout()
    {
        var divider = Frame.Find("Divider") as RectTransform;
        var buttons = Frame.GetComponentsInChildren<Button>().Where(button => button.name.StartsWith("Digit", StringComparison.Ordinal)
            || button.name == "Backspace" || button.name == "Submit").ToArray();
        Check(buttons.Length == 12 && Display != null && Slots != null && Feedback != null, "keypad contains one display and its twelve-key grid");
        Rect display = BoundsIn(Frame, Display), feedback = BoundsIn(Frame, Feedback.rectTransform), separator = BoundsIn(Frame, divider);
        var keys = buttons.Select(button => BoundsIn(Frame, (RectTransform)button.transform)).ToArray();
        float left = keys.Min(rect => rect.xMin) - Frame.rect.xMin;
        float right = Frame.rect.xMax - keys.Max(rect => rect.xMax);
        float bottom = keys.Min(rect => rect.yMin) - Frame.rect.yMin;
        float headerGap = separator.yMin - display.yMax, gridGap = feedback.yMin - keys.Max(rect => rect.yMax);
        Check(Near(left, right) && Near(left, bottom) && left > 0f, "key grid has equal left, right and bottom margins");
        Check(Near(headerGap, 10f) && Near(gridGap, 10f), "display and key grid retain the two ten-pixel main spacing intervals");
        Check(!Frame.GetComponentsInChildren<Image>(true).Any(image => image.sprite != null), "keypad frame and keys contain no metal-plate texture");
        Check(buttons.All(button => Contains(Frame.rect, BoundsIn(Frame, (RectTransform)button.transform)))
            && Contains(Frame.rect, display) && Contains(Frame.rect, feedback), "display, feedback and all keys fit inside the frame");
        Rect first = BoundsIn(Frame, Frame.Find("Digit1") as RectTransform);
        Rect second = BoundsIn(Frame, Frame.Find("Digit2") as RectTransform);
        Rect fourth = BoundsIn(Frame, Frame.Find("Digit4") as RectTransform);
        observations.Add(new { context, type = "layout", frameSize = Vec(Frame.rect.size), displaySize = Vec(Display.rect.size),
            leftMargin = left, rightMargin = right, bottomMargin = bottom, headerGap, feedbackToKeys = gridGap,
            displayToFeedback = display.yMin - feedback.yMax, horizontalKeyGap = second.xMin - first.xMax,
            verticalKeyGap = first.yMin - fourth.yMax, hintWidth = Feedback.rectTransform.rect.width,
            note = "The authored ten-pixel rhythm is between main sections; grid gutters and the display-to-hint gap are reported separately." });
    }

    static void CheckText(TMP_Text label, string kind, bool isFeedback)
    {
        Check(label != null && !string.IsNullOrWhiteSpace(label.text), kind + " is nonempty");
        label.ForceMeshUpdate(true);
        Rect rect = label.rectTransform.rect;
        var preferred = label.GetPreferredValues(label.text, rect.width, float.PositiveInfinity);
        var missing = new HashSet<string>(); var escaped = new HashSet<string>(); int visible = 0;
        for (int i = 0; i < label.textInfo.characterCount; i++)
        {
            var c = label.textInfo.characterInfo[i];
            if (!c.isVisible) continue;
            visible++;
            if (c.textElement?.glyph == null || c.textElement.glyph.index == 0 || c.textElement.unicode != c.character)
                missing.Add("U+" + ((int)c.character).ToString("X4"));
            if (c.bottomLeft.x < rect.xMin - 1f || c.topRight.x > rect.xMax + 1f
                || c.bottomLeft.y < rect.yMin - 1f || c.topRight.y > rect.yMax + 1f)
                escaped.Add("U+" + ((int)c.character).ToString("X4"));
        }
        Check(visible > 0 && missing.Count == 0, kind + " renders every visible glyph: " + string.Join(",", missing));
        Check(!label.isTextTruncated && !label.isTextOverflowing && preferred.x <= rect.width + .1f
            && preferred.y <= rect.height + .1f && escaped.Count == 0, kind + " fits without overflow or clipping: " + string.Join(",", escaped));
        Check(label.fontSize >= 8f && label.fontSize <= 11f && Near(label.fontSize, Mathf.Round(label.fontSize)),
            kind + " fits using the authored integer 8-to-11 font-size range");
        if (isFeedback) Check(Near(rect.width, 196f) && label.textWrappingMode == TextWrappingModes.NoWrap,
            "feedback uses the 196-pixel single-line area");
        observations.Add(new { context, type = "typography", language = data.CurrentLanguageState.ToString(), kind,
            label.text, label.fontSize, font = label.font?.name, rect = Vec(rect.size), preferred = Vec(preferred),
            visibleCharacters = visible, characterCount = label.textInfo.characterCount, label.isTextTruncated, label.isTextOverflowing,
            missing = missing.ToArray(), escaped = escaped.ToArray() });
    }

    static IEnumerator ClickButton(string name, Action beforeClick = null)
    {
        var button = Frame?.GetComponentsInChildren<Button>(true).FirstOrDefault(item => item.name == name);
        Check(button != null && button.IsInteractable() && button.gameObject.activeInHierarchy && (bool)Get(P, "CanInteract"),
            "real modal button accepts input: " + name);
        var rect = (RectTransform)button.transform;
        var canvas = rect.GetComponentInParent<Canvas>();
        Camera camera = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
        Vector2 point = RectTransformUtility.WorldToScreenPoint(camera, rect.TransformPoint(rect.rect.center));
        MoveMouse(point); yield return Settled();
        var pointer = new PointerEventData(EventSystem.current) { position = point, pressPosition = point, pointerId = -1,
            button = PointerEventData.InputButton.Left, clickCount = 1, eligibleForClick = true };
        var hits = new List<RaycastResult>(); EventSystem.current.RaycastAll(pointer, hits);
        var first = hits.Count > 0 ? hits[0] : default;
        Check(first.gameObject != null && first.module is GraphicRaycaster && first.gameObject.GetComponentInParent<Button>() == button,
            "topmost real raycast hits " + name);
        pointer.pointerCurrentRaycast = pointer.pointerPressRaycast = first;
        ExecuteEvents.ExecuteHierarchy(first.gameObject, pointer, ExecuteEvents.pointerDownHandler);
        ExecuteEvents.ExecuteHierarchy(first.gameObject, pointer, ExecuteEvents.pointerUpHandler);
        beforeClick?.Invoke();
        Check(ExecuteEvents.ExecuteHierarchy(first.gameObject, pointer, ExecuteEvents.pointerClickHandler) != null,
            "screen-coordinate pointer click dispatches " + name);
        yield return null;
    }

    static void ObserveSound(string key, float gain, float pitch)
        => sounds.Add(new SoundSample { context = context, key = key, gain = gain, pitch = pitch,
            frame = Time.frameCount, time = Time.realtimeSinceStartup });
    static void MoveMouse(Vector2 point)
    {
        if (testMouse == null) return;
        testMouse.MakeCurrent(); InputState.Change(testMouse, new MouseState { position = point }, InputState.currentUpdateType);
    }
    static IEnumerator Capture(string name)
    {
        yield return Settled();
        string path = Path.Combine(OutputDirectory, name); screenshots.Add(path); ScreenCapture.CaptureScreenshot(path);
        yield return Wait(() => File.Exists(path) && new FileInfo(path).Length > 100, "screenshot written: " + name, 10f);
    }
    static IEnumerator Settled() { Canvas.ForceUpdateCanvases(); yield return null; yield return null; }
    static IEnumerator Wait(Func<bool> predicate, string label, float seconds = 20f)
    {
        float until = Time.realtimeSinceStartup + seconds;
        while (!predicate() && Time.realtimeSinceStartup < until) yield return null;
        Check(predicate(), label);
    }
    static IEnumerator Drive(Func<bool> predicate, string label, float seconds = 30f)
    {
        float until = Time.realtimeSinceStartup + seconds;
        while (!predicate() && Time.realtimeSinceStartup < until)
        {
            if (memory != null) foreach (var text in memory.TextUnits) if (text != null) text.CompleteImmediately();
            yield return null;
        }
        Check(predicate(), label);
    }
    static bool Ready(OSWindowUnit window) => window != null && window.IsOpen && !window.IsOpening && !window.IsClosing
        && !window.IsMinimized && window.gameObject.activeInHierarchy;
    static Rect BoundsIn(RectTransform parent, RectTransform child)
    {
        var corners = new Vector3[4]; child.GetWorldCorners(corners);
        var points = corners.Select(parent.InverseTransformPoint).ToArray();
        return Rect.MinMaxRect(points.Min(point => point.x), points.Min(point => point.y), points.Max(point => point.x), points.Max(point => point.y));
    }
    static bool Contains(Rect outer, Rect inner) => inner.xMin >= outer.xMin - .01f && inner.xMax <= outer.xMax + .01f
        && inner.yMin >= outer.yMin - .01f && inner.yMax <= outer.yMax + .01f;
    static float[] Vec(Vector2 vector) => new[] { vector.x, vector.y };
    static bool Near(float a, float b) => Mathf.Abs(a - b) < .01f;
    static void Check(bool value, string label)
    { if (!value) throw new InvalidOperationException(label); checks.Add(context + ": " + label); }
    static GameSaveData Clone(GameSaveData save) => JsonUtility.FromJson<GameSaveData>(JsonUtility.ToJson(save));
    static object Get(object target, string name) => target?.GetType().GetField(name, Fields)?.GetValue(target)
        ?? target?.GetType().GetProperty(name, Fields)?.GetValue(target);
    static void Set(object target, string name, object value) => target.GetType().GetField(name, Fields).SetValue(target, value);
    static object Call(object target, string name, params object[] args) => target.GetType().GetMethod(name, Fields).Invoke(target, args);
    static string Hash(string path)
    {
        if (string.IsNullOrEmpty(path) || !File.Exists(path)) return "absent";
        using (var hash = SHA256.Create()) return Convert.ToBase64String(hash.ComputeHash(File.ReadAllBytes(path)));
    }
}
