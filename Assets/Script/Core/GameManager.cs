using UnityEngine;
using MrTGame.Systems;

namespace MrTGame.Core
{
    /// <summary>
    /// 全局流程状态机:Boot(GameSetUp) → Prologue(Game_Main 横板) → OSMain(GameUI) → Ending(Game_Main)。
    /// 挂在 GameSetUp 场景的 MrT_Systems 上,与全部系统组件同一 GameObject,随 DontDestroyOnLoad 常驻。
    /// </summary>
    public class GameManager : Singleton<GameManager>
    {
        [SerializeField] GameConfig config;

        [Header("阶段→场景映射")]
        [SerializeField] string setupScene = "GameSetUp";
        [SerializeField] string sideScrollScene = "Game_Main";
        [SerializeField] string osScene = "GameUI";

        public GameConfig Config => config;
        public NarrativeFlags Flags { get; } = new NarrativeFlags();
        public GamePhase Phase { get; private set; } = GamePhase.Boot;
        public string SideScrollSceneName => sideScrollScene;
        public string OSSceneName => osScene;
        public bool HasSideScrollReturnPhase => _hasSideScrollReturnPhase;
        public GamePhase SideScrollReturnPhase => _sideScrollReturnPhase;

        // Loading needs a forced phase notification even after replacing an OS
        // scene while the manager itself stayed in the same phase.
        public void RestoreSessionPhase(GameFlowSaveData saved)
        {
            if (saved == null) return;
            saved.EnsureData();
            _hasSideScrollReturnPhase = saved.hasSideScrollReturnPhase;
            _sideScrollReturnPhase = saved.sideScrollReturnPhase;
            restoredReturnPhasePending = true;
            RequestPhase(saved.phase, true);
        }
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        public void DebugSwitchPhase(GamePhase phase) => RequestPhase(phase, true);
#endif
        public bool IsTransitioning => _isTransitioning;

        GamePhase _sideScrollReturnPhase = GamePhase.Prologue;
        bool _hasSideScrollReturnPhase;
        bool _isTransitioning;
        bool _usesOSFadeTransition;
        bool _isOSFadeOutRunning;
        bool _isBackgroundSceneCleanupPending;
        bool _isOSFadeFinished;
        bool restoredReturnPhasePending;

        protected override void Awake()
        {
            base.Awake();
            if (!IsSingletonInstance)
                return;

            DontDestroyOnLoad(gameObject);
        }

        void OnEnable() => GameEvents.GameOver += OnGameOver;
        void OnDisable() => GameEvents.GameOver -= OnGameOver;

        /// <summary>Called by GameSetup after GameUI has finished loading.</summary>
        public void OpenMainMenu()
        {
            if (GameAutosaveService.Instance?.SessionActive == true
                && GameSessionFlow.Instance != null && !GameSessionFlow.Instance.IsBusy)
            {
                GameSessionFlow.Instance.ReturnToMainMenu();
                return;
            }
            OpenMainMenuWithoutSave();
        }

        public void OpenMainMenuWithoutSave()
        {
            RequestPhase(GamePhase.Boot, true);
        }

        public void SwitchPhase(GamePhase phase)
        {
            RequestPhase(phase, false);
        }

        public void ReturnToSideScroll()
        {
            if (Phase != GamePhase.OSMain)
            {
                Debug.LogWarning(
                    $"[Game] Cannot return to side-scroll while phase is {Phase}.");
                return;
            }

            GamePhase returnPhase = _hasSideScrollReturnPhase
                ? _sideScrollReturnPhase
                : GamePhase.Prologue;
            RequestPhase(returnPhase, false);
        }


