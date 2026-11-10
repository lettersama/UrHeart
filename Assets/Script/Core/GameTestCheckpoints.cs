using System;
using System.Collections;
using MrTGame.Core;
using MrTGame.Narrative;
using MrTGame.Systems;
using UnityEngine;

public enum GameTestCheckpoint
{
    PrologueHeartHeal,
    PrologueFault,
    Chapter01BeforeToolUnlock,
    Chapter01BeforeInteractivePicture,
    Chapter01EndingAnimation,
    // Append new checkpoints so existing Inspector/debug enum values stay stable.
    Chapter02Start,
    Chapter02AfterUpgrade03,
    Chapter02MapPlan,
    Chapter02ClawMachine,
    Chapter02CursorExperiment,
    Chapter02CursorExperimentEnd,
    Chapter02HeartTalk,
    Chapter02HiddenMemory,
    Chapter02PaperPushRematch,
    Chapter02PaperPushMatch2,
    Chapter02ReturnHome,
    Chapter02FinalTalk,
    Chapter03DemoEnd,
    Chapter03MemoryStart,
    Chapter03Bedroom,
    Chapter03LabEntrance,
    Chapter03LetterClosed,
    Chapter03BeforeLetterClose,
    Chapter04StoryStart,
    Chapter04FinalHeartHeal,
    Chapter04FinalMother
}

public sealed partial class GameTest
{
    public bool IsCheckpointJumpRunning { get; private set; }
    public string JumpStatus { get; private set; } = "输入回忆编号，或选择关键区域。";
    public string JumpStatusKey { get; private set; } = "ui.debug_console.status_ready";
    public string JumpStatusFallback { get; private set; } = "就绪";
    public object[] JumpStatusArguments { get; private set; } = Array.Empty<object>();
    public bool JumpSucceeded { get; private set; }

    public static bool IsCheckpointAvailable(GameTestCheckpoint checkpoint)
    {
        return Enum.IsDefined(typeof(GameTestCheckpoint), checkpoint)
            && (!GameSetup.IsDemo || checkpoint <= GameTestCheckpoint.Chapter01EndingAnimation
                || IsChapter02Checkpoint(checkpoint));
    }

    public static bool IsMemoryIndexAvailable(int dataIndex)
    {
        GameDataManager data = GameDataManager.Instance;
        if (data == null || !data.TryGetOSMemoryData(dataIndex, out _)) return false;
        // Use the authored Chapter 2 boundary, including all of its ending memories.
        return !GameSetup.IsDemo
            || data.TryGetOSMemoryChapterProgressRange(2, out var range)
                && range.HasMemoryDataRange
                && dataIndex <= Mathf.Max(range.FirstDataIndex, range.LastDataIndex);
    }

    static bool IsChapter02Checkpoint(GameTestCheckpoint checkpoint)
    {
        return checkpoint == GameTestCheckpoint.Chapter02Start
            || checkpoint == GameTestCheckpoint.Chapter02AfterUpgrade03
            || checkpoint == GameTestCheckpoint.Chapter02MapPlan
            || checkpoint == GameTestCheckpoint.Chapter02ClawMachine
            || checkpoint == GameTestCheckpoint.Chapter02CursorExperiment
            || checkpoint == GameTestCheckpoint.Chapter02CursorExperimentEnd
            || checkpoint == GameTestCheckpoint.Chapter02HeartTalk
            || checkpoint == GameTestCheckpoint.Chapter02HiddenMemory
            || checkpoint == GameTestCheckpoint.Chapter02PaperPushRematch
            || checkpoint == GameTestCheckpoint.Chapter02PaperPushMatch2
            || checkpoint == GameTestCheckpoint.Chapter02ReturnHome
            || checkpoint == GameTestCheckpoint.Chapter02FinalTalk;
    }

    // Keep the legacy Chinese status for existing debug callers; UI binds the key
    // and arguments so an in-flight status can change language without restarting.
    void SetJumpStatus(string key, string fallback, bool succeeded = false, params object[] arguments)
    {
        JumpStatusKey = "ui.debug_console." + key;
        JumpStatusFallback = fallback;
        JumpStatusArguments = arguments ?? Array.Empty<object>();
        JumpSucceeded = succeeded;
        JumpStatus = JumpStatusArguments.Length > 0
            ? string.Format(fallback, JumpStatusArguments) : fallback;
    }

