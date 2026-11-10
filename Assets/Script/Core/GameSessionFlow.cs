using System;
using System.Collections;
using System.Collections.Generic;
using MrTGame.Core;
using UnityEngine;

/// <summary>Owns loading across scenes; it must live beside GameDataManager.</summary>
[DisallowMultipleComponent]
public sealed class GameSessionFlow : Singleton<GameSessionFlow>
{
    public bool IsBusy { get; private set; }
    UnityEngine.UI.Image curtain;
    SessionLoadPanelPresentation newGamePresentation;
    Coroutine operation;
    IDisposable writeSuspension;

    public bool StartNewGame() => BeginLoad(true);
    public bool ContinueGame() => BeginLoad(false);

    bool BeginLoad(bool newGame)
    {
        GameManager manager = GameManager.Instance;
        GameAutosaveService saves = GameAutosaveService.Instance;
        if (IsBusy || saves == null || manager == null || manager.IsTransitioning
            || manager.Phase != GamePhase.Boot || SceneLoadManager.Instance == null)
            return false;

        GameSaveData snapshot;
        if (newGame)
        {
            GameDataManager data = GameDataManager.Instance;
            snapshot = new GameSaveData { farewellSaveId = Guid.NewGuid().ToString("N") };
            snapshot.EnsureData();
            snapshot.gameOptions = JsonUtility.FromJson<GameOptionSaveData>(
                JsonUtility.ToJson(data.GameOptions));
            snapshot.flow.language = data.CurrentLanguageState;
        }
        else if (!saves.TryLoad(out snapshot))
        {
            ShowError("ui.save.load_failed", "无法读取自动存档。原存档没有被覆盖。");
            return false;
        }

        snapshot.EnsureData();
        IsBusy = true;
        writeSuspension = saves.SuspendWrites();
        saves.EndSession();
        operation = StartCoroutine(RunOperation(LoadSession(snapshot, newGame)));
        return true;
    }

    public bool ReturnToMainMenu()
    {
        GameManager manager = GameManager.Instance;
        GameAutosaveService saves = GameAutosaveService.Instance;
        if (IsBusy || manager == null || manager.IsTransitioning || saves == null)
            return false;
        if (saves.SessionActive && !saves.FlushNow())
        {
            ShowError("ui.save.write_failed", "自动存档写入失败，尚未退出游戏。请检查磁盘后重试。");
            return false;
        }
        saves.EndSession();
        manager.OpenMainMenuWithoutSave();
        return true;
    }

    /// <summary>Called just before serialization, after presentation controllers capture their state.</summary>
    public static void Capture(GameSaveData data)
    {
        if (data == null) return;
        if (data.flow == null) data.flow = new GameFlowSaveData();
        data.flow.language = GameDataManager.Instance != null
            ? GameDataManager.Instance.CurrentLanguageState : data.flow.language;
        GameManager manager = GameManager.Instance;
        if (manager == null || manager.Phase == GamePhase.Boot || manager.IsTransitioning
            || Instance != null && Instance.IsBusy)
            return;

        GameFlowSaveData flow = data.flow;
        flow.phase = manager.Phase;
        flow.hasSideScrollReturnPhase = manager.HasSideScrollReturnPhase;
        flow.sideScrollReturnPhase = manager.SideScrollReturnPhase;
        flow.narrativeFlags = manager.Flags.ExportFlags();
        flow.bondLevel = manager.Flags.BondLevel;
        flow.revealStage = manager.Flags.Reveal;
        PlayerCtrlManager.Instance?.PlayerController?.CaptureResumeState(flow);
        MainGameHandle.Instance?.CaptureResumeState(flow);
        flow.EnsureData();
    }