        void RequestPhase(GamePhase phase, bool forceNotification)
        {
            if (_isTransitioning)
            {
                Debug.LogWarning($"[Game] Scene transition already running; ignored phase request: {phase}");
                return;
            }

            if (!forceNotification && phase == Phase)
                return;

            SceneLoadManager loader = SceneLoadManager.Instance;
            if (loader == null)
            {
                Debug.LogError("[Game] SceneLoadManager is missing.");
                return;
            }

            _isTransitioning = true;
            _isOSFadeOutRunning = false;
            _isBackgroundSceneCleanupPending = false;
            _isOSFadeFinished = false;
            bool shouldUseOSFade = phase != Phase
                && (phase == GamePhase.OSMain || Phase == GamePhase.OSMain);
            AdditionHandle addition = AdditionHandle.Instance;
            _usesOSFadeTransition = shouldUseOSFade && addition != null;
            if (_usesOSFadeTransition)
            {
                try
                {
                    addition.FadeIN(
                        () => BeginPhaseTransitionSafely(
                            phase,
                            forceNotification,
                            loader),
                        ClearPhaseTransition);
                }
                catch (System.Exception exception)
                {
                    Debug.LogException(exception, this);
                    FailTransition(
                        $"Could not start the OS fade transition: {exception.Message}");
                }
                return;
            }

            BeginPhaseTransitionSafely(phase, forceNotification, loader);
        }

        void BeginPhaseTransitionSafely(
            GamePhase phase,
            bool forceNotification,
            SceneLoadManager loader)
        {
            if (!_isTransitioning)
                return;

            try
            {
                BeginPhaseTransition(phase, forceNotification, loader);
            }
            catch (System.Exception exception)
            {
                Debug.LogException(exception, this);
                FailTransition(
                    $"Could not begin transition to {phase}: {exception.Message}");
            }
        }


        void BeginPhaseTransition(
            GamePhase phase,
            bool forceNotification,
            SceneLoadManager loader)
        {
            if (loader == null)
            {
                FailTransition("SceneLoadManager became unavailable.");
                return;
            }

            switch (phase)
            {
                case GamePhase.Boot:
                    loader.LoadSceneAdditive(
                        osScene,
                        onLoadCompleted: _ => RunTransitionStep(
                            "Could not activate Boot.",
                            () =>
                            {
                                ConfigureScenePresentation(GamePhase.Boot);
                                CommitPhase(GamePhase.Boot, true);
                                FinishAfterSideScrollUnload(loader);
                            }),
                        onLoadFailed: FailTransition,
                        setActiveScene: true);
                    break;

                case GamePhase.Prologue:
                case GamePhase.Interlude:
                case GamePhase.Ending:
                    loader.LoadSceneAdditive(
                        sideScrollScene,
                        onLoadCompleted: _ => RunTransitionStep(
                            $"Could not activate {phase}.",
                            () =>
                            {
                                // MainMenuHandle synchronizes new-game initialization
                                // with the loading curtain reveal.
                                ConfigureScenePresentation(phase);
                                CommitPhase(phase, forceNotification);
                                FinishTransition();
                            }),
                        onLoadFailed: FailTransition,
                        setActiveScene: true);
                    break;

                case GamePhase.OSMain:
                    loader.LoadSceneAdditive(
                        osScene,
                        onLoadCompleted: _ => RunTransitionStep(
                            "Could not activate OSMain.",
                            () =>
                            {
                                ConfigureScenePresentation(GamePhase.OSMain);
                                CommitPhase(GamePhase.OSMain, forceNotification);
                                FinishTransition();
                            }),
                        onLoadFailed: FailTransition,
                        setActiveScene: true);
                    break;

                default:
                    FailTransition($"Unsupported phase: {phase}");
                    break;
            }
        }

        void RunTransitionStep(string context, System.Action step)
        {
            if (!_isTransitioning)
                return;

            try
            {
                step.Invoke();
            }
            catch (System.Exception exception)
            {
                Debug.LogException(exception, this);
                FailTransition($"{context} {exception.Message}");
            }
        }

