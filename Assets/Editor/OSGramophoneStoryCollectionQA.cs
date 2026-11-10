// Opt-in, short runtime regression for story music collection and audio ownership.
public static class OSGramophoneStoryCollectionQA
{
    public static string Start()
    {
        // Execute once in initialized full-game Play Mode at Boot. Uses actual audio
        // sources/DSP and the production checkpoint launcher; never waits for a full song.
        // Finishes at Boot with the original save object and all original save files intact.
        var data = GameDataManager.Instance;
        var saves = GameAutosaveService.Instance;
        var manager = MrTGame.Core.GameManager.Instance;
        if (!UnityEngine.Application.isPlaying || data == null || saves == null || manager == null
            || GameTest.Instance == null || GameSetup.IsDemo || manager.IsTransitioning
            || manager.Phase != MrTGame.Core.GamePhase.Boot)
            return "Initialized full-game Boot Play Mode required";
        
        var original = data.SaveData;
        var writes = saves.SuspendWrites();
        var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic;
        var restoreSetter = typeof(OSSessionPersistence).GetProperty("IsRestoring").GetSetMethod(true);
        var checks = new System.Collections.Generic.List<string>();
        var failures = new System.Collections.Generic.List<string>();
        var output = System.IO.Path.GetFullPath("Tools/Gramophone/Validation/StoryCollection_1103/runtime-hooks-results.json");
        var saveRoot = System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(saves.SavePath));
        var startedAt = UnityEngine.Time.realtimeSinceStartup;
        OSChapter04FinalHealAudioSession session = null;
        OSChapter03StoryDirector ch3 = null;
        OSChapter04Music ch4 = null;
        bool oldBackground = UnityEngine.Application.runInBackground;
        string context = "prepare";
        
        System.Collections.Generic.Dictionary<string,string> HashSaves()
        {
            var result = new System.Collections.Generic.Dictionary<string,string>();
            if (!System.IO.Directory.Exists(saveRoot)) return result;
            foreach (string path in System.IO.Directory.GetFiles(saveRoot, "*", System.IO.SearchOption.AllDirectories))
                using (var sha = System.Security.Cryptography.SHA256.Create())
                    result[path] = System.BitConverter.ToString(sha.ComputeHash(System.IO.File.ReadAllBytes(path)));
            return result;
        }
        var before = HashSaves();
        void Check(bool ok, string label)
        {
            if (!ok) throw new System.InvalidOperationException(context + ": " + label);
            checks.Add(context + ": " + label);
        }
        object Call(object target, string method, params object[] args)
            => target.GetType().GetMethod(method, flags).Invoke(target, args);
        bool Heard(string id) => data.OSGramophoneState.collectedTrackIds.Contains(id);
        int StoryHeardCount() => data.OSGramophoneState.collectedTrackIds.FindAll(id =>
            id == OSGramophonePlayer.YardSearchTrack || id == OSGramophonePlayer.YardResolveTrack
            || id == OSGramophonePlayer.BreakdownTrack || id == OSGramophonePlayer.Chapter04ThemeTrack
            || id == OSGramophonePlayer.FinalHealBattleTrack || id == OSGramophonePlayer.FinalHealCalmTrack).Count;
        void FreshCollection()
        {
            data.OSGramophoneState.collectedTrackIds.Clear();
            data.OSGramophoneState.storyCollectionVersion = 1;
        }
        System.Collections.IEnumerator Hold(float seconds)
        {
            float until = UnityEngine.Time.realtimeSinceStartup + seconds;
            while (UnityEngine.Time.realtimeSinceStartup < until) yield return null;
        }
        System.Collections.IEnumerator Await(System.Func<bool> ready, string label, float timeout = 8f)
        {
            float until = UnityEngine.Time.realtimeSinceStartup + timeout;
            while (!ready())
            {
                if (UnityEngine.Time.realtimeSinceStartup >= until) throw new System.TimeoutException(context + ": " + label);
                yield return null;
            }
            Check(true, label);
        }
        OSChapter04FinalHealAudioSession NewFinalSession()
        {
            if (session != null) session.DisposeNow();
            UnityEngine.AudioClip Load(string name) => UnityEngine.Resources.Load<UnityEngine.AudioClip>("Chapter04/FinalHeal/Audio/" + name);
            session = OSChapter04FinalHealAudioSession.Create(data, Load("FinalHeal_Battle"), Load("FinalHeal_MamaWait"),
                Load("FinalHeal_Bloom"), Load("FinalHeal_CalmLoop"), .75d, 111d, 5.25d);
            return session;
        }
        void AssertManualBlocked(string label)
        {
            var p = OSGramophonePlayer.Instance;
            string selected = data.OSGramophoneState.selectedTrackId;
            bool wants = data.OSGramophoneState.wantsPlayback;
            p.PlayTrack(OSGramophonePlayer.Secret); p.TogglePlayback(); p.NextTrack(1);
            Check(data.OSGramophoneState.selectedTrackId == selected && data.OSGramophoneState.wantsPlayback == wants,
                label + " leaves selection/play intent intact");
            Check(AudioManager.Instance.CurrentMusicSource == null || !AudioManager.Instance.CurrentMusicSource.isPlaying,
                label + " cannot restart shared music");
        }
        
