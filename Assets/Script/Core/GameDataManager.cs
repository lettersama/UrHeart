using System.Collections.Generic;
using UnityEngine;
using MrTGame.Core;
using MrTGame.Narrative;
using MrTGame.OS;

public sealed class OSResourceRuntimeState
{
    public const int LowResourceThreshold = 20;
    public const int HighFaultThreshold = 80;

    public bool IsHeartLow { get; private set; }
    public bool IsEnergyLow { get; private set; }
    public bool IsFaultHigh { get; private set; }

    internal void Refresh(OSResourceSaveData resources)
    {
        if (resources == null)
        {
            IsHeartLow = false;
            IsEnergyLow = false;
            IsFaultHigh = false;
            return;
        }

        IsHeartLow = resources.heart <= LowResourceThreshold;
        IsEnergyLow = resources.energy <= LowResourceThreshold;
        IsFaultHigh = resources.error >= HighFaultThreshold;
    }
}

public partial class GameDataManager : Singleton<GameDataManager>, IEventDialogueTextResolver
{
    public const int FirstHeartTreatmentEnergy = 95;
    public const int Chapter01EntryMinimumEnergy = 65;

    [Header("Language")]
    [SerializeField] LanguageState currentLanguageState = LanguageState.zh_CN;

    [Header("Data")]
    [SerializeField] AudioData_SO audioData;
    [SerializeField] EventData_SO eventData;
    [SerializeField] LocalizationData_SO localizationData;
    [SerializeField] OSMemoryData osMemoryData;
    [SerializeField] RecordSO recordSO;
    [SerializeField] HeartHealData_SO heartHealData;
    [SerializeField] OSToolProgramData_SO osToolProgramData;
    [SerializeField]
    OSCharacterInteractionData_SO osCharacterInteractionData;

    [Header("Save Data")]
    [SerializeField] GameSaveData saveData = new GameSaveData();
    readonly OSResourceRuntimeState osResourceRuntimeState =
        new OSResourceRuntimeState();

    public bool IsInitialized { get; private set; }
    public LanguageState CurrentLanguageState => currentLanguageState;
    public AudioData_SO AudioData => audioData;
    public EventData_SO EventData => eventData;
    public LocalizationData_SO LocalizationData => localizationData;
    public OSMemoryData OSMemoryData => osMemoryData;
    public RecordSO RecordSO => recordSO;
    public HeartHealData_SO HeartHealData => heartHealData;
    public OSToolProgramData_SO OSToolProgramData => osToolProgramData;
    public OSCharacterInteractionData_SO OSCharacterInteractionData =>
        osCharacterInteractionData;

    public int OSMemoryStartDataIndex => osMemoryData != null
        ? osMemoryData.StartDataIndex
        : OSMemoryData.InvalidDataIndex;
    public IReadOnlyList<OSMemoryChapterProgressRange>
        OSMemoryChapterProgressRanges => osMemoryData != null
            ? osMemoryData.ChapterProgressRanges
            : System.Array.Empty<OSMemoryChapterProgressRange>();
    public GameSaveData SaveData => saveData;
    public OSResourceRuntimeState OSResourceState =>
        osResourceRuntimeState;
    public GameOptionSaveData GameOptions
    {
        get
        {
            EnsureSaveData();
            return saveData.gameOptions;
        }
    }
    public OSResourceSaveData OSResourceData
    {
        get
        {
            EnsureSaveData();
            return saveData.osResources;
        }
    }
    public OSProcessSaveData OSProcessData
    {
        get
        {
            EnsureSaveData();
            return saveData.osProcess;
        }
    }
    public MainGameSaveData MainGameData
    {
        get
        {
            EnsureSaveData();
            return saveData.mainGame;
        }
    }
    public IReadOnlyList<EventCompletionData> EventCompletionList
    {
        get
        {
            EnsureSaveData();
            return saveData.eventCompletionList;
        }
    }