    IEnumerator LoadSession(GameSaveData snapshot, bool newGame)
    {
        GameManager manager = GameManager.Instance;
        SceneLoadManager loader = SceneLoadManager.Instance;
        GameDataManager data = GameDataManager.Instance;
        PlayerCtrlManager.Instance?.SetControlState(false, false);
        if (newGame)
        {
            newGamePresentation = AdditionHandle.Instance?.CreateSessionLoadPresentation(transform);
            if (newGamePresentation == null)
                throw new InvalidOperationException("The authored new-game loading presentation is unavailable.");
            yield return newGamePresentation.WaitUntilReadyToLoad();
        }
        else
            yield return FadeCurtain(1f, .2f);

        // OnDisable handlers write presentation checkpoints into the old object.
        // Install the replacement only after both old scenes have disappeared.
        yield return ChangeScene(loader, manager.OSSceneName, false);
        yield return ChangeScene(loader, manager.SideScrollSceneName, false);
        data.ReplaceSaveData(snapshot);
        manager.Flags.Import(snapshot.flow.narrativeFlags,
            snapshot.flow.bondLevel, snapshot.flow.revealStage);

        bool needsSideScroll = snapshot.flow.phase != GamePhase.OSMain
            || snapshot.flow.hasSideScrollReturnPhase || snapshot.osProcess.currentChapterIndex == 0;
        if (needsSideScroll)
            yield return ChangeScene(loader, manager.SideScrollSceneName, true,
                progress => newGamePresentation?.SetBackendProgress(progress * .8f));
        yield return ChangeScene(loader, manager.OSSceneName, true);
        newGamePresentation?.SetBackendProgress(1f);

        MainGameHandle main = MainGameHandle.Instance;
        if (!newGame && main != null)
        {
            main.LoadFromSave();
            main.RestoreResumeState(snapshot.flow);
        }
        if (!newGame) PlayerCtrlManager.Instance?.SetControlState(true, true);
        PlayerCtrlManager.Instance?.PlayerController?.RestoreResumeState(snapshot.flow, false);

        manager.RestoreSessionPhase(snapshot.flow);
        yield return WaitForTransition(manager);
        if (manager.Phase != snapshot.flow.phase)
            throw new InvalidOperationException("The saved game phase could not be restored.");
        yield return OSSessionPersistence.Restore(snapshot);
        if (!newGame && snapshot.flow.phase != GamePhase.OSMain)
            PlayerCtrlManager.Instance?.PlayerController?.RestoreResumeState(snapshot.flow, true);

        if (newGame)
        {
            yield return newGamePresentation.Complete(() => {
                // Start the movement tutorial, rain and player at the same
                // background reveal point as the original opening sequence.
                main?.InitializeNewGame();
                PlayerCtrlManager.Instance?.SetControlState(true, true);
            });
        }
        GameAutosaveService.Instance.BeginSession(snapshot);
        if (!newGame) yield return FadeCurtain(0f, .25f);
    }

    // Nested enumerators are driven explicitly so an exception releases the IO
    // suspension and gives the player a usable menu instead of a black screen.
    IEnumerator RunOperation(IEnumerator root)
    {
        var stack = new Stack<IEnumerator>();
        stack.Push(root);
        Exception failure = null;
        while (stack.Count > 0)
        {
            object next = null;
            bool finished;
            try
            {
                finished = !stack.Peek().MoveNext();
                if (!finished) next = stack.Peek().Current;
            }
            catch (Exception error) { failure = error; break; }
            if (finished)
            {
                (stack.Pop() as IDisposable)?.Dispose();
                continue;
            }
            if (next is IEnumerator nested) stack.Push(nested);
            else yield return next;
        }
        while (stack.Count > 0) (stack.Pop() as IDisposable)?.Dispose();

        if (newGamePresentation != null)
        {
            Destroy(newGamePresentation.gameObject);
            newGamePresentation = null;
        }

        if (failure != null)
        {
            Debug.LogException(failure, this);
            GameAutosaveService.Instance?.EndSession();
            // Recover without touching the previous disk snapshot.
            bool menuLoaded = false;
            SceneLoadManager loader = SceneLoadManager.Instance;
            GameManager manager = GameManager.Instance;
            if (loader != null && manager != null)
            {
                loader.LoadSceneAdditive(manager.OSSceneName,
                    onLoadCompleted: _ => menuLoaded = true,
                    onLoadFailed: _ => menuLoaded = true);
                float deadline = Time.realtimeSinceStartup + 30f;
                while (!menuLoaded && Time.realtimeSinceStartup < deadline) yield return null;
                manager.OpenMainMenuWithoutSave();
                while (manager.IsTransitioning && Time.realtimeSinceStartup < deadline) yield return null;
            }
            PlayerCtrlManager.Instance?.SetControlState(true, true);
            if (curtain != null) curtain.gameObject.SetActive(false);
        }

        writeSuspension?.Dispose();writeSuspension = null;
        IsBusy = false;operation = null;
        if (failure == null && GameManager.Instance?.Phase == GamePhase.OSMain
            && (GameDataManager.Instance?.OSProcessData.chapter01Ending.phase == OSChapter01EndingPhase.None
                || GameDataManager.Instance?.OSProcessData.chapter01Ending.phase == OSChapter01EndingPhase.Chapter02Started
                || GameDataManager.Instance?.OSProcessData.chapter01Ending.phase == OSChapter01EndingPhase.RecoveryAwaitingConfirmation))
            OS_Handle.Instance?.LinkMessageController?.NotifyOSBecameVisible();
        MainMenuHandle.Instance?.RefreshSaveAvailability();
        if (failure != null)
            ShowError("ui.save.load_failed", "无法读取自动存档。原存档没有被覆盖。");
    }