        System.Collections.IEnumerator Run()
        {
            saves.EnterTestSession();
            data.ReplaceSaveData(UnityEngine.JsonUtility.FromJson<GameSaveData>(UnityEngine.JsonUtility.ToJson(original)));
            UnityEngine.Application.runInBackground = true;
            context = "chapter3 fixture";
            Check(GameTest.Instance.JumpToCheckpoint(GameTestCheckpoint.Chapter03Bedroom), "bedroom checkpoint accepted");
            yield return Await(() => {
                ch3 = OS_Handle.Instance?.Chapter03Story;
                ch3?.DebugSetFocus(true);
                GameTest.Instance.GetComponent<GameTestConsole>()?.SetOpen(false);
                return !GameTest.Instance.IsCheckpointJumpRunning && !manager.IsTransitioning && ch3?.IsAvailable == true;
            }, "actual Chapter 3 home is available", 60f);
            FreshCollection();
            var state3 = ch3.State;
            state3.memoryQueue.Clear(); state3.liveQueue.Clear();
            OSGramophonePlayer.Instance.StopPlayback();
            yield return Hold(.2f);
        
            context = "chapter3 silent lease";
            state3.phase = (int)OSChapter03StoryDirector.Phase.Mirror;
            state3.Add("mirror_done"); state3.Add("breakdown_music_fading");
            Check(ch3.OwnsGramophoneAudio, "first-frame Mirror ownership is visible before Update");
            yield return Hold(.2f);
            Check(AudioManager.Instance.IsBackgroundAudioSuppressed, "Mirror silence holds a real audio lease");
            AssertManualBlocked("Play/Toggle/Next during Mirror silence");
            state3.phase = (int)OSChapter03StoryDirector.Phase.KeyReady;
            Call(ch3, "StopMusicNow");
            yield return Hold(.1f);
            Check(!ch3.OwnsGramophoneAudio && !AudioManager.Instance.IsBackgroundAudioSuppressed,
                "KeyReady without tails releases the lease");
            OSGramophonePlayer.Instance.PlayTrack(OSGramophonePlayer.Secret);
            yield return Await(() => OSGramophonePlayer.Instance.IsPlaying, "ordinary exploration permits manual playback");
            OSGramophonePlayer.Instance.StopPlayback();
            yield return Hold(.2f);
        
            context = "chapter3 actual YardSearch";
            state3.flags.Remove("yard_returned");
            state3.phase = (int)OSChapter03StoryDirector.Phase.Yard;
            yield return Await(() => Heard(OSGramophonePlayer.YardSearchTrack), "actual search source records YardSearch");
            Check(ch3.OwnsGramophoneAudio, "Yard holds ownership");
            Call(ch3, "StopMusicNow");
        
            context = "chapter3 actual YardResolve";
            state3.phase = (int)OSChapter03StoryDirector.Phase.YardReturn;
            Call(ch3, "PlayMusicCue", "yard_resolve");
            yield return Await(() => Heard(OSGramophonePlayer.YardResolveTrack), "actual one-shot source records YardResolve");
            state3.phase = (int)OSChapter03StoryDirector.Phase.KeyReady;
            Check(ch3.OwnsGramophoneAudio, "resolve tail keeps its lease after the cinematic phase");
            Call(ch3, "StopMusicNow");
        
            context = "chapter3 actual Breakdown prelude";
            state3.phase = (int)OSChapter03StoryDirector.Phase.Mirror;
            state3.flags.Remove("mirror_done"); state3.flags.Remove("breakdown_music_fading");
            state3.flags.Remove("breakdown_music"); state3.flags.Remove("breakdown_quiet");
            state3.Add("paper_requested_note_09");
            yield return Await(() => Heard(OSGramophonePlayer.BreakdownTrack), "real prelude records the complete Breakdown track");
            Check(StoryHeardCount() == 3, "only the three heard Chapter 3 records are collected");
            state3.phase = (int)OSChapter03StoryDirector.Phase.Offline;
            Call(ch3, "StopMusicNow");
            yield return Hold(.1f);
            Check(!ch3.OwnsGramophoneAudio && !AudioManager.Instance.IsBackgroundAudioSuppressed,
                "ordinary Offline exploration releases the lease");
        
            context = "Battle scheduled pause and restore";
            NewFinalSession().StartBattle();
            Check(!Heard(OSGramophonePlayer.FinalHealBattleTrack), "scheduling Battle does not collect it");
            session.SetPaused(true);
            yield return Hold(.18f);
            Check(!Heard(OSGramophonePlayer.FinalHealBattleTrack) && !session.BattleSource.isPlaying,
                "pausing before DSP prevents collection and playback");
            restoreSetter.Invoke(null, new object[] { true });
            session.SetPaused(false);
            yield return Await(() => session.BattleSource.isPlaying && session.BattleSource.timeSamples > 0,
                "Battle actually reaches its rescheduled DSP start");
            yield return Hold(.1f);
            Check(!Heard(OSGramophonePlayer.FinalHealBattleTrack), "restore suppression does not collect a running source");
            Check(!(bool)session.GetType().GetField("battleCollected", flags).GetValue(session),
                "restore suppression retains the pending collection latch");
            restoreSetter.Invoke(null, new object[] { false });
            yield return Await(() => Heard(OSGramophonePlayer.FinalHealBattleTrack), "Battle collects after restoration finishes");
            session.DisposeNow(); session = null;
            yield return null;
        
            context = "CalmLoop ending-only start";
            NewFinalSession().StartCalmLoop();
            Check(!Heard(OSGramophonePlayer.FinalHealCalmTrack), "scheduling CalmLoop does not collect it");
            session.SetPaused(true);
            yield return Hold(.1f);
            Check(!Heard(OSGramophonePlayer.FinalHealCalmTrack), "paused CalmLoop stays uncollected");
            session.SetPaused(false);
            yield return Await(() => Heard(OSGramophonePlayer.FinalHealCalmTrack), "real calm source collects on resume");
            Check(!session.BattleSource.isPlaying && session.BattleSource.timeSamples == 0,
                "ending-only start does not play Battle");
            Check(StoryHeardCount() == 5, "only the five actual music hooks have fired");
            session.DisposeNow(); session = null;
        
            context = "chapter4 actual Theme";
            Check(GameTest.Instance.JumpToCheckpoint(GameTestCheckpoint.Chapter04StoryStart), "TV checkpoint accepted");
            yield return Await(() => {
                var story = OS_Handle.Instance?.Chapter04Story;
                story?.DebugSetFocus(true); OS_Handle.Instance?.Chapter04Entry?.DebugSetFocus(true);
                GameTest.Instance.GetComponent<GameTestConsole>()?.SetOpen(false);
                ch4 = OS_Handle.Instance?.GetComponent<OSChapter04Music>();
                return !GameTest.Instance.IsCheckpointJumpRunning && !manager.IsTransitioning
                    && story?.IsAvailable == true && ch4 != null && !ch4.IsPaused
                    && ch4.MelodySource?.isPlaying == true && ch4.AccompanimentSource?.isPlaying == true
                    && Heard(OSGramophonePlayer.Chapter04ThemeTrack);
            }, "both real TV stems start and collect the complete Theme", 60f);
            Check(ch4.OwnsGramophoneAudio, "TV ownership is queryable");
            yield return Hold(.2f);
            AssertManualBlocked("Play/Toggle/Next during TV ownership");
            OS_Handle.Instance.TVWindow.Minimize();
            yield return Await(() => ch4.IsPaused, "actual TV minimize pauses music");
            yield return Hold(.12f);
            int melodySample = ch4.MelodySource.timeSamples;
            yield return Hold(.15f);
            Check(ch4.OwnsGramophoneAudio && !ch4.MelodySource.isPlaying
                && ch4.MelodySource.timeSamples == melodySample, "paused TV keeps ownership and frozen samples");
            AssertManualBlocked("Play/Toggle/Next while TV is paused");
            OS_Handle.Instance.OpenTVWindow();
            yield return Await(() => !ch4.IsPaused && ch4.MelodySource.isPlaying, "real TV restore resumes its audio");
        
            context = "Boot release";
            manager.OpenMainMenuWithoutSave();
            yield return Await(() => manager.Phase == MrTGame.Core.GamePhase.Boot && !manager.IsTransitioning,
                "returns to Boot", 30f);
            yield return Hold(.15f);
            Check(!ch4.OwnsGramophoneAudio && !AudioManager.Instance.IsBackgroundAudioSuppressed,
                "Boot releases all story background leases");
        }
        