        void FinishAfterSideScrollUnload(SceneLoadManager loader)
        {
            if (loader == null)
            {
                FailTransition("SceneLoadManager became unavailable before scene cleanup.");
                return;
            }

            if (_usesOSFadeTransition)
            {
                _isBackgroundSceneCleanupPending = true;
                _isOSFadeFinished = false;
                FinishTransition();
                UnloadSceneInBackground(loader, sideScrollScene);
                return;
            }

            loader.UnloadScene(
                sideScrollScene,
                onUnloadCompleted: _ => FinishTransition(),
                onUnloadFailed: FailTransition);
        }

        void UnloadSceneInBackground(
            SceneLoadManager loader,
            string sceneName)
        {
            try
            {
                loader.UnloadScene(
                    sceneName,
                    onUnloadCompleted: _ => CompleteBackgroundSceneCleanup(),
                    onUnloadFailed: reason =>
                    {
                        Debug.LogError(
                            $"[Game] Background scene cleanup failed for '{sceneName}': {reason}");
                        CompleteBackgroundSceneCleanup();
                    });
            }
            catch (System.Exception exception)
            {
                Debug.LogException(exception, this);
                Debug.LogError(
                    $"[Game] Background scene cleanup failed for '{sceneName}'.");
                CompleteBackgroundSceneCleanup();
            }
        }




        void ConfigureScenePresentation(GamePhase phase)
        {
            SceneLoadManager loader = SceneLoadManager.Instance;
            if (loader == null)
                return;

            bool useSideScroll = phase == GamePhase.Prologue
                || phase == GamePhase.Interlude
                || phase == GamePhase.Ending;

            if (phase == GamePhase.OSMain)
            {
                // All OS entrances (including same-phase reloads) release the
                // side-scroll rain before OS phase listeners start their audio.
                if (MainGameHandle.Instance != null)
                    MainGameHandle.Instance.SuspendSceneAmbientForOS();
                else
                    MainGameHandle.StopRainAmbient();
            }

            if (PlayerCtrlManager.Instance != null)
                PlayerCtrlManager.Instance.SetPlayerActive(useSideScroll);
            else
                Debug.LogWarning("[Game] PlayerCtrlManager is missing.");

            if (useSideScroll)
            {
                loader.SetScenePresentationEnabled(sideScrollScene, true);
                bool sideScrollHasCamera = loader.HasEnabledCamera(sideScrollScene);
                loader.SetScenePresentationEnabled(setupScene, !sideScrollHasCamera);
                loader.SetScenePresentationEnabled(osScene, false);
            }
            else
            {
                loader.SetScenePresentationEnabled(setupScene, false);
                loader.SetScenePresentationEnabled(sideScrollScene, false);
                loader.SetScenePresentationEnabled(osScene, true);
            }
        }

        void CommitPhase(GamePhase phase, bool forceNotification)
        {
            GamePhase previousPhase = Phase;
            if (phase == GamePhase.OSMain && previousPhase != GamePhase.OSMain
                && !restoredReturnPhasePending)
            {
                _hasSideScrollReturnPhase = previousPhase == GamePhase.Prologue
                    || previousPhase == GamePhase.Interlude
                    || previousPhase == GamePhase.Ending;
                if (_hasSideScrollReturnPhase)
                    _sideScrollReturnPhase = previousPhase;
            }

            bool changed = phase != previousPhase;
            Phase = phase;
            restoredReturnPhasePending = false;
            if (changed || forceNotification)
                GameEvents.RaisePhaseChanged(phase);

            bool useSideScroll = phase == GamePhase.Prologue
                || phase == GamePhase.Interlude
                || phase == GamePhase.Ending;
            AdditionHandle.Instance?.SetBKActive(useSideScroll);
        }