    public IReadOnlyList<int> TriggeredOSMemoryDataIndices =>
        OSProcessData.memory.triggeredDataIndices;
    public IReadOnlyList<string> OSAlwaysOnTopWindowStack =>
        OSProcessData.AlwaysOnTopWindowStack;
    public int CurrentOSMemoryDataIndex =>
        OSProcessData.memory.currentDataIndex;
    public int CurrentChapterIndex =>
        OSProcessData.currentChapterIndex;
    public int CurrentHeartHealProcessIndex =>
        OSProcessData.heartHealProcessIndex;
    public float CurrentStateCheckProgress =>
        OSProcessData.stateCheckProgress;
    public OSCharacterInteractionSaveData OSCharacterInteractionState =>
        OSProcessData.characterInteraction;
    public OSGramophoneSaveData OSGramophoneState => OSProcessData.gramophone;
    public OSEnergyLoopSaveData OSEnergyLoopState => OSProcessData.energyLoop;
    // Installation records also restore desktop visibility and the upgraded Memory icon.
    public bool IsGramophoneInstalled => IsToolProgramInstalled(OSToolProgramIds.Gramophone);
    public bool IsEnergyLoopInstalled => IsToolProgramInstalled(OSToolProgramIds.EnergyLoop);
    public bool IsMemoryRepair01Installed => IsToolProgramInstalled(OSToolProgramIds.MemoryRepair01);
    public bool IsMemoryRepair02Installed => IsToolProgramInstalled(OSToolProgramIds.MemoryRepair02);
    public bool IsMemoryRepair03Installed => IsToolProgramInstalled(OSToolProgramIds.MemoryRepair03);
    public bool IsMemoryRepair04Installed => IsToolProgramInstalled(OSToolProgramIds.MemoryRepair04);
    public bool IsTVInstalled => IsToolProgramInstalled(OSToolProgramIds.TV);
    public bool IsChapter03HomeActive
    {
        get
        {
            if (!IsMemoryRepair04Installed || CurrentChapterIndex != 3) return false;
            var story = OSProcessData.memory.chapter03Story;
            return story != null && story.version > 0 ? story.homeOpened : CurrentOSMemoryDataIndex >= 8090;
        }
    }
    public int MemoryUpgradeLevel => IsMemoryRepair04Installed ? 4 : IsMemoryRepair03Installed ? 3 : IsMemoryRepair02Installed ? 2 : IsMemoryRepair01Installed ? 1 : 0;
    public bool IsMemoryRepair02Available => IsMemoryRepair02Installed
        || CurrentOSMemoryDataIndex == OSMemoryData.Chapter01Upgrade02DataIndex
        || OSProcessData.memory.triggeredDataIndices.Contains(OSMemoryData.Chapter01Upgrade02DataIndex);
    public bool IsMemoryRepair03Available => IsMemoryRepair03Installed
        || (OSCharacterInteractionState.activeSequenceId == "chapter02.recovery"
            && OSCharacterInteractionState.HasCompletedEntry("chapter02_return_working"));
    public bool IsMemoryRepair04Available => IsMemoryRepair04Installed
        || OSProcessData.memory.chapter03EntryStep >= (int)OSChapter03EntryDirector.EntryStep.UpgradeOffer;
    public bool IsMemoryInteractiveSceneActive => IsMemoryRepair02Installed
        && OSProcessData.memory.interactiveSceneDataIndex != OSMemoryProcessSaveData.InvalidDataIndex;
    public bool UsesMemoryInteractiveLayout => IsMemoryRepair02Installed
        && (IsMemoryInteractiveSceneActive
            || (CurrentChapterIndex <= 1
                && CurrentOSMemoryDataIndex == OSMemoryData.Chapter01Upgrade02DataIndex));
    public double TotalPlayTimeSeconds
    {
        get
        {
            EnsureSaveData();
            return saveData.totalPlayTimeSeconds;
        }
    }
    public float OverallGameProgress => osMemoryData != null
        ? osMemoryData.CalculateOverallProgress(
            CurrentChapterIndex,
            CurrentOSMemoryDataIndex,
            TriggeredOSMemoryDataIndices)
        : 0f;
    public IReadOnlyDictionary<string, bool> DesktopProcessNewStates =>
        OSProcessData.DesktopProcessNewStates;
    public IReadOnlyDictionary<string, bool> DesktopProcessLockedStates =>
        OSProcessData.DesktopProcessLockedStates;
    public IReadOnlyList<OSDesktopProcessLayoutSaveData>
        DesktopProcessLayouts => OSProcessData.DesktopProcessLayouts;
    public bool IsChapter01KeyXAcquired =>
        OSProcessData.chapter01KeyXAcquired;
    public bool IsToolUnlocked => OSProcessData.toolUnlocked;
    public bool HasToolInitialized => OSProcessData.toolInitializationCompleted;
    public bool HasToolProgramsUnpacked => OSProcessData.toolProgramsUnpacked;
    public OSToolDecorationState ToolDecorationState => OSProcessData.toolDecorationState;
    public bool IsToolDecorated => ToolDecorationState == OSToolDecorationState.Completed;
    public bool CanAccessToolPrograms => IsToolUnlocked && IsChapter01KeyXAcquired
        && CurrentOSMemoryDataIndex != OSMemoryProcessSaveData.InvalidDataIndex;
    public event System.Action<string> ToolProgramInstalled;
    public event System.Action<int> RecordUnlocked;
    public IReadOnlyList<int> UnlockedRecordDataIndices
    {
        get
        {
            EnsureSaveData();
            return saveData.unlockedRecordDataIndices;
        }
    }

    public event System.Action<bool> OSPostProcessingOptionChanged;
    public event System.Action<string, bool>
        DesktopProcessNewStateChanged;
    public event System.Action<string, bool>
        DesktopProcessLockStateChanged;

