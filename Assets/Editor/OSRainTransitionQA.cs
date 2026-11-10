#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections;
using System.Collections.Generic;
using MrTGame.Core;
using UnityEngine;
using UnityEngine.SceneManagement;

// Manual PlayMode QA: a separate persistent MonoBehaviour hosts
// StartCoroutine(OSRainTransitionQA_Draft.Run()). Do not host it on GameTest.
// Scene reloads are intentional. SaveData is restored in finally; presentation
// remains at the test location. Stop Play afterwards. Test-session protection
// deliberately remains enabled, so neither completion nor quitting saves QA state.
public static class OSRainTransitionQA_Draft
{
    public static IEnumerator Run()
    {
        var data = GameDataManager.Instance;
        var saves = GameAutosaveService.Instance;
        var audio = AudioManager.Instance;
        var manager = GameManager.Instance;
        var loader = SceneLoadManager.Instance;
        var test = GameTest.Instance;
        Check(Application.isPlaying && data != null && saves != null && audio != null
            && manager != null && loader != null && test != null, "PlayMode managers missing");
        Check(!manager.IsTransitioning && !loader.IsBusy && !saves.IsBusy
            && !test.IsCheckpointJumpRunning, "Wait until loading/debug jumps finish");
        AudioClip rain = data.AudioData?.AMBIENT?.Find(x => x != null && x.keyid == "Rain")?.audioclip;
        Check(rain != null && data.TryGetOSMemoryData(1260, out _), "Rain / memory 1260 missing");
        Check(audio.AmbientSource_00 != null && audio.AmbientSource_01 != null
            && audio.MusicSource_00 != null && audio.MusicSource_01 != null, "Audio channels missing");

        // Keep an independent backup: unloading controllers can mutate old object references.
        var backup = JsonUtility.FromJson<GameSaveData>(JsonUtility.ToJson(data.SaveData));
        var fixture = JsonUtility.FromJson<GameSaveData>(JsonUtility.ToJson(backup));
        var pausedPlayers = new List<OSGramophonePlayer>();
        AudioClip other = null, music = null;
        IDisposable writeScope = null;
        UnityEngine.Events.UnityAction<Scene, LoadSceneMode> pauseCompetingAudio = (_, __) =>
        {
            // Its first chapter-01 Update calls StopAmbient(.65), which would mask
            // a broken OS transition. Disable before the new scene's first Update.
            var player = OSGramophonePlayer.Instance;
            if (player == null || !player.enabled) return;
            pausedPlayers.Add(player);
            player.enabled = false;
        };

        saves.EnterTestSession();
        try
        {
            writeScope = saves.SuspendWrites();
            SceneManager.sceneLoaded += pauseCompetingAudio;
            pauseCompetingAudio(default(Scene), LoadSceneMode.Additive);
            data.ReplaceSaveData(fixture);
            other = AudioClip.Create("QA_OtherAmbient", 44100 * 4, 1, 44100, false);
            music = AudioClip.Create("QA_Music", 44100 * 4, 1, 44100, false);

            manager.DebugSwitchPhase(GamePhase.Prologue);
            float deadline = Time.realtimeSinceStartup + 40f;
            while ((manager.IsTransitioning || loader.IsBusy) && Time.realtimeSinceStartup < deadline)
                yield return null;
            Check(!manager.IsTransitioning && !loader.IsBusy && manager.Phase == GamePhase.Prologue,
                "Prologue preparation timed out");
            yield return null;
            audio.PlayAmbient(rain, 0f);
            Check(PlayingAmbient(audio, rain), "Real Rain did not start before checkpoint");

            // 1. Ordinary console API; never use Chapter01EndingAnimation here.
            Check(test.JumpToCheckpoint(GameTestCheckpoint.Chapter01BeforeToolUnlock), "Checkpoint rejected");
            deadline = Time.realtimeSinceStartup + 45f;
            while ((test.IsCheckpointJumpRunning || manager.IsTransitioning || loader.IsBusy)
                && Time.realtimeSinceStartup < deadline) yield return null;
            Check(!test.IsCheckpointJumpRunning && !manager.IsTransitioning && !loader.IsBusy,
                "Checkpoint timed out: " + test.JumpStatus);
            yield return new WaitForSecondsRealtime(.35f);
            Check(manager.Phase == GamePhase.OSMain && OS_Handle.Instance?.IsOSVisible == true
                && data.CurrentOSMemoryDataIndex == 1260, "Checkpoint did not reach OS memory 1260");
            Check(OS_Handle.Instance.Chapter01Ending?.IsPresenting != true, "Ending would mask rain failure");
            Check(!PlayingAmbient(audio, rain), "FAIL 1: checkpoint left Rain playing");
            Debug.Log("[OSRainQA] PASS 1: ordinary chapter-01 checkpoint releases Rain");

            // 2. Already-visible OS skips phase switching; its memory jump must clean up too.
            audio.PlayAmbient(rain, 0f);
            Check(PlayingAmbient(audio, rain), "Rain did not restart for memory jump");
            test.JumpToMemoryIndex(1260);
            deadline = Time.realtimeSinceStartup + 3f;
            do { yield return null; }
            while (PlayingAmbient(audio, rain) && Time.realtimeSinceStartup < deadline);
            Check(!PlayingAmbient(audio, rain) && manager.Phase == GamePhase.OSMain,
                "FAIL 2: already-visible OS memory jump left Rain playing");
            Debug.Log("[OSRainQA] PASS 2: repeated memory jump releases Rain");

            // 3. Rain is the outgoing source; the new ambience and music retain ownership.
            var main = MainGameHandle.Instance;
            Check(main != null, "Checkpoint did not retain Game_Main");
            audio.PlayAmbient(rain, 0f);
            audio.PlayAmbient(other, 1f);
            audio.PlayMusic(music, 0f);
            yield return new WaitForSecondsRealtime(.05f);
            Check(PlayingAmbient(audio, rain) && PlayingAmbient(audio, other), "Crossfade fixture incomplete");
            AudioSource musicSource = audio.CurrentMusicSource;
            Check(musicSource != null && musicSource.isPlaying && musicSource.clip == music, "Music fixture failed");
            float musicTime = musicSource.time;
            main.SuspendSceneAmbientForOS();
            yield return new WaitForSecondsRealtime(.35f);
            Check(!PlayingAmbient(audio, rain) && PlayingAmbient(audio, other), "FAIL 3: wrong ambient stopped");
            Check(audio.CurrentMusicSource == musicSource && musicSource.isPlaying
                && musicSource.clip == music && musicSource.time > musicTime, "FAIL 3: music was interrupted");
            Debug.Log("[OSRainQA] PASS 3: non-Rain ambience and Music continue across cleanup");

            // 4. The normal return API must restart rain, and repeated cleanup must still work.
            main.RestoreSceneAmbientAfterOS(.1f);
            yield return new WaitForSecondsRealtime(.2f);
            Check(PlayingAmbient(audio, rain), "FAIL 4: normal return did not restore Rain");
            main.SuspendSceneAmbientForOS();
            main.SuspendSceneAmbientForOS();
            yield return new WaitForSecondsRealtime(.35f);
            Check(!PlayingAmbient(audio, rain), "FAIL 4: repeated suspension left Rain playing");
            Debug.Log("[OSRainQA] PASS 4: normal return restores Rain; repeated suspension stops it");
            Check(saves.IsTestSession, "QA save isolation unexpectedly ended");
            Debug.Log("[OSRainQA] ALL FOUR PASSED");
        }
        finally
        {
            SceneManager.sceneLoaded -= pauseCompetingAudio;
            // Cancel our debug operations if a timeout/assertion interrupted the run.
            if (test != null) test.StopAllCoroutines();
            if (audio != null)
            {
                audio.StopAmbientClip(rain, 0f);
                audio.StopAmbientClip(other, 0f);
                if (audio.CurrentMusicSource != null && audio.CurrentMusicSource.clip == music)
                    audio.StopMusic(0f);
            }
            if (data != null) data.ReplaceSaveData(backup);
            foreach (var player in pausedPlayers) if (player != null) player.enabled = true;
            if (other != null) UnityEngine.Object.Destroy(other);
            if (music != null) UnityEngine.Object.Destroy(music);
            writeScope?.Dispose();
            Debug.Log("[OSRainQA] Cloned memory SaveData restored; test-session protection retained. Stop Play.");
        }
    }

    static bool PlayingAmbient(AudioManager audio, AudioClip clip) => clip != null
        && (Playing(audio.AmbientSource_00, clip) || Playing(audio.AmbientSource_01, clip));
    static bool Playing(AudioSource source, AudioClip clip) => source != null
        && source.isPlaying && source.clip == clip;
    static void Check(bool passed, string message)
    {
        if (!passed) throw new InvalidOperationException("[OSRainQA] " + message);
    }
}
#endif
