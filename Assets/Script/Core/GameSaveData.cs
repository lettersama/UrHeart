using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using MrTGame.Core;
using UnityEngine;

public enum OSToolDecorationState
{
    None = 0,
    Offered = 1,
    Declined = 2,
    Pending = 3,
    Completed = 4
}

[Serializable]
public sealed class EventCompletionData
{
    public string eventID;
    public bool isActive;
    public bool isCompleted;
}

[Serializable]
public sealed class OSResourceSaveData
{
    public const int MinValue = 0;
    public const int MaxValue = 100;
    public const int InitialHeart = 53;
    public const int InitialEnergy = 70;
    public const int InitialFault = 10;

    public int heart = InitialHeart;
    public int energy = InitialEnergy;
    public int error = InitialFault;

    public int Get(ResourceType type)
    {
        switch (type)
        {
            case ResourceType.Heart:
                return heart;
            case ResourceType.Battery:
                return energy;
            case ResourceType.Fault:
                return error;
            default:
                return MinValue;
        }
    }

    public int Set(ResourceType type, int value)
    {
        int clampedValue = Mathf.Clamp(value, MinValue, MaxValue);
        switch (type)
        {
            case ResourceType.Heart:
                heart = clampedValue;
                break;
            case ResourceType.Battery:
                energy = clampedValue;
                break;
            case ResourceType.Fault:
                error = clampedValue;
                break;
        }

        return clampedValue;
    }

    public int Modify(ResourceType type, int delta)
    {
        return Set(type, Get(type) + delta);
    }
}

public enum GameOptionVolumeType
{
    Master,
    Music,
    Ambient,
    Sfx
}

[Serializable]
public sealed class GameOptionSaveData
{
    public const int MinVolume = 0;
    public const int MaxVolume = 100;
    public const int MinTextSpeed = 1;
    public const int MaxTextSpeed = 3;

    public int masterVolume = 75;
    public int musicVolume = 75;
    public int ambientVolume = 75;
    public int sfxVolume = 75;
    public bool isFullScreen = true;
    public bool isOSPostProcessingEnabled = true;
    public int textSpeed = MinTextSpeed;

    public void Sanitize()
    {
        masterVolume = ClampVolume(masterVolume);
        musicVolume = ClampVolume(musicVolume);
        ambientVolume = ClampVolume(ambientVolume);
        sfxVolume = ClampVolume(sfxVolume);
        textSpeed = Mathf.Clamp(textSpeed, MinTextSpeed, MaxTextSpeed);
    }

    public int GetVolume(GameOptionVolumeType type)
    {
        switch (type)
        {
            case GameOptionVolumeType.Master:
                return masterVolume;
            case GameOptionVolumeType.Music:
                return musicVolume;
            case GameOptionVolumeType.Ambient:
                return ambientVolume;
            case GameOptionVolumeType.Sfx:
                return sfxVolume;
            default:
                return MinVolume;
        }
    }

    public int SetVolume(GameOptionVolumeType type, int value)
    {
        int clampedValue = ClampVolume(value);
        switch (type)
        {
            case GameOptionVolumeType.Master:
                masterVolume = clampedValue;
                break;
            case GameOptionVolumeType.Music:
                musicVolume = clampedValue;
                break;
            case GameOptionVolumeType.Ambient:
                ambientVolume = clampedValue;
                break;
            case GameOptionVolumeType.Sfx:
                sfxVolume = clampedValue;
                break;
        }

        return clampedValue;
    }

    public int SetTextSpeed(int value)
    {
        textSpeed = Mathf.Clamp(value, MinTextSpeed, MaxTextSpeed);
        return textSpeed;
    }

    static int ClampVolume(int value)
    {
        return Mathf.Clamp(value, MinVolume, MaxVolume);
    }
}

[Serializable]
public sealed class OSWindowLayoutSaveData
{
    public const int InvalidContentContextIndex = -1;

    public string windowId;
    public Vector2 anchoredPosition;
    public Vector2 size;
    public int siblingIndex;
    public bool hasContentState;
    public string contentStateId;
    public int contentContextIndex = InvalidContentContextIndex;
    public float contentProgress;
    public int contentCheckpointValue;
    public Vector2 contentPosition;
}

[Serializable]
public sealed class OSMemoryProcessSaveData
{
    public const int InvalidDataIndex = -1;

    public List<int> triggeredDataIndices = new List<int>();
    public int currentDataIndex = InvalidDataIndex;
    // Separate from the reading cursor so later hotspot descriptions can keep this mode active.
    public int interactiveSceneDataIndex = InvalidDataIndex;
    // Only the player's pin toggle changes this; closing a window must not clear it.
    public bool interactiveWindowPinned = true;
    public bool chapter01MirrorHeartCostApplied;
    // Collection invitation: 0 waiting, 1 observe, 2 roam, 3 notice, 4 busy,
    // 5 assemble, 6 return, 7 offer, 8 complete. Old saves reconstruct from receipts.
    public int chapter02CollectionIntroStep;
    public bool chapter02ClawMachineDiscovered;
    public bool chapter02ClawMachineOnDesktop;
    public bool chapter02ClawIntroShown;
    public bool chapter02ClawThanksShown;
    // The target cue starts when the intro finishes typing, before its read hold ends.
    public bool chapter02ClawTargetRevealed;
    public int chapter02ClawCapturedMask;
    // 0 = empty; 1..4 = prize index + 1. A caught prize resumes at the outlet.
    public int chapter02ClawOutletPrize;
    public bool chapter02ClawFirstPrizeTaken;
    public bool chapter02ClawNextRequestShown;
    // The second target cue starts when its request finishes typing.
    public bool chapter02ClawSecondTargetRevealed;
    // A captured pointer resumes hanging at the top; physical mouse coordinates are never saved.
    public bool chapter02ClawCursorCaptured;
    public float chapter02ClawCursorHeadX = 124f;
    // 0 = not started, 1..3 = active experiment round, 4 = all tests complete.
    public int chapter02CursorExperimentRound;
    public int chapter02CursorExperimentPhase;
    public bool chapter02CursorExperimentHasPosition;
    public Vector2 chapter02CursorExperimentPosition;
    public Vector2[] chapter02CursorExperimentTrail = new Vector2[0];
    public int chapter02CursorExperimentFailures;
    // Only reverse-control (third maze) collisions count toward assistance.
    public int chapter02CursorExperimentThirdFailures;
    public bool chapter02CursorExperimentAssisted;
    // 0 = not started, 1 = caught, 2 = study, 3 = question, 4 = branch,
    // 5 = tether, 6 = legacy first-test instructions, 7 = introductory story complete,
    // 8 = safety gesture prompt, 9 = safety reply / first-test instructions,
    // 10 = first-test result question, 11 = reply, 12 = second-test rules, 13 = second test released,
    // 14 = second-test return, 15 = question, 16 = reply, 17 = promise, 18 = third-test rules, 19 = third test released,
    // 20 = third-test assistance, 21 = sixth-failure reaction.
    public int chapter02CursorStoryStep;
    // 0 = no answer, 1 = "not a bug", 2 = "human". Set by the cage gesture prompt.
    public int chapter02CursorStoryAnswer;
    // Separate prompt: 0 = blank, 1 = deny/shake, 2 = confirm/nod.
    public int chapter02CursorStorySafetyAnswer;
    public int chapter02CursorStorySecondTestAnswer;
    public int chapter02CursorStoryThirdTestAnswer;
    // Separate from test steps: completed tests never send the pointer back to a cage.
    public int chapter02CursorReleaseStep;
    public int chapter02CursorReleaseChoice;
    public bool chapter02CursorReleaseHeartGranted;
    public bool chapter02RedCursorUnlocked;
    public bool chapter02RedCursorHasPosition;
    public Vector2 chapter02RedCursorPosition;
    public bool chapter02CursorStoryTetherAttached;
    // Retained for compatibility with earlier timed-dialogue saves.
    public float chapter02CursorStoryElapsed;
    public OSMemoryPicSaveData pictureScenario = new OSMemoryPicSaveData();

