using System;
using System.Collections.Generic;
using UnityEngine;

public enum OSCharacterInteractionTriggerType
{
    OSBecameVisible = 0,
    DesktopProcessPointerEnter = 1,
    DesktopProcessClicked = 2,
    WindowOpened = 3,
    ExternalSignal = 4,
    WindowClosed = 5
}

public enum OSCharacterInteractionPartType
{
    Text = 0,
    TextWithOptions = 1,
    Image = 2
}

public enum OSCharacterInteractionReplacementPolicy
{
    InterruptCurrent = 0,
    WaitForCurrentTextComplete = 1
}

public enum OSCharacterInteractionDisplayPolicy
{
    DoText = 0,
    Immediate = 1
}

[Serializable]
public sealed class OSCharacterInteractionOptionData
{
    [SerializeField] string optionId;
    [SerializeField] string localizationKey;
    [SerializeField, TextArea] string fallbackText;
    [SerializeField] string nextEntryId;
    [SerializeField] MrTGame.Narrative.OSMemoryResourceChange resourceChange =
        new MrTGame.Narrative.OSMemoryResourceChange();

    public string OptionId => optionId;
    public string LocalizationKey => localizationKey;
    public string FallbackText => fallbackText;
    // Legacy assets contain the literal "null" for an absent successor.
    public string NextEntryId => nextEntryId == "null" ? null : nextEntryId;
    public MrTGame.Narrative.OSMemoryResourceChange ResourceChange =>
        resourceChange;
}

[Serializable]
public sealed class OSCharacterInteractionEntryData
{
    [SerializeField] string entryId;
    [SerializeField] OSCharacterInteractionTriggerType triggerType;
    [SerializeField] string triggerTargetId;
    [SerializeField] OSCharacterInteractionPartType partType;
    [SerializeField] string localizationKey;
    [SerializeField, TextArea] string fallbackText;
    [SerializeField]
    OSCharacterInteractionReplacementPolicy replacementPolicy;
    [SerializeField] OSCharacterInteractionDisplayPolicy displayPolicy;
    [SerializeField] string presentationCueId;
    [SerializeField] int requiredHeartHealProcessIndex = -1;
    [SerializeField] int requiredChapterIndex = -1;
    [SerializeField]
    List<string> requiredCompletedEntryIds = new List<string>();
    [SerializeField]
    List<OSCharacterInteractionOptionData> options =
        new List<OSCharacterInteractionOptionData>();
    [SerializeField, Min(0f)] float triggerDelay;
    [SerializeField, Min(0f)] float autoCloseDelay;
    [SerializeField] bool repeatable;
    [SerializeField] string nextEntryId;
    [SerializeField, Min(0f)] float nextEntryDelay;
    [SerializeField] bool loopTrailingDots;
    [SerializeField] bool waitForExternalCompletion;
    [SerializeField] bool preserveOnOwnerClose;
    [SerializeField, Min(0f)] float optionTimeoutDelay;
    [SerializeField] string timeoutOptionId;
    [SerializeField] bool selectTimeoutOptionOnInterrupt;
    [Tooltip("This question may finish unanswered. Required story choices must leave this off.")]
    [SerializeField] bool allowUnansweredTimeout;
    [Tooltip("Upcoming main messages in story order, excluding option replies. Alternative versions share one slot. Only the next three are inspected.")]
    [SerializeField] List<string> followingMessageEntryIds = new List<string>();

