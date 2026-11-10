using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;
using MrTGame.Core;
using MrTGame.Narrative;

public class GameSetup : MonoBehaviour
{
    const int Chapter01Sequence = 1;
    const float RebootCompletedStateCheckProgress = 2f;

    [Header("Release")]
    [Tooltip("Demo includes Chapters 1 and 2. End after the final talk and State Check thunder flash. Disable for the full game.")]
    [SerializeField] bool isDemo = true;
    [Tooltip("itch.io release: skip Steam initialization and all Steam integration. Set before starting or building; independent of isDemo.")]
    [SerializeField] bool isItch;
    static GameSetup activeSetup;
    public static bool IsDemo => activeSetup == null || activeSetup.isDemo;
    public static bool IsItch
    {
        get
        {
            // SteamManager.Awake (or an early Initialized query) may run first.
            // Read the scene's serialized setting without depending on Awake order.
            if (activeSetup == null)
                activeSetup = FindFirstObjectByType<GameSetup>(FindObjectsInactive.Include);
            return activeSetup != null && activeSetup.isItch;
        }
    }

    [Tooltip("False: normal boot. True: test mode.")]
    [SerializeField] bool isTest;
    [Tooltip("AlphaT test mode: load GameUI and Game_Main additively at startup.")]
    [SerializeField] bool isTestForAlphaT;
    [Tooltip("When isTest is enabled, start from the Chapter 00 OS reload sequence.")]
    [SerializeField] bool isTestForChapter00Reload;
    [Tooltip("When isTest is enabled, start after Chapter 01 has been unlocked and entered.")]
    [SerializeField] bool isTestForChapter01;

    [Header("Text Testing (Play Mode)")]
    [InspectorName("IsOpenFastMemory")]
    [Tooltip("开启后，回忆区可按住左/右 Ctrl 快进。回复选项、交互与剧情等待仍按正常流程处理。")]
    public bool IsOpenFastMemory;
    float nextFastMemoryAdvanceTime;

    [Header("Scenes")]
    [SerializeField] string gameUIScene = "GameUI";
    [SerializeField] string gameMainScene = "Game_Main";

    public static bool UseFormalFlow { get; private set; }
    public static bool IsTestMode { get; private set; } = true;
    public bool IsTest => isTest || isTestForAlphaT;

    void Awake()
    {
        activeSetup = this;
        IsTestMode = isTest || isTestForAlphaT;
        UseFormalFlow = !IsTestMode;
    }

    void OnDestroy()
    {
        if (activeSetup == this) activeSetup = null;
    }

    void Update()
    {
        Keyboard keyboard = Keyboard.current;
        if (!IsOpenFastMemory || keyboard == null
            || (!keyboard.leftCtrlKey.isPressed && !keyboard.rightCtrlKey.isPressed))
        {
            nextFastMemoryAdvanceTime = 0f;
            return;
        }

        if (Time.unscaledTime < nextFastMemoryAdvanceTime) return;
        nextFastMemoryAdvanceTime = Time.unscaledTime + 0.08f;

        // E already advances this frame through the memory controller's own Update.
        // Reuse its normal input so options, interactions and story waits stay intact.
        if (!keyboard.eKey.wasPressedThisFrame)
            OS_Handle.Instance?.MemoryWindowController?.TryAdvance();
    }

    void Start()
    {
        PlayerCtrlManager.Instance?.SetPlayerActive(false);
        GameDataManager.Instance?.Initialize();

        if (IsTestMode)
        {
            SceneLoadManager loader = SceneLoadManager.Instance;
            if (isTestForAlphaT)
            {
                if (loader == null)
                {
                    Debug.LogError("[GameSetup] SceneLoadManager is missing.");
                    return;
                }

                loader.LoadSceneAdditive(
                    gameUIScene,
                    onLoadFailed: reason => Debug.LogError(
                        $"[GameSetup] AlphaT GameUI startup failed: {reason}"));
                loader.LoadSceneAdditive(
                    gameMainScene,
                    onLoadCompleted: _ => InitializeTestGameMain(),
                    onLoadFailed: reason => Debug.LogError(
                        $"[GameSetup] AlphaT Game_Main startup failed: {reason}"));
                return;
            }

            if (isTest && isTestForChapter01)
            {
                StartCoroutine(InitializeTestChapter01());
                return;
            }

            if (isTest && isTestForChapter00Reload)
            {
                StartCoroutine(InitializeTestChapter00Reload());
                return;
            }

            if (loader != null && loader.IsSceneLoaded(gameMainScene))
                InitializeTestGameMain();

            return;
        }

        if (SceneLoadManager.Instance == null)
        {
            Debug.LogError("[GameSetup] SceneLoadManager is missing.");
            return;
        }

        SceneLoadManager.Instance.LoadSceneAdditive(
            gameUIScene,
            onLoadCompleted: _ => InitializeMainMenu(),
            onLoadFailed: reason => Debug.LogError($"[GameSetup] GameUI startup failed: {reason}"));
    }