    /// <summary>Load preferences at boot; gameplay is restored through Continue.</summary>
    public void Initialize()
    {
        if (IsInitialized)
            return;

        EnsureSaveData();
        var autosave = GetComponent<GameAutosaveService>();
        if (autosave == null) autosave = gameObject.AddComponent<GameAutosaveService>();
        autosave.Initialize(this);
        if (GetComponent<GameSessionFlow>() == null) gameObject.AddComponent<GameSessionFlow>();
        RefreshOSResourceRuntimeState();
        ApplyGameOptions();
        IsInitialized = true;
        var achievements = GetComponent<SteamAchievementService>();
        if (achievements == null) achievements = gameObject.AddComponent<SteamAchievementService>();
        achievements.Initialize(this);
        var farewell = GetComponent<PostGameFarewellService>();
        if (farewell == null) farewell = gameObject.AddComponent<PostGameFarewellService>();
        farewell.Initialize(this);
    }

    void Update()
    {
        if (!IsInitialized || GameManager.Instance?.Phase == GamePhase.Boot
            || GameSessionFlow.Instance?.IsBusy == true)
            return;

        saveData.AddPlayTime(Time.unscaledDeltaTime);
        TickEnergyCharge(Time.unscaledDeltaTime);
    }

    public void ReplaceSaveData(GameSaveData next, bool preserveCurrentSettings = false)
    {
        if (next == null) throw new System.ArgumentNullException(nameof(next));
        next.EnsureData();
        NormalizeRetiredBreakfastNodes(next.osProcess.memory);
        if (preserveCurrentSettings)
        {
            next.gameOptions = JsonUtility.FromJson<GameOptionSaveData>(JsonUtility.ToJson(GameOptions));
            next.flow.language = CurrentLanguageState;
        }
        saveData = next;
        RefreshOSResourceRuntimeState();
        SetCurrentLanguage(next.flow.language);
        ApplyGameOptions();
        foreach (ResourceType resource in System.Enum.GetValues(typeof(ResourceType)))
            GameEvents.RaiseResourceChanged(resource, saveData.osResources.Get(resource), OSResourceSaveData.MaxValue);
    }

    void NormalizeRetiredBreakfastNodes(OSMemoryProcessSaveData memory)
    {
        // These five paragraphs were removed from the approved breakfast edit.
        // Move a legacy cursor forward; do not mark its unread successor as read.
        memory.currentDataIndex = ResolveRetiredBreakfastNode(memory.currentDataIndex);
        for (int i = memory.triggeredDataIndices.Count - 1; i >= 0; i--)
        {
            int index = memory.triggeredDataIndices[i];
            if (ResolveRetiredBreakfastNode(index) != index)
                memory.triggeredDataIndices.RemoveAt(i);
        }
    }

    int ResolveRetiredBreakfastNode(int index)
    {
        int successor;
        switch (index)
        {
            case 4390:
            case 4400:
                successor = 4410;
                break;
            case 4560:
            case 4570:
            case 4580:
                successor = 4590;
                break;
            default:
                return index;
        }
        // Leave older/partially imported content intact until the edited graph exists.
        return osMemoryData != null && !osMemoryData.TryGetData(index, out _)
            && osMemoryData.TryGetData(successor, out _) ? successor : index;
    }


    public int GetGameOptionVolume(GameOptionVolumeType type)
    {
        return GameOptions.GetVolume(type);
    }

    public int SetGameOptionVolume(GameOptionVolumeType type, int value)
    {
        int clampedValue = GameOptions.SetVolume(type, value);
        ApplyAudioVolume(type, clampedValue);
        return clampedValue;
    }

    public void SetFullScreenOption(bool isFullScreen)
    {
        GameOptions.isFullScreen = isFullScreen;
        Screen.fullScreen = isFullScreen;
    }

    public void SetOSPostProcessingOption(bool isEnabled)
    {
        GameOptions.isOSPostProcessingEnabled = isEnabled;
        OSPostProcessingOptionChanged?.Invoke(isEnabled);
    }

    public int SetTextSpeedOption(int value)
    {
        return GameOptions.SetTextSpeed(value);
    }

    public void ApplyGameOptions()
    {
        GameOptionSaveData options = GameOptions;
        ApplyAudioVolume(
            GameOptionVolumeType.Master,
            options.masterVolume);
        ApplyAudioVolume(
            GameOptionVolumeType.Music,
            options.musicVolume);
        ApplyAudioVolume(
            GameOptionVolumeType.Ambient,
            options.ambientVolume);
        ApplyAudioVolume(
            GameOptionVolumeType.Sfx,
            options.sfxVolume);
        Screen.fullScreen = options.isFullScreen;
        OSPostProcessingOptionChanged?.Invoke(
            options.isOSPostProcessingEnabled);
    }