    public string EntryId => entryId;
    public OSCharacterInteractionTriggerType TriggerType => triggerType;
    public string TriggerTargetId => triggerTargetId;
    public OSCharacterInteractionPartType PartType => partType;
    public string LocalizationKey => localizationKey;
    public string FallbackText => fallbackText;
    public OSCharacterInteractionReplacementPolicy ReplacementPolicy =>
        replacementPolicy;
    public OSCharacterInteractionDisplayPolicy DisplayPolicy => displayPolicy;
    public string PresentationCueId => presentationCueId;
    public int RequiredHeartHealProcessIndex =>
        requiredHeartHealProcessIndex;
    public int RequiredChapterIndex => requiredChapterIndex;
    public IReadOnlyList<string> RequiredCompletedEntryIds =>
        requiredCompletedEntryIds;
    public IReadOnlyList<OSCharacterInteractionOptionData> Options => options;
    public float TriggerDelay => triggerDelay;
    public float AutoCloseDelay => autoCloseDelay;
    public bool Repeatable => repeatable;
    // Keep terminal text on its normal completion/auto-close path.
    public string NextEntryId => nextEntryId == "null" ? null : nextEntryId;
    public float NextEntryDelay => nextEntryDelay;
    public bool LoopTrailingDots => loopTrailingDots;
    public bool WaitForExternalCompletion => waitForExternalCompletion;
    public bool PreserveOnOwnerClose => preserveOnOwnerClose;
    public float OptionTimeoutDelay => optionTimeoutDelay;
    public string TimeoutOptionId => timeoutOptionId;
    public bool SelectTimeoutOptionOnInterrupt => selectTimeoutOptionOnInterrupt;
    public bool AllowUnansweredTimeout => allowUnansweredTimeout;
    public IReadOnlyList<string> FollowingMessageEntryIds => followingMessageEntryIds;
}

[Serializable]
public sealed class OSCharacterInteractionSequenceData
{
    [SerializeField] string sequenceId;
    [SerializeField]
    List<OSCharacterInteractionEntryData> entries =
        new List<OSCharacterInteractionEntryData>();

    public string SequenceId => sequenceId;
    public IReadOnlyList<OSCharacterInteractionEntryData> Entries => entries;

    public bool TryGetEntry(
        string targetEntryId,
        out OSCharacterInteractionEntryData entry)
    {
        entry = null;
        if (string.IsNullOrWhiteSpace(targetEntryId) || entries == null)
            return false;

        string normalizedId = targetEntryId.Trim();
        for (int i = 0; i < entries.Count; i++)
        {
            OSCharacterInteractionEntryData candidate = entries[i];
            if (candidate != null
                && string.Equals(
                    candidate.EntryId,
                    normalizedId,
                    StringComparison.Ordinal))
            {
                entry = candidate;
                return true;
            }
        }

        return false;
    }
}

[CreateAssetMenu(
    fileName = "OSCharacterInteractionData",
    menuName = "MrTGame/OS/Character Interaction Data")]
public sealed class OSCharacterInteractionData_SO : ScriptableObject
{
    [SerializeField]
    List<OSCharacterInteractionSequenceData> sequences =
        new List<OSCharacterInteractionSequenceData>();
    [SerializeField]
    List<OSCharacterInteractionEntryData> globalEntries =
        new List<OSCharacterInteractionEntryData>();

    public IReadOnlyList<OSCharacterInteractionSequenceData> Sequences =>
        sequences;
    public IReadOnlyList<OSCharacterInteractionEntryData> GlobalEntries =>
        globalEntries;

    public bool TryGetSequence(
        string targetSequenceId,
        out OSCharacterInteractionSequenceData sequence)
    {
        sequence = null;
        if (string.IsNullOrWhiteSpace(targetSequenceId) || sequences == null)
            return false;

        string normalizedId = targetSequenceId.Trim();
        for (int i = 0; i < sequences.Count; i++)
        {
            OSCharacterInteractionSequenceData candidate = sequences[i];
            if (candidate != null
                && string.Equals(
                    candidate.SequenceId,
                    normalizedId,
                    StringComparison.Ordinal))
            {
                sequence = candidate;
                return true;
            }
        }

        return false;
    }