    // Heart talk follows release; values 0..21 are an append-only save protocol.
    public int chapter02HeartTalkStep;
    public int chapter02HeartTalkScrollAnswer;
    public int chapter02HeartTalkFixAnswer;
    public int chapter02HeartTalkIdentityAnswer;
    // Retired task/care question. Keep its serialized field for older saves.
    public int chapter02HeartTalkMemoryAnswer;
    // Zero: unconfirmed wait line. Positive: confirmed, elapsed silent wait.
    public float chapter02HeartTalkWaitElapsed;

    // Read-along choices: 0 = unanswered, 1 / 2 = the authored option.
    public int chapter02ReadIronFrame;
    public int chapter02ReadHeartbeat;
    public int chapter02ReadFoolReading;
    public int chapter02ReadAssistant;
    // Retired 6160 callback; no current dialogue reads this value.
    public int chapter02ReadTrade;
    // Retired 6940 choice; that message is now a plain comment without a callback.
    public int chapter02ReadMakeup;
    // 7160 daughter question.
    public int chapter02ReadDaughter;
    // Completed memory lines wait here while an earlier Live question is open.
    public List<int> chapter02ReadAlongQueue = new List<int>();

    // Makeup pattern: 0 = unchosen, 1 = cat, 2 = heart.
    // Keep 0 until the player chooses at 7180; the old sleeve choice is unrelated.
    public int chapter02MakeupPattern;

    // Finale after 7460. Independent of the earlier cursor-test heart talk.
    // Step values are the append-only OSChapter02FinaleDirector.FinaleStep protocol.
    public int chapter02FinaleStep;
    // Next unfinished hidden beat (0..24), including its two sound-only beats.
    public int chapter02FinaleHiddenLine;
    // 0 = unanswered, 1 = "me", 2 = "who_else".
    public int chapter02FinaleRecognitionChoice;

    // Separate from both the cursor-test conversation and the hidden memory.
    // OSChapter02FinalTalkDirector.FinalTalkStep is an append-only save protocol.
    public int chapter02FinalTalkStep;
    // Mandatory invitation at the end of paper-push, before Wrap; persists until selected.
    public bool chapter02FinalTalkInvitationPending;
    // 0 = unanswered; 1 = together / ask; 2 = silent / wait.
    public int chapter02FinalTalkComfortAnswer;
    public int chapter02FinalTalkQuestionAnswer;
    // Demo-only presentation checkpoint. The full game keeps the Chapter03Ready
    // story boundary so a later chapter can continue from the same save.
    public bool chapter02DemoEndReached;

    // Chapter 3 unlock/upgrade handoff. Values are the append-only EntryStep protocol.
    public int chapter03EntryStep;
    // 0 = unanswered, 1 = assumed a direct install, 2 = never mind.
    public int chapter03EntryInstallAnswer;
    // Presentation position is independent of the current memory paragraph.
    public bool chapter03HomeInitialized;
    public OSHomeView.State chapter03HomeView;
    public OSChapter03StoryState chapter03Story = new OSChapter03StoryState();

    // Chapter 3 -> TV handoff. Independent, append-only EntryStep protocol.
    public int chapter04EntryStep;
    public float chapter04EntryElapsed;
    public bool chapter04TVCurtainOpen;
    // Defaults to pinned; only the player's pin button changes this preference.
    public bool chapter04TVPinned = true;
    public OSChapter04StoryState chapter04Story = new OSChapter04StoryState();

    public void EnsureData()
    {
        if (triggeredDataIndices == null)
            triggeredDataIndices = new List<int>();
        if (pictureScenario == null)
            pictureScenario = new OSMemoryPicSaveData();
        if (chapter02ReadAlongQueue == null)
            chapter02ReadAlongQueue = new List<int>();
        chapter02ReadAlongQueue.RemoveAll(node => node == 6160 || node == 7240 || node == 7320);
        // Values 15/16 were the removed task/care exchange; 17 is Quake.
        if (chapter02HeartTalkStep == 15 || chapter02HeartTalkStep == 16)
        {
            chapter02HeartTalkStep = 17;
            chapter02HeartTalkWaitElapsed = 0f;
        }
        if (chapter02MakeupPattern < 0 || chapter02MakeupPattern > 2)
            chapter02MakeupPattern = 0;
        chapter02FinaleHiddenLine = Mathf.Clamp(chapter02FinaleHiddenLine, 0, 24);
        chapter02FinaleRecognitionChoice = Mathf.Clamp(chapter02FinaleRecognitionChoice, 0, 2);
        chapter02FinalTalkStep = Mathf.Clamp(chapter02FinalTalkStep, 0, 17);
        if (chapter02FinalTalkStep > 0) chapter02FinalTalkInvitationPending = false;
        chapter02FinalTalkComfortAnswer = Mathf.Clamp(chapter02FinalTalkComfortAnswer, 0, 2);
        chapter02FinalTalkQuestionAnswer = Mathf.Clamp(chapter02FinalTalkQuestionAnswer, 0, 2);
        chapter03EntryStep = Mathf.Clamp(chapter03EntryStep, 0, 9);
        chapter03EntryInstallAnswer = Mathf.Clamp(chapter03EntryInstallAnswer, 0, 2);
        chapter04EntryStep = Mathf.Clamp(chapter04EntryStep, 0, 12);
        chapter04EntryElapsed = float.IsNaN(chapter04EntryElapsed) || float.IsInfinity(chapter04EntryElapsed)
            ? 0f : Mathf.Clamp(chapter04EntryElapsed, 0f, OSChapter04EntryDirector.InstallationSeconds);
        if (chapter03Story == null) chapter03Story = new OSChapter03StoryState();
        chapter03Story.EnsureData();
        if (chapter04Story == null) chapter04Story = new OSChapter04StoryState();
        chapter04Story.EnsureData();
        chapter02CollectionIntroStep = Mathf.Clamp(chapter02CollectionIntroStep, 0, 8);
        pictureScenario.EnsureData();
    }

    public void RecordTriggeredData(int dataIndex)
    {
        if (dataIndex == InvalidDataIndex)
            return;

        EnsureData();
        triggeredDataIndices.Add(dataIndex);
        currentDataIndex = dataIndex;
    }
}

public static class OSDesktopProcessKeys
{
    public const string Memory = "memory";
    public const string Record = "record";
    public const string Tool = "tool";
    public const string Gramophone = "gramophone";
    public const string EnergyLoop = "energy_loop";
    public const string PaperPush = "paper_push";
    public const string ClawMachine = "claw_machine";
    public const string File = "file";
    public const string StateCheck = "state_check";
    public const string Character = "character";
    public const string HeartHealTool = "heart_heal_tool";
    public const string LockHeart = "lock_heart";
    public const string KeyX = "key_x";
    public const string Gift = "gift";
    public const string TV = "tv";
}

public static class OSDesktopProcessContainerIds
{
    public const string Desktop = "desktop";
    public const string Folder = "folder";

    public static string Normalize(string containerId)
    {
        if (string.Equals(
                containerId,
                Desktop,
                StringComparison.Ordinal))
        {
            return Desktop;
        }

        if (string.Equals(
                containerId,
                Folder,
                StringComparison.Ordinal))
        {
            return Folder;
        }

        return null;
    }
}

