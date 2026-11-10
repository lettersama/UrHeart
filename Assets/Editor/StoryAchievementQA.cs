using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

// Edit Mode only. All saves and Steam operations below are in-memory fixtures.
// This runner does not instantiate the live Steam service or touch player saves.
public static class StoryAchievementQA
{
    [Serializable] sealed class Report
    {
        public string status;
        public int passed;
        public int failed;
        public string[] checks;
        public string[] errors;
    }

    static readonly List<string> checks = new List<string>();
    static readonly List<string> errors = new List<string>();
    static readonly string[] ids =
    {
        "ACH_GRAVEKEEPER_TREATED", "ACH_ROBOT_BROUGHT_HOME", "ACH_CHAPTER_01_COMPLETE",
        "ACH_CURSOR_TESTS_COMPLETE", "ACH_PAPER_PUSH_GEAR_USED", "ACH_CHAPTER_02_COMPLETE",
        "ACH_MOTHERS_LETTER_READ", "ACH_CHAPTER_03_COMPLETE", "ACH_GAME_COMPLETE",
        "ACH_MY_DAUGHTER", "ACH_SECRET_GIFT_RECEIVED"
    };

    public static string Run()
    {
        if (Application.isPlaying) return "Run StoryAchievementQA.Run() in Edit Mode only.";
        checks.Clear(); errors.Clear();
        Case("API names", Names);
        Case("milestone persistence", Milestones);
        Case("legacy boundaries", LegacyBoundaries);
        Case("later legacy boundaries", LaterLegacyBoundaries);
        Case("farewell receipt", FarewellReceipt);
        Case("release policy", ReleasePolicy);
        Case("linear catch-up", CatchUp);
        Case("existing remote achievements", ExistingRemote);
        Case("unavailable backend", Unavailable);
        Case("read/write retries", ReadWriteRetries);
        Case("partial batch retry", PartialBatchRetry);
        Case("store return failure", StoreReturnFailure);
        Case("store callback failure", StoreCallbackFailure);
        Case("server rollback and rejected restage", ServerRollback);
        Case("store callback timeout", StoreCallbackTimeout);
        Case("late store callbacks", LateStoreCallbacks);
        Case("account isolation", AccountIsolation);
        Case("offline transaction", OfflineTransaction);
        Case("progress during store", ProgressDuringStore);
        var report = new Report
        {
            status = errors.Count == 0 ? "PASS" : "FAIL",
            passed = checks.Count, failed = errors.Count,
            checks = checks.ToArray(), errors = errors.ToArray()
        };
        string json = JsonUtility.ToJson(report, true);
        string folder = Path.GetFullPath(Path.Combine("Temp", "StoryAchievementQA"));
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "results.json"), json);
        return json;
    }

    static void Case(string name, Action action)
    {
        try { action(); }
        catch (Exception error) { errors.Add(name + ": " + error); }
    }

    static void Check(bool value, string description)
    {
        if (!value) throw new InvalidOperationException(description);
        checks.Add(description);
    }

    static void Names()
    {
        Check(StoryAchievementProgress.Count == 11, "All eleven authored achievements are enabled");
        for (int i = 1; i <= ids.Length; i++)
            Check(StoryAchievementProgress.ApiName(i) == ids[i - 1], "Steam API spelling: " + ids[i - 1]);
        Check(StoryAchievementProgress.ApiName(0) == null && StoryAchievementProgress.ApiName(12) == null
            && StoryAchievementProgress.ApiName(-1) == null, "Out-of-range milestones cannot name future achievements");
        Check(StoryAchievementProgress.GetReached(null) == 0, "Missing save has no earned achievements");
        Check((int)StoryAchievementMilestone.Chapter02Complete == 6
            && (int)StoryAchievementMilestone.MothersLetterRead == 7
            && (int)StoryAchievementMilestone.Chapter03Complete == 8
            && (int)StoryAchievementMilestone.GameComplete == 9
            && (int)StoryAchievementMilestone.MyDaughter == 10
            && (int)StoryAchievementMilestone.SecretGiftReceived == 11,
            "New milestones append in actual story order without changing the first six");
        Check(ids.Distinct().Count() == ids.Length, "Every achievement uses a distinct Steam API name");
    }

    static void Milestones()
    {
        var save = new GameSaveData();
        save.EnsureData();
        Check(save.storyAchievementVersion == 2 && StoryAchievementProgress.GetReached(save) == 0,
            "Fresh save initializes achievement schema without awarding anything");
        Check(StoryAchievementProgress.Mark(save, StoryAchievementMilestone.Chapter01Complete),
            "Authored milestone advances saved progress");
        Check(StoryAchievementProgress.GetReached(save) == 3, "Third milestone implicitly covers the first three");
        Check(!StoryAchievementProgress.Mark(save, StoryAchievementMilestone.Chapter01Complete)
            && !StoryAchievementProgress.Mark(save, StoryAchievementMilestone.GravekeeperTreated),
            "Duplicate and earlier authored events are idempotent");
        save.osProcess.currentChapterIndex = 3;
        save.osProcess.memory.chapter02FinalTalkStep = 17;
        save.EnsureData();
        Check(StoryAchievementProgress.GetReached(save) == 3,
            "Current-schema saves never infer new awards from transient/checkpoint story fields");
        var copy = JsonUtility.FromJson<GameSaveData>(JsonUtility.ToJson(save));
        copy.EnsureData();
        Check(copy.storyAchievementVersion == 2 && StoryAchievementProgress.GetReached(copy) == 3,
            "Recorded milestone survives save JSON round-trip independently of story fields");
        save.storyAchievementMilestone = 100;
        save.EnsureData();
        Check(StoryAchievementProgress.GetReached(save) == 11, "Corrupt/future progress is bounded by the eleven authored milestones");
        save.storyAchievementMilestone = -20;
        save.EnsureData();
        Check(StoryAchievementProgress.GetReached(save) == 0, "Negative stored progress is sanitized");

        var legacy = JsonUtility.FromJson<GameSaveData>("{\"osProcess\":{\"currentChapterIndex\":3}}");
        legacy.EnsureData();
        Check(legacy.storyAchievementVersion == 2 && StoryAchievementProgress.GetReached(legacy) == 6,
            "Old JSON with absent achievement fields migrates all six from chapter three");
        legacy.osProcess.currentChapterIndex = 0;
        legacy.EnsureData();
        Check(StoryAchievementProgress.GetReached(legacy) == 6, "Migration is durable after old fields disappear");
        var damaged = new GameSaveData { osProcess = null, eventCompletionList = null };
        damaged.EnsureData();
        Check(StoryAchievementProgress.GetReached(damaged) == 0, "Legacy null nested data does not invent progress");
    }

    static void Legacy(Action<GameSaveData> configure, int expected, string description)
    {
        var save = new GameSaveData();
        configure(save);
        save.EnsureData();
        Check(StoryAchievementProgress.GetReached(save) == expected, description);
        Check(save.storyAchievementVersion == 2, "Migration schema committed: " + description);
    }

    static void LegacyBoundaries()
    {
        Legacy(s => { }, 0, "Untouched legacy save grants none");
        Legacy(s => s.eventCompletionList.Add(new EventCompletionData { eventID = "Event_02", isActive = true }),
            1, "Event 02 activation proves gravekeeper treatment completion");
        Legacy(s => s.eventCompletionList.Add(new EventCompletionData { eventID = "Event_02", isCompleted = true }),
            1, "Event 02 completion proves gravekeeper treatment completion");
        Legacy(s => s.eventCompletionList.Add(new EventCompletionData { eventID = "Event_02" }),
            0, "An unstarted Event 02 entry is not treatment completion");
        Legacy(s => s.eventCompletionList.Add(new EventCompletionData { eventID = "Event_01", isCompleted = true }),
            0, "Starting the gravekeeper treatment does not complete it");
        Legacy(s => s.osProcess.memory.currentDataIndex = 2680,
            1, "Arriving at the home-upgrade text does not yet open the interactive home");
        Legacy(s => { s.osProcess.memory.pictureScenario.scenarioId = "chapter01_home"; },
            0, "Configured home scenario without initialization is not entering the interactive scene");
        Legacy(s => { s.osProcess.memory.pictureScenario.scenarioId = "unrelated";
            s.osProcess.memory.pictureScenario.initialized = true; },
            0, "An unrelated initialized picture scenario is not chapter-one home");
        Legacy(s => { s.osProcess.memory.pictureScenario.scenarioId = "chapter01_home";
            s.osProcess.memory.pictureScenario.initialized = true; },
            2, "Actually initialized chapter-one home catches up the first two");
        Legacy(s => s.osProcess.chapter01Ending.phase = OSChapter01EndingPhase.DoorKnocked,
            2, "DoorKnocked enum value 8 remains before ending animation completion");
        Legacy(s => s.osProcess.chapter01Ending.phase = OSChapter01EndingPhase.Animation,
            2, "An in-progress ending animation does not complete chapter one");
        foreach (var phase in new[] { OSChapter01EndingPhase.Glitch, OSChapter01EndingPhase.Completed,
            OSChapter01EndingPhase.RecoveryAwaitingConfirmation, OSChapter01EndingPhase.Chapter02Started })
        {
            var captured = phase;
            Legacy(s => s.osProcess.chapter01Ending.phase = captured, 3,
                "Explicit post-animation phase completes chapter one: " + phase);
        }
        Legacy(s => s.osProcess.currentChapterIndex = 2, 3, "Chapter two infers exactly the first three");
        Legacy(s => { s.osProcess.currentChapterIndex = 2; s.osProcess.memory.chapter02CursorExperimentRound = 3; },
            3, "Entering mouse test three does not finish the tests");
        Legacy(s => { s.osProcess.currentChapterIndex = 2; s.osProcess.memory.chapter02CursorExperimentRound = 3;
            s.osProcess.memory.chapter02CursorStoryStep = 21; s.osProcess.memory.chapter02CursorExperimentAssisted = true; },
            3, "An assistance reaction still inside mouse test three is not its completion");
        Legacy(s => { s.osProcess.memory.chapter02CursorExperimentRound = 4; },
            4, "Completed mouse experiment catches up the first four");
        Legacy(s => s.osProcess.memory.chapter02CursorReleaseStep = 1,
            4, "Mouse-release story is sufficient evidence of all tests complete");
        Legacy(s => { s.osProcess.currentChapterIndex = 2; s.osProcess.energyLoop.chapter02PaperPushStep = 5;
            s.osProcess.energyLoop.chapter02PaperPushTiltWin = 1;
            s.osProcess.energyLoop.paperPushMatch = new OSPaperPushMatchSaveData { active = true, chapter = 2, matchStage = 2 }; },
            4, "Old active second match with stale tilt outcome must not grant gear achievement");
        foreach (int result in new[] { 1, 2 })
        {
            int captured = result;
            Legacy(s => { s.osProcess.energyLoop.chapter02PaperPushStep = 5;
                s.osProcess.energyLoop.chapter02PaperPushTiltWin = captured;
                s.osProcess.energyLoop.paperPushMatch = new OSPaperPushMatchSaveData { active = false, chapter = 2, matchStage = 2 }; },
                5, "Completed gear match grants achievement regardless of win/loss: " + result);
        }
        Legacy(s => s.osProcess.energyLoop.chapter02PaperPushStep = 6,
            5, "Paper-push wrap is after the second completed match");
        Legacy(s => s.osProcess.memory.chapter02FinalTalkStep = 15,
            5, "Final goodbye is still before the chapter-two completion boundary");
        Legacy(s => s.osProcess.memory.chapter02FinalTalkStep = 16,
            6, "Legacy Chapter03Ready catches up all six");
        Legacy(s => s.osProcess.memory.chapter03EntryStep = 1,
            6, "Chapter-three entry checkpoint catches up all six");
        Legacy(s => s.osProcess.memory.chapter02DemoEndReached = true,
            6, "A finished demo catches up all six in the full game");
        Legacy(s => s.osProcess.memory.triggeredDataIndices.Add(2700),
            2, "Legacy home-scene history remains evidence when the current reading cursor moved");
        Legacy(s => s.osProcess.memory.triggeredDataIndices.Add(4000),
            3, "Legacy chapter-two reading history infers chapter one complete");
        Legacy(s => s.osProcess.memory.triggeredDataIndices.Add(5410),
            4, "Legacy post-experiment reading history infers all tests complete");
        Legacy(s => { s.osProcess.memory.currentDataIndex = 200000;
            s.osProcess.memory.triggeredDataIndices.Add(100000); },
            0, "Unrelated large node numbers cannot act as chronological progression");
    }

    static void LaterLegacy(int version, Action<GameSaveData> configure, int expected, string description)
    {
        var save = new GameSaveData { storyAchievementVersion = version, storyAchievementMilestone = 6 };
        configure(save);
        save.EnsureData();
        Check(StoryAchievementProgress.GetReached(save) == expected, "Schema " + version + ": " + description);
        Check(save.storyAchievementVersion == 2, "Later migration commits schema 2: " + description);
        var copy = JsonUtility.FromJson<GameSaveData>(JsonUtility.ToJson(save));
        copy.osProcess = new OSProcessSaveData();
        copy.EnsureData();
        Check(StoryAchievementProgress.GetReached(copy) == expected,
            "Later migrated receipt survives JSON and missing old story evidence: " + description);
    }

    static void LaterLegacyBoundaries()
    {
        foreach (int version in new[] { 0, 1 })
        {
            LaterLegacy(version, s => { }, 6, "No later evidence preserves the first six");
            foreach (string flag in new[] { "letter_started", "letter_unfolded" })
            {
                string captured = flag;
                LaterLegacy(version, s => s.osProcess.memory.chapter03Story.Add(captured), 6,
                    "An unfinished letter is not collected: " + flag);
            }
            LaterLegacy(version, s => s.osProcess.memory.chapter03Story.letterPage = 99, 6,
                "Reading every page alone does not collect the letter");
            LaterLegacy(version, s => s.osProcess.memory.chapter03Story.Add("letter_collected"), 7,
                "The collected letter catches up through MothersLetterRead");
            LaterLegacy(version, s => s.osProcess.memory.chapter03Story.Add("letter_collecting"), 7,
                "Legacy collection animation restores directly to collected and earns MothersLetterRead");
            for (int step = 1; step <= 12; step++)
            {
                int captured = step;
                LaterLegacy(version, s => s.osProcess.memory.chapter04EntryStep = captured, step < 9 ? 7 : 8,
                    "Chapter-four entry step " + step + (step < 9 ? " is still before the chapter-card click" : " proves the chapter-card click"));
            }
            LaterLegacy(version, s => s.osProcess.currentChapterIndex = 4, 8,
                "Chapter four catches up through Chapter03Complete");
            LaterLegacy(version, s => s.osProcess.memory.chapter04Story.started = true, 8,
                "Started TV story proves entry into chapter four");
            LaterLegacy(version, s => s.osProcess.memory.chapter04Story.completed = true, 8,
                "Completed TV story alone is still before final treatment");
            foreach (var stage in new[] { OSChapter04FinalTreatmentStage.Pending, OSChapter04FinalTreatmentStage.Opened,
                OSChapter04FinalTreatmentStage.WaitingForWarning, OSChapter04FinalTreatmentStage.FailureWarning,
                OSChapter04FinalTreatmentStage.FinalHeal })
            {
                var captured = stage;
                LaterLegacy(version, s => s.osProcess.memory.chapter04Story.finalTreatmentStage = captured, 8,
                    "Pre-cure treatment stage cannot complete the game: " + stage);
            }
            LaterLegacy(version, s => { s.osProcess.memory.chapter04Story.finalTreatmentStage = OSChapter04FinalTreatmentStage.FinalHeal;
                s.osProcess.memory.chapter04Story.finalHealSection = 4; }, 8,
                "Reaching the mother-note section without the successful last tap is not GameComplete");
            LaterLegacy(version, s => s.osProcess.memory.chapter04Story.finalTreatmentStage = OSChapter04FinalTreatmentStage.Ending, 9,
                "Ending proves the last mother tap, but not postcard collection");
            LaterLegacy(version, s => s.osProcess.memory.chapter04Story.finalTreatmentStage = OSChapter04FinalTreatmentStage.Completed, 10,
                "Completed Staff proves postcard collection, but not the later Meta display");
            LaterLegacy(version, s => { s.storyAchievementMilestone = 11; s.osProcess.currentChapterIndex = 4; }, 11,
                "Migration never lowers a recorded later receipt");
        }

        var firstSix = new GameSaveData { storyAchievementVersion = 1, storyAchievementMilestone = 2 };
        firstSix.osProcess.currentChapterIndex = 3;
        firstSix.osProcess.memory.chapter02FinalTalkStep = 17;
        firstSix.EnsureData();
        Check(StoryAchievementProgress.GetReached(firstSix) == 2,
            "Upgrading schema 1 never re-infers the original six from transient fields");
        firstSix.osProcess.memory.chapter04Story.finalTreatmentStage = OSChapter04FinalTreatmentStage.Completed;
        firstSix.EnsureData();
        Check(StoryAchievementProgress.GetReached(firstSix) == 2,
            "Schema 2 does not repeatedly infer later achievements after migration");

        var current = new GameSaveData(); current.EnsureData();
        current.osProcess.memory.chapter03Story.Add("letter_collecting");
        current.EnsureData();
        Check(StoryAchievementProgress.GetReached(current) == 0,
            "The legacy collecting fallback does not infer awards in schema 2 saves");
        current.osProcess.currentChapterIndex = 4;
        current.osProcess.memory.chapter03Story.Add("letter_collected");
        current.osProcess.memory.chapter04EntryStep = 12;
        current.osProcess.memory.chapter04Story.finalTreatmentStage = OSChapter04FinalTreatmentStage.Completed;
        current.EnsureData();
        Check(StoryAchievementProgress.GetReached(current) == 0,
            "Fresh schema 2 saves only advance through explicit completion hooks");
        for (int milestone = 7; milestone <= 11; milestone++)
        {
            Check(StoryAchievementProgress.Mark(current, (StoryAchievementMilestone)milestone)
                && StoryAchievementProgress.GetReached(current) == milestone,
                "Explicit later hook advances exactly through milestone " + milestone);
            Check(!StoryAchievementProgress.Mark(current, (StoryAchievementMilestone)milestone),
                "Repeated later hook is idempotent at milestone " + milestone);
        }
    }

    static void FarewellReceipt()
    {
        Check(StoryAchievementProgress.GetFarewellReached(null) == 0, "Missing farewell receipt cannot grant the secret gift");
        for (int bits = 0; bits < 8; bits++)
        {
            var receipt = new PostGameFarewellState { completed = (bits & 1) != 0,
                windowShown = (bits & 2) != 0, windowDismissed = (bits & 4) != 0 };
            bool earned = (bits & 1) != 0 && (bits & 6) != 0;
            Check(StoryAchievementProgress.GetFarewellReached(receipt) == (earned ? 11 : 0),
                "Secret gift requires completion plus a shown or legacy-dismissed Meta window, combination " + bits);
        }
        var deliveryOnly = new PostGameFarewellState { completed = true, deliveryAttempted = true, deliveryStarted = true };
        Check(StoryAchievementProgress.GetFarewellReached(deliveryOnly) == 0,
            "Desktop letter delivery without showing the Meta window is not SecretGiftReceived");
        var legacy = JsonUtility.FromJson<PostGameFarewellState>(
            "{\"version\":1,\"completed\":true,\"completedRunId\":\"previous-run\",\"windowDismissed\":true}");
        Check(!legacy.windowShown && StoryAchievementProgress.GetFarewellReached(legacy) == 11,
            "Legacy schema 1 dismissed receipts still earn the gift without a windowShown field");
        var committed = new PostGameFarewellState { completed = true, completedRunId = "previous-run", windowShown = true };
        Check(!committed.windowDismissed && StoryAchievementProgress.GetFarewellReached(committed) == 11,
            "Successfully showing the Meta window earns the gift before it is closed");
        var restored = JsonUtility.FromJson<PostGameFarewellState>(JsonUtility.ToJson(committed));
        Check(restored.version == 1 && restored.windowShown && !restored.windowDismissed
            && StoryAchievementProgress.GetFarewellReached(restored) == 11,
            "The shown but unclosed lifecycle receipt preserves SecretGiftReceived across restart in schema 1");
        var preferences = new GameSaveData(); preferences.EnsureData();
        var fake = new FakeBackend { Ready = false }; var sync = new SteamAchievementSync(fake);
        int observed = Math.Max(StoryAchievementProgress.GetReached(preferences), StoryAchievementProgress.GetFarewellReached(restored));
        sync.Tick(observed, 0);
        Check(StoryAchievementProgress.GetReached(preferences) == 0 && fake.Unlocked.Count == 0,
            "An offline Boot receipt does not require mutating preferences-only SaveData");
        fake.Ready = true; sync.Tick(observed, 30);
        Check(fake.Unlocked.SequenceEqual(ids) && fake.StoreCalls == 1,
            "A restored shown Meta receipt catches up all eleven when Steam returns");
        sync.OnStatsStored(true, 31); sync.Tick(observed, 100);
        Check(fake.Unlocked.Count == 11 && fake.StoreCalls == 1, "Secret gift catch-up settles without duplicate writes");
    }

    static void ReleasePolicy()
    {
        for (int bits = 0; bits < 32; bits++)
        {
            bool allowed = SteamAchievementService.CanSubmit((bits & 1) != 0, (bits & 2) != 0,
                (bits & 4) != 0, (bits & 8) != 0, (bits & 16) != 0);
            Check(allowed == (bits == 0), "Steam submission gate combination " + bits);
        }
    }

    static void CatchUp()
    {
        for (int milestone = 0; milestone <= ids.Length; milestone++)
        {
            var fake = new FakeBackend(); var sync = new SteamAchievementSync(fake);
            sync.Tick(milestone, 0);
            Check(fake.Unlocked.SequenceEqual(ids.Take(milestone)), "Linear catch-up unlocks exactly 1.." + milestone);
            Check(fake.StoreCalls == (milestone == 0 ? 0 : 1), "Catch-up uses one batch for milestone " + milestone);
            if (milestone > 0) sync.OnStatsStored(true, 1);
            int reads = fake.Reads.Count;
            sync.Tick(milestone, 100);
            Check(fake.Unlocked.Count == milestone && fake.StoreCalls == (milestone == 0 ? 0 : 1),
                "Confirmed catch-up is idempotent for milestone " + milestone);
            Check(fake.Reads.Count == reads, "Confirmed account cache avoids redundant reads for " + milestone);
        }
        var clamp = new FakeBackend(); var bounded = new SteamAchievementSync(clamp);
        bounded.Tick(99, 0);
        Check(clamp.Unlocked.SequenceEqual(ids), "Even excessive desired progress cannot request future Steam IDs");
        bounded.OnStatsStored(true, 1);
        bounded.Tick(-9, 100);
        Check(clamp.StoreCalls == 1 && clamp.Unlocked.Count == ids.Length, "Lower/invalid progress never revokes earned achievements");
    }

    static void ExistingRemote()
    {
        var fake = new FakeBackend();
        fake.Achieved.UnionWith(ids.Take(3));
        var sync = new SteamAchievementSync(fake);
        sync.Tick(ids.Length, 0);
        Check(fake.Unlocked.SequenceEqual(ids.Skip(3)), "Already unlocked Steam achievements are left intact");
        sync.OnStatsStored(true, 1); sync.Tick(ids.Length, 30);
        Check(fake.StoreCalls == 1, "Mixed existing/new catch-up settles after one success callback");
        var complete = new FakeBackend(); complete.Achieved.UnionWith(ids);
        new SteamAchievementSync(complete).Tick(ids.Length, 0);
        Check(complete.Unlocked.Count == 0 && complete.StoreCalls == 0,
            "Fully completed Steam account needs no writes");
    }

    static void Unavailable()
    {
        var fake = new FakeBackend { Ready = false }; var sync = new SteamAchievementSync(fake);
        sync.Tick(ids.Length, 0);
        Check(fake.Reads.Count == 0 && fake.Unlocked.Count == 0 && fake.StoreCalls == 0,
            "Offline backend is never called");
        fake.Ready = true; sync.Tick(ids.Length, 100);
        Check(fake.Unlocked.SequenceEqual(ids), "Coming online catches up all saved achievements");
        var zero = new FakeBackend { AccountId = 0 }; var zeroSync = new SteamAchievementSync(zero);
        zeroSync.Tick(ids.Length, 0);
        Check(zero.Reads.Count == 0 && zero.StoreCalls == 0, "An absent Steam account cannot receive progress");
        zero.AccountId = 2; zeroSync.Tick(ids.Length, 100);
        Check(zero.Unlocked.SequenceEqual(ids), "Account becomes available without losing desired progress");
    }

    static void ReadWriteRetries()
    {
        var read = new FakeBackend { ReadsSucceed = false }; var readSync = new SteamAchievementSync(read);
        readSync.Tick(1, 0);
        Check(read.Unlocked.Count == 0 && read.StoreCalls == 0, "Failed GetAchievement is not treated as a locked achievement");
        int attempts = read.Reads.Count; readSync.Tick(1, 14);
        Check(read.Reads.Count == attempts, "Failed reads respect the retry interval");
        read.ReadsSucceed = true; readSync.Tick(1, 16);
        Check(read.Unlocked.SequenceEqual(ids.Take(1)) && read.StoreCalls == 1, "Failed read retries and then catches up");
        var write = new FakeBackend { UnlocksSucceed = false }; var writeSync = new SteamAchievementSync(write);
        writeSync.Tick(1, 0);
        Check(write.Unlocked.Count == 0 && write.StoreCalls == 0, "Rejected SetAchievement is not submitted as success");
        int writes = write.UnlockAttempts.Count; writeSync.Tick(1, 14);
        Check(write.UnlockAttempts.Count == writes, "Failed writes respect the retry interval");
        write.UnlocksSucceed = true; writeSync.Tick(1, 16);
        Check(write.Unlocked.SequenceEqual(ids.Take(1)) && write.StoreCalls == 1, "Failed unlock retries successfully");
    }

    static void StoreReturnFailure()
    {
        var fake = new FakeBackend { StoresSucceed = false }; var sync = new SteamAchievementSync(fake);
        sync.Tick(1, 0);
        Check(fake.Achieved.Contains(ids[0]) && fake.StoreCalls == 1 && !sync.IsWaitingForStore,
            "A failed StoreStats leaves SetAchievement visible only in the fake local cache");
        sync.Tick(1, 14); Check(fake.StoreCalls == 1, "Rejected store does not spin before retry interval");
        fake.StoresSucceed = true; sync.Tick(1, 16);
        Check(fake.StoreCalls == 2 && sync.IsWaitingForStore,
            "Store failure retries the dirty achievement even when GetAchievement now returns true");
        sync.OnStatsStored(true, 17); sync.Tick(1, 100);
        Check(fake.StoreCalls == 2 && !sync.IsWaitingForStore, "Successful retried store clears the transaction");
    }

    static void PartialBatchRetry()
    {
        var fake = new FakeBackend(); fake.Unreadable.Add(ids[1]); fake.Unwritable.Add(ids[3]);
        var sync = new SteamAchievementSync(fake);
        sync.Tick(4, 0);
        Check(fake.Unlocked.SequenceEqual(new[] { ids[0], ids[2] }) && fake.StoreCalls == 1,
            "A partial Steam batch stores successful independent achievements");
        sync.OnStatsStored(true, 1);
        fake.Unreadable.Clear(); fake.Unwritable.Clear();
        sync.Tick(4, 20);
        Check(fake.Unlocked.SequenceEqual(new[] { ids[0], ids[2], ids[1], ids[3] }) && fake.StoreCalls == 2,
            "Successful callback does not erase failed reads or unlocks from a partial batch");
        sync.OnStatsStored(true, 21); sync.Tick(4, 100);
        Check(fake.StoreCalls == 2 && fake.Unlocked.Count == 4, "Partial batch recovery settles without duplicate writes");
    }

    static void StoreCallbackFailure()
    {
        var fake = new FakeBackend(); var sync = new SteamAchievementSync(fake);
        sync.Tick(1, 0); sync.OnStatsStored(false, 1);
        sync.Tick(1, 15); Check(fake.StoreCalls == 1, "Rejected callback waits before resubmitting");
        sync.Tick(1, 17);
        Check(fake.StoreCalls == 2 && sync.IsWaitingForStore,
            "Failed callback retries cached local unlock rather than accepting it as server confirmation");
        sync.OnStatsStored(true, 18); sync.Tick(1, 100);
        Check(fake.StoreCalls == 2 && !sync.IsWaitingForStore, "Callback retry eventually settles exactly once");
    }

    static void StoreCallbackTimeout()
    {
        var fake = new FakeBackend(); var sync = new SteamAchievementSync(fake);
        sync.Tick(1, 0); sync.Tick(1, 59);
        Check(fake.StoreCalls == 1 && sync.IsWaitingForStore, "A live callback window prevents parallel stores");
        sync.Tick(1, 61); sync.Tick(1, 77);
        Check(fake.StoreCalls == 2 && sync.IsWaitingForStore, "A missing callback times out and retries the unconfirmed store");
        sync.OnStatsStored(true, 78); sync.Tick(1, 200);
        Check(fake.StoreCalls == 2, "Recovered timeout remains idempotent");
    }

    static void ServerRollback()
    {
        var fake = new FakeBackend(); var sync = new SteamAchievementSync(fake);
        sync.Tick(1, 0); sync.OnStatsStored(false, 1);
        // Steam can roll its local stats cache back after a rejected store.
        fake.Achieved.Remove(ids[0]); fake.Unwritable.Add(ids[0]);
        sync.Tick(2, 16);
        Check(fake.Unlocked.SequenceEqual(new[] { ids[0], ids[1] }) && fake.StoreCalls == 2,
            "Another valid unlock can be stored while a rolled-back achievement rejects restaging");
        sync.OnStatsStored(true, 17);
        sync.Tick(2, 18);
        Check(!fake.Achieved.Contains(ids[0]) && fake.StoreCalls == 2,
            "Successful partial-store callback does not confirm an old pending bit that failed restaging");
        fake.Unwritable.Clear(); sync.Tick(2, 34);
        Check(fake.Unlocked.SequenceEqual(new[] { ids[0], ids[1], ids[0] })
            && fake.StoreCalls == 3 && sync.IsWaitingForStore,
            "Rolled-back achievement is Set and stored again when restaging becomes available");
        sync.OnStatsStored(true, 35); sync.Tick(2, 100);
        Check(fake.StoreCalls == 3 && !sync.IsWaitingForStore,
            "Restored achievement only settles after its own successful transaction");
    }

    static void LateStoreCallbacks()
    {
        var fake = new FakeBackend(); var sync = new SteamAchievementSync(fake);
        sync.Tick(1, 0);
        sync.Tick(2, 61);
        Check(fake.StoreCalls == 2 && fake.Unlocked.SequenceEqual(ids.Take(2)) && sync.IsWaitingForStore,
            "After store A times out, store B contains old and newly reached milestones");
        // Steam callbacks have no request identifier: late A success appears to confirm B.
        sync.OnStatsStored(true, 62);
        sync.OnStatsStored(false, 63);
        sync.Tick(2, 77);
        Check(fake.StoreCalls == 2 && !sync.IsWaitingForStore,
            "Late failure after an apparent success schedules retry without spinning");
        sync.Tick(2, 79);
        Check(fake.StoreCalls == 3 && sync.IsWaitingForStore,
            "B failure arriving after late A success still resubmits all this session's writes");
        sync.OnStatsStored(true, 80); sync.Tick(2, 100);
        Check(fake.StoreCalls == 3 && !sync.IsWaitingForStore,
            "Delayed-callback recovery eventually settles after its successful retry");

        var settled = new FakeBackend(); settled.Achieved.Add(ids[0]);
        var settledSync = new SteamAchievementSync(settled);
        settledSync.Tick(3, 0); settledSync.OnStatsStored(true, 1);
        int reads = settled.Reads.Count;
        settledSync.OnStatsStored(false, 2);
        settledSync.Tick(3, 16);
        Check(settled.StoreCalls == 1, "Failure after settled success respects the fifteen-second retry interval");
        settledSync.Tick(3, 18);
        Check(settled.StoreCalls == 2 && settledSync.IsWaitingForStore
            && settled.Reads.Skip(reads).SequenceEqual(ids.Skip(1).Take(2)),
            "Failure after settled success rechecks session writes while preserving pre-existing remote confirmations");
        settledSync.OnStatsStored(true, 19); settledSync.Tick(3, 100);
        Check(settled.StoreCalls == 2 && settled.Unlocked.SequenceEqual(ids.Skip(1).Take(2)),
            "Late failure recovery stores cached writes without duplicate SetAchievement calls");
    }

    static void AccountIsolation()
    {
        var fake = new FakeBackend(); var sync = new SteamAchievementSync(fake);
        sync.Tick(3, 0); sync.OnStatsStored(true, 1);
        fake.AccountId = 2;
        sync.Tick(3, 2);
        Check(fake.Unlocked.Count == 6 && fake.Reads.Count == 6 && fake.StoreCalls == 2,
            "Changing Steam accounts clears confirmation state and reads/unlocks the new account");
        sync.OnStatsStored(true, 3);
        fake.AccountId = 1; sync.Tick(3, 4);
        Check(fake.Reads.Count == 9 && fake.Unlocked.Count == 6 && fake.StoreCalls == 2,
            "Returning to a previous account rereads its own existing achievements without duplicate writes");
        fake.Ready = false; sync.Tick(ids.Length, 10);
        fake.Ready = true; sync.Tick(ids.Length, 100);
        Check(fake.Unlocked.Count == ids.Length + 3 && fake.Unlocked.Skip(6).SequenceEqual(ids.Skip(3)),
            "Later offline progress resumes on the currently active account");
    }

    static void ProgressDuringStore()
    {
        var fake = new FakeBackend(); var sync = new SteamAchievementSync(fake);
        sync.Tick(1, 0); sync.Tick(ids.Length, 1);
        Check(fake.StoreCalls == 1 && fake.Unlocked.Count == 1, "New story progress waits while a store is in flight");
        sync.OnStatsStored(true, 2); sync.Tick(ids.Length, 3);
        Check(fake.StoreCalls == 2 && fake.Unlocked.SequenceEqual(ids), "Higher progress is submitted after the first callback");
        sync.OnStatsStored(true, 4); sync.Tick(2, 50);
        Check(fake.StoreCalls == 2 && fake.Unlocked.Count == ids.Length, "Loading an earlier checkpoint never revokes server progress");
    }

    static void OfflineTransaction()
    {
        var fake = new FakeBackend(); var sync = new SteamAchievementSync(fake);
        sync.Tick(2, 0); fake.Ready = false;
        sync.OnStatsStored(true, 1); sync.Tick(2, 100);
        Check(fake.StoreCalls == 1, "Disconnected callbacks do not confirm pending writes");
        fake.Ready = true; sync.Tick(2, 101);
        Check(fake.StoreCalls == 2 && sync.IsWaitingForStore,
            "Reconnecting after callback timeout resubmits pending local unlocks");
        sync.OnStatsStored(true, 102); sync.Tick(2, 200);
        Check(fake.StoreCalls == 2, "Recovered offline transaction becomes stable after confirmation");

        var changed = new FakeBackend(); var changedSync = new SteamAchievementSync(changed);
        changedSync.Tick(1, 0); changed.AccountId = 2;
        changedSync.OnStatsStored(true, 1); changedSync.Tick(1, 2);
        Check(changed.StoreCalls == 2 && changed.Unlocked.Count == 2,
            "A callback after account change cannot consume the previous account's pending transaction");
    }

    sealed class FakeBackend : ISteamAchievementBackend
    {
        public bool Ready { get; set; } = true;
        public ulong AccountId { get; set; } = 1;
        public bool ReadsSucceed = true, UnlocksSucceed = true, StoresSucceed = true;
        public readonly List<string> Reads = new List<string>();
        public readonly List<string> UnlockAttempts = new List<string>();
        public readonly List<string> Unlocked = new List<string>();
        public readonly HashSet<string> Unreadable = new HashSet<string>();
        public readonly HashSet<string> Unwritable = new HashSet<string>();
        public int StoreCalls;
        readonly Dictionary<ulong, HashSet<string>> accounts = new Dictionary<ulong, HashSet<string>>();
        public HashSet<string> Achieved
        {
            get
            {
                if (!accounts.TryGetValue(AccountId, out var set))
                    accounts.Add(AccountId, set = new HashSet<string>(StringComparer.Ordinal));
                return set;
            }
        }
        public bool TryGetAchievement(string id, out bool achieved)
        { Reads.Add(id); achieved = Achieved.Contains(id); return ReadsSucceed && !Unreadable.Contains(id); }
        public bool UnlockAchievement(string id)
        {
            UnlockAttempts.Add(id);
            if (!UnlocksSucceed || Unwritable.Contains(id)) return false;
            Achieved.Add(id); Unlocked.Add(id); return true;
        }
        public bool StoreStats() { StoreCalls++; return StoresSucceed; }
    }
}
