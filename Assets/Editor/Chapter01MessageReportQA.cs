using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using MrTGame.Core;
using MrTGame.Systems;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// Focused Play Mode QA. No shared assets are edited and normal autosave writes
// stay disabled. Invoke Start(), then inspect Status and OutputDirectory.
public static class Chapter01MessageReportQA
{
    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    const string Sequence = OSCharacterInteractionIds.Chapter01MemoryRepair;
    const string Recall = "chapter01_memory_mother_daughter_recall";
    const string Reassure = "reassure_mother_daughter";
    const string UpgradeHint = "chapter01_memory_upgrade_02";
    const string Installed = "chapter01_memory_upgrade_02_installed";
    const string Prefix = "ui.os.character.link_message.";
    static readonly List<string> checks = new List<string>();
    static OSLinkMessageController link;
    static OS_Handle os;
    static float installedTextFinished;
    static int installedStarts, choiceClicks;

    public static string Status { get; private set; } = "idle";
    public static string OutputDirectory { get; private set; }

    public static string Start()
    {
        if (Status == "running") return Status;
        var saves = GameAutosaveService.Instance;
        if (!Application.isPlaying || saves == null || GameDataManager.Instance == null
            || GameManager.Instance == null || SceneLoadManager.Instance == null || GameTest.Instance == null)
            return "Play Mode initialization required";
        if (saves.IsBusy || saves.IsWriting || GameManager.Instance.IsTransitioning
            || SceneLoadManager.Instance.IsBusy || GameTest.Instance.IsCheckpointJumpRunning)
            return "Wait for active save/scene operation";
        checks.Clear();link = null;os = null;installedTextFinished = -1;
        installedStarts = choiceClicks = 0;
        OutputDirectory = Path.GetFullPath(Path.Combine("Temp", "Chapter01MessageReportQA", Guid.NewGuid().ToString("N")));
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
        string[] hashes = null;var work = new Stack<IEnumerator>();
        try
        {
            saves.EnterTestSession();hashes = paths.Select(Fingerprint).ToArray();
            data.ReplaceSaveData(Clone(original));Application.runInBackground = true;
            if (resources != null) resources.enabled = false;
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
                    Status = "FAILED: " + error;Debug.LogError("[Chapter 01 Message QA] " + Status);yield break;
                }
                if (next is IEnumerator nested) work.Push(nested);else yield return next;
            }
            Check(paths.Select(Fingerprint).SequenceEqual(hashes), "normal save files unchanged");
            Check(saves.IsTestSession && !saves.IsWriting, "test session still blocks autosave writes");
            Status = "PASS: " + checks.Count + " message checks";
            Debug.Log("[Chapter 01 Message QA] " + Status + " | " + OutputDirectory);
        }
        finally
        {
            while (work.Count > 0) (work.Pop() as IDisposable)?.Dispose();
            if (link != null)
            {
                link.EntryPresentationStarted -= OnStarted;link.EntryTextPlaybackCompleted -= OnTextFinished;
                link.OptionSelected -= OnChoice;link.NotifyOSHidden();
            }
            saves.EnterTestSession();saves.EndSession();
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
        Check(GameTest.Instance.JumpToCheckpoint(GameTestCheckpoint.Chapter01BeforeInteractivePicture), "isolated chapter-one checkpoint accepted");
        yield return Wait(() => !GameTest.Instance.IsCheckpointJumpRunning, "checkpoint finished", 65);
        Check(GameTest.Instance.JumpSucceeded, "checkpoint succeeded");
        os = OS_Handle.Instance;link = Field<OSLinkMessageController>(os, "linkMessageController");
        Check(os != null && link != null && os.IsOSVisible, "OS and message controller are ready");
        link.EntryPresentationStarted += OnStarted;link.EntryTextPlaybackCompleted += OnTextFinished;link.OptionSelected += OnChoice;
        var data = GameDataManager.Instance;
        os.MemoryWindowController.GetComponent<OSWindowUnit>().CloseSilently();
        data.OSProcessData.installedToolProgramIds.Remove(OSToolProgramIds.MemoryRepair02);
        data.SetCurrentOSMemoryData(2630);ResetMessages();data.SetCurrentLanguage(LanguageState.zh_CN);
        data.SetOSResourceValue(ResourceType.Heart, 80);
        ValidateLocalization(data);
        Check(data.OSCharacterInteractionData.TryGetSequence(Sequence, out var sequence), "chapter-one sequence exists");
        Check(sequence.TryGetEntry(Installed, out var installed) && installed.RequiredChapterIndex == 1
            && installed.AutoCloseDelay == 5f && !installed.Repeatable && installed.PreserveOnOwnerClose,
            "installed message is chapter-one-only, once, resumable, with a five-second read hold");
        Check(link.TryNotifyExternalSignal("memory.text_completed.2630"), "2630 completion triggers empathy choice");
        yield return Wait(() => link.DisplayedEntryId == Recall && link.IsWaitingForOption, "empathy choice finished typing");
        os.NotifyWindowPointerPressed(link.LinkWindow);yield return null;yield return null;
        var option = Field<OSLinkMessageSelectUnit[]>(link, "optionUnits").First(u => u != null && u.gameObject.activeInHierarchy && u.IsInteractable);
        yield return Shot("01-empathy-confirmation");
        int before = data.GetOSResourceValue(ResourceType.Heart);var pointer = PointerFor(option);
        ExecuteEvents.Execute(option.gameObject, pointer, ExecuteEvents.pointerClickHandler);
        Check(data.GetOSResourceValue(ResourceType.Heart) == before + 5, "actual option pointer click grants exactly five heart");
        Check(!link.LinkWindow.IsOpen && !link.LinkWindow.gameObject.activeSelf && !link.IsWaitingForOption,
            "actual option click closes the Live message immediately");
        ExecuteEvents.Execute(option.gameObject, pointer, ExecuteEvents.pointerClickHandler);
        Check(choiceClicks == 1 && data.GetOSResourceValue(ResourceType.Heart) == before + 5, "repeat pointer click is ignored");
        Check(data.OSCharacterInteractionState.HasCompletedEntry(Recall), "choice completion persisted");
        Check(sequence.TryGetEntry(Recall, out var recall), "empathy entry exists");
        var reassure = recall.Options.First(o => o.OptionId == Reassure);
        ReloadSnapshot();
        Check(!data.TryApplyCharacterOptionResources(Sequence, Recall, reassure)
            && data.GetOSResourceValue(ResourceType.Heart) == before + 5, "serialized reload preserves the one-time heart receipt");
        Check(!link.TryNotifyExternalSignal("memory.text_completed.2630") && !link.LinkWindow.IsOpen,
            "serialized reload does not replay the confirmed choice");

        data.SetCurrentOSMemoryData(2670);data.UnlockTool();data.MarkToolInitialized();data.MarkToolProgramsUnpacked();
        Check(link.TryNotifyExternalSignal("memory.text_completed.2670"), "2670 completion triggers package hint");
        yield return Wait(() => link.DisplayedEntryId == UpgradeHint && !link.IsPresenting, "package hint finished typing");
        var text = Field<TMP_Text>(link, "textLabel");
        Check(text.richText && text.text.Contains("<color=red>工具箱</color>"), "visible package hint retains red Toolbox rich text");
        yield return Wait(() => !link.LinkWindow.IsOpen, "package hint closes before install confirmation");
        data.SetOSResourceValue(ResourceType.Battery, 95);installedStarts = 0;installedTextFinished = -1;
        Check(data.TryInstallToolProgram(OSToolProgramIds.MemoryRepair02) == OSToolInstallResult.Success,
            "actual upgrade-two installation succeeds");
        yield return Wait(() => link.DisplayedEntryId == Installed && link.IsPresenting, "installation triggers completion message");
        yield return Wait(() => installedTextFinished >= 0, "installation message finishes typing");
        Check(text.text == "呼，现在应该能正常读取了吧？", "installation message matches requested Chinese copy");
        yield return Shot("02-upgrade-installed");
        yield return VerifyFiveSecondClose();
        Check(installedStarts == 1 && data.OSCharacterInteractionState.HasCompletedEntry(Installed),
            "installation completion is saved and shown once");
        yield return Hold(.4f);
        ReloadSnapshot();yield return Hold(.4f);
        Check(installedStarts == 1 && !link.LinkWindow.IsOpen, "completion does not repeat after serialized reload");
        Check(data.TryInstallToolProgram(OSToolProgramIds.MemoryRepair02) == OSToolInstallResult.AlreadyInstalled,
            "reinstall request cannot repeat the installation");
        yield return Hold(.3f);Check(installedStarts == 1, "reinstall request does not repeat the message");

        // Recreate the reported user-driven closure while a protected hint is typing.
        data.OSProcessData.installedToolProgramIds.Remove(OSToolProgramIds.MemoryRepair02);ResetMessages();
        Check(link.TryPresentEntry(UpgradeHint), "closed-Live fixture starts the package hint");
        yield return null;var live = os.CharacterWindowController.GetComponent<OSWindowUnit>();
        live.Close();yield return null;
        Check(!live.IsOpen && data.OSCharacterInteractionState.presentingEntryId == UpgradeHint,
            "closing Live retains the unfinished protected hint");
        data.SetOSResourceValue(ResourceType.Battery, 95);installedTextFinished = -1;
        Check(data.TryInstallToolProgram(OSToolProgramIds.MemoryRepair02) == OSToolInstallResult.Success,
            "install succeeds while Live is closed");
        yield return Wait(() => live.IsOpen && link.DisplayedEntryId == Installed && link.IsPresenting,
            "installed response reopens Live instead of being blocked by the hidden obsolete hint");
        live.Close();yield return null;
        Check(!live.IsOpen && data.OSCharacterInteractionState.presentingEntryId == Installed,
            "closing the new message preserves its in-progress presentation");
        installedTextFinished = -1;os.OpenCharacterWindowForReconnect(0f);
        yield return Wait(() => link.DisplayedEntryId == Installed && installedTextFinished >= 0,
            "reopening Live resumes the installation response");
        yield return VerifyFiveSecondClose();
        int startsAfterResume = installedStarts;ReloadSnapshot();yield return Hold(.3f);
        Check(installedStarts == startsAfterResume && !link.LinkWindow.IsOpen, "resumed completion stays consumed after reload");
    }

    static void ValidateLocalization(GameDataManager data)
    {
        foreach (string key in new[] { Prefix + UpgradeHint, Prefix + Installed })
        {
            var entry = data.LocalizationData.Entries.SingleOrDefault(e => e.Key == key);
            Check(entry != null && entry.Values.Count == 10 && entry.Values.Select(v => v.Language).Distinct().Count() == 10,
                key + " has all ten explicit locales without fallback");
            foreach (LanguageState language in Enum.GetValues(typeof(LanguageState)))
            {
                string value = entry.Values.Single(v => v.Language == language).Text;
                Check(!string.IsNullOrWhiteSpace(value) && value != key, key + " / " + language + " contains text");
                if (key == Prefix + UpgradeHint)
                {
                    string tool = data.LocalizationData.GetText("ui.os.app.tool.name", language);
                    Check(value.Contains("<color=red>" + tool + "</color>"), "Toolbox red target matches localized app name / " + language);
                }
            }
        }
    }

    static IEnumerator VerifyFiveSecondClose()
    {
        Check(installedTextFinished >= 0, "completion message has a measured text-finish timestamp");
        yield return Wait(() => !link.LinkWindow.IsOpen, "installation response auto-closes", 6.5f);
        float elapsed = Time.realtimeSinceStartup - installedTextFinished;
        Check(elapsed >= 4.9f && elapsed <= 6f, "auto-close elapsed from completed text: " + elapsed.ToString("F2") + " seconds");
    }

    static void ResetMessages()
    {
        link.NotifyOSHidden();var state = GameDataManager.Instance.OSCharacterInteractionState;
        state.ActivateSequence("qa.message_reset");state.ActivateSequence(Sequence);
        state.pendingGlobalEntryId = state.presentingGlobalEntryId = null;
        link.SetGlobalMessagesSuppressed(true);link.NotifyOSBecameVisible();
    }
    static void ReloadSnapshot()
    {
        link.NotifyOSHidden();var data = GameDataManager.Instance;
        data.ReplaceSaveData(Clone(data.SaveData));link.NotifyOSBecameVisible();
    }
    static GameSaveData Clone(GameSaveData data) => JsonUtility.FromJson<GameSaveData>(JsonUtility.ToJson(data));
    static void OnStarted(string sequence, OSCharacterInteractionEntryData entry)
    { if (sequence == Sequence && entry.EntryId == Installed) installedStarts++; }
    static void OnTextFinished(string sequence, OSCharacterInteractionEntryData entry)
    { if (sequence == Sequence && entry.EntryId == Installed) installedTextFinished = Time.realtimeSinceStartup; }
    static void OnChoice(string sequence, OSCharacterInteractionEntryData entry, OSCharacterInteractionOptionData option)
    { if (sequence == Sequence && entry.EntryId == Recall && option.OptionId == Reassure) choiceClicks++; }
    static PointerEventData PointerFor(OSLinkMessageSelectUnit option)
    {
        var rect = option.ScriptTarget;var canvas = option.GetComponentInParent<Canvas>();
        var pointer = new PointerEventData(EventSystem.current) { button = PointerEventData.InputButton.Left,
            position = RectTransformUtility.WorldToScreenPoint(canvas.worldCamera, rect.TransformPoint(rect.rect.center)) };
        var hits = new List<RaycastResult>();EventSystem.current.RaycastAll(pointer, hits);
        Check(hits.Count > 0 && hits[0].gameObject.GetComponentInParent<OSLinkMessageSelectUnit>() == option,
            "empathy option is the frontmost pointer target");return pointer;
    }
    static IEnumerator Wait(Func<bool> condition, string label, float seconds = 8)
    {
        float deadline = Time.realtimeSinceStartup + seconds;
        while (!condition() && Time.realtimeSinceStartup < deadline) yield return null;
        Check(condition(), label);
    }
    static IEnumerator Hold(float seconds)
    { float deadline = Time.realtimeSinceStartup + seconds;while (Time.realtimeSinceStartup < deadline) yield return null; }
    static IEnumerator Shot(string name)
    {
        yield return new WaitForEndOfFrame();var texture = ScreenCapture.CaptureScreenshotAsTexture();
        try { File.WriteAllBytes(Path.Combine(OutputDirectory, name + ".png"), texture.EncodeToPNG()); }
        finally { UnityEngine.Object.Destroy(texture); }
    }
    static void Check(bool ok, string label)
    { if (!ok) throw new InvalidOperationException(label);checks.Add(label); }
    static T Field<T>(object owner, string name) => (T)owner.GetType().GetField(name, Private).GetValue(owner);
    static string Fingerprint(string path)
    {
        if (!File.Exists(path)) return "missing";
        using (var sha = SHA256.Create()) return Convert.ToBase64String(sha.ComputeHash(File.ReadAllBytes(path)));
    }
}