[Serializable]
public sealed class OSDesktopProcessLayoutSaveData
{
    public string processKey;
    public string containerId;
    public int gridIndex;

    public OSDesktopProcessLayoutSaveData()
    {
    }

    public OSDesktopProcessLayoutSaveData(
        string key,
        string container,
        int index)
    {
        processKey = key;
        containerId = container;
        gridIndex = index;
    }
}

[Serializable]
public sealed class DesktopProcessNewSaveData
{
    public string processKey;
    public bool isNew;
    public bool isLocked;

    public DesktopProcessNewSaveData()
    {
    }

    public DesktopProcessNewSaveData(
        string key,
        bool newState,
        bool lockedState = false)
    {
        processKey = key;
        isNew = newState;
        isLocked = lockedState;
    }
}

[Serializable]
public enum OSCharacterInteractionSavePhase
{
    Inactive = 0,
    WaitingForTrigger = 1,
    Presenting = 2,
    Completed = 3
}

[Serializable]
public sealed class OSCharacterDeferredMessageSaveData
{
    public string sequenceId;
    public string entryId;
}

[Serializable]
public sealed class OSCharacterInteractionSaveData
{
    const int CurrentDataVersion = 1;
    static readonly string[] RemovedChapter02MessageIds = {
        "heart_talk_ch02_memory", "heart_talk_ch02_memory_reply",
        "chapter02_readalong_6160", "chapter02_readalong_6160_not_task",
        "chapter02_readalong_6160_reply_1", "chapter02_readalong_6160_reply_2",
        "chapter02_readalong_7240_a1", "chapter02_readalong_7240_a2", "chapter02_readalong_7320"
    };

    // Also accepts countdown keys in the form "sequence/entry".
    static bool IsRemovedChapter02Message(string id)
    {
        if (string.IsNullOrEmpty(id)) return false;
        int start = id.LastIndexOf('/') + 1;
        int length = id.Length - start;
        foreach (string removed in RemovedChapter02MessageIds)
            if (length == removed.Length && string.CompareOrdinal(id, start, removed, 0, length) == 0)
                return true;
        return false;
    }

    static bool IsRetiredChapter02Countdown(string key)
        => IsRemovedChapter02Message(key)
            || (!string.IsNullOrEmpty(key)
                && key.EndsWith("/chapter02_readalong_6940", StringComparison.Ordinal));

    public int dataVersion;
    public string activeSequenceId;
    public List<string> consumedEntryIds = new List<string>();
    public List<string> completedEntryIds = new List<string>();
    // Resource receipts outlive the active sequence and its presentation history.
    public List<string> appliedOptionResourceKeys = new List<string>();
    public string presentingEntryId;
    public string pendingEntryId;
    // Authored text-to-text delays survive closing Live and save/load.
    public string nextEntryCountdownKey;
    public float nextEntryCountdownRemaining;
    public OSCharacterInteractionSavePhase phase;
    public bool isCompleted;
    public List<string> consumedGlobalEntryIds = new List<string>();
    public string presentingGlobalEntryId;
    public string pendingGlobalEntryId;
    // The visible reading time survives hiding Live, changing language and loading a save.
    public string optionCountdownKey;
    public float optionCountdownElapsed;
    public string optionCountdownReplyKey;
    public string optionCountdownReleaseKey;
    public List<OSCharacterDeferredMessageSaveData> optionCountdownQueue =
        new List<OSCharacterDeferredMessageSaveData>();

    // EnsureData also runs on ordinary state reads. Reuse validation scratch
    // space without skipping normalization of fields changed during gameplay.
    [NonSerialized] HashSet<string> normalizedEntryIds;

    public IReadOnlyList<string> ConsumedEntryIds
    {
        get
        {
            EnsureData();
            return consumedEntryIds;
        }
    }

    public void EnsureData()
    {
        if (consumedEntryIds == null)
            consumedEntryIds = new List<string>();
        if (completedEntryIds == null)
            completedEntryIds = new List<string>();
        if (appliedOptionResourceKeys == null)
            appliedOptionResourceKeys = new List<string>();
        if (consumedGlobalEntryIds == null)
            consumedGlobalEntryIds = new List<string>();
        if (optionCountdownQueue == null)
            optionCountdownQueue = new List<OSCharacterDeferredMessageSaveData>();

        activeSequenceId = NormalizeId(activeSequenceId);
        presentingEntryId = NormalizeId(presentingEntryId);
        pendingEntryId = NormalizeId(pendingEntryId);
        presentingGlobalEntryId = NormalizeId(presentingGlobalEntryId);
        pendingGlobalEntryId = NormalizeId(pendingGlobalEntryId);

        // Removed entries must not reserve the message slot after loading.
        if (IsRemovedChapter02Message(presentingGlobalEntryId)) presentingGlobalEntryId = null;
        if (IsRemovedChapter02Message(pendingGlobalEntryId)) pendingGlobalEntryId = null;
        if (IsRetiredChapter02Countdown(optionCountdownKey))
        {
            optionCountdownKey = null;
            optionCountdownElapsed = 0f;
        }
        if (IsRetiredChapter02Countdown(optionCountdownReplyKey)) optionCountdownReplyKey = null;
        if (IsRetiredChapter02Countdown(optionCountdownReleaseKey)) optionCountdownReleaseKey = null;
        optionCountdownQueue.RemoveAll(item => item != null && IsRemovedChapter02Message(item.entryId));

        NormalizeIdList(consumedEntryIds);
        NormalizeIdList(completedEntryIds);
        NormalizeIdList(appliedOptionResourceKeys);
        NormalizeIdList(consumedGlobalEntryIds);

        if (dataVersion < CurrentDataVersion)
        {
            for (int i = 0; i < consumedEntryIds.Count; i++)
            {
                string entryId = consumedEntryIds[i];
                if (!string.Equals(
                        entryId,
                        presentingEntryId,
                        StringComparison.Ordinal)
                    && !completedEntryIds.Contains(entryId))
                {
                    completedEntryIds.Add(entryId);
                }
            }

            dataVersion = CurrentDataVersion;
        }

        for (int i = completedEntryIds.Count - 1; i >= 0; i--)
            if (!consumedEntryIds.Contains(completedEntryIds[i]))
                completedEntryIds.RemoveAt(i);

        if (presentingGlobalEntryId != null
            && !consumedGlobalEntryIds.Contains(presentingGlobalEntryId))
        {
            consumedGlobalEntryIds.Add(presentingGlobalEntryId);
        }
        if (pendingGlobalEntryId != null
            && consumedGlobalEntryIds.Contains(pendingGlobalEntryId))
        {
            pendingGlobalEntryId = null;
        }

        if (activeSequenceId == null)
        {
            consumedEntryIds.Clear();
            completedEntryIds.Clear();
            presentingEntryId = null;
            pendingEntryId = null;
            phase = OSCharacterInteractionSavePhase.Inactive;
            isCompleted = false;
            return;
        }

        if (isCompleted)
        {
            presentingEntryId = null;
            pendingEntryId = null;
            phase = OSCharacterInteractionSavePhase.Completed;
        }
        else if (phase == OSCharacterInteractionSavePhase.Presenting
                 && presentingEntryId == null)
        {
            phase = OSCharacterInteractionSavePhase.WaitingForTrigger;
        }
        else if (phase == OSCharacterInteractionSavePhase.Inactive
                 || phase == OSCharacterInteractionSavePhase.Completed)
        {
            phase = OSCharacterInteractionSavePhase.WaitingForTrigger;
        }
    }