        void FinishTransition()
        {
            if (!_isTransitioning || _isOSFadeOutRunning)
                return;

            if (!_usesOSFadeTransition)
            {
                ClearPhaseTransition();
                return;
            }

            _usesOSFadeTransition = false;
            _isOSFadeOutRunning = true;
            AdditionHandle addition = AdditionHandle.Instance;
            if (addition == null)
            {
                CompleteOSFadeTransition();
                return;
            }

            try
            {
                addition.FadeOUT(
                    CompleteOSFadeTransition,
                    CompleteOSFadeTransition);
            }
            catch (System.Exception exception)
            {
                Debug.LogException(exception, this);
                CompleteOSFadeTransition();
            }
        }

        void ClearPhaseTransition()
        {
            _usesOSFadeTransition = false;
            _isOSFadeOutRunning = false;
            _isBackgroundSceneCleanupPending = false;
            _isOSFadeFinished = false;
            _isTransitioning = false;
        }


        void FailTransition(string reason)
        {
            Debug.LogError($"[Game] Phase transition failed: {reason}");
            FinishTransition();
        }

        // ---- 检查点:章开头存一份(flags+羁绊+揭示+资源快照),GameOver按快照回退 ----

        [System.Serializable]
        public class CheckpointData
        {
            public string chapterId;
            public int bondLevel;
            public int revealStage;
            public System.Collections.Generic.List<string> flags;
            public float heart, battery, fault;
            public string savedAt;
        }

        static string CheckpointPath => System.IO.Path.Combine(Application.persistentDataPath, "checkpoint.json");

        public bool HasCheckpoint => System.IO.File.Exists(CheckpointPath);

        /// <summary>OSMainDirector 在章节开播时调用。</summary>
        public void SaveCheckpoint(string chapterId)
        {
            var res = GameResourceSystem.Instance;
            var data = new CheckpointData
            {
                chapterId = chapterId,
                bondLevel = Flags.BondLevel,
                revealStage = (int)Flags.Reveal,
                flags = Flags.ExportFlags(),
                heart = res != null ? res.Get(ResourceType.Heart) : 0f,
                battery = res != null ? res.Get(ResourceType.Battery) : 0f,
                fault = res != null ? res.Get(ResourceType.Fault) : 0f,
                savedAt = System.DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
            };
            System.IO.File.WriteAllText(CheckpointPath, JsonUtility.ToJson(data, true));
        }

        public CheckpointData LoadCheckpointData()
        {
            if (!HasCheckpoint) return null;
            try { return JsonUtility.FromJson<CheckpointData>(System.IO.File.ReadAllText(CheckpointPath)); }
            catch (System.Exception e) { Debug.LogWarning("[Game] 检查点读取失败:" + e.Message); return null; }
        }

        void ApplyCheckpoint(CheckpointData data)
        {
            Flags.Import(data.flags, data.bondLevel, (RevealStage)data.revealStage);
            if (GameResourceSystem.Instance != null)
                GameResourceSystem.Instance.ApplySnapshot(data.heart, data.battery, data.fault);
            // 章节定位:当前仅 T0,由 OSMainDirector 的序列化引用决定;
            // TODO Phase C: 多章后加"章节注册表",按 data.chapterId 找回对应 StoryChapter
        }

        /// <summary>标题页"继续":读检查点→应用→进OS主体。</summary>
        public void ContinueFromCheckpoint()
        {
            // The chapter-start file is not a complete resume snapshot. Keep
            // old callers routed through the single automatic save instead.
            GameSessionFlow.Instance?.ContinueGame();
        }

        void OnGameOver()
        {
            // Reserved for the future Game Over presentation and flow.
        }


        void CompleteBackgroundSceneCleanup()
        {
            _isBackgroundSceneCleanupPending = false;
            if (_isOSFadeFinished)
                ClearPhaseTransition();
        }


        void CompleteOSFadeTransition()
        {
            _isOSFadeOutRunning = false;
            _isOSFadeFinished = true;
            if (!_isBackgroundSceneCleanupPending)
                ClearPhaseTransition();
        }
    }
}