    public void SetCurrentLanguage(LanguageState language)
    {
        if (currentLanguageState == language)
            return;

        currentLanguageState = language;
        MrTGame.Core.GameEvents.RaiseLanguageChanged(language);
    }

    public bool TryGetLocalizedText(string key, out string text)
    {
        if (localizationData == null)
        {
            text = null;
            return false;
        }

        return localizationData.TryGetText(key, currentLanguageState, out text);
    }

    public string GetLocalizedText(string key, string fallback = null)
    {
        if (localizationData != null)
            return localizationData.GetText(key, currentLanguageState, fallback);

        if (!string.IsNullOrEmpty(fallback))
            return fallback;

        return key ?? string.Empty;
    }

    public string Resolve(
        string eventID,
        int dialogueIndex,
        string characterID,
        string fallbackText)
    {
        string key = EventDialogueLocalizationKeys.GetLineKey(
            eventID,
            dialogueIndex);
        return string.IsNullOrEmpty(key)
            ? fallbackText ?? string.Empty
            : GetLocalizedText(key, fallbackText);
    }

    public bool TryGetOSMemoryData(int dataIndex, out OSMemoryReadData data)
    {
        data = null;
        if (osMemoryData == null || !osMemoryData.TryGetData(dataIndex, out OSMemoryTextData source))
            return false;

        string textKey = OSMemoryLocalizationKeys.GetTextKey(source.DataIndex);
        string text = ResolveOSMemoryText(textKey, source.SourceText);
        var options = new List<OSMemoryReadOptionData>(source.Options.Count);

        for (int i = 0; i < source.Options.Count; i++)
        {
            OSMemoryOptionData sourceOption = source.Options[i];
            if (sourceOption == null)
                continue;

            string optionKey = OSMemoryLocalizationKeys.GetOptionKey(
                source.DataIndex,
                sourceOption.OptionId);
            string optionText = ResolveOSMemoryText(optionKey, sourceOption.SourceText);

            options.Add(new OSMemoryReadOptionData(sourceOption, optionText, optionKey));
        }

        data = new OSMemoryReadData(source, text, textKey, options);
        return true;
    }

    public bool TryGetRecordData(int recordIndex, out RecordData data)
    {
        data = null;
        return recordSO != null
            && recordSO.TryGetRecordData(recordIndex, out data);
    }

    public string GetLocalizedRecordName(RecordData data)
    {
        return data == null
            ? string.Empty
            : GetLocalizedRecordText(
                data.RecordNameLocalizationKey,
                data.RecordName);
    }

    public string GetLocalizedRecordDescription(RecordData data)
    {
        return data == null
            ? string.Empty
            : GetLocalizedRecordText(
                data.RecordDescriptionLocalizationKey,
                data.RecordDescription);
    }

    string GetLocalizedRecordText(string key, string fallback)
    {
        return localizationData != null
               && localizationData.TryGetText(
                   key,
                   currentLanguageState,
                   out string localizedText)
            ? localizedText
            : fallback ?? string.Empty;
    }

    public bool TryGetHeartHealPuzzle(
        int heartIndex,
        out OSHeartHealPuzzleData puzzle)
    {
        puzzle = null;
        return heartHealData != null
            && heartHealData.TryGetPuzzle(heartIndex, out puzzle);
    }

    public bool IsRecordDataUnlocked(int recordIndex)
    {
        EnsureSaveData();
        return saveData.IsRecordDataUnlocked(recordIndex);
    }

    public bool UnlockRecordData(int recordIndex)
    {
        if (!TryGetRecordData(recordIndex, out _))
            return false;

        EnsureSaveData();
        bool wasUnlocked = saveData.UnlockRecordData(recordIndex);
        if (wasUnlocked)
        {
            SetDesktopProcessIsNew(
                OSDesktopProcessKeys.Record,
                true);
            RecordUnlocked?.Invoke(recordIndex);
        }

        return wasUnlocked;
    }

    public bool TryGetDesktopProcessIsNew(
        string processKey,
        out bool isNew)
    {
        EnsureSaveData();
        return saveData.osProcess.TryGetDesktopProcessIsNew(
            processKey,
            out isNew);
    }

    public bool TryGetDesktopProcessIsLocked(
        string processKey,
        out bool isLocked)
    {
        EnsureSaveData();
        return saveData.osProcess.TryGetDesktopProcessIsLocked(
            processKey,
            out isLocked);
    }

    public void GetOrInitializeDesktopProcessState(
        string processKey,
        bool initialIsNew,
        bool initialIsLocked,
        out bool isNew,
        out bool isLocked)
    {
        EnsureSaveData();
        saveData.osProcess.GetOrInitializeDesktopProcessState(
            processKey,
            initialIsNew,
            initialIsLocked,
            out isNew,
            out isLocked);
    }