    public bool ActivateSequence(string sequenceId)
    {
        EnsureData();
        string normalizedId = NormalizeId(sequenceId);
        if (string.Equals(
            activeSequenceId,
            normalizedId,
            StringComparison.Ordinal))
        {
            return false;
        }

        activeSequenceId = normalizedId;
        nextEntryCountdownKey = null;
        nextEntryCountdownRemaining = 0f;
        consumedEntryIds.Clear();
        completedEntryIds.Clear();
        presentingEntryId = null;
        pendingEntryId = null;
        isCompleted = false;
        phase = normalizedId == null
            ? OSCharacterInteractionSavePhase.Inactive
            : OSCharacterInteractionSavePhase.WaitingForTrigger;
        return true;
    }

    public bool HasConsumedEntry(string entryId)
    {
        EnsureData();
        string normalizedId = NormalizeId(entryId);
        return normalizedId != null
            && consumedEntryIds.Contains(normalizedId);
    }

    internal bool TryMarkOptionResourcesApplied(
        string sequenceId,
        string entryId,
        string optionId)
    {
        EnsureData();
        string sequence = NormalizeId(sequenceId);
        string entry = NormalizeId(entryId);
        string option = NormalizeId(optionId);
        if (sequence == null || entry == null || option == null)
            return false;

        // Length prefixes keep IDs containing punctuation from colliding.
        string key = $"{sequence.Length}:{sequence}{entry.Length}:{entry}{option.Length}:{option}";
        if (appliedOptionResourceKeys.Contains(key))
            return false;

        appliedOptionResourceKeys.Add(key);
        return true;
    }

    public bool HasCompletedEntry(string entryId)
    {
        EnsureData();
        string normalizedId = NormalizeId(entryId);
        return normalizedId != null
            && completedEntryIds.Contains(normalizedId);
    }

    public void BeginPresentation(string entryId, bool consume = true)
    {
        EnsureData();
        string normalizedId = NormalizeId(entryId);
        if (activeSequenceId == null || normalizedId == null)
            return;

        if (consume && !consumedEntryIds.Contains(normalizedId))
            consumedEntryIds.Add(normalizedId);

        presentingEntryId = normalizedId;
        pendingEntryId = null;
        phase = OSCharacterInteractionSavePhase.Presenting;
        isCompleted = false;
    }

    public void SetPendingEntry(string entryId)
    {
        EnsureData();
        pendingEntryId = NormalizeId(entryId);
    }

    public void CompletePresentation(
        string entryId,
        bool markCompleted = true)
    {
        EnsureData();
        string normalizedId = NormalizeId(entryId);
        if (normalizedId != null
            && !string.Equals(
                presentingEntryId,
                normalizedId,
                StringComparison.Ordinal))
        {
            return;
        }

        if (markCompleted
            && normalizedId != null
            && !completedEntryIds.Contains(normalizedId))
        {
            completedEntryIds.Add(normalizedId);
        }

        presentingEntryId = null;
        phase = OSCharacterInteractionSavePhase.WaitingForTrigger;
    }

    public void SetEntryConsumed(string entryId, bool consumed)
    {
        EnsureData();
        string normalizedId = NormalizeId(entryId);
        if (normalizedId == null)
            return;

        if (consumed)
        {
            if (!consumedEntryIds.Contains(normalizedId))
                consumedEntryIds.Add(normalizedId);
            return;
        }

        consumedEntryIds.Remove(normalizedId);
        completedEntryIds.Remove(normalizedId);
        if (presentingEntryId == normalizedId)
            presentingEntryId = null;
        if (pendingEntryId == normalizedId)
            pendingEntryId = null;

        if (activeSequenceId != null && isCompleted)
        {
            isCompleted = false;
            phase = OSCharacterInteractionSavePhase.WaitingForTrigger;
        }
    }

    public bool HasConsumedGlobalEntry(string entryId)
    {
        EnsureData();
        string normalizedId = NormalizeId(entryId);
        return normalizedId != null
            && consumedGlobalEntryIds.Contains(normalizedId);
    }

    public bool QueueGlobalEntry(string entryId, bool repeatable = false)
    {
        EnsureData();
        string normalizedId = NormalizeId(entryId);
        if (normalizedId == null
            || (!repeatable && consumedGlobalEntryIds.Contains(normalizedId))
            || presentingGlobalEntryId == normalizedId
            || pendingGlobalEntryId == normalizedId)
        {
            return false;
        }

        // Restored/global presentations are conservatively marked consumed by
        // EnsureData. Rearm only an authored repeatable entry, after rejecting
        // duplicates, so EnsureData will also preserve its new pending slot.
        if (repeatable)
            consumedGlobalEntryIds.Remove(normalizedId);
        pendingGlobalEntryId = normalizedId;
        return true;
    }

    public void BeginGlobalPresentation(
        string entryId,
        bool consume = true)
    {
        EnsureData();
        string normalizedId = NormalizeId(entryId);
        if (normalizedId == null)
            return;

        if (consume && !consumedGlobalEntryIds.Contains(normalizedId))
            consumedGlobalEntryIds.Add(normalizedId);

        pendingGlobalEntryId = null;
        presentingGlobalEntryId = normalizedId;
    }

    public void CompleteGlobalPresentation(string entryId)
    {
        EnsureData();
        string normalizedId = NormalizeId(entryId);
        if (normalizedId == null
            || presentingGlobalEntryId == normalizedId)
        {
            presentingGlobalEntryId = null;
        }
    }

    public void CancelGlobalPresentation(bool requeue = false)
    {
        EnsureData();
        string interruptedEntryId = presentingGlobalEntryId;
        presentingGlobalEntryId = null;
        if (requeue && interruptedEntryId != null)
        {
            consumedGlobalEntryIds.Remove(interruptedEntryId);
            pendingGlobalEntryId = interruptedEntryId;
        }
    }

    public void SetGlobalEntryConsumed(string entryId, bool consumed)
    {
        EnsureData();
        string normalizedId = NormalizeId(entryId);
        if (normalizedId == null)
            return;

        if (consumed)
        {
            if (!consumedGlobalEntryIds.Contains(normalizedId))
                consumedGlobalEntryIds.Add(normalizedId);
            pendingGlobalEntryId = null;
            return;
        }

        consumedGlobalEntryIds.Remove(normalizedId);
        if (presentingGlobalEntryId == normalizedId)
            presentingGlobalEntryId = null;
        if (pendingGlobalEntryId == normalizedId)
            pendingGlobalEntryId = null;
    }

    public void CancelPresentation(bool clearPending = true)
    {
        EnsureData();
        presentingEntryId = null;
        if (clearPending)
            pendingEntryId = null;

        phase = activeSequenceId == null
            ? OSCharacterInteractionSavePhase.Inactive
            : isCompleted
                ? OSCharacterInteractionSavePhase.Completed
                : OSCharacterInteractionSavePhase.WaitingForTrigger;
    }

    public void MarkCompleted()
    {
        EnsureData();
        if (activeSequenceId == null)
            return;

        presentingEntryId = null;
        pendingEntryId = null;
        isCompleted = true;
        phase = OSCharacterInteractionSavePhase.Completed;
    }

