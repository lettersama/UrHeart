using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using MrTGame.Core;
using MrTGame.Systems;
using TMPro;
using UnityEngine;

// Copy into Assets/Editor only after leaving Play Mode. All writes use Temp.
public static class GameAutosaveFlowQA
{
    const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    static readonly List<string> checks = new List<string>();
    public static string Status { get; private set; } = "idle";
    public static string OutputDirectory { get; private set; }
    public static string Start()
    {
        if (!Application.isPlaying || GameAutosaveService.Instance == null || GameSessionFlow.Instance == null)
            return "Play Mode and initialized managers required";
        if (Status == "running" || GameSessionFlow.Instance.IsBusy || GameManager.Instance.IsTransitioning) return "busy";
        checks.Clear();Status = "running";
        GameDataManager.Instance.StartCoroutine(Guarded());
        return Status;
    }
    static IEnumerator Guarded()
    {
        var data = GameDataManager.Instance;
        var service = GameAutosaveService.Instance;
        var original = data.SaveData;
        var language = data.CurrentLanguageState;
        bool background = Application.runInBackground;
        var resources = GameResourceSystem.Instance;
        bool resourceEnabled = resources != null && resources.enabled;
        OutputDirectory = Path.GetFullPath(Path.Combine("Temp", "AutosaveFlowQA", Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(OutputDirectory);
        service.EnterTestSession();service.UseIsolatedStorageForTests(OutputDirectory);
        data.ReplaceSaveData(JsonUtility.FromJson<GameSaveData>(JsonUtility.ToJson(original)));
        Application.runInBackground = true;
        if (resources != null) resources.enabled = false;
        var stack = new Stack<IEnumerator>();stack.Push(Run());
        try
        {
            while (stack.Count > 0)
            {
                object next;
                try
                {
                    if (!stack.Peek().MoveNext()) { (stack.Pop() as IDisposable)?.Dispose();continue; }
                    next = stack.Peek().Current;
                }
                catch (Exception error) { Status = "FAILED: " + error;Debug.LogError(Status);yield break; }
                if (next is IEnumerator nested) stack.Push(nested);else yield return next;
            }
            Status = "PASS: " + checks.Count + " flow/menu checks";
            Debug.Log("[Autosave Flow QA] " + Status);
        }
        finally
        {
            while (stack.Count > 0) (stack.Pop() as IDisposable)?.Dispose();
            service.EnterTestSession();service.EndSession();
            data.ReplaceSaveData(original);data.SetCurrentLanguage(language);
            Application.runInBackground = background;
            if (resources != null) resources.enabled = resourceEnabled;
            File.WriteAllText(Path.Combine(OutputDirectory, "results.txt"), Status + "\n" + string.Join("\n", checks));
        }
    }
    static IEnumerator Run()
    {
        var data = GameDataManager.Instance;var service = GameAutosaveService.Instance;var flow = GameSessionFlow.Instance;
        Check(flow.ReturnToMainMenu(), "initial menu entry");yield return WaitMenu();
        var menu = MainMenuHandle.Instance;menu.InitializeMainMenu();
        menu.RefreshSaveAvailability();
        Check(!service.HasSave && !ContinueButton().IsInteractable, "empty store disables Continue");
        data.SetCurrentLanguage(LanguageState.ja);data.SetGameOptionVolume(GameOptionVolumeType.Music, 27);
        int originalAdditionId = AdditionHandle.Instance.GetInstanceID();
        menu.OpenNewGamePanel();
        Check(flow.IsBusy, "first New Game starts directly without a slot or confirmation");
        Check(!flow.StartNewGame(), "duplicate new-game start is rejected while loading");
        yield return WaitNewGamePresentation(originalAdditionId);
        Check(data.CurrentLanguageState == LanguageState.ja && data.GetGameOptionVolume(GameOptionVolumeType.Music) == 27,
            "new game keeps language and options");
        Check(data.GetOSResourceValue(ResourceType.Heart) == 53 && data.GetOSResourceValue(ResourceType.Battery) == 70
            && data.GetOSResourceValue(ResourceType.Fault) == 10, "new game restores authored 53/70/10 resources");
        Check(!data.MainGameData.movementTutorialCompleted && !data.IsEventCompleted("Event_00"), "new game clears tutorial/story progress");
        Check(data.CurrentHeartHealProcessIndex == 0 && data.CurrentChapterIndex == 0, "new game does not automatically begin first treatment");
        Check(OS_Handle.Instance.CharacterWindowController.CurrentState == OSCharacterWindowState.VoidMessage,
            "fresh hidden OS keeps default character presentation");

        var player = PlayerCtrlManager.Instance.PlayerController;
        var sceneEvent = UnityEngine.Object.FindObjectsByType<SceneEventBase>(FindObjectsInactive.Include).First(x => x.EventId == "Event_00");
        var fixture = new GameFlowSaveData { phase = GamePhase.Prologue, hasPlayerPosition = true,
            playerPosition = sceneEvent.transform.position, playerFacing = -1, dialogueEventId = "Event_00", dialogueLineIndex = 10 };
        player.RestoreResumeState(fixture, false);
        yield return new WaitForFixedUpdate();yield return null;
        player.RestoreResumeState(fixture, true);
        Check(player.IsInEventDialogue, "fixture enters a mid-event dialogue line");
        data.SetOSResourceValue(ResourceType.Battery, 41);
        GameManager.Instance.Flags.Set("qa.flow.persisted");
        Check(service.FlushNow(), "write prologue dialogue and position snapshot");
        var saved = Read();
        Check(saved.flow.phase == GamePhase.Prologue && saved.flow.dialogueEventId == "Event_00"
            && saved.flow.dialogueLineIndex == 10 && saved.flow.playerFacing == -1, "disk captures phase/facing/current dialogue line");
        Check(flow.ReturnToMainMenu(), "return from prologue dialogue");yield return WaitMenu();
        menu = MainMenuHandle.Instance;menu.InitializeMainMenu();menu.RefreshSaveAvailability();
        Check(ContinueButton().IsInteractable, "Continue enables after first autosave");

        foreach (LanguageState lang in Enum.GetValues(typeof(LanguageState)))
        {
            data.SetCurrentLanguage(lang);menu.OpenNewGamePanel();
            var dialog = Field<GameObject>(menu, "saveDialog");
            Check(dialog.activeSelf && !flow.IsBusy, "existing save requires explicit new-game confirmation: " + lang);
            foreach (var label in dialog.GetComponentsInChildren<TMP_Text>(true))
            {
                label.ForceMeshUpdate(true);
                Check(!string.IsNullOrEmpty(label.text) && !label.isTextOverflowing, "confirmation text fits: " + lang + "/" + label.name);
            }
            menu.CloseSaveDialog();
        }
        data.SetCurrentLanguage(LanguageState.ja);
        Check(flow.ContinueGame(), "continue starts scene reconstruction");yield return WaitSession(GamePhase.Prologue);
        player = PlayerCtrlManager.Instance.PlayerController;
        var actual = new GameFlowSaveData();player.CaptureResumeState(actual);
        Check(Vector3.Distance(actual.playerPosition, saved.flow.playerPosition) < .35f && actual.playerFacing == -1,
            "side-scroll position and facing survive scene reconstruction");
        Check(player.IsInEventDialogue && actual.dialogueEventId == "Event_00" && actual.dialogueLineIndex == 10,
            "continue resumes current dialogue line without completing the event");
        Check(!data.IsEventCompleted("Event_00") && data.GetOSResourceValue(ResourceType.Battery) == 41
            && GameManager.Instance.Flags.Has("qa.flow.persisted"), "continue preserves event/resource/narrative state");
        Check(data.CurrentHeartHealProcessIndex == 0, "continue does not grant/reset first-treatment energy");

        Check(flow.ReturnToMainMenu(), "return before overwrite test");yield return WaitMenu();
        menu = MainMenuHandle.Instance;menu.InitializeMainMenu();menu.OpenNewGamePanel();
        Check(Field<GameObject>(menu, "saveDialog").activeSelf && !flow.IsBusy, "overwrite waits for confirmation");
        menu.CloseSaveDialog();
        Check(!flow.IsBusy && data.GetOSResourceValue(ResourceType.Battery) == 41, "cancel preserves existing game");
        originalAdditionId = AdditionHandle.Instance.GetInstanceID();
        menu.OpenNewGamePanel();menu.ConfirmNewGame();menu.ConfirmNewGame();
        yield return WaitNewGamePresentation(originalAdditionId);
        Check(data.GetOSResourceValue(ResourceType.Battery) == 70 && !GameManager.Instance.Flags.Has("qa.flow.persisted")
            && !PlayerCtrlManager.Instance.PlayerController.IsInEventDialogue, "confirmed overwrite starts a fresh story exactly once");
        Check(data.CurrentLanguageState == LanguageState.ja && data.GetGameOptionVolume(GameOptionVolumeType.Music) == 27,
            "confirmed overwrite retains language and settings");
        Check(service.FlushNow(), "fresh run persisted to the same automatic slot");
        Check(flow.ReturnToMainMenu(), "flow QA returns to menu");yield return WaitMenu();
    }
    static MainMenuButton ContinueButton() => MainMenuHandle.Instance.GetComponentsInChildren<MainMenuButton>(true)
        .First(x => x.Action == MainMenuButtonAction.ContinueGame);
    static GameSaveData Read()
    {
        var read = new GameAutosaveStorage(OutputDirectory).Read();
        if (!read.Success) throw new Exception(read.Error);
        return JsonUtility.FromJson<GameSaveData>(read.Payload);
    }
    static IEnumerator WaitSession(GamePhase phase)
    {
        yield return Wait(() => !GameSessionFlow.Instance.IsBusy && !GameManager.Instance.IsTransitioning
            && GameManager.Instance.Phase == phase && GameAutosaveService.Instance.SessionActive, "session ready: " + phase, 90);
    }
    static IEnumerator WaitNewGamePresentation(int previousAdditionId)
    {
        var presentation = GameSessionFlow.Instance.GetComponentInChildren<SessionLoadPanelPresentation>(true);
        Check(presentation != null && presentation.IsRunning, "new game uses the authored loading presentation");
        string expectedText = presentation.Label.text;
        Check(!string.IsNullOrWhiteSpace(expectedText), "loading text has localized content");
        bool textSurvivedSceneReload = false, heartbeatVisible = false, lockedBeforeReveal = false;
        float deadline = Time.realtimeSinceStartup + 90f;
        while (GameSessionFlow.Instance.IsBusy && Time.realtimeSinceStartup < deadline)
        {
            if (presentation != null && presentation.IsRunning)
            {
                bool uiReloaded = AdditionHandle.Instance != null && AdditionHandle.Instance.GetInstanceID() != previousAdditionId;
                textSurvivedSceneReload |= uiReloaded && presentation.Label.color.a > .9f
                    && presentation.Label.text == expectedText && presentation.Label.gameObject.activeInHierarchy;
                heartbeatVisible |= uiReloaded && presentation.ProgressImage.color.a > .9f;
                lockedBeforeReveal |= GameManager.Instance.Phase == GamePhase.Prologue
                    && presentation.Background.color.a > .99f && !PlayerCtrlManager.Instance.CanMove;
            }
            yield return null;
        }
        yield return WaitSession(GamePhase.Prologue);
        Check(textSurvivedSceneReload, "localized opening text survives destruction and recreation of GameUI");
        Check(heartbeatVisible, "authored heartbeat loading effect remains visible after GameUI reload");
        Check(lockedBeforeReveal, "player movement stays locked behind the loading presentation");
        Check(PlayerCtrlManager.Instance.CanMove && PlayerCtrlManager.Instance.CanInteractive,
            "opening reveal restores player control");
        yield return null;
        Check(GameSessionFlow.Instance.GetComponentInChildren<SessionLoadPanelPresentation>(true) == null,
            "temporary presentation is released after the reveal");
    }
    static IEnumerator WaitMenu() => Wait(() => GameManager.Instance.Phase == GamePhase.Boot && !GameManager.Instance.IsTransitioning
        && !GameSessionFlow.Instance.IsBusy, "menu settled", 60);
    static IEnumerator Wait(Func<bool> test, string label, float timeout)
    {
        float deadline = Time.realtimeSinceStartup + timeout;
        while (!test() && Time.realtimeSinceStartup < deadline) yield return null;
        Check(test(), label);
    }
    static T Field<T>(object target, string name) => (T)target.GetType().GetField(name, Hidden).GetValue(target);
    static void Check(bool value, string name)
    {
        if (!value) throw new InvalidOperationException(name);
        checks.Add(name);
    }
}