    static IEnumerator ChangeScene(SceneLoadManager loader, string sceneName, bool load, Action<float> progress = null)
    {
        bool completed = false;
        string failure = null;
        if (load)
            loader.LoadSceneAdditive(sceneName,
                onLoadCompleted: _ => completed = true,
                onLoadFailed: reason => { failure = reason;completed = true; },
                onLoadProgress: progress);
        else
            loader.UnloadScene(sceneName,
                onUnloadCompleted: _ => completed = true,
                onUnloadFailed: reason => { failure = reason;completed = true; });
        float deadline = Time.realtimeSinceStartup + 45f;
        while (!completed && Time.realtimeSinceStartup < deadline) yield return null;
        if (!completed || failure != null)
            throw new InvalidOperationException(failure ?? "Scene operation timed out: " + sceneName);
        yield return null;
    }

    static IEnumerator WaitForTransition(GameManager manager)
    {
        float deadline = Time.realtimeSinceStartup + 45f;
        while (manager.IsTransitioning && Time.realtimeSinceStartup < deadline) yield return null;
        if (manager.IsTransitioning) throw new InvalidOperationException("Saved phase transition timed out.");
        yield return null;
    }

    IEnumerator FadeCurtain(float target, float duration)
    {
        if (curtain == null)
        {
            var root = new GameObject("SessionLoadCurtain", typeof(RectTransform),
                typeof(Canvas), typeof(UnityEngine.UI.GraphicRaycaster), typeof(UnityEngine.UI.Image));
            root.transform.SetParent(transform, false);
            var canvas = root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;canvas.sortingOrder = short.MaxValue;
            curtain = root.GetComponent<UnityEngine.UI.Image>();
            curtain.color = Color.clear;curtain.raycastTarget = true;
        }
        curtain.gameObject.SetActive(true);
        float start = curtain.color.a;
        for (float elapsed = 0; elapsed < duration; elapsed += Time.unscaledDeltaTime)
        {
            curtain.color = new Color(0, 0, 0, Mathf.Lerp(start, target, elapsed / duration));
            yield return null;
        }
        curtain.color = new Color(0, 0, 0, target);
        if (target <= 0) curtain.gameObject.SetActive(false);
    }

    void ShowError(string key, string fallback)
    {
        string message = GameDataManager.Instance?.GetLocalizedText(key, fallback) ?? fallback;
        if (MainMenuHandle.Instance != null && MainMenuHandle.Instance.IsMainMenuVisible)
            MainMenuHandle.Instance.ShowSaveError(key, fallback);
        else
            AdditionHandle.Instance?.ShowItemAcquiredTip(message);
        Debug.LogWarning("[Autosave] " + message + " " + GameAutosaveService.Instance?.LastError, this);
    }

    protected override void OnDestroy()
    {
        if (operation != null) StopCoroutine(operation);
        writeSuspension?.Dispose();writeSuspension = null;
        base.OnDestroy();
    }
}