    static string NormalizeId(string value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    void NormalizeIdList(List<string> entryIds)
    {
        if (entryIds.Count == 0) return;
        if (normalizedEntryIds == null)
            normalizedEntryIds = new HashSet<string>(StringComparer.Ordinal);
        var uniqueEntryIds = normalizedEntryIds;
        uniqueEntryIds.Clear();
        for (int i = 0; i < entryIds.Count; i++)
        {
            string entryId = NormalizeId(entryIds[i]);
            if (entryId == null || !uniqueEntryIds.Add(entryId))
            {
                entryIds.RemoveAt(i);
                i--;
                continue;
            }

            entryIds[i] = entryId;
        }
    }
}

[Serializable]
public sealed class OSGramophoneSaveData
{
    public bool chapter01MusicInitialized;
    public bool chapter01DoorwayMusicInitialized;
    public bool doorwayCollected;
    public bool paperPushCollected;
    public string selectedTrackId;
    public bool wantsPlayback;
    public float playbackSeconds;
    public float volume = .7f;
    public float lastAudibleVolume = .7f;
    public List<string> manuallyPlayedTrackIds = new List<string>();
    public List<string> collectedTrackIds = new List<string>();
    // Versioned evidence migration; new listening receipts use collectedTrackIds.
    public int storyCollectionVersion;

    [NonSerialized] HashSet<string> normalizedTrackIds;

    public void EnsureData()
    {
        if (manuallyPlayedTrackIds == null) manuallyPlayedTrackIds = new List<string>();
        if (collectedTrackIds == null) collectedTrackIds = new List<string>();
        if (normalizedTrackIds == null)
            normalizedTrackIds = new HashSet<string>(StringComparer.Ordinal);
        var uniqueTracks = normalizedTrackIds;
        uniqueTracks.Clear();
        for (int i = 0; i < collectedTrackIds.Count; i++)
        {
            string id = collectedTrackIds[i]?.Trim();
            if (string.IsNullOrEmpty(id) || !uniqueTracks.Add(id))
            {
                collectedTrackIds.RemoveAt(i--);
                continue;
            }
            collectedTrackIds[i] = id;
        }
        volume = float.IsNaN(volume) || float.IsInfinity(volume) ? .7f : Mathf.Clamp01(volume);
        lastAudibleVolume = float.IsNaN(lastAudibleVolume) || float.IsInfinity(lastAudibleVolume)
            || lastAudibleVolume <= 0f ? .7f : Mathf.Clamp01(lastAudibleVolume);
        if (float.IsNaN(playbackSeconds) || float.IsInfinity(playbackSeconds)) playbackSeconds = 0f;
        playbackSeconds = Mathf.Max(0f, playbackSeconds);
    }
}

[Serializable]
public sealed class OSEnergyLoopSaveData
{
    public bool isCharging;
    // Old completed charges were already credited. Missing fields must not create a new reward.
    public bool chargeReadyToClaim;
    public float chargeDurationSeconds = GameDataManager.EnergyChargeDurationSeconds;
    public float remainingChargeSeconds;
    // Missing from legacy saves: the first/newly migrated charge has no cooldown.
    public float remainingCooldownSeconds;
    public int completedChargeCount;
    public int lastEnergyGained;
    public bool minigameDiscovered;
    public bool minigameUnlocked;
    public int paperPushWins;
    public int paperPushLosses;
    public int chapter01PaperPushWins;
    public bool paperPushSealPending;
    public bool paperPushSealed;
    // Chapter 1 invitations are transactions: a reload resumes the pending action.
    public bool paperPushFirstRematchPending;
    public bool paperPushFirstRematchDone;
    public bool paperPushFinalInvitationPending;
    // Chapter 2 rematch protocol. Values are append-only; zero preserves the seal.
    public int chapter02PaperPushStep;
    // The final talk begins fading audio as Live leaves the game for the center.
    public bool chapter02HeartTalkAudioStarted;
    public int chapter02PaperPushMatch1Won;
    public int chapter02PaperPushHintStage;
    // Second-match outcome: 0 unfinished, 1 player won, 2 player lost.
    public int chapter02PaperPushTiltWin;
    public List<string> chapter02PaperPushCueQueue = new List<string>();
    public List<string> chapter02PaperPushCueHistory = new List<string>();
    public List<string> paperPushReactionHistory = new List<string>();
    public List<string> paperPushReactionQueue = new List<string>();
    public OSPaperPushMatchSaveData paperPushMatch = new OSPaperPushMatchSaveData();

    public void EnsureData()
    {
        // Preserve visible progress when a legacy 90-second charge is loaded.
        // Already claimable energy stays claimable; no reward is granted twice.
        if (Mathf.Approximately(chargeDurationSeconds, 90f))
        {
            if (isCharging && !chargeReadyToClaim)
                remainingChargeSeconds = Mathf.Clamp01(remainingChargeSeconds / 90f)
                    * GameDataManager.EnergyChargeDurationSeconds;
            chargeDurationSeconds = GameDataManager.EnergyChargeDurationSeconds;
        }
        if (float.IsNaN(remainingCooldownSeconds) || float.IsInfinity(remainingCooldownSeconds))
            remainingCooldownSeconds = 0f;
        remainingCooldownSeconds = Mathf.Clamp(
            remainingCooldownSeconds, 0f, GameDataManager.EnergyChargeCooldownSeconds);
        if (paperPushReactionHistory == null) paperPushReactionHistory = new List<string>();
        if (paperPushReactionQueue == null) paperPushReactionQueue = new List<string>();
        if (paperPushMatch == null) paperPushMatch = new OSPaperPushMatchSaveData();
        if (chapter02PaperPushCueQueue == null) chapter02PaperPushCueQueue = new List<string>();
        if (chapter02PaperPushCueHistory == null) chapter02PaperPushCueHistory = new List<string>();
        chapter02PaperPushMatch1Won = Mathf.Clamp(chapter02PaperPushMatch1Won, 0, 2);
        chapter02PaperPushHintStage = Mathf.Clamp(chapter02PaperPushHintStage, 0, 2);
        chapter02PaperPushTiltWin = Mathf.Clamp(chapter02PaperPushTiltWin, 0, 2);
        paperPushMatch.chapter02PushAttempts = Mathf.Clamp(paperPushMatch.chapter02PushAttempts, 0, 2);
        paperPushMatch.chapter02LastCountedPushRound = Mathf.Max(0, paperPushMatch.chapter02LastCountedPushRound);
        if (float.IsNaN(paperPushMatch.chapter02TiltDegrees) || float.IsInfinity(paperPushMatch.chapter02TiltDegrees))
            paperPushMatch.chapter02TiltDegrees = 0f;
        if (float.IsNaN(paperPushMatch.chapter02SlideVelocity) || float.IsInfinity(paperPushMatch.chapter02SlideVelocity))
            paperPushMatch.chapter02SlideVelocity = 0f;
        paperPushMatch.chapter02TiltDegrees = Mathf.Clamp(paperPushMatch.chapter02TiltDegrees, -18f, 18f);
        paperPushMatch.chapter02SlideVelocity = Mathf.Clamp(paperPushMatch.chapter02SlideVelocity, -48f, 48f);
        paperPushMatch.matchStage = Mathf.Clamp(paperPushMatch.matchStage, 1, paperPushMatch.chapter == 1 ? 3 : 2);
        chapter01PaperPushWins = Mathf.Clamp(chapter01PaperPushWins, 0, 3);
        if (chargeReadyToClaim) { isCharging = false; remainingChargeSeconds = 0f; }
        if (paperPushSealed)
        {
            paperPushSealPending = false;
            if (paperPushMatch.chapter == 1) paperPushMatch.active = false;
        }
        if (chapter01PaperPushWins >= 2) paperPushFirstRematchPending = false;
        if (chapter01PaperPushWins >= 3) paperPushFinalInvitationPending = false;
    }
}

// Checkpoint at the start of an interval/choice/resolution. A load during a tween
// replays that action from its starting positions, without recording a second win.
[Serializable]
public sealed class OSPaperPushMatchSaveData
{
    public bool active;
    public int chapter;
    public int matchStage = 1;
    public bool chapter02BraceReleased;
    public int phase; // 0: pushing, 1: choosing, 2: resolving, 3: curtain intro, 4: chapter 2 sliding
    // Legacy field names retained for saves; these now count all completed rounds.
    public int chapter02PushAttempts;
    public int chapter02LastCountedPushRound;
    public float chapter02TiltDegrees;
    public float chapter02SlideVelocity;
    public int round;
    public int characterLossStreak;
    public int previousPlayerMove;
    public int characterMove;
    public int playerMove;
    public float playerX = -67f;
    public float characterX = 67f;
    // Live progress of a pushing interval. Older saves default to its beginning.
    public float pushElapsed;
    public float pushStartPlayerX;
    public float pushStartCharacterX;
    public bool characterHadLead;
}

[Serializable]
public sealed class OSProcessSaveData
{
    public const int MinHeartHealProcessIndex = 0;
    public const int MaxHeartHealProcessIndex = 4;
    public const int MinChapterIndex = 0;