    public bool TryGetGlobalEntry(
        string targetEntryId,
        out OSCharacterInteractionEntryData entry)
    {
        entry = null;
        if (string.IsNullOrWhiteSpace(targetEntryId)
            || globalEntries == null)
        {
            return false;
        }

        string normalizedId = targetEntryId.Trim();
        for (int i = 0; i < globalEntries.Count; i++)
        {
            OSCharacterInteractionEntryData candidate = globalEntries[i];
            if (candidate != null
                && string.Equals(
                    candidate.EntryId,
                    normalizedId,
                    StringComparison.Ordinal))
            {
                entry = candidate;
                return true;
            }
        }

        return false;
    }
}

public static class OSCharacterInteractionIds
{
    public const string GlobalSequence = "global";
    public const string PrologueEvent01HeartHeal =
        "prologue.event01.heart_heal";
    public const string PrologueEvent01HeartHealEnding =
        "prologue.event01.heart_heal_ending";
    public const string Chapter01LiveReconnect =
        "chapter01.live_reconnect";
    public const string Chapter01MemoryRepair = "chapter01.memory_repair";

    public static class Chapter01Tool
    {
        public const string FilesFound = "chapter01_tool_files_found";
        public const string Processing = "chapter01_tool_processing";
        public const string AwaitShortcut = "chapter01_tool_await_shortcut";
        public const string Ready = "chapter01_tool_ready";
        public const string FollowUp = "chapter01_tool_follow_up";
        public const string InstallHint = "chapter01_tool_install_hint";
        public const string DecorationPrompt = "chapter01_tool_decoration_prompt";
        public const string DecorationDone = "chapter01_tool_decoration_done";
        public const string Decorate = "decorate_tool";
        public const string SkipDecoration = "skip_decoration";
        public const string LibraryOpenedSignal = "chapter01.tool.library_opened";
        public const string DecorationDoneSignal = "chapter01.tool.decoration_done";
        public const string StartProcessing = "start_processing";
        public const string InitializedSignal = "chapter01.tool.initialized";
        public const string InstallHintSignal = "chapter01.tool.install_hint";
        public const string MemoryUpgrade02OpenedSignal = "chapter01.tool.memory_upgrade_02_opened";
    }

    public static class Chapter01Signals
    {
        public const string LiveWaitFirst =
            "chapter01.live.wait_first";
        public const string LiveWaitSecond =
            "chapter01.live.wait_second";
        public const string LiveEntryNormal =
            "chapter01.live.entry_normal";
        public const string LiveEntryForced =
            "chapter01.live.entry_forced";
        public const string LiveEntryConfirm =
            "chapter01.live.entry_confirm";
        public const string StateCheckOpened =
            "chapter01.state_check.opened";
        public const string RecordHovered =
            "chapter01.state_check.record_hovered";
        public const string RecordHoverTimedOut =
            "chapter01.state_check.record_hover_timed_out";
        public const string RecordStartPrompt =
            "chapter01.state_check.record_start_prompt";
        public const string KeySearchStarted =
            "chapter01.key.search_started";
    }

    public static class Chapter01Options
    {
        public const string Confirm = "confirm";
        public const string StartRepair = "start_repair";
    }

    public static class Event01Entries
    {
        public const string OpeningFindToolChoice =
            "opening_find_tool_choice";
        public const string OpeningRememberedFolder =
            "opening_remembered_folder";
        public const string HoverStateCheckFirst =
            "hover_state_check_first";
        public const string HoverStateCheckSecond =
            "hover_state_check_second";
        public const string ClickStateCheck = "click_state_check";
        public const string HoverFolder = "hover_folder";
        public const string HoverLive = "hover_live";
        public const string ClickLive = "click_live";
        public const string HoverHeartHealTool = "hover_heart_heal_tool";
        public const string HeartHealRecallChoice =
            "heart_heal_recall_choice";
        public const string HeartHealShockFirst =
            "heart_heal_shock_first";
        public const string HeartHealStartAfterShock =
            "heart_heal_start_after_shock";
        public const string HeartHealStartFirst =
            "heart_heal_start_first";
        public const string HeartHealShockAfterStart =
            "heart_heal_shock_after_start";
        public const string HeartHealStartHoverReady =
            "heart_heal_start_hover_ready";
        public const string HeartHealFirstHit =
            "heart_heal_first_hit";
        public const string HeartHealFirstMiss =
            "heart_heal_first_miss";
        public const string HeartHealStateAbove65 =
            "heart_heal_state_above_65";
        public const string HeartHealSuccess =
            "heart_heal_success";
        public const string HeartHealFail =
            "heart_heal_fail";
    }