        System.Collections.IEnumerator Guarded()
        {
            var stack = new System.Collections.Generic.Stack<System.Collections.IEnumerator>();
            stack.Push(Run());
            try
            {
                while (stack.Count > 0)
                {
                    object next = null; bool failed = false;
                    try
                    {
                        if (!stack.Peek().MoveNext()) { (stack.Pop() as System.IDisposable)?.Dispose(); continue; }
                        next = stack.Peek().Current;
                    }
                    catch (System.Exception error) { failures.Add(error.ToString()); failed = true; }
                    if (failed) break;
                    if (next is System.Collections.IEnumerator child) stack.Push(child); else yield return next;
                }
            }
            finally
            {
                while (stack.Count > 0) (stack.Pop() as System.IDisposable)?.Dispose();
                restoreSetter.Invoke(null, new object[] { false });
                if (session != null) session.DisposeNow();
                ch3?.DebugSetFocus(false);
                OS_Handle.Instance?.Chapter04Story?.DebugSetFocus(false);
                OS_Handle.Instance?.Chapter04Entry?.DebugSetFocus(false);
                if (ch4 != null) Call(ch4, "StopPlayback", false);
                saves.EndSession(); manager.OpenMainMenuWithoutSave(); data.ReplaceSaveData(original);
                UnityEngine.Application.runInBackground = oldBackground;
                var after = HashSaves();
                bool equal = before.Count == after.Count;
                foreach (var pair in before) equal &= after.TryGetValue(pair.Key, out string hash) && hash == pair.Value;
                if (!equal) failures.Add("Original Editor save directory hashes changed");
                else checks.Add("cleanup: original Editor save directory hashes are unchanged");
                writes.Dispose();
                System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(output));
                System.IO.File.WriteAllText(output, Newtonsoft.Json.JsonConvert.SerializeObject(new {
                    status = failures.Count == 0 ? "PASS" : "FAILED", context, checks, failures,
                    elapsedSeconds = UnityEngine.Time.realtimeSinceStartup - startedAt,
                    originalSaveRestored = object.ReferenceEquals(data.SaveData, original), before, after,
                    note = "Real DSP/source hooks; short fixture playback only, no whole-song listening claim. Production checkpoint launcher enters a test session, retained until Play Mode exits."
                }, Newtonsoft.Json.Formatting.Indented));
                UnityEngine.Debug.Log("[Gramophone story hooks] " + (failures.Count == 0 ? "PASS" : "FAILED") + " | " + output);
            }
        }
        data.StartCoroutine(Guarded());
        return "RUNNING: " + output;
        
        
    }
}