    public List<OSWindowLayoutSaveData> windowLayouts =
        new List<OSWindowLayoutSaveData>();
    public List<string> alwaysOnTopWindowStack = new List<string>();
    public OSMemoryProcessSaveData memory = new OSMemoryProcessSaveData();
    public OSGramophoneSaveData gramophone = new OSGramophoneSaveData();
    public OSEnergyLoopSaveData energyLoop = new OSEnergyLoopSaveData();
    public OSChapter01EndingSaveData chapter01Ending = new OSChapter01EndingSaveData();
    public OSCharacterInteractionSaveData characterInteraction =
        new OSCharacterInteractionSaveData();
    public List<DesktopProcessNewSaveData> desktopProcessNewStateEntries =
        new List<DesktopProcessNewSaveData>();
    public List<OSDesktopProcessLayoutSaveData> desktopProcessLayouts =
        new List<OSDesktopProcessLayoutSaveData>();
    public bool chapter01KeyXAcquired;
    public bool toolUnlocked;
    public bool toolInitializationCompleted;
    public bool toolProgramsUnpacked;
    public OSToolDecorationState toolDecorationState;
    public List<string> installedToolProgramIds = new List<string>();
    public int currentChapterIndex = MinChapterIndex;
    public int heartHealProcessIndex;
    public float stateCheckProgress;

    [NonSerialized]
    Dictionary<string, bool> desktopProcessNewStates =
        new Dictionary<string, bool>(StringComparer.Ordinal);
    [NonSerialized]
    IReadOnlyDictionary<string, bool> desktopProcessNewStatesView;
    [NonSerialized]
    Dictionary<string, bool> desktopProcessLockedStates =
        new Dictionary<string, bool>(StringComparer.Ordinal);
    [NonSerialized]
    IReadOnlyDictionary<string, bool> desktopProcessLockedStatesView;
    [NonSerialized]
    bool desktopProcessNewStateLookupInitialized;
    [NonSerialized]
    bool desktopProcessLayoutsSanitized;

    public IReadOnlyList<string> AlwaysOnTopWindowStack
    {
        get
        {
            EnsureData();
            return alwaysOnTopWindowStack;
        }
    }

    public IReadOnlyDictionary<string, bool> DesktopProcessNewStates
    {
        get
        {
            EnsureData();
            return desktopProcessNewStatesView;
        }
    }

    public IReadOnlyDictionary<string, bool> DesktopProcessLockedStates
    {
        get
        {
            EnsureData();
            return desktopProcessLockedStatesView;
        }
    }

    public IReadOnlyList<OSDesktopProcessLayoutSaveData>
        DesktopProcessLayouts
    {
        get
        {
            EnsureData();
            return desktopProcessLayouts;
        }
    }

    public void EnsureData()
    {
        if (windowLayouts == null)
            windowLayouts = new List<OSWindowLayoutSaveData>();

        if (installedToolProgramIds == null)
            installedToolProgramIds = new List<string>();

        // Older saves did not record unpacking; these later steps prove it finished.
        if (!toolProgramsUnpacked
            && (installedToolProgramIds.Count > 0 || toolDecorationState != OSToolDecorationState.None))
            toolProgramsUnpacked = true;

        if (alwaysOnTopWindowStack == null)
            alwaysOnTopWindowStack = new List<string>();

        if (memory == null)
            memory = new OSMemoryProcessSaveData();

        if (gramophone == null)
            gramophone = new OSGramophoneSaveData();
        gramophone.EnsureData();
        if (energyLoop == null)
            energyLoop = new OSEnergyLoopSaveData();
        if (chapter01Ending == null)
            chapter01Ending = new OSChapter01EndingSaveData();
        chapter01Ending.EnsureData();

        if (characterInteraction == null)
        {
            characterInteraction = new OSCharacterInteractionSaveData();
        }

        if (desktopProcessNewStateEntries == null)
        {
            desktopProcessNewStateEntries =
                new List<DesktopProcessNewSaveData>();
            desktopProcessNewStateLookupInitialized = false;
        }

        if (desktopProcessLayouts == null)
        {
            desktopProcessLayouts =
                new List<OSDesktopProcessLayoutSaveData>();
            desktopProcessLayoutsSanitized = false;
        }

        if (!desktopProcessNewStateLookupInitialized)
            RebuildDesktopProcessNewStateLookup();
        if (!desktopProcessLayoutsSanitized)
            SanitizeDesktopProcessLayouts();

        currentChapterIndex = Mathf.Max(
            MinChapterIndex,
            currentChapterIndex);
        heartHealProcessIndex = Mathf.Clamp(
            heartHealProcessIndex,
            MinHeartHealProcessIndex,
            MaxHeartHealProcessIndex);
        if (float.IsNaN(stateCheckProgress)
            || float.IsInfinity(stateCheckProgress))
        {
            stateCheckProgress = 0f;
        }

        stateCheckProgress = Mathf.Max(0f, stateCheckProgress);
        memory.EnsureData();
        energyLoop.EnsureData();
        characterInteraction.EnsureData();
    }

    public bool TryGetDesktopProcessIsNew(
        string processKey,
        out bool isNew)
    {
        EnsureData();
        string key = NormalizeDesktopProcessKey(processKey);
        if (key == null)
        {
            isNew = false;
            return false;
        }

        return desktopProcessNewStates.TryGetValue(key, out isNew);
    }

    public bool TryGetDesktopProcessIsLocked(
        string processKey,
        out bool isLocked)
    {
        EnsureData();
        string key = NormalizeDesktopProcessKey(processKey);
        if (key == null)
        {
            isLocked = false;
            return false;
        }

        return desktopProcessLockedStates.TryGetValue(key, out isLocked);
    }

    public void GetOrInitializeDesktopProcessState(
        string processKey,
        bool initialIsNew,
        bool initialIsLocked,
        out bool isNew,
        out bool isLocked)
    {
        EnsureData();
        string key = NormalizeDesktopProcessKey(processKey);
        if (key == null)
        {
            isNew = false;
            isLocked = false;
            return;
        }

        if (desktopProcessNewStates.TryGetValue(key, out isNew)
            && desktopProcessLockedStates.TryGetValue(key, out isLocked))
        {
            return;
        }

        isNew = initialIsNew;
        isLocked = initialIsLocked;
        desktopProcessNewStateEntries.Add(
            new DesktopProcessNewSaveData(key, isNew, isLocked));
        desktopProcessNewStates[key] = isNew;
        desktopProcessLockedStates[key] = isLocked;
    }