    public bool GetOrInitializeDesktopProcessIsNew(
        string processKey,
        bool initialIsNew)
    {
        EnsureSaveData();
        return saveData.osProcess.GetOrInitializeDesktopProcessIsNew(
            processKey,
            initialIsNew);
    }

    public bool GetOrInitializeDesktopProcessIsLocked(
        string processKey,
        bool initialIsLocked)
    {
        EnsureSaveData();
        return saveData.osProcess.GetOrInitializeDesktopProcessIsLocked(
            processKey,
            initialIsLocked);
    }

    public bool SetDesktopProcessIsNew(
        string processKey,
        bool isNew = true)
    {
        EnsureSaveData();
        string key = string.IsNullOrWhiteSpace(processKey)
            ? null
            : processKey.Trim();
        if (key == null
            || !saveData.osProcess.SetDesktopProcessIsNew(key, isNew))
        {
            return false;
        }

        DesktopProcessNewStateChanged?.Invoke(key, isNew);
        return true;
    }

    public bool SetDesktopProcessIsLocked(
        string processKey,
        bool isLocked = true)
    {
        EnsureSaveData();
        string key = string.IsNullOrWhiteSpace(processKey)
            ? null
            : processKey.Trim();
        if (key == null
            || !saveData.osProcess.SetDesktopProcessIsLocked(key, isLocked))
        {
            return false;
        }

        DesktopProcessLockStateChanged?.Invoke(key, isLocked);
        return true;
    }

    public bool TryGetDesktopProcessLayout(
        string processKey,
        out OSDesktopProcessLayoutSaveData layout)
    {
        EnsureSaveData();
        return saveData.osProcess.TryGetDesktopProcessLayout(
            processKey,
            out layout);
    }

    public bool SetDesktopProcessLayout(
        string processKey,
        string containerId,
        int gridIndex)
    {
        EnsureSaveData();
        return saveData.osProcess.SetDesktopProcessLayout(
            processKey,
            containerId,
            gridIndex);
    }

    public bool SetChapter01KeyXAcquired(bool isAcquired = true)
    {
        OSProcessSaveData processData = OSProcessData;
        if (processData.chapter01KeyXAcquired == isAcquired)
            return false;

        processData.chapter01KeyXAcquired = isAcquired;
        return true;
    }

    public void UnlockTool()
    {
        OSProcessData.toolUnlocked = true;
        SetDesktopProcessIsLocked(OSDesktopProcessKeys.Tool, false);
    }

    public void MarkToolInitialized()
    {
        OSProcessData.toolInitializationCompleted = true;
    }

    public void MarkToolProgramsUnpacked()
    {
        OSProcessData.toolProgramsUnpacked = true;
    }

    public void MarkToolDecorationOffered()
    {
        if (ToolDecorationState == OSToolDecorationState.None)
            OSProcessData.toolDecorationState = OSToolDecorationState.Offered;
    }

    public void ChooseToolDecoration(bool accepted)
    {
        if (ToolDecorationState == OSToolDecorationState.Offered)
            OSProcessData.toolDecorationState = accepted
                ? OSToolDecorationState.Pending : OSToolDecorationState.Declined;
    }

    public void CompleteToolDecoration()
    {
        if (ToolDecorationState == OSToolDecorationState.Pending)
            OSProcessData.toolDecorationState = OSToolDecorationState.Completed;
    }

    public bool IsToolProgramInstalled(string programId)
    {
        return OSProcessData.installedToolProgramIds.Contains(programId);
    }

    public OSToolInstallResult CheckToolProgramInstallation(string programId)
    {
        if (!CanAccessToolPrograms || osToolProgramData == null
            || !osToolProgramData.TryGetProgram(programId, out OSToolProgramDefinition program))
            return OSToolInstallResult.Unavailable;
        if (IsToolProgramInstalled(program.Id))
            return OSToolInstallResult.AlreadyInstalled;
        if (!IsToolProgramAvailable(program.Id)
            || (program.Id == OSToolProgramIds.MemoryRepair02 && !IsMemoryRepair01Installed)
            || (program.Id == OSToolProgramIds.MemoryRepair03 && !IsMemoryRepair02Installed)
            || (program.Id == OSToolProgramIds.MemoryRepair04 && !IsMemoryRepair03Installed))
            return OSToolInstallResult.Unavailable;
        return GetOSResourceValue(ResourceType.Battery) >= program.BatteryCost
            ? OSToolInstallResult.Success : OSToolInstallResult.InsufficientBattery;
    }

