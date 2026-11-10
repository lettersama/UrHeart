using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using DG.Tweening;
using MrTGame.Core;
using MrTGame.Narrative;
using MrTGame.Systems;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.UI;

// Opt-in, muted Play Mode integration checks. Saves are cloned and redirected to
// Temp before the production checkpoint is entered. This is not a listening test.
public static class OSChapter03SfxQA
{
    const BindingFlags Fields = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    static readonly List<string> checks = new List<string>();
    static readonly List<object> observations = new List<object>();
    static readonly List<Cue> cues = new List<Cue>();
    sealed class Cue { public string key; public float gain, pitch; public int frame; }
    static GameSetup setup;
    static Mouse testMouse;
    static OS_Handle os;
    static OSMemoryWindowController memory;
    static OSChapter03HomeController home;
    static OSChapter03StoryDirector story;
    static OSWindowUnit memoryWindow;
    static GameSaveData baseline;
    static string context;
    static bool cancel;
    static OSChapter03StoryState S => story.State;
    static OSHomeView V => home.View;
    static OSChapter03StoryPresentation P => story.Presentation;
    static OSLinkMessageController Messages => os.LinkMessageController;
    public static string Status { get; private set; } = "idle";
    public static string OutputDirectory { get; private set; }
    public static void Cancel() => cancel = true;