    public bool GetOrInitializeDesktopProcessIsNew(
        string processKey,
        bool initialIsNew)
    {
        GetOrInitializeDesktopProcessState(
            processKey,
            initialIsNew,
            false,
            out bool isNew,
            out _);
        return isNew;
    }

    public bool GetOrInitializeDesktopProcessIsLocked(
        string processKey,
        bool initialIsLocked)
    {
        GetOrInitializeDesktopProcessState(
            processKey,
            false,
            initialIsLocked,
            out _,
            out bool isLocked);
        return isLocked;
    }

    public bool SetDesktopProcessIsNew(string processKey, bool isNew)
    {
        EnsureData();
        string key = NormalizeDesktopProcessKey(processKey);
        if (key == null)
            return false;

        for (int i = 0; i < desktopProcessNewStateEntries.Count; i++)
        {
            DesktopProcessNewSaveData entry =
                desktopProcessNewStateEntries[i];
            if (entry == null
                || !string.Equals(
                    entry.processKey,
                    key,
                    StringComparison.Ordinal))
            {
                continue;
            }

            if (entry.isNew == isNew)
                return false;

            entry.isNew = isNew;
            desktopProcessNewStates[key] = isNew;
            return true;
        }

        desktopProcessNewStateEntries.Add(
            new DesktopProcessNewSaveData(key, isNew));
        desktopProcessNewStates.Add(key, isNew);
        desktopProcessLockedStates.Add(key, false);
        return true;
    }

    public bool SetDesktopProcessIsLocked(
        string processKey,
        bool isLocked)
    {
        EnsureData();
        string key = NormalizeDesktopProcessKey(processKey);
        if (key == null)
            return false;

        for (int i = 0; i < desktopProcessNewStateEntries.Count; i++)
        {
            DesktopProcessNewSaveData entry =
                desktopProcessNewStateEntries[i];
            if (entry == null
                || !string.Equals(
                    entry.processKey,
                    key,
                    StringComparison.Ordinal))
            {
                continue;
            }

            if (entry.isLocked == isLocked)
                return false;

            entry.isLocked = isLocked;
            desktopProcessLockedStates[key] = isLocked;
            return true;
        }

        desktopProcessNewStateEntries.Add(
            new DesktopProcessNewSaveData(key, false, isLocked));
        desktopProcessNewStates.Add(key, false);
        desktopProcessLockedStates.Add(key, isLocked);
        return true;
    }

    void RebuildDesktopProcessNewStateLookup()
    {
        if (desktopProcessNewStates == null)
        {
            desktopProcessNewStates =
                new Dictionary<string, bool>(StringComparer.Ordinal);
            desktopProcessNewStatesView = null;
        }
        else
        {
            desktopProcessNewStates.Clear();
        }

        if (desktopProcessLockedStates == null)
        {
            desktopProcessLockedStates =
                new Dictionary<string, bool>(StringComparer.Ordinal);
            desktopProcessLockedStatesView = null;
        }
        else
        {
            desktopProcessLockedStates.Clear();
        }

        for (int i = 0; i < desktopProcessNewStateEntries.Count; i++)
        {
            DesktopProcessNewSaveData entry =
                desktopProcessNewStateEntries[i];
            string key = entry != null
                ? NormalizeDesktopProcessKey(entry.processKey)
                : null;
            if (key == null || desktopProcessNewStates.ContainsKey(key))
            {
                desktopProcessNewStateEntries.RemoveAt(i);
                i--;
                continue;
            }

            entry.processKey = key;
            desktopProcessNewStates.Add(key, entry.isNew);
            desktopProcessLockedStates.Add(key, entry.isLocked);
        }

        if (desktopProcessNewStatesView == null)
        {
            desktopProcessNewStatesView =
                new ReadOnlyDictionary<string, bool>(
                    desktopProcessNewStates);
        }

        if (desktopProcessLockedStatesView == null)
        {
            desktopProcessLockedStatesView =
                new ReadOnlyDictionary<string, bool>(
                    desktopProcessLockedStates);
        }

        desktopProcessNewStateLookupInitialized = true;
    }

    static string NormalizeDesktopProcessKey(string processKey)
    {
        return string.IsNullOrWhiteSpace(processKey)
            ? null
            : processKey.Trim();
    }

    public bool TryGetDesktopProcessLayout(
        string processKey,
        out OSDesktopProcessLayoutSaveData layout)
    {
        EnsureData();
        string key = NormalizeDesktopProcessKey(processKey);
        if (key == null)
        {
            layout = null;
            return false;
        }

        layout = desktopProcessLayouts.Find(item =>
            item != null
            && string.Equals(
                item.processKey,
                key,
                StringComparison.Ordinal));
        return layout != null;
    }

    public bool SetDesktopProcessLayout(
        string processKey,
        string containerId,
        int gridIndex)
    {
        EnsureData();
        string key = NormalizeDesktopProcessKey(processKey);
        string container = OSDesktopProcessContainerIds.Normalize(
            containerId);
        if (key == null || container == null)
            return false;

        int sanitizedIndex = Mathf.Max(0, gridIndex);
        OSDesktopProcessLayoutSaveData target = desktopProcessLayouts.Find(
            item => item != null
                && string.Equals(
                    item.processKey,
                    key,
                    StringComparison.Ordinal));
        if (target == null)
        {
            desktopProcessLayouts.Add(
                new OSDesktopProcessLayoutSaveData(
                    key,
                    container,
                    sanitizedIndex));
            return true;
        }

        if (string.Equals(
                target.containerId,
                container,
                StringComparison.Ordinal)
            && target.gridIndex == sanitizedIndex)
        {
            return false;
        }

        target.containerId = container;
        target.gridIndex = sanitizedIndex;
        return true;
    }

    void SanitizeDesktopProcessLayouts()
    {
        var processKeys = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < desktopProcessLayouts.Count; i++)
        {
            OSDesktopProcessLayoutSaveData layout =
                desktopProcessLayouts[i];
            string key = layout != null
                ? NormalizeDesktopProcessKey(layout.processKey)
                : null;
            string container = layout != null
                ? OSDesktopProcessContainerIds.Normalize(layout.containerId)
                : null;
            if (key == null
                || container == null
                || !processKeys.Add(key))
            {
                desktopProcessLayouts.RemoveAt(i);
                i--;
                continue;
            }

            layout.processKey = key;
            layout.containerId = container;
            layout.gridIndex = Mathf.Max(0, layout.gridIndex);
        }

        desktopProcessLayoutsSanitized = true;
    }

    public bool TryGetWindowLayout(
        string windowId,
        out OSWindowLayoutSaveData layout)
    {
        EnsureData();
        layout = windowLayouts.Find(item =>
            item != null && item.windowId == windowId);
        return layout != null;
    }

    public void SetWindowLayout(OSWindowLayoutSaveData source)
    {
        if (source == null || string.IsNullOrWhiteSpace(source.windowId))
            return;

        EnsureData();
        OSWindowLayoutSaveData target = windowLayouts.Find(item =>
            item != null && item.windowId == source.windowId);
        if (target == null)
        {
            target = new OSWindowLayoutSaveData();
            windowLayouts.Add(target);
        }

        target.windowId = source.windowId;
        target.anchoredPosition = source.anchoredPosition;
        target.size = source.size;
        target.siblingIndex = source.siblingIndex;
    }

    public bool TryGetWindowContentState(
        string windowId,
        out OSWindowLayoutSaveData windowState)
    {
        return TryGetWindowLayout(windowId, out windowState)
               && windowState.hasContentState
               && !string.IsNullOrWhiteSpace(windowState.contentStateId);
    }