    void InitializeMainMenu()
    {
        if (MainMenuHandle.Instance != null)
            MainMenuHandle.Instance.InitializeMainMenu();
        else
            Debug.LogError("[GameSetup] MainMenuHandle is missing.");

        if (GameManager.Instance != null)
            GameManager.Instance.OpenMainMenu();
        else
            Debug.LogError("[GameSetup] GameManager is missing.");
    }

    void InitializeTestGameMain()
    {
        PlayerCtrlManager.Instance?.SetPlayerActive(true);

        if (MainGameHandle.Instance != null)
            MainGameHandle.Instance.InitializeNewGame();
        else
            Debug.LogWarning("[GameSetup] MainGameHandle is missing.");
    }

    IEnumerator InitializeTestChapter00Reload()
    {
        yield return EnterTestOSMain("Chapter 00 reload");
        if (!IsTestOSMainReady())
            yield break;

        OS_Handle osHandle = OS_Handle.Instance;
        OSSFXAction sfxAction = osHandle != null
            ? osHandle.SFXAction
            : null;
        if (sfxAction == null)
        {
            Debug.LogError(
                "[GameSetup] Chapter 00 reload test requires the OS "
                + "CloseAndReOpenPart.");
            yield break;
        }

        GameDataManager dataManager = GameDataManager.Instance;
        if (dataManager != null)
        {
            dataManager.SetCurrentStateCheckProgress(
                RebootCompletedStateCheckProgress);
        }
        else
            Debug.LogWarning("[GameSetup] GameDataManager is missing.");

        sfxAction.PlayCloseAndReOpenPart();
    }

    IEnumerator InitializeTestChapter01()
    {
        if (!PrepareTestChapter01State())
            yield break;

        yield return EnterTestOSMain("Chapter 01");
        if (!IsTestOSMainReady())
            yield break;

        OS_Handle osHandle = OS_Handle.Instance;
        OSCharacterWindowController characterWindow = osHandle != null
            ? osHandle.CharacterWindowController
            : null;
        if (characterWindow == null)
        {
            Debug.LogWarning(
                "[GameSetup] Chapter 01 test could not set the Character "
                + "window to AnimaState.");
            yield break;
        }

        characterWindow.ShowAnimaState(false);
    }

    IEnumerator EnterTestOSMain(string testName)
    {
        GameManager gameManager = GameManager.Instance;
        if (gameManager == null)
        {
            Debug.LogError("[GameSetup] GameManager is missing.");
            yield break;
        }

        if (SceneLoadManager.Instance == null)
        {
            Debug.LogError("[GameSetup] SceneLoadManager is missing.");
            yield break;
        }

        gameManager.SwitchPhase(GamePhase.OSMain);
        while (gameManager != null && gameManager.IsTransitioning)
            yield return null;

        if (!IsTestOSMainReady())
        {
            Debug.LogError(
                $"[GameSetup] {testName} test could not enter OSMain.");
            yield break;
        }

        // Let newly loaded GameUI objects finish their Start callbacks.
        yield return null;
    }

    bool PrepareTestChapter01State()
    {
        GameDataManager dataManager = GameDataManager.Instance;
        if (dataManager == null)
        {
            Debug.LogError("[GameSetup] GameDataManager is missing.");
            return false;
        }

        if (!dataManager.TryGetOSMemoryChapterProgressRange(
                Chapter01Sequence,
                out OSMemoryChapterProgressRange range)
            || range == null
            || !range.HasMemoryDataRange)
        {
            Debug.LogError(
                "[GameSetup] Chapter 01 test requires a valid memory range.");
            return false;
        }

        dataManager.SetCurrentStateCheckProgress(
            RebootCompletedStateCheckProgress);
        dataManager.SetCurrentChapterIndex(Chapter01Sequence);
        dataManager.SetCurrentOSMemoryData(range.FirstDataIndex);

        SetTestDesktopProcessUnlocked(
            dataManager,
            OSDesktopProcessKeys.Memory);
        SetTestDesktopProcessUnlocked(
            dataManager,
            OSDesktopProcessKeys.Record);
        dataManager.SetDesktopProcessIsNew(
            OSDesktopProcessKeys.StateCheck,
            false);
        dataManager.SetChapter01KeyXAcquired();

        OSCharacterInteractionSaveData interaction =
            dataManager.OSCharacterInteractionState;
        interaction.ActivateSequence(null);
        interaction.ActivateSequence(
            OSCharacterInteractionIds.Chapter01LiveReconnect);
        interaction.SetEntryConsumed(
            OSCharacterInteractionIds.Chapter01Entries.RecordStartPrompt,
            true);
        interaction.BeginPresentation(
            OSCharacterInteractionIds.Chapter01Entries.KeyHovered);
        interaction.CompletePresentation(
            OSCharacterInteractionIds.Chapter01Entries.KeyHovered);
        return true;
    }

    static void SetTestDesktopProcessUnlocked(
        GameDataManager dataManager,
        string processKey)
    {
        dataManager.SetDesktopProcessIsNew(processKey, true);
        dataManager.SetDesktopProcessIsLocked(processKey, false);
    }

    static bool IsTestOSMainReady()
    {
        GameManager gameManager = GameManager.Instance;
        return gameManager != null && gameManager.Phase == GamePhase.OSMain;
    }

}