    public OSToolInstallResult TryInstallToolProgram(string programId)
    {
        OSToolInstallResult result = CheckToolProgramInstallation(programId);
        if (result != OSToolInstallResult.Success)
            return result;

        osToolProgramData.TryGetProgram(programId, out OSToolProgramDefinition program);
        // Commit both values before notifying listeners, which may advance the story.
        OSProcessData.installedToolProgramIds.Add(program.Id);
        if (program.BatteryCost > 0)
        {
            int remaining = ModifyOSResourceValue(ResourceType.Battery, -program.BatteryCost);
            if (MrTGame.Systems.GameResourceSystem.Instance != null)
                MrTGame.Systems.GameResourceSystem.Instance.NotifySavedResourceChanged(ResourceType.Battery, remaining);
            else
                GameEvents.RaiseResourceChanged(ResourceType.Battery, remaining, OSResourceSaveData.MaxValue);
        }
        ToolProgramInstalled?.Invoke(program.Id);
        return OSToolInstallResult.Success;
    }

    public bool IsMemoryWaitingForToolRepair(int dataIndex)
    {
        if (osMemoryData == null || !osMemoryData.TryGetData(dataIndex, out var data))
            return false;
        if (!string.IsNullOrEmpty(GetMissingOSMemoryToolProgram(data.OnEnterActions)))
            return true;
        foreach (OSMemoryOptionData option in data.Options)
        {
            if (option != null && !string.IsNullOrEmpty(GetMissingOSMemoryToolProgram(option.Actions)))
                return true;
        }
        return false;
    }

    public bool IsToolProgramAvailable(string programId)
    {
        if (programId == OSToolProgramIds.TV)
            return IsTVInstalled || !GameSetup.IsDemo
                && CurrentChapterIndex >= 3
                && (OSProcessData.memory.chapter04EntryStep >= (int)OSChapter04EntryDirector.EntryStep.AwaitTV
                    || OSProcessData.memory.chapter04EntryStep == (int)OSChapter04EntryDirector.EntryStep.Installing
                        && OSProcessData.memory.chapter04EntryElapsed >= OSChapter04EntryDirector.InstallationSeconds);
        if (programId == OSToolProgramIds.MemoryRepair02)
            return IsMemoryRepair02Available;
        if (programId == OSToolProgramIds.MemoryRepair03)
            return IsMemoryRepair03Available;
        if (programId == OSToolProgramIds.MemoryRepair04)
            return IsMemoryRepair04Available;
        return true;
    }

    public void EnterMemoryInteractiveScene(int dataIndex)
    {
        if (IsMemoryRepair02Installed && osMemoryData != null
            && osMemoryData.TryGetData(dataIndex, out var data) && data.IsInteractiveSceneEntry)
            OSProcessData.memory.interactiveSceneDataIndex = dataIndex;
    }

    public string GetMissingOSMemoryToolProgram(IReadOnlyList<OSMemoryActionData> actions)
    {
        if (actions == null)
            return null;
        foreach (OSMemoryActionData action in actions)
        {
            if (action != null && action.ActionId == OSMemoryActionIds.RequireToolProgram
                && !string.IsNullOrWhiteSpace(action.Parameter)
                && !IsToolProgramInstalled(action.Parameter))
                return action.Parameter;
        }
        return null;
    }

    public bool SetMainGameMovementTutorialCompleted(bool isCompleted = true)
    {
        MainGameSaveData mainGame = MainGameData;
        if (mainGame.movementTutorialCompleted == isCompleted)
            return false;

        mainGame.movementTutorialCompleted = isCompleted;
        return true;
    }

    public bool TryGetOSWindowLayout(
        string windowId,
        out OSWindowLayoutSaveData layout)
    {
        EnsureSaveData();
        return saveData.osProcess.TryGetWindowLayout(windowId, out layout);
    }

    public void SetOSWindowLayout(OSWindowLayoutSaveData layout)
    {
        EnsureSaveData();
        saveData.osProcess.SetWindowLayout(layout);
    }

    public bool TryGetOSWindowContentState(
        string windowId,
        out OSWindowLayoutSaveData windowState)
    {
        EnsureSaveData();
        return saveData.osProcess.TryGetWindowContentState(
            windowId,
            out windowState);
    }

    public void SetOSWindowContentState(
        string windowId,
        string stateId,
        int contextIndex,
        float progress,
        int checkpointValue,
        Vector2 position = default)
    {
        EnsureSaveData();
        saveData.osProcess.SetWindowContentState(
            windowId,
            stateId,
            contextIndex,
            progress,
            checkpointValue,
            position);
    }

    public void ClearOSWindowContentState(string windowId)
    {
        EnsureSaveData();
        saveData.osProcess.ClearWindowContentState(windowId);
    }

    public void SetOSAlwaysOnTopWindowStack(
        IReadOnlyList<string> windowIds)
    {
        EnsureSaveData();
        saveData.osProcess.SetAlwaysOnTopWindowStack(windowIds);
    }

    public void RecordTriggeredOSMemoryData(int dataIndex)
    {
        EnsureSaveData();
        saveData.osProcess.memory.RecordTriggeredData(dataIndex);
    }