    public static string Start()
    {
        if (Status == "running") return Status;
        var saves = GameAutosaveService.Instance;
        if (!Application.isPlaying || saves == null || GameDataManager.Instance == null
            || GameManager.Instance == null || SceneLoadManager.Instance == null
            || GameSessionFlow.Instance == null || GameTest.Instance == null)
            return "Play Mode initialization required";
        if (saves.IsBusy || saves.IsWriting || GameManager.Instance.IsTransitioning
            || SceneLoadManager.Instance.IsBusy || GameSessionFlow.Instance.IsBusy
            || GameTest.Instance.IsCheckpointJumpRunning) return "Wait for active save/scene operation";
        setup = typeof(GameSetup).GetField("activeSetup", BindingFlags.Static | BindingFlags.NonPublic)?.GetValue(null) as GameSetup;
        if (setup == null) return "Active GameSetup is required";
        checks.Clear(); observations.Clear(); cues.Clear(); cancel = false;
        os = null; memory = null; home = null; story = null; baseline = null;
        context = "initialization";
        OutputDirectory = Path.GetFullPath(Path.Combine("Temp", "OSChapter03SfxQA", Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(OutputDirectory); Status = "running";
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
        bool demo = (bool)Get(setup, "isDemo");
        float listenerVolume = AudioListener.volume;
        var random = UnityEngine.Random.state;
        var previousMouse = Mouse.current;
        string[] files = { saves.SavePath, saves.SavePath + ".bak", saves.SavePath + ".tmp" };
        string[] hashes = null;
        var work = new Stack<IEnumerator>();
        try
        {
            saves.EnterTestSession(); hashes = files.Select(Hash).ToArray();
            saves.UseIsolatedStorageForTests(OutputDirectory); saves.EnterTestSession();
            data.ReplaceSaveData(Clone(original));
            Set(setup, "isDemo", false); Application.runInBackground = true; AudioListener.volume = 0f;
            if (resources != null) resources.enabled = false;
            testMouse = InputSystem.AddDevice<Mouse>("Chapter03SfxQAMouse");
            InputState.Change(testMouse, new MouseState { position = new Vector2(-10000f, -10000f) }, InputState.currentUpdateType);
            work.Push(Run());
            while (work.Count > 0)
            {
                object next;
                try
                {
                    testMouse?.MakeCurrent(); story?.DebugSetFocus(true); os?.Chapter03Entry?.DebugSetFocus(true);
                    if (cancel) throw new OperationCanceledException("SFX QA cancelled");
                    if (!work.Peek().MoveNext()) { (work.Pop() as IDisposable)?.Dispose(); continue; }
                    next = work.Peek().Current;
                }
                catch (Exception error)
                {
                    try { if (story != null && home?.View != null) Mark("failure-state"); } catch { }
                    Status = "FAILED at " + context + "\n" + Describe() + "\n" + error;
                    Debug.LogError("[Chapter03 SFX QA] " + Status); yield break;
                }
                if (next is IEnumerator nested) work.Push(nested); else yield return next;
            }
            Check(files.Select(Hash).SequenceEqual(hashes), "original save files remain unchanged");
            Status = "PASS";
        }
        finally
        {
            while (work.Count > 0) (work.Pop() as IDisposable)?.Dispose();
            try
            {
                if (story != null) story.DebugSfxPlayed -= OnCue;
                if (GameTest.Instance?.IsCheckpointJumpRunning == true) GameTest.Instance.StopAllCoroutines();
                if (os != null) Messages.NotifyOSHidden();
                story?.ResetPresentation(); home?.Suspend();
                os?.Chapter03Entry?.ResetPresentation(); os?.Chapter02FinalTalk?.ResetPresentation();
                GameTest.Instance?.GetComponent<GameTestConsole>()?.SetOpen(false);
                saves.EnterTestSession(); saves.EndSession();
                GameManager.Instance?.OpenMainMenuWithoutSave();
                data.ReplaceSaveData(original); data.SetCurrentLanguage(language);
                if (setup != null) Set(setup, "isDemo", demo);
                if (resources != null) resources.enabled = resourcesEnabled;
                Application.runInBackground = background; AudioListener.volume = listenerVolume;
                if (testMouse != null && testMouse.added) InputSystem.RemoveDevice(testMouse);
                if (previousMouse != null && previousMouse.added) previousMouse.MakeCurrent();
                UnityEngine.Random.state = random;
                if (!ReferenceEquals(data.SaveData, original) || data.CurrentLanguageState != language || GameSetup.IsDemo != demo
                    || Application.runInBackground != background || AudioListener.volume != listenerVolume
                    || resources != null && resources.enabled != resourcesEnabled
                    || previousMouse != null && previousMouse.added && Mouse.current != previousMouse)
                    throw new InvalidOperationException("Original save or runtime options were not restored");
                if (hashes != null && !files.Select(Hash).SequenceEqual(hashes)) throw new IOException("Original save files changed");
                checks.Add("cleanup restores original save object, language, mode, resources, input, random, volume and file hashes");
            }
            catch (Exception error) { Status = "FAILED during cleanup: " + error + "\n" + Status; }
            if (Status == "running") Status = "FAILED: interrupted at " + context;
            if (Status == "PASS") Status = "PASS: " + checks.Count + " checks";
            File.WriteAllText(Path.Combine(OutputDirectory, "results.json"), JsonConvert.SerializeObject(new {
                status = Status, context, checks, observations, originalFiles = files, originalHashes = hashes,
                finalHashes = files.Select(Hash).ToArray(), testSession = saves.IsTestSession,
                note = "Muted background Play Mode. Real Home events, saved queue execution and production modal callbacks are exercised. Existing Live holds can be completed through their existing callback; natural hold duration and subjective listening quality are not verified. Loop envelope checks may advance TickSfx deterministically. Exit Play Mode after this isolated run."
            }, Formatting.Indented));
            File.WriteAllText(Path.Combine(OutputDirectory, "results.txt"), Status + "\n" + string.Join("\n", checks));
            Debug.Log("[Chapter03 SFX QA] " + Status + " | " + OutputDirectory);
            os = null; memory = null; home = null; story = null; memoryWindow = null; baseline = null; setup = null; testMouse = null;
        }
    }

    static IEnumerator Run()
    {
        context = "checkpoint";
        Check(GameTest.Instance.JumpToCheckpoint(GameTestCheckpoint.Chapter03MemoryStart), "Chapter 3 checkpoint accepted");
        yield return Wait(() => !GameTest.Instance.IsCheckpointJumpRunning && !GameManager.Instance.IsTransitioning
            && !SceneLoadManager.Instance.IsBusy && OS_Handle.Instance != null, "checkpoint ready", 80f);
        os = OS_Handle.Instance; memory = os.MemoryWindowController;
        home = memory.GetComponent<OSChapter03HomeController>(); story = os.Chapter03Story;
        memoryWindow = memory.GetComponent<OSWindowUnit>();
        Check(GameTest.Instance.JumpSucceeded && GameAutosaveService.Instance.IsTestSession
            && home?.View != null && story != null, "real Home and story resolve with isolated storage");
        GameTest.Instance.GetComponent<GameTestConsole>()?.SetOpen(false);
        baseline = Clone(GameDataManager.Instance.SaveData); story.DebugSfxPlayed += OnCue;
        yield return TextAndPaper();
        yield return Walking();
        yield return RoomTransitions();
        yield return Keypad();
        yield return Pausing();
        yield return QuietPassages();
        yield return Ownership();
    }

    static IEnumerator Prepare(string name, string room, Action<OSChapter03StoryState> configure = null)
    {
        context = name;
        Messages.NotifyOSHidden(); story.ResetPresentation();
        foreach (var window in os.GetComponentsInChildren<OSWindowUnit>(true))
            if (window.IsOpen) window.CloseSilently();
        home.ResetRuntime(); memory.PrepareRuntime();
        var save = Clone(baseline);
        save.flow.language = LanguageState.zh_CN;
        save.osProcess.memory.currentDataIndex = 8110;
        save.osProcess.memory.chapter03HomeInitialized = true;
        save.osProcess.memory.chapter03HomeView = new OSHomeView.State {
            room = room, me = new Vector2Int(7, 5), dong = new Vector2Int(8, 5), dongHidden = false
        };
        var state = new OSChapter03StoryState { version = 1,
            phase = (int)(room == "yard" ? OSChapter03StoryDirector.Phase.Yard : OSChapter03StoryDirector.Phase.Explore), homeOpened = true };
        foreach (string flag in new[] { "home_revealed", "first_floor", "opening_8020", "opening_8070", "opening_8080", "opening_8110", "paper_note_01" }) state.Add(flag);
        foreach (string id in new[] { "study", "west", "hall", "kitchen", "east", "store", "bed", "bath", "lab", "yard", "junk" }) state.Add("room_" + id);
        state.papers.Add("note_01"); configure?.Invoke(state);
        save.osProcess.memory.chapter03Story = state;
        save.osProcess.characterInteraction = new OSCharacterInteractionSaveData(); save.osResume = new OSResumeSaveData();
        GameDataManager.Instance.ReplaceSaveData(save);
        Messages.SetGlobalMessagesSuppressed(false); Messages.NotifyOSBecameVisible();
        os.OpenCharacterWindowForReconnect(0f);
        Check(os.OpenMemoryWindow(), "real Memory window opens");
        yield return Wait(() => story.IsAvailable && Ready(home.HomeWindow) && V.CurrentRoom?.id == room
            && V.InputEnabled && memory.CurrentData?.DataIndex == 8110, "exploration fixture ready");
        CompleteMemory(); cues.Clear();
        Check(S.memoryQueue.Count == 0 && S.liveQueue.Count == 0 && !story.BlocksHomeInput, "fixture starts with empty queues");
    }

    static IEnumerator TextAndPaper()
    {
        yield return Prepare("text first begin and restore", "kitchen");
        Thing("kitchen_drawer");
        yield return Wait(() => Count("Chapter02_Drawer") == 1, "drawer text starts one actual sound");
        Check(S.memoryQueue.Any(b => b.key == "interaction.kitchen.drawer" && b.begun), "playing drawer beat stores begun");
        int before = Count("Chapter02_Drawer");
        yield return Roundtrip("begun-drawer");
        yield return Drain("restored drawer finishes");
        Check(Count("Chapter02_Drawer") == before, "restoring the begun text never replays its sound");
        Thing("kitchen_drawer");
        yield return Wait(() => Count("Chapter02_Drawer") == before + 1, "a later fresh drawer interaction sounds again");
        yield return Drain("fresh drawer finishes");
        Check(cues.Where(c => c.key == "Chapter02_Drawer").All(c => Near(c.gain, .35f)), "reused drawer gain is quiet");
        Mark("drawer restored and freshly reused");

        yield return Prepare("paper first begin and restore", "study");
        Thing("study_desk");
        yield return Wait(() => P.IsModalOpen, "real diary modal opens");
        Check(Count("Chapter02_PaperRustle") == 1, "paper open starts one rustle");
        yield return Roundtrip("open-paper");
        Mark("paper immediately after session restore");
        Check(Count("Chapter02_PaperRustle") == 1, "session restoration itself does not replay paper rustle");
        // OSSessionPersistence clears IsRestoring as its coroutine returns.
        // IsAvailable is true immediately, but TickMemoryQueue rebuilds the saved
        // paper modal on the director's next available Update.
        yield return Wait(() => P.IsModalOpen, "saved paper modal rebuilds after session restoration");
        Check(Count("Chapter02_PaperRustle") == 1, "reconstructed open paper does not rustle again");
        P.HideModal(true); yield return Drain("paper collection callback finishes");
        Check(S.papers.Contains("diary_07"), "actual paper completion still collects diary");
        Mark("paper restore");

        cues.Clear();
        foreach (string key in new[] { "zoom_in", "zoom_out", "live_to_center", "live_to_home" })
        {
            var beat = new OSChapter03StoryBeat("effect", key);
            Call(story, "PlayEffectSfx", beat); Call(story, "PlayEffectSfx", beat);
            Check(beat.begun, key + " persists sound ownership on its saved beat");
        }
        Check(Count("Ch01End_ShotSwitch") == 2 && Count("Chapter02_CursorCapture") == 1 && Count("Chapter02_CursorRelease") == 1,
            "zoom and window-carry sound hooks ignore already-begun beats");
        Check(cues.Where(c => c.key == "Ch01End_ShotSwitch").All(c => Near(c.gain, .25f)), "reused zoom cue uses requested gain");

        cues.Clear(); S.homeOpened = false; S.flags.Remove("home_revealed");
        story.PlayLampFaultSfx(V, "qa-lamp", 100000f);
        Check(Count("Chapter03_LampFlicker") == 0, "hidden Home lamp faults remain silent");
        S.homeOpened = true; S.Add("home_revealed");
        story.PlayLampFaultSfx(V, "qa-lamp", 100000f);
        Check(Count("Chapter03_LampFlicker") == 0, "revealing Home does not replay the hidden fault");
        story.PlayLampFaultSfx(V, "qa-lamp", 100001f); story.PlayLampFaultSfx(V, "qa-lamp", 100001f);
        Check(Count("Chapter03_LampFlicker") == 1, "next visible lamp fault starts once despite repeated flicker observation");
        Mark("effects and lamp reveal");
    }

    static IEnumerator Walking()
    {
        yield return Prepare("dry walking", "study");
        yield return WalkSamples(new Vector2Int(7, 5), new Vector2Int(6, 5), "Chapter03_Step_", 8);
        yield return Prepare("wet walking", "kitchen");
        yield return WalkSamples(new Vector2Int(8, 3), new Vector2Int(9, 3), "Chapter03_StepWet_", 8);
        yield return Prepare("yard mud walking", "yard");
        yield return WalkSamples(new Vector2Int(7, 3), new Vector2Int(8, 3), "Chapter03_StepMud_", 8);
        Check(Loops().Length == 0, "yard adds no room ambience");
        var random = UnityEngine.Random.state;
        Call(story, "PlayStepSfx", false);
        Check(UnityEngine.Random.state.Equals(random), "SFX variant selection leaves story Unity.Random untouched");
    }

    static IEnumerator WalkSamples(Vector2Int a, Vector2Int b, string prefix, int count)
    {
        V.Teleport(a); cues.Clear();
        for (int i = 0; i < count; i++)
        {
            Check(V.WalkTo(i % 2 == 0 ? b : a), "real one-tile walk accepted");
            yield return Wait(() => !V.IsWalking, "real step completes");
        }
        var steps = cues.Where(c => c.key.StartsWith("Chapter03_Step", StringComparison.Ordinal)).ToArray();
        Check(steps.Length == count && steps.All(c => c.key.StartsWith(prefix, StringComparison.Ordinal)), "each tile completion selects the expected surface sound");
        Check(steps.All(c => c.pitch >= .97f && c.pitch <= 1.03f && Near(c.gain, .3f)), "steps use requested gain and pitch range");
        Check(steps.Zip(steps.Skip(1), (aCue, bCue) => aCue.key != bCue.key).All(value => value), "consecutive steps avoid the same sample");
        Check(steps.Select(c => c.pitch).Distinct().Count() > 1, "step pitch actually varies");
        int before = steps.Length; V.Teleport(a);
        Check(cues.Count(c => c.key.StartsWith(prefix, StringComparison.Ordinal)) == before, "teleport creates no footstep");
        Mark(prefix);
    }

    static IEnumerator RoomTransitions()
    {
        yield return Prepare("room ambience and one door per transition", "kitchen");
        AdvanceSfx(1f);
        ExpectLoops("Chapter03_KitchenClock", "Chapter03_Drip");
        var kitchen = Loops();
        Check(V.EnterRoom("hall", new Vector2Int(7, 5), true), "real animated room transition accepted");
        yield return Wait(() => !V.IsTransitioning && V.CurrentRoom.id == "hall", "hall entered through fade");
        Check(Count("MemoryHome_DoorOpen") == 1, "ordinary room fade plays one door sound");
        AdvanceSfx(1f); yield return null;
        ExpectLoops("Chapter03_RoomTone", "Chapter03_LampBuzz");
        Check(kitchen.All(source => source == null), "departed room loop objects are removed after fading");
        var lamp = V.CurrentRoom.lights.First(light => light.kind == OSHomeLightKind.Breathing);
        story.ObserveLampSfx(V, lamp, .4f); Call(story, "TickSfx", 0f);
        Check(Near(LoopTarget("Chapter03_LampBuzz"), .04f), "hall hum follows actual breathing-light strength");
        cues.Clear();
        S.memoryQueue.Add(new OSChapter03StoryBeat("room", "kitchen", "7,5"));
        yield return Wait(() => S.memoryQueue.Count == 0 && V.CurrentRoom.id == "kitchen" && !V.IsTransitioning, "saved room beat completes its fade");
        Check(Count("MemoryHome_DoorOpen") == 1, "saved room beat and RoomEntered share exactly one door sound");
        cues.Clear();
        S.memoryQueue.Add(new OSChapter03StoryBeat("room", "hall", "7,5") { begun = true });
        yield return Wait(() => S.memoryQueue.Count == 0 && V.CurrentRoom.id == "hall" && !V.IsTransitioning, "begun saved room beat rebuilds");
        Check(Count("MemoryHome_DoorOpen") == 0, "begun room reconstruction remains silent through RoomEntered");
        Mark("room ownership");
    }

    static IEnumerator Keypad()
    {
        yield return Prepare("keypad production callbacks", "east", s => s.Add("password_ready"));
        S.memoryQueue.Add(new OSChapter03StoryBeat("keypad"));
        yield return Wait(() => P.IsModalOpen && (bool)Get(P, "CanInteract"), "keypad becomes interactive");
        PressDigits("1111"); int digits = cues.Count(c => c.key.StartsWith("Chapter03_KeypadPress_", StringComparison.Ordinal));
        Call(P, "KeypadPress", "2");
        Check(digits == 4 && cues.Count(c => c.key.StartsWith("Chapter03_KeypadPress_", StringComparison.Ordinal)) == 4, "four valid digit inputs sound and rejected fifth input stays silent");
        SubmitKeypad();
        Check(Count("Chapter03_KeypadError") == 1 && !S.Has("lab_open"), "wrong code produces one error through real submission");
        PressDigits("0710"); SubmitKeypad();
        Check(Count("Chapter03_KeypadAccept") == 1 && S.Has("lab_open"), "correct code produces one acceptance and opens lab");
        Check(cues.Where(c => c.key.StartsWith("Chapter03_KeypadPress_", StringComparison.Ordinal)).All(c => c.pitch >= .97f && c.pitch <= 1.03f), "keypad variants remain within pitch range");
        Mark("keypad wrong and accepted");
    }

    static IEnumerator Pausing()
    {
        yield return Prepare("Settings and OS visibility pause", "kitchen");
        Call(story, "PlayTextSfx", "interaction.kitchen.machine.brew", true);
        AdvanceSfx(1f);
        var brew = Voices().Single(source => source.name == "Chapter03Sfx_Chapter03_CoffeeBrew");
        var loops = Loops(); var settings = os.transform.Find("Windows/WindowUnit_Option").GetComponent<OSWindowUnit>();
        settings.OpenStandalone();
        yield return Wait(() => !story.IsAvailable && (bool)Get(story, "sfxPaused"), "Settings pauses owned SFX");
        float position = brew.time;
        Check(!brew.isPlaying && loops.All(source => !source.isPlaying), "one-shot and ambient sources are paused");
        yield return ForSeconds(.25f);
        Check(Near(brew.time, position, .025f), "paused one-shot position does not advance");
        settings.CloseSilently();
        yield return Wait(() => story.IsAvailable && !(bool)Get(story, "sfxPaused"), "closing Settings resumes SFX");
        Check(brew.isPlaying && loops.All(source => source.isPlaying) && Count("Chapter03_CoffeeBrew") == 1, "resume continues original sources without retriggering");
        Call(os, "ApplyPresentation", false);
        yield return Wait(() => !story.IsAvailable && (bool)Get(story, "sfxPaused"), "hidden OS pauses SFX");
        Check(!brew.isPlaying && loops.All(source => !source.isPlaying), "hidden OS pauses all owned channels");
        Call(os, "ApplyPresentation", true);
        yield return Wait(() => story.IsAvailable && !(bool)Get(story, "sfxPaused"), "visible OS resumes SFX");
        Check(Count("Chapter03_CoffeeBrew") == 1, "OS resume does not restart the one-shot");
        Mark("pause and resume");
    }

    static IEnumerator QuietPassages()
    {
        yield return Prepare("quiet passages and envelope exception", "kitchen");
        foreach (var phase in new[] { OSChapter03StoryDirector.Phase.FinalKnock, OSChapter03StoryDirector.Phase.Letter, OSChapter03StoryDirector.Phase.Complete })
        {
            S.phase = (int)phase; cues.Clear();
            Call(story, "PlayTextSfx", "interaction.kitchen.drawer", true); Call(story, "PlayStepSfx", false);
            AdvanceSfx(1f);
            Check(cues.Count == 0 && Loops().Length == 0 && Voices().Length == 0, phase + " suppresses room ambience and normal foley");
        }
        S.phase = (int)OSChapter03StoryDirector.Phase.Explore;
        S.memoryQueue.Add(new OSChapter03StoryBeat("effect", "resolve", delay: 4f));
        Call(story, "PlayTextSfx", "interaction.kitchen.drawer", true); AdvanceSfx(1f);
        Check(cues.Count == 0 && Loops().Length == 0, "resolve white-strip passage remains quiet");
        S.memoryQueue.Clear();
        S.liveQueue.Add(new OSChapter03StoryBeat("live", "live.breakdown.01"));
        Call(story, "PlayTextSfx", "interaction.kitchen.drawer", true); AdvanceSfx(1f);
        Check(cues.Count == 0 && Loops().Length == 0, "queued breakdown suppresses additional foley and ambience");
        S.liveQueue.Clear();
        foreach (string key in new[] { "memory.hall.heartbeat", "memory.store.heartbeat", "memory.lab.diary13.heartbeat", "memory.9080", "memory.lab.bed.second.heartbeat" })
            Call(story, "PlayTextSfx", key, true);
        foreach (string key in new[] { "reasoning", "resolve", "absorb_dong", "go_offline", "failed_typing", "yard_arrival" })
            Call(story, "PlayEffectSfx", new OSChapter03StoryBeat("effect", key));
        Check(cues.Count == 0, "existing heartbeat, reasoning, Dong, typing and scripted yard effects gain no new cue");
        S.phase = (int)OSChapter03StoryDirector.Phase.Letter;
        var envelope = new OSChapter03StoryBeat("letter");
        Call(story, "PlayLetterSfx", envelope); Call(story, "PlayLetterSfx", envelope);
        Check(Count("Chapter02_PaperRustle") == 1 && Count("Chapter03_EnvelopeOpen") == 0 && envelope.begun,
            "envelope entrance rustles once during the otherwise quiet letter phase");
        story.PlayLetterOpenSfx();
        Check(Count("Chapter03_EnvelopeOpen") == 1 && Count("Chapter02_PaperRustle") == 1,
            "the explicit envelope opening cue is allowed separately from its entrance rustle");
        S.Add("letter_unfolded"); Call(story, "PlayLetterSfx", new OSChapter03StoryBeat("letter"));
        Check(Count("Chapter02_PaperRustle") == 1 && Count("Chapter03_EnvelopeOpen") == 1,
            "restored unfolded letter body creates neither another entrance rustle nor another opening cue");
        Mark("quiet boundaries");
        yield return null;
    }

    static IEnumerator Ownership()
    {
        yield return Prepare("SFX ownership and cleanup", "yard");
        yield return ForSeconds(.2f);
        var external = os.GetComponentsInChildren<AudioSource>(true)
            .Where(source => source.name == "NightfallRain" || source.name.StartsWith("Chapter03Music_", StringComparison.Ordinal)).ToArray();
        var clips = external.Select(source => source.clip).ToArray();
        Check(external.Any(source => source.name == "NightfallRain"), "existing rain source is present");
        Call(story, "PlayStepSfx", false);
        Check(Voices().Length > 0 && Voices().All(source => source.outputAudioMixerGroup == AudioManager.Instance.SFXMixerGroup
            && !source.loop && Near(source.spatialBlend, 0f)), "owned one-shots use the SFX mixer and 2D playback");
        Call(story, "StopSfx");
        Check(Voices().Length == 0 && Loops().Length == 0 && ((IList)Get(story, "sfxPending")).Count == 0, "StopSfx clears all owned sources and delayed cues");
        yield return null;
        Check(external.Select((source, index) => source != null && source.clip == clips[index]).All(value => value), "SFX cleanup preserves rain and existing music source identity and clips");
        Mark("ownership");
    }

    static IEnumerator Roundtrip(string name)
    {
        home.CaptureViewState(); OSSessionPersistence.Capture(GameDataManager.Instance.SaveData);
        GameSessionFlow.Capture(GameDataManager.Instance.SaveData);
        string json = JsonUtility.ToJson(GameDataManager.Instance.SaveData, true);
        File.WriteAllText(Path.Combine(OutputDirectory, name + ".json"), json);
        var save = JsonUtility.FromJson<GameSaveData>(json);
        Messages.NotifyOSHidden(); story.ResetPresentation();
        GameDataManager.Instance.ReplaceSaveData(save); home.Suspend();
        yield return OSSessionPersistence.Restore(save);
        Messages.SetGlobalMessagesSuppressed(false); Messages.NotifyOSBecameVisible();
        if (!memoryWindow.IsOpen) os.OpenMemoryWindow();
        yield return Wait(() => story.IsAvailable && Ready(home.HomeWindow), "restored Home becomes available");
    }

    static AudioSource[] Voices() => ((IEnumerable)Get(story, "sfxVoices")).Cast<object>().Select(voice => Get(voice, "source") as AudioSource).Where(source => source != null).ToArray();
    static AudioSource[] Loops() => ((IDictionary)Get(story, "sfxLoops")).Values.Cast<object>().Select(loop => Get(loop, "source") as AudioSource).Where(source => source != null).ToArray();
    static float LoopTarget(string key) => (float)Get(((IDictionary)Get(story, "sfxLoops"))[key], "target");
    static void AdvanceSfx(float seconds) { for (int i = 0; i < Mathf.CeilToInt(seconds / .05f); i++) Call(story, "TickSfx", .05f); }
    static void ExpectLoops(params string[] keys)
    {
        var sources = Loops();
        Check(sources.Select(source => source.name.Replace("Chapter03Ambient_", "")).OrderBy(key => key)
            .SequenceEqual(keys.OrderBy(key => key)), "exact ambient set: " + string.Join(", ", keys));
        Check(sources.All(source => source.loop && Near(source.pitch, 1f) && source.outputAudioMixerGroup == AudioManager.Instance.AmbientMixerGroup
            && source.volume >= 0f && source.volume <= .2f), "room loops use ambient mixer and quiet levels");
    }
    static void PressDigits(string digits) { foreach (char digit in digits) Call(P, "KeypadPress", digit.ToString()); }
    static void SubmitKeypad() => P.GetComponentsInChildren<Button>(true).Single(button => button.name == "Submit").onClick.Invoke();
    static void Thing(string id)
    {
        if (!story.IsAvailable || story.BlocksHomeInput) throw new InvalidOperationException("Thing input unavailable: " + id);
        var thing = V.CurrentRoom?.FindThing(id);
        if (thing == null || thing.hidden || !thing.interactive) throw new InvalidOperationException("Visible thing missing: " + id);
        Call(V, "Activate", thing);
    }
    static int Count(string key) => cues.Count(cue => cue.key == key);
    static void OnCue(string key, float gain, float pitch) => cues.Add(new Cue { key = key, gain = gain, pitch = pitch, frame = Time.frameCount });
    static bool Ready(OSWindowUnit window) => window != null && window.IsOpen && !window.IsOpening && !window.IsClosing;
    static bool Near(float a, float b, float tolerance = .001f) => Mathf.Abs(a - b) <= tolerance;
    static void CompleteMemory() { foreach (var unit in memory.TextUnits) if (unit != null) unit.CompleteImmediately(); }
    static IEnumerator Drain(string label)
    {
        float until = Time.realtimeSinceStartup + 25f;
        while ((S.memoryQueue.Count > 0 || S.liveQueue.Count > 0 || Messages.IsReadingHoldActive) && Time.realtimeSinceStartup < until)
        {
            CompleteMemory();
            string id = Messages.DisplayedEntryId;
            if (OSChapter03StoryDirector.IsOwnEntry(id)) Messages.TryCompleteDisplayedTextImmediately(id);
            if (OSChapter03StoryDirector.IsOwnEntry(id) && Messages.IsReadingHoldActive)
                (Get(Messages, "autoCloseTween") as Tween)?.Complete(true);
            yield return null;
        }
        Check(S.memoryQueue.Count == 0 && S.liveQueue.Count == 0 && !Messages.IsReadingHoldActive, label);
    }
    static IEnumerator Wait(Func<bool> predicate, string label, float seconds = 20f)
    {
        float until = Time.realtimeSinceStartup + seconds;
        while (!predicate() && Time.realtimeSinceStartup < until) yield return null;
        Check(predicate(), label);
    }
    static IEnumerator ForSeconds(float seconds)
    {
        float until = Time.realtimeSinceStartup + seconds;
        while (Time.realtimeSinceStartup < until) yield return null;
    }
    static void Mark(string label) => observations.Add(new { context, label, room = V.CurrentRoom?.id, frame = Time.frameCount,
        modalOpen = P?.IsModalOpen, restoring = OSSessionPersistence.IsRestoring,
        runtimeMemoryStarted = Get(story, "runtimeMemoryStarted"), sfxPaused = Get(story, "sfxPaused"),
        cues = cues.ToArray(), voices = Voices().Select(source => new { source.name, source.volume, source.pitch }).ToArray(),
        loops = Loops().Select(source => new { source.name, source.volume, source.isPlaying }).ToArray() });
    static void Check(bool value, string label)
    {
        if (!value) throw new InvalidOperationException(label + " | " + Describe());
        checks.Add(context + ": " + label);
    }
    static string Describe() => "room=" + home?.View?.CurrentRoom?.id + ", available=" + story?.IsAvailable
        + ", memory=" + memory?.CurrentData?.DataIndex + ", queue=" + (story?.State == null ? "null" : string.Join(",", S.memoryQueue.Select(beat => beat.type + ":" + beat.key + ":" + beat.begun)));
    static GameSaveData Clone(GameSaveData save) => JsonUtility.FromJson<GameSaveData>(JsonUtility.ToJson(save));
    static object Get(object target, string name) => target?.GetType().GetField(name, Fields)?.GetValue(target)
        ?? target?.GetType().GetProperty(name, Fields)?.GetValue(target);
    static void Set(object target, string name, object value)
    {
        var field = target?.GetType().GetField(name, Fields);
        if (field == null) throw new MissingFieldException(target?.GetType().Name, name);
        field.SetValue(target, value);
    }
    static object Call(object target, string name, params object[] args)
    {
        var method = target?.GetType().GetMethod(name, Fields);
        if (method == null) throw new MissingMethodException(target?.GetType().Name, name);
        return method.Invoke(target, args);
    }
    static string Hash(string path)
    {
        if (string.IsNullOrEmpty(path) || !File.Exists(path)) return "absent";
        using (var hash = SHA256.Create()) return Convert.ToBase64String(hash.ComputeHash(File.ReadAllBytes(path)));
    }
}