    public void SetWindowContentState(
        string windowId,
        string stateId,
        int contextIndex,
        float progress,
        int checkpointValue,
        Vector2 position = default)
    {
        string normalizedWindowId = string.IsNullOrWhiteSpace(windowId)
            ? null
            : windowId.Trim();
        string normalizedStateId = string.IsNullOrWhiteSpace(stateId)
            ? null
            : stateId.Trim();
        if (normalizedWindowId == null || normalizedStateId == null)
            return;

        EnsureData();
        OSWindowLayoutSaveData target = windowLayouts.Find(item =>
            item != null && item.windowId == normalizedWindowId);
        if (target == null)
        {
            target = new OSWindowLayoutSaveData
            {
                windowId = normalizedWindowId
            };
            windowLayouts.Add(target);
        }

        target.hasContentState = true;
        target.contentStateId = normalizedStateId;
        target.contentContextIndex = contextIndex;
        target.contentProgress = float.IsNaN(progress)
                                 || float.IsInfinity(progress)
            ? 0f
            : progress;
        target.contentCheckpointValue = checkpointValue;
        target.contentPosition = IsFinite(position.x) && IsFinite(position.y)
            ? position
            : Vector2.zero;
    }

    public void ClearWindowContentState(string windowId)
    {
        if (!TryGetWindowLayout(windowId, out OSWindowLayoutSaveData target))
            return;

        target.hasContentState = false;
        target.contentStateId = string.Empty;
        target.contentContextIndex =
            OSWindowLayoutSaveData.InvalidContentContextIndex;
        target.contentProgress = 0f;
        target.contentCheckpointValue = 0;
        target.contentPosition = Vector2.zero;
    }

    static bool IsFinite(float value)
    {
        return !float.IsNaN(value) && !float.IsInfinity(value);
    }

    public void SetAlwaysOnTopWindowStack(
        IReadOnlyList<string> windowIds)
    {
        EnsureData();
        alwaysOnTopWindowStack.Clear();
        if (windowIds == null)
            return;

        var uniqueIds = new HashSet<string>();
        for (int i = 0; i < windowIds.Count; i++)
        {
            string windowId = windowIds[i];
            if (!string.IsNullOrWhiteSpace(windowId)
                && uniqueIds.Add(windowId))
            {
                alwaysOnTopWindowStack.Add(windowId);
            }
        }
    }
}

[Serializable]
public sealed class MainGameSaveData
{
    public bool movementTutorialCompleted;
}

[Serializable]
public sealed class GameSaveData
{
    public int saveVersion = 1;
    // Assigned only by New Game, then preserved by every save/load. Empty legacy
    // saves keep using the original farewell receipt so upgrading cannot resend it.
    public string farewellSaveId;
    // Missing fields identify legacy saves. Do not initialize this version to 1.
    public int storyAchievementVersion;
    public int storyAchievementMilestone;
    public bool hasStartedGame;
    public GameFlowSaveData flow = new GameFlowSaveData();
    public OSResumeSaveData osResume = new OSResumeSaveData();
    public List<EventCompletionData> eventCompletionList =
        new List<EventCompletionData>();
    public List<int> unlockedRecordDataIndices = new List<int>();
    public MainGameSaveData mainGame = new MainGameSaveData();
    public OSProcessSaveData osProcess = new OSProcessSaveData();
    public OSResourceSaveData osResources = new OSResourceSaveData();
    public GameOptionSaveData gameOptions = new GameOptionSaveData();
    public double totalPlayTimeSeconds;

    public void EnsureData()
    {
        if (flow == null) flow = new GameFlowSaveData();
        if (osResume == null) osResume = new OSResumeSaveData();
        flow.EnsureData();osResume.EnsureData();
        if (eventCompletionList == null)
            eventCompletionList = new List<EventCompletionData>();

        if (unlockedRecordDataIndices == null)
            unlockedRecordDataIndices = new List<int>();

        if (mainGame == null)
            mainGame = new MainGameSaveData();

        if (osProcess == null)
            osProcess = new OSProcessSaveData();

        if (osResources == null)
            osResources = new OSResourceSaveData();

        if (gameOptions == null)
            gameOptions = new GameOptionSaveData();

        if (double.IsNaN(totalPlayTimeSeconds)
            || double.IsInfinity(totalPlayTimeSeconds)
            || totalPlayTimeSeconds < 0d)
        {
            totalPlayTimeSeconds = 0d;
        }

        osProcess.EnsureData();
        gameOptions.Sanitize();
        StoryAchievementProgress.MigrateLegacyProgress(this);
    }

    public double AddPlayTime(double seconds)
    {
        EnsureData();
        if (double.IsNaN(seconds)
            || double.IsInfinity(seconds)
            || seconds <= 0d)
        {
            return totalPlayTimeSeconds;
        }

        totalPlayTimeSeconds += seconds;
        return totalPlayTimeSeconds;
    }

    public bool IsRecordDataUnlocked(int recordIndex)
    {
        EnsureData();
        return unlockedRecordDataIndices.Contains(recordIndex);
    }

    public bool UnlockRecordData(int recordIndex)
    {
        EnsureData();
        if (unlockedRecordDataIndices.Contains(recordIndex))
            return false;

        unlockedRecordDataIndices.Add(recordIndex);
        return true;
    }

    public bool IsEventCompleted(string eventID)
    {
        EventCompletionData completion = FindEvent(eventID);
        return completion != null && completion.isCompleted;
    }

    public bool TryGetEventState(
        string eventID,
        out bool isActive,
        out bool isCompleted)
    {
        EventCompletionData completion = FindEvent(eventID);
        if (completion == null)
        {
            isActive = false;
            isCompleted = false;
            return false;
        }

        isActive = completion.isActive;
        isCompleted = completion.isCompleted;
        return true;
    }

    public void GetOrInitializeEventState(
        string eventID,
        bool initialIsActive,
        out bool isActive,
        out bool isCompleted)
    {
        EventCompletionData completion = GetOrCreateEvent(
            eventID,
            initialIsActive);
        isActive = completion != null && completion.isActive;
        isCompleted = completion != null && completion.isCompleted;
    }

    public void SetEventState(
        string eventID,
        bool isActive,
        bool isCompleted)
    {
        EventCompletionData completion = GetOrCreateEvent(eventID, isActive);
        if (completion == null)
            return;

        completion.isActive = isActive;
        completion.isCompleted = isCompleted;
    }

    public void SetEventActive(string eventID, bool isActive)
    {
        EventCompletionData completion = GetOrCreateEvent(eventID, isActive);
        if (completion != null)
            completion.isActive = isActive;
    }

    public void SetEventCompleted(string eventID, bool isCompleted)
    {
        EventCompletionData completion = GetOrCreateEvent(
            eventID,
            !isCompleted);
        if (completion != null)
            completion.isCompleted = isCompleted;
    }

    EventCompletionData GetOrCreateEvent(
        string eventID,
        bool initialIsActive)
    {
        if (string.IsNullOrEmpty(eventID))
            return null;

        if (eventCompletionList == null)
            eventCompletionList = new List<EventCompletionData>();

        EventCompletionData completion = FindEvent(eventID);
        if (completion != null)
            return completion;

        completion = new EventCompletionData
        {
            eventID = eventID,
            isActive = initialIsActive
        };
        eventCompletionList.Add(completion);
        return completion;
    }

    EventCompletionData FindEvent(string eventID)
    {
        if (string.IsNullOrEmpty(eventID) || eventCompletionList == null)
            return null;

        return eventCompletionList.Find(completion =>
            completion != null && completion.eventID == eventID);
    }
}