    public void SetCurrentOSMemoryData(int dataIndex)
    {
        EnsureSaveData();
        saveData.osProcess.memory.currentDataIndex = dataIndex;
    }

    public int SetCurrentChapterIndex(int chapterIndex)
    {
        EnsureSaveData();
        int sanitizedIndex = Mathf.Max(
            OSProcessSaveData.MinChapterIndex,
            chapterIndex);
        // Saved chapter progress also guards the allowance after reopening/loading.
        // Returning from a later chapter must not grant it again.
        bool enteringChapter01 = saveData.osProcess.currentChapterIndex < 1
            && sanitizedIndex == 1;
        saveData.osProcess.currentChapterIndex = sanitizedIndex;
        if (enteringChapter01)
            EnsureChapter01EntryEnergy();
        return sanitizedIndex;
    }

    void EnsureChapter01EntryEnergy()
    {
        int missing = Chapter01EntryMinimumEnergy
            - GetOSResourceValue(ResourceType.Battery);
        if (missing <= 0)
            return;

        var resources = MrTGame.Systems.GameResourceSystem.Instance;
        if (resources != null)
        {
            resources.Modify(ResourceType.Battery, missing);
            return;
        }

        int value = ModifyOSResourceValue(ResourceType.Battery, missing);
        GameEvents.RaiseResourceChanged(
            ResourceType.Battery, value, OSResourceSaveData.MaxValue);
    }

    public bool TryGetOSMemoryChapterProgressRange(
        int chapterSequence,
        out OSMemoryChapterProgressRange range)
    {
        range = null;
        return osMemoryData != null
            && osMemoryData.TryGetChapterRange(
                chapterSequence,
                out range);
    }

    public int SetCurrentHeartHealProcessIndex(int processIndex)
    {
        EnsureSaveData();
        int clampedIndex = Mathf.Clamp(
            processIndex,
            OSProcessSaveData.MinHeartHealProcessIndex,
            OSProcessSaveData.MaxHeartHealProcessIndex);
        bool enteringFirstTutorial = saveData.osProcess.currentChapterIndex == 0
            && saveData.osProcess.heartHealProcessIndex == 0 && clampedIndex == 1;
        saveData.osProcess.heartHealProcessIndex = clampedIndex;
        if (enteringFirstTutorial)
            ApplyNarrativeResourceDelta(ResourceType.Battery,
                FirstHeartTreatmentEnergy - GetOSResourceValue(ResourceType.Battery));
        return clampedIndex;
    }

    public bool TryApplyChapter01MirrorHeartCost()
    {
        EnsureSaveData();
        if (CurrentChapterIndex != 1 || saveData.osProcess.memory.chapter01MirrorHeartCostApplied)
            return false;
        saveData.osProcess.memory.chapter01MirrorHeartCostApplied = true;
        ApplyNarrativeResourceDelta(ResourceType.Heart, -3);
        return true;
    }

    public bool TryApplyCharacterOptionResources(
        string sequenceId,
        string entryId,
        OSCharacterInteractionOptionData option)
    {
        OSMemoryResourceChange change = option?.ResourceChange;
        if (change == null)
            return false;

        int heartDelta = Mathf.RoundToInt(change.Heart);
        int energyDelta = Mathf.RoundToInt(change.Battery);
        int faultDelta = Mathf.RoundToInt(change.Fault);
        if (heartDelta == 0 && energyDelta == 0 && faultDelta == 0)
            return false;

        EnsureSaveData();
        if (!saveData.osProcess.characterInteraction.TryMarkOptionResourcesApplied(
                sequenceId, entryId, option.OptionId))
        {
            return false;
        }

        OSResourceSaveData resources = saveData.osResources;
        int previousHeart = resources.heart;
        int previousEnergy = resources.energy;
        int previousFault = resources.error;

        // Store the receipt and every resource before a listener can save or re-enter.
        int heart = heartDelta != 0
            ? resources.Modify(ResourceType.Heart, heartDelta) : previousHeart;
        int energy = energyDelta != 0
            ? resources.Modify(ResourceType.Battery, energyDelta) : previousEnergy;
        int fault = faultDelta != 0
            ? resources.Modify(ResourceType.Fault, faultDelta) : previousFault;
        RefreshOSResourceRuntimeState();

        PublishCharacterOptionResourceChange(ResourceType.Heart, previousHeart, heart);
        PublishCharacterOptionResourceChange(ResourceType.Battery, previousEnergy, energy);
        PublishCharacterOptionResourceChange(ResourceType.Fault, previousFault, fault);
        if (energyDelta < 0 && energy == 0)
            GameEvents.RaiseEmptyEnergyUsed();
        return true;
    }