    public bool JumpToCheckpoint(GameTestCheckpoint checkpoint, int heartTalkReleaseChoice = 1)
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        if (!Application.isPlaying || IsCheckpointJumpRunning
            || GameManager.Instance == null || GameDataManager.Instance == null
            || SceneLoadManager.Instance == null || GameManager.Instance.IsTransitioning
            || SceneLoadManager.Instance.IsBusy || GameSessionFlow.Instance?.IsBusy == true
            || !Enum.IsDefined(typeof(GameTestCheckpoint), checkpoint))
        {
            SetJumpStatus("status_wait", "请等待游戏初始化或场景切换完成。");
            return false;
        }
        if (!IsCheckpointAvailable(checkpoint))
        {
            SetJumpStatus("status_demo_unavailable", "Demo 模式仅开放序章、第一章和第二章。");
            return false;
        }
        if (memoryJumpRoutine != null)
        {
            StopCoroutine(memoryJumpRoutine);
            memoryJumpRoutine = null;
        }
        IsCheckpointJumpRunning = true;
        GameAutosaveService.Instance?.EnterTestSession();
        SetJumpStatus("status_preparing", "正在准备测试区域…");
        StartCoroutine(JumpToCheckpointRoutine(checkpoint, heartTalkReleaseChoice == 2 ? 2 : 1));
        return true;
#else
        return false;
#endif
    }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
    IEnumerator JumpToCheckpointRoutine(GameTestCheckpoint checkpoint, int heartTalkReleaseChoice)
    {
        // Drive nested enumerators here so a load failure also releases the busy flag.
        var work = new System.Collections.Generic.Stack<IEnumerator>();
        work.Push(PrepareCheckpoint(checkpoint, heartTalkReleaseChoice));
        try
        {
            while (work.Count > 0)
            {
                object next;
                try
                {
                    if (!work.Peek().MoveNext())
                    {
                        (work.Pop() as IDisposable)?.Dispose();
                        continue;
                    }
                    next = work.Peek().Current;
                }
                catch (Exception exception)
                {
                    SetJumpStatus("status_failed", "跳转失败，请重试。详情见 Unity 控制台。");
                    JumpStatus = "跳转失败：" + exception.Message;
                    Debug.LogException(exception, this);
                    yield break;
                }
                if (next is IEnumerator nested)
                    work.Push(nested);
                else
                    yield return next;
            }
            SetJumpStatus("status_complete", "关键区域已就绪。", true);
        }
        finally
        {
            while (work.Count > 0)
                (work.Pop() as IDisposable)?.Dispose();
            IsCheckpointJumpRunning = false;
        }
    }

    IEnumerator PrepareCheckpoint(GameTestCheckpoint checkpoint, int heartTalkReleaseChoice)
    {
        GameManager manager = GameManager.Instance;
        SceneLoadManager loader = SceneLoadManager.Instance;
        GameDataManager data = GameDataManager.Instance;
        bool chapter03DemoEnd = checkpoint == GameTestCheckpoint.Chapter03DemoEnd;
        bool chapter04FinalMother = checkpoint == GameTestCheckpoint.Chapter04FinalMother;
        bool chapter04FinalHeal = checkpoint == GameTestCheckpoint.Chapter04FinalHeartHeal || chapter04FinalMother;
        bool chapter04Story = checkpoint == GameTestCheckpoint.Chapter04StoryStart || chapter04FinalHeal;
        bool chapter03Bedroom = checkpoint == GameTestCheckpoint.Chapter03Bedroom;
        bool chapter03LabEntrance = checkpoint == GameTestCheckpoint.Chapter03LabEntrance;
        bool chapter03LetterClosed = checkpoint == GameTestCheckpoint.Chapter03LetterClosed;
        bool chapter03BeforeLetterClose = checkpoint == GameTestCheckpoint.Chapter03BeforeLetterClose;
        bool chapter03Letter = chapter03LetterClosed || chapter03BeforeLetterClose || chapter04Story;
        bool chapter03Memory = checkpoint == GameTestCheckpoint.Chapter03MemoryStart
            || chapter03Bedroom || chapter03LabEntrance || chapter03Letter;
        // Chapter 3 shortcuts inherit the completed Chapter 2 setup.
        bool chapter02 = IsChapter02Checkpoint(checkpoint) || chapter03DemoEnd || chapter03Memory;
        bool chapter01 = checkpoint >= GameTestCheckpoint.Chapter01BeforeToolUnlock
            && checkpoint <= GameTestCheckpoint.Chapter01EndingAnimation;
        bool memoryCheckpoint = chapter01 || chapter02;
        bool endingAnimation = checkpoint == GameTestCheckpoint.Chapter01EndingAnimation;
        bool clawMachine = checkpoint == GameTestCheckpoint.Chapter02ClawMachine;
        bool heartTalk = checkpoint == GameTestCheckpoint.Chapter02HeartTalk;
        bool hiddenMemory = checkpoint == GameTestCheckpoint.Chapter02HiddenMemory;
        bool returnHome = checkpoint == GameTestCheckpoint.Chapter02ReturnHome;
        bool finalTalk = checkpoint == GameTestCheckpoint.Chapter02FinalTalk || chapter03DemoEnd || chapter03Memory;
        bool secondMatch = checkpoint == GameTestCheckpoint.Chapter02PaperPushMatch2;
        bool rematch = checkpoint == GameTestCheckpoint.Chapter02PaperPushRematch || secondMatch || finalTalk;
        bool cursorExperimentEnd = checkpoint == GameTestCheckpoint.Chapter02CursorExperimentEnd || heartTalk;
        bool cursorExperiment = checkpoint == GameTestCheckpoint.Chapter02CursorExperiment || cursorExperimentEnd;
        int memoryIndex = chapter03Letter ? OSChapter03StoryContent.Instance.Node("interaction.lab.envelope.small_print")
            : chapter03LabEntrance ? OSChapter03StoryContent.Instance.Node("memory.9210")
            : chapter03Bedroom ? OSChapter03StoryContent.Instance.Node("memory.bed.enter")
            : chapter03Memory ? 8000
            : checkpoint == GameTestCheckpoint.Chapter02Start ? 4000
            : checkpoint == GameTestCheckpoint.Chapter02AfterUpgrade03 ? 4320
            : checkpoint == GameTestCheckpoint.Chapter02MapPlan ? 4620
            : returnHome ? 6020
            : hiddenMemory || rematch ? OSChapter02FinaleDirector.MemoryEndDataIndex
            : clawMachine || cursorExperiment ? OSMemoryData.Chapter02CollectionWaitingDataIndex
            : checkpoint == GameTestCheckpoint.Chapter01BeforeToolUnlock
            ? 1260 : endingAnimation ? 3770 : MemoryHomeEntryDataIndex;
        if (memoryCheckpoint && !data.TryGetOSMemoryData(memoryIndex, out _))
            throw new InvalidOperationException($"回忆节点 {memoryIndex} 不存在。");

        // Reload the two presentation scenes to discard running tutorials, tweens,
        // puzzle sessions and cached window state before installing the new snapshot.
        // GameSetUp and its managers stay alive. No disk save is written.
        yield return ChangeScene(loader, manager.OSSceneName, false);
        yield return ChangeScene(loader, manager.SideScrollSceneName, false);

        data.SaveData.osProcess = new OSProcessSaveData();
        data.SaveData.osResume = new OSResumeSaveData();
        data.MainGameData.movementTutorialCompleted = true;
        for (int i = 0; i <= 5; i++)
            data.SetEventState($"Event_{i:00}", false, memoryCheckpoint
                || i < (checkpoint == GameTestCheckpoint.PrologueHeartHeal ? 1 : 5));
        data.SetCurrentChapterIndex(chapter03Memory ? 3 : chapter02 ? 2 : chapter01 ? 1 : 0);
        data.SetCurrentStateCheckProgress(memoryCheckpoint ? 2f : 0f);
        if (GameResourceSystem.Instance != null)
            GameResourceSystem.Instance.ApplySnapshot(80, 95, 10);
        else
        {
            data.SetOSResourceValue(ResourceType.Heart, 80);
            data.SetOSResourceValue(ResourceType.Battery, 95);
            data.SetOSResourceValue(ResourceType.Fault, 10);
        }

        if (memoryCheckpoint)
        {
            data.SetChapter01KeyXAcquired();
            data.SetDesktopProcessIsLocked(OSDesktopProcessKeys.Memory, false);
            data.SetDesktopProcessIsLocked(OSDesktopProcessKeys.Record, false);
            data.SetCurrentOSMemoryData(memoryIndex);
            if (chapter02)
                PrepareChapter02Test(data, checkpoint != GameTestCheckpoint.Chapter02Start);
            else
                data.OSCharacterInteractionState.ActivateSequence(
                    OSCharacterInteractionIds.Chapter01MemoryRepair);
            if (clawMachine || cursorExperiment)
                PrepareChapter02ClawMachineTest(data);
            if (cursorExperiment)
            {
                var memory = data.OSProcessData.memory;
                memory.chapter02ClawCursorCaptured = true;
                memory.chapter02CursorExperimentRound = cursorExperimentEnd ? 4 : 1;
                memory.chapter02CursorExperimentPhase = (int)(cursorExperimentEnd
                    ? OSCursorExperimentController.ExperimentPhase.Completed
                    : OSCursorExperimentController.ExperimentPhase.RoundIntro);
                if (cursorExperimentEnd)
                {
                    // Resume the apology choices or their completed conversation.
                    // The fresh osProcess clears branch history and reward receipts
                    // so either route can be tested again on every jump.
                    memory.chapter02CursorStoryStep = OSCursorExperimentStoryDirector.ThirdTestReleasedStep;
                    memory.chapter02CursorReleaseStep = (int)(heartTalk
                        ? OSCursorReleaseDirector.ReleaseStep.ConversationHold
                        : OSCursorReleaseDirector.ReleaseStep.Explain);
                    memory.chapter02RedCursorUnlocked = true;
                    if (heartTalk)
                    {
                        // The apology has finished. Only its branch is carried
                        // into the fresh conversation; do not replay its reward.
                        memory.chapter02CursorReleaseChoice = heartTalkReleaseChoice;
                        memory.chapter02CursorReleaseHeartGranted = true;
                        memory.chapter02HeartTalkStep = 0; // Inactive -> Seat after the jump settles.
                    }
                }
            }
            if (chapter02 || checkpoint == GameTestCheckpoint.Chapter01BeforeInteractivePicture || endingAnimation)
            {
                data.UnlockTool();
                data.MarkToolInitialized();
                data.OSProcessData.installedToolProgramIds.Add(OSToolProgramIds.MemoryRepair01);
                data.OSProcessData.installedToolProgramIds.Add(OSToolProgramIds.MemoryRepair02);
            }
            if (endingAnimation)
                PrepareChapter01EndingTest(data);
            if (returnHome)
                PrepareChapter02PostCursorTest(data);
            if (hiddenMemory || rematch)
                PrepareChapter02FinaleTest(data);
            if (rematch)
            {
                var memory = data.OSProcessData.memory;
                memory.chapter02FinaleStep = (int)OSChapter02FinaleDirector.FinaleStep.Complete;
                memory.chapter02FinaleHiddenLine = 24;
                OSChapter02PaperPushDirector.PrepareDebugCheckpoint(data.OSEnergyLoopState, secondMatch ? 2 : 1);
                if (finalTalk)
                    OSChapter02FinalTalkDirector.PrepareDebugCheckpoint(memory, data.OSEnergyLoopState);
                if (chapter03DemoEnd)
                {
                    memory.chapter02FinalTalkStep = (int)OSChapter02FinalTalkDirector.FinalTalkStep.Chapter03Ready;
                    // Nightfall must remain untriggered: the ordinary StateCheck
                    // click chooses the Demo ending or production Chapter 3 flow.
                }
                if (chapter03Memory)
                {
                    memory.chapter02FinalTalkStep = (int)OSChapter02FinalTalkDirector.FinalTalkStep.Complete;
                    memory.chapter03EntryStep = (int)OSChapter03EntryDirector.EntryStep.Complete;
                    memory.chapter03EntryInstallAnswer = 1;
                    data.OSProcessData.installedToolProgramIds.Add(OSToolProgramIds.MemoryRepair04);
                    // The new OS snapshot already has a fresh Home and story.
                    // Do not carry the Chapter 2 recovery sequence into its opening.
                    data.OSProcessData.characterInteraction = new OSCharacterInteractionSaveData();
                    if (chapter04Story)
                    {
                        PrepareChapter04StoryTest(data);
                        if (chapter04FinalHeal) PrepareChapter04FinalHealTest(data);
                    }
                    else if (chapter03BeforeLetterClose) PrepareChapter03BeforeLetterCloseTest(data);
                    else if (chapter03LetterClosed) PrepareChapter03LetterClosedTest(data);
                    else if (chapter03LabEntrance) PrepareChapter03LabEntranceTest(data);
                    else if (chapter03Bedroom) PrepareChapter03BedroomTest(memory);
                }
            }
        }

        yield return ChangeScene(loader, manager.OSSceneName, true);
        yield return ChangeScene(loader, manager.SideScrollSceneName, true);
        MainGameHandle.Instance?.InitializeNewGame();
        MainGameHandle.Instance?.LoadFromSave();
        PlayerCtrlManager.Instance?.SetControlState(true, true);

        // Force notification even when returning to the same phase after a reload.
        manager.DebugSwitchPhase(memoryCheckpoint ? GamePhase.OSMain : GamePhase.Prologue);
        yield return WaitForPhase(manager);
        OS_Handle os = OS_Handle.Instance;
        if (os == null)
            throw new InvalidOperationException("OS 窗口未能初始化。");

        if (chapter04Story)
        {
            os.SetCharacterWindowState(OSCharacterWindowState.Anima, false);
            os.OpenCharacterWindowForReconnect(0f);
            // The public launcher correctly rejects input while this checkpoint
            // owns the console guard. The prepared snapshot is already past
            // installation, so open its existing window; story starts after
            // the jump releases its input guard on the following frame.
            os.ResolveChapter04TVReferences();
            if (os.TVWindow == null)
                throw new InvalidOperationException("第四章 TV 未能打开。");
            os.TVWindow.OpenStandalone();
            os.Chapter04Story?.ResumeDialogue();
            if (chapter04FinalHeal)
            {
                // Completed dialogue has no active text to open its carrier.
                // Build the real chat shell so the saved grave history is visible.
                os.CharacterWindowController.GetComponent<OSWindowUnit>().CompleteOpeningFromScript();
                if (!os.LinkMessageController.OpenChapter04DialogueShell(os.Chapter04Story))
                    throw new InvalidOperationException("第四章墓前聊天背景未能打开。");
                OSChapter04ChatView.Attach(os.Chapter04Story, os.LinkMessageController);
                if (chapter04FinalMother && !os.Chapter04Story.DebugBeginMotherCheckpoint())
                    throw new InvalidOperationException("最终治疗的妈妈前检查点未能启动。");
            }
        }
        else if (endingAnimation)
        {
            // The ending owns the foreground; opening the memory window here would
            // replay the interactive-picture setup before the opening slides.
            if (os.Chapter01Ending == null || !os.Chapter01Ending.DebugBeginSlides())
                throw new InvalidOperationException("第一章结尾 PPT 未能启动，请检查结尾配置。");
        }
        else if (memoryCheckpoint)
        {
            os.SetCharacterWindowState(checkpoint == GameTestCheckpoint.Chapter02Start
                ? OSCharacterWindowState.VoidMessage : OSCharacterWindowState.Anima, false);
            if (chapter02)
            {
                os.OpenCharacterWindowForReconnect(0f);
                // Installed previews have finished the reconnect dialogue. Let
                // the memory illustration appear above Live while testing it.
                if (checkpoint != GameTestCheckpoint.Chapter02Start)
                    os.CharacterWindowController?.GetComponent<OSWindowUnit>()?.SetAlwaysOnTop(false);
            }
            if (chapter03Memory)
                os.Chapter02FinalTalk?.Nightfall?.RestoreEntryNight();
            if (chapter03Memory || !hiddenMemory && !rematch)
            {
                // At either letter checkpoint, restore its already-read small print.
                // A debug text jump would replay that line's typewriter animation.
                if ((!chapter03Letter && !os.MemoryWindowController.DebugJumpToData(memoryIndex))
                    || !os.OpenMemoryWindow())
                    throw new InvalidOperationException("无法打开回忆窗口。");
                os.NotifyTaskbarProcessPressed(os.MemoryWindowController.GetComponent<OSWindowUnit>());
            }
            // The finale begins after 7460 has finished. Its director owns the
            // hidden screen and starts once this jump/console input guard clears.
            // The rematch director likewise opens its own title after the guard,
            // or replays unsealing before opening it for the first shortcut.
            if (clawMachine)
            {
                // Initialize/unlock the launcher through the ordinary discovery
                // path, then use the same launch path as an actual icon click.
                os.OpenChapter02ClawMachineWindow(false);
                if (!os.RequestChapter02ClawMachineLaunch())
                    throw new InvalidOperationException("无法打开抓娃娃机窗口。");
            }
            // The experiment shortcut does not relaunch the discarded fake app.
            // OS_Handle.RefreshCursorExperiment resumes the independent window
            // after this jump and its console input guard have finished.
        }
        else
        {
            string eventId = checkpoint == GameTestCheckpoint.PrologueHeartHeal ? "Event_01" : "Event_05";
            SceneEventBase target = null;
            foreach (SceneEventBase sceneEvent in UnityEngine.Object.FindObjectsByType<SceneEventBase>(FindObjectsInactive.Include))
                if (sceneEvent.EventId == eventId && sceneEvent.gameObject.scene.name == manager.SideScrollSceneName)
                    target = sceneEvent;
            if (target == null)
                throw new InvalidOperationException($"找不到场景事件 {eventId}。");
            var player = PlayerCtrlManager.Instance?.PlayerController;
            if (player != null)
            {
                // Event roots mark trigger centers, not the character's ground height.
                // Keep the Y/Z established by InitializeNewGame and only jump along X.
                Vector3 position = player.transform.position;
                position.x = target.transform.position.x;
                player.TeleportTo(position);
            }
            if (checkpoint == GameTestCheckpoint.PrologueFault)
                target.OnDialogueLineEntered(2, null);
            target.CompleteEvent();
            yield return WaitForPhase(manager);
        }
    }

    static void PrepareChapter04StoryTest(GameDataManager data)
    {
        PrepareChapter03LetterClosedTest(data);
        var memory = data.OSProcessData.memory;
        memory.chapter03Story.memoryQueue.Clear();
        memory.chapter03Story.liveQueue.Clear();
        memory.chapter03Story.phase = (int)OSChapter03StoryDirector.Phase.Complete;
        memory.chapter04EntryStep = (int)OSChapter04EntryDirector.EntryStep.Complete;
        memory.chapter04EntryElapsed = 0f;
        memory.chapter04Story = new OSChapter04StoryState();
        if (!data.OSProcessData.installedToolProgramIds.Contains(OSToolProgramIds.TV))
            data.OSProcessData.installedToolProgramIds.Add(OSToolProgramIds.TV);
        data.SetCurrentChapterIndex(4);
        // Start from the authored low-heart Chapter 3 boundary. Prior story-fault
        // receipts prevent the generic catch-up pass from charging those beats again.
        foreach (string id in new[] { "ch01_ending_glitch", "ch02_quake", "ch02_collapse", "ch02_hidden_memory", "ch02_nightfall" })
            data.OSCharacterInteractionState.TryMarkOptionResourcesApplied("story.fault", id, "applied");
        if (GameResourceSystem.Instance != null) GameResourceSystem.Instance.ApplySnapshot(34, 95, 54);
        else
        {
            data.SetOSResourceValue(ResourceType.Heart, 34);
            data.SetOSResourceValue(ResourceType.Battery, 95);
            data.SetOSResourceValue(ResourceType.Fault, 54);
        }
    }

    static void PrepareChapter04FinalHealTest(GameDataManager data)
    {
        // The last grave line has finished. Preserve the real warning delay,
        // its button and the playable treatment, rather than auto-hitting QA.
        var content = OSChapter04StoryContent.Instance;
        var state = new OSChapter04StoryState
        {
            version = 3, started = true, completed = true,
            currentEntryId = "chapter04_story_grave_16", currentVisit = 16,
            currentTextCompleted = true, currentCueCommitted = true,
            currentResourcesPrepared = true, currentResourcesCommitted = true,
            resourceBaselineCaptured = true, initialHeart = 34, initialFault = 54,
            heartWeightApplied = content.HeartWeightBudget,
            faultWeightApplied = content.FaultWeightBudget,
            currentHeartTarget = OSChapter04StoryDirector.TargetHeart,
            currentFaultTarget = OSChapter04StoryDirector.TargetFault,
            theatreState = OSChapter04PresentationBridge.GraveBreakdown,
            finalTreatmentStage = OSChapter04FinalTreatmentStage.WaitingForWarning
        };
        for (int line = 13; line <= 16; line++)
            state.history.Add(new OSChapter04StoryRecord { visit = line, entryId = "chapter04_story_grave_" + line });
        data.OSProcessData.memory.chapter04Story = state;
        if (GameResourceSystem.Instance != null)
            GameResourceSystem.Instance.ApplySnapshot(state.currentHeartTarget, 95, state.currentFaultTarget);
        else
        {
            data.SetOSResourceValue(ResourceType.Heart, state.currentHeartTarget);
            data.SetOSResourceValue(ResourceType.Battery, 95);
            data.SetOSResourceValue(ResourceType.Fault, state.currentFaultTarget);
        }
    }

    static void PrepareChapter03BedroomTest(OSMemoryProcessSaveData memory)
    {
        // The spare key has opened Mom's bedroom. Leave the bathroom note and
        // mirror untouched so entering the bathroom plays the real breakdown.
        memory.chapter03HomeInitialized = true;
        memory.chapter03HomeView = new OSHomeView.State {
            room = "bed", me = new Vector2Int(6, 1), dong = new Vector2Int(7, 1), dongHidden = false
        };
        var story = new OSChapter03StoryState {
            version = 1, phase = (int)OSChapter03StoryDirector.Phase.KeyReady,
            clock = 3, knowAnswer = 1, homeOpened = true
        };
        foreach (string flag in new[] {
            "home_revealed", "first_floor", "opening_8020", "opening_8070", "opening_8080", "opening_8110", "paper_note_01",
            "room_study", "room_west", "room_hall", "room_kitchen", "room_east", "room_store", "room_bed",
            "bed_knocked", "doubt_raised", "shoes_seen", "calendar_seen", "machine_seen", "morning_seen", "morning_said",
            "medicine_seen", "deduction_queued", "deduction_done", "final_knock", "yard_all_seen", "yard_zoom", "yard_zoom_done",
            "yard_returned", "store_door_seen", "drawer_seen", "screwdriver_taken", "store_open", "makeup_seen", "key_taken",
            "bed_open", "knows_question", "knows_reply_done", "paper_requested_diary_08"
        }) story.Add(flag);
        story.papers.AddRange(new[] { "note_01", "diary_08" });
        story.yardVisited.AddRange(new[] { "yard_bedwindow", "yard_clothes", "yard_tree", "yard_fence", "yard_gate", "yard_seedlings" });
        // Key and screwdriver were consumed; coffee is optional and not prepared.
        memory.chapter03Story = story;
    }

    static void PrepareChapter03LabEntranceTest(GameDataManager data)
    {
        var memory = data.OSProcessData.memory;
        PrepareChapter03BedroomTest(memory);
        memory.chapter03HomeView = new OSHomeView.State {
            room = "east", me = new Vector2Int(13, 4), dong = new Vector2Int(12, 4), dongHidden = false
        };
        var story = memory.chapter03Story;
        story.phase = (int)OSChapter03StoryDirector.Phase.Offline;
        story.liveOffline = true;
        story.coffeeStep = 5;
        foreach (string flag in new[] {
            "room_bath", "coffee_water", "coffee_beans", "coffee_brewed", "coffee_taken",
            "mirror_visible", "mirror_identified", "mirror_dialogue_9040", "mirror_dialogue_9045",
            "mirror_pause_started", "mirror_breakdown_queued", "mirror_playback_paused", "mirror_live_focus",
            "live_moved_center", "closeup_open", "breakdown_music", "breakdown_music_fading", "breakdown_quiet",
            "closeup_closed", "live_back_home", "mirror_playback_resumed", "mirror_self", "mirror_done",
            "left_mirror", "offline_east", "offline_east_leak", "offline_read_receipt", "password_ready", "lab_door_seen"
        }) story.Add(flag);

        // Everything collectible outside the lab is ready, including the cup's
        // diary. Keep the coffee for the first bedside visit inside the lab.
        story.papers.Clear();
        story.papers.AddRange(new[] {
            "note_01", "diary_02", "diary_03", "diary_04", "diary_05", "diary_06",
            "diary_07", "diary_08", "husband_letter", "note_09"
        });
        foreach (string paper in story.papers)
            if (paper != "note_01") story.Add("paper_requested_" + paper);
        story.items.AddRange(new[] { "coffee", "letters" });
        OSStoryRecordUnlocks.RefreshDiary(data);

        // Both the corridor cry and its reply have finished. Register the real
        // consumption receipts without replaying either message or its choice.
        var interaction = data.OSCharacterInteractionState;
        foreach (string key in new[] { "live.offline.east", "live.offline.initial_offer" })
        {
            string entry = OSChapter03StoryDirector.LiveId(key);
            interaction.BeginGlobalPresentation(entry);
            interaction.CompleteGlobalPresentation(entry);
        }
        // The fresh story keeps both queues empty. The locked door/keypad owns
        // the next interaction; no lab paper, room entry or keypad is prequeued.
    }

    static void PrepareChapter03BeforeLetterCloseTest(GameDataManager data)
    {
        PrepareChapter03LetterClosedTest(data);
        var story = data.OSProcessData.memory.chapter03Story;
        story.flags.Remove("letter_collected");
        // The real letter beat restores the open sheet and owns its Close callback.
        // Only that click may collect it and begin the ordinary closing pause.
        story.memoryQueue.Insert(0, new OSChapter03StoryBeat("letter"));
    }

    static void PrepareChapter03LetterClosedTest(GameDataManager data)
    {
        PrepareChapter03LabEntranceTest(data);
        var memory = data.OSProcessData.memory;
        memory.chapter03HomeView = new OSHomeView.State {
            room = "lab", me = new Vector2Int(7, 5), dong = new Vector2Int(8, 5), dongHidden = true
        };
        int lastLine = OSChapter03StoryContent.Instance.Node("interaction.lab.envelope.small_print");
        memory.triggeredDataIndices.Add(lastLine);
        var story = memory.chapter03Story;
        story.phase = (int)OSChapter03StoryDirector.Phase.Letter;
        story.liveOffline = false;
        story.flags.Remove("reconnecting");
        foreach (string flag in new[] {
            "room_lab", "lab_open", "lab_master_seen", "dong_absorbed", "bed_first", "lab_lamp_on",
            "coffee_placed", "bed_second_started", "bed_second_done", "reunion_started", "live_returned",
            "box_open", "hairclip_seen", "letter_started", "letter_unfolded", "letter_collected"
        }) story.Add(flag);
        story.items.Remove("coffee");
        foreach (string paper in new[] { "diary_10", "repair_11", "diary_13" })
        {
            story.papers.Add(paper);
            story.Add("paper_requested_" + paper);
        }
        OSStoryRecordUnlocks.RefreshDiary(data);
        // Chapter 2's rematch reward was already collected. Reconciliation must
        // not overlay this closing pause with its old acquisition notification.
        data.UnlockRecordData(OSStoryRecordUnlocks.ResearcherPaperDollRecordIndex);

        var interaction = data.OSCharacterInteractionState;
        foreach (string key in new[] {
            "live.offline.lab_open", "live.offline.lab_offer", "live.offline.diary13", "live.offline.bed",
            "live.return.01", "live.return.02", "live.return.03", "live.return.04", "live.return.05",
            "live.return.06", "live.return.07", "live.return.08", "live.return.09", "live.return.10",
            "live.letter.hairclip", "live.letter.silly"
        })
        {
            string entry = OSChapter03StoryDirector.LiveId(key);
            interaction.BeginGlobalPresentation(entry);
            interaction.CompleteGlobalPresentation(entry);
        }

        // Resume exactly after collecting the letter. The ordinary story queue
        // retains its three-second pause, then starts the Chapter 4 transition.
        // Fresh osProcess keeps TV uninstalled and the entry director inactive.
        story.memoryQueue.Clear();
        story.liveQueue.Clear();
        story.memoryQueue.Add(new OSChapter03StoryBeat("delay", delay: 3f));
        story.memoryQueue.Add(new OSChapter03StoryBeat("phase",
            index: (int)OSChapter03StoryDirector.Phase.Complete));
    }

    static void PrepareChapter02Test(GameDataManager data, bool afterUpgrade03)
    {
        data.OSProcessData.chapter01Ending.phase = OSChapter01EndingPhase.Chapter02Started;
        data.OSProcessData.chapter01Ending.completed = true;
        data.MarkToolProgramsUnpacked();

        // Chapter 2 inherits the discovered, sealed game. Its ending must be able
        // to reveal and open this icon after any shortcut's fresh OS snapshot.
        var energy = data.OSEnergyLoopState;
        energy.minigameDiscovered = energy.minigameUnlocked = true;
        energy.paperPushSealed = true;

        var interaction = data.OSCharacterInteractionState;
        interaction.ActivateSequence("chapter02.recovery");
        // At 4000 the player has already opened Chapter 2 while Live is away.
        // Leave the return entries untouched so reaching the 4310 repair gate
        // still plays the signal-in, choices and upgrade delivery normally.
        foreach (string entry in new[]
        {
            "chapter02_recovery_bugs", "chapter02_recovery_done", "chapter02_departure_busy"
        })
        {
            interaction.BeginPresentation(entry);
            interaction.CompletePresentation(entry);
        }

        if (!afterUpgrade03) return;

        data.OSProcessData.installedToolProgramIds.Add(OSToolProgramIds.MemoryRepair03);
        foreach (string entry in new[]
        {
            "chapter02_return_arrived", "chapter02_return_working",
            "chapter02_return_ready", "chapter02_return_reaction",
            "chapter02_memory_upgrade_03_installed"
        })
        {
            interaction.BeginPresentation(entry);
            interaction.CompletePresentation(entry);
        }
        // Installed previews begin after the final "....?" has closed. No old
        // recovery messages or temporary extended animation should resume.
        interaction.isCompleted = true;
        interaction.phase = OSCharacterInteractionSavePhase.Completed;
    }

    static void PrepareChapter02ClawMachineTest(GameDataManager data)
    {
        var interaction = data.OSCharacterInteractionState;
        foreach (string entry in new[]
        {
            "chapter02_collecting_wait", "chapter02_collecting_new_app"
        })
        {
            interaction.BeginPresentation(entry);
            interaction.CompletePresentation(entry);
        }
        interaction.isCompleted = true;
        interaction.phase = OSCharacterInteractionSavePhase.Completed;

        // This gameplay shortcut skips the app-offer beat. A desktop launcher
        // permits normal close/reopen testing without replaying that offer.
        // PrepareCheckpoint already replaced osProcess, so prizes, both desire
        // messages and cursor capture all start fresh on every jump.
        data.OSProcessData.memory.chapter02ClawMachineDiscovered = true;
        data.OSProcessData.memory.chapter02CollectionIntroStep = 8;
        data.OSProcessData.memory.chapter02ClawMachineOnDesktop = true;
    }

    static void PrepareChapter01EndingTest(GameDataManager data)
    {
        OSMemoryProcessSaveData memory = data.OSProcessData.memory;
        memory.interactiveSceneDataIndex = MemoryHomeEntryDataIndex;
        var picture = new OSMemoryPicSaveData
        {
            scenarioId = "chapter01_home",
            currentSceneId = "workshop",
            initialized = true
        };
        // Completed repair, after the knock and return from the wiring close-up.
        // A fresh snapshot leaves no pending effects or previous DEMO completion.
        foreach (string flag in new[]
        {
            "robot_placed", "glass_broken", "window_inspected", "boards_placed",
            "window_fixed", "glass_swept", "wires_connected", "fuse_fixed",
            "robot_repaired", "knock_heard"
        })
            picture.Set(flag);
        for (int i = 0; i < 3; i++)
            picture.Set("board_" + i);
        for (int i = 0; i < 6; i++)
            picture.Set("nail_" + i + "_done");
        for (int i = 0; i < 4; i++)
            picture.Set("wire_" + i);
        memory.pictureScenario = picture;
    }

    static void PrepareChapter02PostCursorTest(GameDataManager data)
    {
        PrepareChapter02ClawMachineTest(data);
        var memory = data.OSProcessData.memory;
        // Later memory shortcuts have already left the cursor-test conversation.
        // Complete that flow so it cannot reopen its room or cage after the jump.
        memory.chapter02ClawCursorCaptured = true;
        memory.chapter02CursorExperimentRound = 4;
        memory.chapter02CursorExperimentPhase = (int)OSCursorExperimentController.ExperimentPhase.Completed;
        memory.chapter02CursorStoryStep = OSCursorExperimentStoryDirector.ThirdTestReleasedStep;
        memory.chapter02CursorReleaseStep = (int)OSCursorReleaseDirector.ReleaseStep.ConversationHold;
        memory.chapter02CursorReleaseChoice = 1;
        memory.chapter02CursorReleaseHeartGranted = true;
        memory.chapter02RedCursorUnlocked = true;
        memory.chapter02HeartTalkStep = (int)OSCursorHeartTalkDirector.HeartTalkStep.Done;
    }

    static void PrepareChapter02FinaleTest(GameDataManager data)
    {
        PrepareChapter02PostCursorTest(data);
        // Do not manufacture prior reading answers or replay their rewards.
        OSChapter02FinaleDirector.PrepareDebugCheckpoint(data.OSProcessData.memory);
    }

    static IEnumerator ChangeScene(SceneLoadManager loader, string sceneName, bool load)
    {
        bool done = false;
        string failure = null;
        if (load)
            loader.LoadSceneAdditive(sceneName, onLoadCompleted: _ => done = true,
                onLoadFailed: reason => { failure = reason; done = true; });
        else
            loader.UnloadScene(sceneName, onUnloadCompleted: _ => done = true,
                onUnloadFailed: reason => { failure = reason; done = true; });
        float deadline = Time.realtimeSinceStartup + 30f;
        while (!done && Time.realtimeSinceStartup < deadline)
            yield return null;
        if (!done || failure != null)
            throw new InvalidOperationException(failure ?? $"场景 {sceneName} 加载超时。");
        yield return null; // Awake/Start and deferred destruction must finish.
    }

    static IEnumerator WaitForPhase(GameManager manager)
    {
        float deadline = Time.realtimeSinceStartup + 30f;
        while (manager.IsTransitioning && Time.realtimeSinceStartup < deadline)
            yield return null;
        if (manager.IsTransitioning)
            throw new InvalidOperationException("场景切换超时。");
        yield return null;
    }
#endif
}
