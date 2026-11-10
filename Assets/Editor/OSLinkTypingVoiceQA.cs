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
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

// Real text tweens and SFX sources, with natural frame timing. Uses the same
// cloned-save/isolated-storage cleanup as the Chapter 3 interaction runners.
public static class OSLinkTypingVoiceQA
{
    const BindingFlags Fields = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    static readonly List<string> checks = new List<string>();
    static readonly List<object> frames = new List<object>();
    static GameDataManager data;
    static GameSetup setup;
    static OS_Handle os;
    static OSChapter03StoryDirector story;
    static OSLinkMessageController messages;
    static OSCharacterWindowController character;
    static TMP_Text label;
    static Mouse mouse;
    static string context;
    static bool chapter03;
    static Tween TextTween => Get(messages, "textTween") as Tween;
    static AudioSource Voice => Get(character, "typingVoiceSource") as AudioSource;
    static bool Gate => Get(character, "activeTypingVoiceSfx") != null;
    static bool Playing => Voice != null && Voice.isPlaying && Voice.clip == Get(character, "typingVoiceClip") as AudioClip;
    public static string Status { get; private set; } = "idle";
    public static string OutputDirectory { get; private set; }

    public static string Start()
    {
        if (Status == "running") return Status;
        var saves = GameAutosaveService.Instance;
        if (!Application.isPlaying || saves == null || GameDataManager.Instance == null || GameManager.Instance == null
            || SceneLoadManager.Instance == null || GameSessionFlow.Instance == null || GameTest.Instance == null)
            return "Play Mode initialization required";
        if (saves.IsBusy || saves.IsWriting || saves.IsTestSession || GameManager.Instance.IsTransitioning
            || SceneLoadManager.Instance.IsBusy || GameSessionFlow.Instance.IsBusy || GameTest.Instance.IsCheckpointJumpRunning)
            return "Fresh idle Play Mode required";
        setup = typeof(GameSetup).GetField("activeSetup", BindingFlags.Static | BindingFlags.NonPublic)?.GetValue(null) as GameSetup;
        if (setup == null) return "Active GameSetup required";
        data = GameDataManager.Instance; checks.Clear(); frames.Clear(); context = "initialization";
        OutputDirectory = Path.GetFullPath(Path.Combine("Temp", "OSLinkTypingVoiceQA", Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(OutputDirectory); Status = "running";
        data.StartCoroutine(Guarded()); return Status;
    }

    static IEnumerator Guarded()
    {
        var saves = GameAutosaveService.Instance; var original = data.SaveData; var language = data.CurrentLanguageState;
        var resources = GameResourceSystem.Instance; bool resourcesEnabled = resources != null && resources.enabled;
        bool background = Application.runInBackground, demo = (bool)Get(setup, "isDemo"); float volume = AudioListener.volume;
        var random = UnityEngine.Random.state; var previousMouse = Mouse.current;
        string[] files = new[] { saves.SavePath, saves.SavePath + ".bak", saves.SavePath + ".tmp" }
            .Concat(Directory.GetFiles("Assets/Scenes", "*.unity", SearchOption.AllDirectories)).ToArray();
        string[] hashes = null; var work = new Stack<IEnumerator>();
        try
        {
            saves.EnterTestSession(); hashes = files.Select(Hash).ToArray();
            saves.UseIsolatedStorageForTests(OutputDirectory); saves.EnterTestSession();
            data.ReplaceSaveData(JsonUtility.FromJson<GameSaveData>(JsonUtility.ToJson(original)));
            Set(setup, "isDemo", false); Application.runInBackground = true; AudioListener.volume = 0f;
            if (resources != null) resources.enabled = false;
            mouse = InputSystem.AddDevice<Mouse>("LinkTypingVoiceQAMouse");
            InputState.Change(mouse, new MouseState { position = new Vector2(-10000f, -10000f) }, InputState.currentUpdateType);
            work.Push(Run());
            while (work.Count > 0)
            {
                object next;
                try
                {
                    mouse.MakeCurrent(); story?.DebugSetFocus(true); os?.Chapter03Entry?.DebugSetFocus(true);
                    if (!work.Peek().MoveNext()) { (work.Pop() as IDisposable)?.Dispose(); continue; }
                    next = work.Peek().Current;
                }
                catch (Exception error) { Status = "FAILED at " + context + "\n" + error; Debug.LogError(Status); yield break; }
                if (next is IEnumerator nested) work.Push(nested); else yield return next;
            }
            Check(files.Select(Hash).SequenceEqual(hashes), "original save/backup/tmp and all scenes remain unchanged");
            Status = "PASS";
        }
        finally
        {
            while (work.Count > 0) (work.Pop() as IDisposable)?.Dispose();
            try
            {
                if (GameTest.Instance?.IsCheckpointJumpRunning == true) GameTest.Instance.StopAllCoroutines();
                messages?.NotifyOSHidden(); story?.ResetPresentation(); story?.DebugSetFocus(false);
                os?.MemoryWindowController?.GetComponent<OSChapter03HomeController>()?.Suspend();
                os?.Chapter03Entry?.DebugSetFocus(false); os?.Chapter03Entry?.ResetPresentation(); os?.Chapter02FinalTalk?.ResetPresentation();
                GameTest.Instance?.GetComponent<GameTestConsole>()?.SetOpen(false);
                saves.EnterTestSession(); saves.EndSession(); GameManager.Instance?.OpenMainMenuWithoutSave();
                data.ReplaceSaveData(original); data.SetCurrentLanguage(language); Set(setup, "isDemo", demo);
                if (resources != null) resources.enabled = resourcesEnabled;
                Application.runInBackground = background; AudioListener.volume = volume;
                if (mouse != null && mouse.added) InputSystem.RemoveDevice(mouse);
                if (previousMouse != null && previousMouse.added) previousMouse.MakeCurrent(); UnityEngine.Random.state = random;
                Check(ReferenceEquals(data.SaveData, original) && data.CurrentLanguageState == language && GameSetup.IsDemo == demo
                    && Application.runInBackground == background && AudioListener.volume == volume
                    && (resources == null || resources.enabled == resourcesEnabled)
                    && (previousMouse == null || !previousMouse.added || Mouse.current == previousMouse)
                    && UnityEngine.Random.state.Equals(random), "cleanup restores save object, language, mode, listener, resources, background, mouse and RNG");
                Check(hashes != null && files.Select(Hash).SequenceEqual(hashes), "cleanup leaves original save and scene hashes unchanged");
            }
            catch (Exception error) { Status = "FAILED during cleanup: " + error + "\n" + Status; }
            if (Status == "running") Status = "FAILED: interrupted";
            if (Status == "PASS") Status = "PASS: " + checks.Count + " checks";
            File.WriteAllText(Path.Combine(OutputDirectory, "results.json"), JsonConvert.SerializeObject(new {
                status = Status, context, checks, frames, originalFiles = files, originalHashes = hashes, finalHashes = files.Select(Hash).ToArray(),
                note = "Natural real Tween/SFX timing; only AudioListener.volume is muted. No Editor focus or screenshots. Temporary entries use real BeginTextPlayback and labels. Exit Play Mode after this isolated run."
            }, Formatting.Indented));
            Debug.Log("[Live Typing Voice QA] " + Status + " | " + OutputDirectory);
            data = null; setup = null; os = null; story = null; messages = null; character = null; label = null; mouse = null;
        }
    }

    static IEnumerator Run()
    {
        context = "bedroom fixture";
        data.SetCurrentLanguage(LanguageState.zh_CN);
        Check(GameTest.Instance.JumpToCheckpoint((GameTestCheckpoint)Enum.Parse(typeof(GameTestCheckpoint), "Chapter03Bedroom")), "real bedroom checkpoint accepted");
        yield return Wait(() => !GameTest.Instance.IsCheckpointJumpRunning && !GameManager.Instance.IsTransitioning
            && !SceneLoadManager.Instance.IsBusy && OS_Handle.Instance != null, 80f);
        os = OS_Handle.Instance; story = os.Chapter03Story; messages = os.LinkMessageController; character = os.CharacterWindowController;
        story.DebugSetFocus(true); os.Chapter03Entry?.DebugSetFocus(true);
        GameTest.Instance.GetComponent<GameTestConsole>()?.SetOpen(false);
        messages.SetGlobalMessagesSuppressed(true); messages.NotifyOSBecameVisible(); os.OpenCharacterWindowForReconnect(0f); os.OpenMemoryWindow();
        yield return Wait(() => story.IsAvailable, 20f);
        Check(GameTest.Instance.JumpSucceeded && GameAutosaveService.Instance.IsTestSession, "real Home/Live initialized in isolated storage");
        context = "failed typing title only";
        for (int i = 0; i < 3; i++)
        {
            character.BeginLinkMessageTyping(); float until = Time.realtimeSinceStartup + .65f;
            while (Time.realtimeSinceStartup < until) { Quiet(); Sample(); yield return null; }
            character.EndLinkMessageTyping(); Quiet();
        }
        Check(true, "repeated failed_typing Begin/End stays silent for three natural 0.65-second stages");
        yield return Natural("three ellipsis pauses", true, "甲乙丙丁...戊己庚辛…壬癸甲乙⋯丙丁戊己", true);
        yield return Natural("ellipsis only", true, "...……⋯⋯", false);
        context = "pause resume complete";
        Begin(new string('字', 120), true); yield return Wait(() => Playing, 3f);
        var tween = TextTween; var oldSource = Voice; var oldClip = oldSource.clip; int visible = label.maxVisibleCharacters;
        tween.Pause(); Quiet(oldSource, oldClip); float end = Time.realtimeSinceStartup + .25f;
        while (Time.realtimeSinceStartup < end) { Quiet(); Sample(); Require(label.maxVisibleCharacters == visible, "paused text moved"); yield return null; }
        tween.Play(); yield return Wait(() => Playing && label.maxVisibleCharacters > visible, 3f);
        oldSource = Voice; oldClip = oldSource.clip; tween.Complete(true); Quiet(oldSource, oldClip);
        Check(true, "Pause stops its real source, Resume waits for new letters, Complete stops immediately");
        foreach (string action in new[] { "kill", "hide" })
        {
            context = action; Begin(new string('字', 120), true); yield return Wait(() => Playing, 3f);
            oldSource = Voice; oldClip = oldSource.clip;
            if (action == "kill") TextTween.Kill(false); else messages.NotifyOSHidden();
            Quiet(oldSource, oldClip); yield return null; Quiet(); Check(TextTween == null, action + " stops voice and clears the text tween");
        }
        context = "immediate display"; messages.NotifyOSBecameVisible(); Begin("立即显示...无打字声", true, true); Quiet(); yield return null; Quiet();
        Check(label.maxVisibleCharacters == int.MaxValue && TextTween == null, "Immediate text creates no typing source");

        // Ordinary entries belong to a real non-Chapter-3 fixture. The story's
        // live reservation must remain enabled throughout the Chapter 3 cases.
        context = "ordinary Chapter 1 fixture";
        messages.NotifyOSHidden(); story.State.liveQueue.Clear(); story.ResetPresentation();
        os = null; story = null; messages = null; character = null; label = null;
        Check(GameTest.Instance.JumpToCheckpoint(GameTestCheckpoint.Chapter01BeforeInteractivePicture), "real Chapter 1 checkpoint accepted");
        yield return Wait(() => !GameTest.Instance.IsCheckpointJumpRunning && !GameManager.Instance.IsTransitioning
            && !SceneLoadManager.Instance.IsBusy && OS_Handle.Instance != null, 80f);
        os = OS_Handle.Instance; story = os.Chapter03Story; messages = os.LinkMessageController; character = os.CharacterWindowController;
        data.OSProcessData.characterInteraction = new OSCharacterInteractionSaveData();
        GameTest.Instance.GetComponent<GameTestConsole>()?.SetOpen(false);
        messages.SetGlobalMessagesSuppressed(true); messages.NotifyOSBecameVisible(); os.OpenCharacterWindowForReconnect(0f);
        Check(GameTest.Instance.JumpSucceeded && data.CurrentChapterIndex == 1 && !story.IsActive,
            "ordinary message uses a real Chapter 1 fixture without Chapter 3 reservation");
        yield return Natural("regular Chapter 1/2 rich text DOText", false, "<color=#ff6060>Hello</color>... <b>world</b>", true);
    }

    static IEnumerator Natural(string name, bool ownEntry, string text, bool expectVoice)
    {
        context = name; Begin(text, ownEntry);
        bool heard = false; int previous = 0, resumes = 0; bool afterDot = false;
        AudioSource lastSource = null; AudioClip lastClip = null;
        var holds = new Dictionary<char, float> { ['.'] = 0f, ['…'] = 0f, ['⋯'] = 0f };
        float deadline = Time.realtimeSinceStartup + 10f;
        while (TextTween != null)
        {
            Require(Time.realtimeSinceStartup < deadline, "natural typing timed out");
            int count = Visible(); char last = Last(); Sample(); heard |= Playing;
            if (Voice != null) { lastSource = Voice; lastClip = Voice.clip; }
            if (holds.ContainsKey(last))
            {
                Quiet(lastSource, lastClip); afterDot = true;
                if (count == previous) holds[last] += Time.unscaledDeltaTime;
            }
            else if (afterDot && Playing) { resumes++; afterDot = false; }
            if (!expectVoice) Quiet(); previous = count; yield return null;
        }
        Sample(); Require((bool)Get(messages, "activeTextCompleted"), "text was interrupted before its natural completion");
        Quiet(lastSource, lastClip); Check(heard == expectVoice, name + ": expected real SFX playback observed, with every visible dot silent");
        if (ownEntry && expectVoice)
            Check(holds.All(pair => pair.Value >= .15f) && resumes == 3, "all three ellipsis forms hold silently and restart voice on following words");
        if (!ownEntry)
            Check(label.GetParsedText() == "Hello... world", "ordinary DOText retains rich-text parsing and exact final text");
    }

    static void Begin(string text, bool ownEntry, bool immediate = false)
    {
        chapter03 = ownEntry; var entry = new OSCharacterInteractionEntryData();
        Set(entry, "entryId", ownEntry ? OSChapter03StoryDirector.LiveId("live.audio_qa") : "chapter02_audio_qa");
        Set(entry, "fallbackText", text); Set(entry, "waitForExternalCompletion", true);
        Set(entry, "displayPolicy", immediate ? OSCharacterInteractionDisplayPolicy.Immediate : OSCharacterInteractionDisplayPolicy.DoText);
        Call(messages, "StopVisualPresentation", true);
        if (ownEntry)
        {
            story.State.liveQueue.Clear();
            story.State.liveQueue.Add(new OSChapter03StoryBeat("live", "live.audio_qa") { begun = true });
        }
        // This production entry point establishes the active/global identities
        // and calls ShowEntry -> BeginTextPlayback on the actual label.
        Call(messages, "BeginEntryVisualPresentation", null, entry, ownEntry,
            ownEntry ? OSCharacterInteractionIds.GlobalSequence : "audio-qa");
        label = (TMP_Text)Get(messages, "textLabel");
        Require(messages.IsPresenting && (!ownEntry || story.ReservedEntryId == entry.EntryId), "temporary entry has no legitimate presentation reservation");
    }

    static int Visible() => label == null ? 0 : Mathf.Min(label.textInfo.characterCount, chapter03 ? label.maxVisibleCharacters : int.MaxValue);
    static char Last() => Visible() > 0 ? label.textInfo.characterInfo[Visible() - 1].character : '\0';
    static void Sample() => frames.Add(new { context, frame = Time.frameCount, time = Time.realtimeSinceStartup,
        visible = Visible(), maxVisible = label != null ? label.maxVisibleCharacters : 0, last = Last().ToString(),
        gate = Gate, playing = Playing, clip = Voice?.clip?.name, tweenPlaying = TextTween?.IsPlaying() == true,
        displayed = messages?.DisplayedEntryId, presenting = messages?.IsPresenting, reserved = story?.ReservedEntryId,
        partActive = label != null && label.gameObject.activeInHierarchy });
    static void Quiet(AudioSource oldSource = null, AudioClip oldClip = null)
    {
        Require(!Gate && Voice == null, "voice gate/source survived a silent boundary");
        Require(oldSource == null || oldSource.clip != oldClip || !oldSource.isPlaying, "old voice source kept playing after stop");
    }
    static IEnumerator Wait(Func<bool> condition, float seconds)
    {
        float deadline = Time.realtimeSinceStartup + seconds;
        while (!condition()) { Require(Time.realtimeSinceStartup < deadline, "timed out"); Sample(); yield return null; }
    }
    static void Require(bool value, string detail) { if (!value) throw new InvalidOperationException(context + ": " + detail); }
    static void Check(bool value, string detail) { Require(value, detail); checks.Add(context + ": " + detail); }
    static object Get(object target, string name) => target?.GetType().GetField(name, Fields)?.GetValue(target);
    static void Set(object target, string name, object value) => target.GetType().GetField(name, Fields).SetValue(target, value);
    static object Call(object target, string name, params object[] args) => target.GetType().GetMethod(name, Fields).Invoke(target, args);
    static string Hash(string path)
    {
        if (!File.Exists(path)) return "absent";
        using (var hash = SHA256.Create()) return Convert.ToBase64String(hash.ComputeHash(File.ReadAllBytes(path)));
    }
}