    void PublishCharacterOptionResourceChange(
        ResourceType type,
        int previousValue,
        int value)
    {
        if (previousValue == value)
            return;

        var resources = MrTGame.Systems.GameResourceSystem.Instance;
        if (resources != null)
            resources.NotifySavedResourceChanged(type, previousValue, value);
        else
            GameEvents.RaiseResourceChanged(type, value, OSResourceSaveData.MaxValue);
    }

    void ApplyNarrativeResourceDelta(ResourceType type, int delta)
    {
        if (delta == 0)
            return;
        var resources = MrTGame.Systems.GameResourceSystem.Instance;
        if (resources != null)
            resources.Modify(type, delta);
        else
        {
            int value = ModifyOSResourceValue(type, delta);
            GameEvents.RaiseResourceChanged(type, value, OSResourceSaveData.MaxValue);
        }
    }

    public float SetCurrentStateCheckProgress(float progress)
    {
        EnsureSaveData();
        float sanitizedProgress = float.IsNaN(progress)
            || float.IsInfinity(progress)
            ? 0f
            : Mathf.Max(0f, progress);
        saveData.osProcess.stateCheckProgress = sanitizedProgress;
        return sanitizedProgress;
    }

    public int GetOSResourceValue(ResourceType type)
    {
        return OSResourceData.Get(type);
    }

    public int SetOSResourceValue(ResourceType type, int value)
    {
        int result = OSResourceData.Set(type, value);
        RefreshOSResourceRuntimeState();
        return result;
    }

    public int ModifyOSResourceValue(ResourceType type, int delta)
    {
        int result = OSResourceData.Modify(type, delta);
        RefreshOSResourceRuntimeState();
        if (type == ResourceType.Battery && delta < 0 && result == 0)
            GameEvents.RaiseEmptyEnergyUsed();
        return result;
    }

    public void RefreshOSResourceRuntimeState()
    {
        EnsureSaveData();
        osResourceRuntimeState.Refresh(saveData.osResources);
    }

    public bool IsEventCompleted(string eventID)
    {
        EnsureSaveData();
        return saveData.IsEventCompleted(eventID);
    }

    public bool TryGetEventState(
        string eventID,
        out bool isActive,
        out bool isCompleted)
    {
        EnsureSaveData();
        return saveData.TryGetEventState(
            eventID,
            out isActive,
            out isCompleted);
    }

    public void GetOrInitializeEventState(
        string eventID,
        bool initialIsActive,
        out bool isActive,
        out bool isCompleted)
    {
        EnsureSaveData();
        saveData.GetOrInitializeEventState(
            eventID,
            initialIsActive,
            out isActive,
            out isCompleted);
    }

    public void SetEventState(
        string eventID,
        bool isActive,
        bool isCompleted)
    {
        if (string.IsNullOrEmpty(eventID))
        {
            Debug.LogWarning("[GameDataManager] Cannot save an empty eventID.", this);
            return;
        }

        EnsureSaveData();
        saveData.SetEventState(eventID, isActive, isCompleted);
    }

    public void SetEventActive(string eventID, bool isActive)
    {
        if (string.IsNullOrEmpty(eventID))
        {
            Debug.LogWarning("[GameDataManager] Cannot save an empty eventID.", this);
            return;
        }

        EnsureSaveData();
        saveData.SetEventActive(eventID, isActive);
    }

    public void SetEventCompleted(string eventID, bool isCompleted = true)
    {
        if (string.IsNullOrEmpty(eventID))
        {
            Debug.LogWarning("[GameDataManager] Cannot save an empty eventID.", this);
            return;
        }

        EnsureSaveData();
        saveData.SetEventCompleted(eventID, isCompleted);
    }

    void EnsureSaveData()
    {
        if (saveData == null)
            saveData = new GameSaveData();

        saveData.EnsureData();
    }

    static void ApplyAudioVolume(GameOptionVolumeType type, int value)
    {
        AudioManager audioManager = AudioManager.Instance;
        if (audioManager == null)
            return;

        float normalizedValue = Mathf.Clamp(
            value,
            GameOptionSaveData.MinVolume,
            GameOptionSaveData.MaxVolume) / 100f;
        switch (type)
        {
            case GameOptionVolumeType.Master:
                audioManager.SetMasterVolume(normalizedValue);
                break;
            case GameOptionVolumeType.Music:
                audioManager.SetMusicVolume(normalizedValue);
                break;
            case GameOptionVolumeType.Ambient:
                audioManager.SetAmbientVolume(normalizedValue);
                break;
            case GameOptionVolumeType.Sfx:
                audioManager.SetSfxVolume(normalizedValue);
                break;
        }
    }

    string ResolveOSMemoryText(string key, string sourceText)
    {
        if (string.IsNullOrEmpty(sourceText))
            return string.Empty;

        if (CurrentLanguageState == LanguageState.zh_CN)
            return sourceText ?? string.Empty;

        return GetLocalizedText(key, sourceText);
    }

}