    public static class Event01Signals
    {
        public const string HeartHealStartHovered =
            "event01.heart_heal.start_hovered";
        public const string HeartHealFirstHit =
            "event01.heart_heal.first_hit";
        public const string HeartHealFirstMiss =
            "event01.heart_heal.first_miss";
        public const string HeartHealStateAbove65 =
            "event01.heart_heal.state_above_65";
        public const string HeartHealSuccess =
            "event01.heart_heal.success";
        public const string HeartHealFail =
            "event01.heart_heal.fail";
        public const string HeartHealEndingLiveOpened =
            "event01.heart_heal.ending.live_opened";
        public const string HeartHealEndingSpecialOpened =
            "event01.heart_heal.ending.special_opened";
        public const string HeartHealEndingCursorReady =
            "event01.heart_heal.ending.cursor_ready";
        public const string HeartHealEndingWindowsArranged =
            "event01.heart_heal.ending.windows_arranged";
        public const string HeartHealEndingInspectionComplete =
            "event01.heart_heal.ending.inspection_complete";
        public const string HeartHealEndingRelocked =
            "event01.heart_heal.ending.relocked";
        public const string HeartHealEndingReturnReady =
            "event01.heart_heal.ending.return_ready";
    }

    public static class Event01EndingEntries
    {
        public const string Fear = "heart_heal_ending_fear";
        public const string Searching = "heart_heal_ending_searching";
        public const string Transition =
            "heart_heal_ending_transition";
        public const string KeyStuck = "heart_heal_ending_key_stuck";
        public const string CursorTakeover =
            "heart_heal_ending_cursor_takeover";
        public const string KeyRecovered =
            "heart_heal_ending_key_recovered";
        public const string CheckNormal =
            "heart_heal_ending_check_normal";
        public const string CheckResult =
            "heart_heal_ending_check_result";
        public const string PostRelock =
            "heart_heal_ending_post_relock";
        public const string Return = "heart_heal_ending_return";
    }

    public static class GlobalEntries
    {
        public const string FirstLiveReconnect = "first_live_reconnect";
        public const string Chapter01LiveWaitFirst =
            "chapter01_live_wait_first";
    }

    public static class Chapter01Entries
    {
        public const string LiveWaitSecond =
            "chapter01_live_wait_second";
        public const string LiveEntryNormal =
            "chapter01_live_entry_normal";
        public const string LiveEntryForced =
            "chapter01_live_entry_forced";
        public const string LiveEntryConfirm =
            "chapter01_live_entry_confirm";
        public const string StateCheckMemoryDamaged =
            "chapter01_state_check_memory_damaged";
        public const string RecordHover =
            "chapter01_record_hover";
        public const string RecordHoverTimedOut =
            "chapter01_record_hover_timed_out";
        public const string RecordStartPrompt =
            "chapter01_record_start_prompt";
        public const string FindKey = "chapter01_find_key";
        public const string KeyHovered = "chapter01_key_hovered";
    }

    public static class PresentationCues
    {
        public const string RestoreEvent01Desktop =
            "restore_event01_desktop";
        public const string UnlockEvent01HeartHeal =
            "unlock_event01_heart_heal";
        public const string ReconnectLive = "reconnect_live";
        public const string HeartHealEndingTransition =
            "heart_heal_ending_transition";
        public const string HeartHealEndingCursorTakeover =
            "heart_heal_ending_cursor_takeover";
    }
}
