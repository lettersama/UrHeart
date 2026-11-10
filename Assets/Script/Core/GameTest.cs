using System;
using System.Collections;
using System.Collections.Generic;
using MrTGame.Core;
using MrTGame.Narrative;
using Sirenix.OdinInspector;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;

#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
#endif

[DisallowMultipleComponent]
public sealed partial class GameTest : Singleton<GameTest>
{
    const int MemoryHomeEntryDataIndex = 2680;

    static readonly LanguageState[] Languages =
        (LanguageState[])Enum.GetValues(typeof(LanguageState));

    [Title("Localization Preview")]
    [InfoBox("Edit Mode previews loaded scene objects only. Saving restores authored Simplified Chinese text automatically.")]
    [SerializeField, Required] LocalizationData_SO localizationData;
    [ShowInInspector, LabelText("Preview Language")]
    LanguageState previewLanguage = LanguageState.zh_CN;

    int lastRefreshedTextCount;

    [Title("Memory Jump (Play Mode)")]
    [InfoBox("输入 OSMemory 节点编号（例如 1270），确认后打开回忆窗口并从该条重播。目标镜头及后续依赖同一升级的镜头可连续预览；不安装升级，选项前置仍按实际状态检查。会更新本次运行的回忆进度。")]
    [ShowInInspector, LabelText("Memory Index"), MinValue(0)]
    int memoryDataIndex = 1270;
    Coroutine memoryJumpRoutine;
    public bool IsMemoryJumpRunning => memoryJumpRoutine != null;

    protected override void Awake()
    {
        base.Awake();
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        if (IsSingletonInstance && GetComponent<GameTestConsole>() == null)
            gameObject.AddComponent<GameTestConsole>();
#endif
    }

    [Button("确认跳转回忆", ButtonSizes.Medium), EnableIf("@UnityEngine.Application.isPlaying")]
    public void JumpToMemoryIndex()
    {
        JumpToMemoryIndex(memoryDataIndex);
    }

    public void JumpToMemoryIndex(int dataIndex)
    {
        JumpToMemoryIndex(dataIndex, false);
    }

    [InfoBox("安装回忆区升级 #1 / #2，并重置本段插画交互与临时背包，从家门前（2680）重新开始测试。仅修改本次运行的状态，不消耗电量。")]
    [Button("安装回忆升级并测试家中交互", ButtonSizes.Medium), EnableIf("@UnityEngine.Application.isPlaying")]
    public void JumpToMemoryHomeTest()
    {
        memoryDataIndex = MemoryHomeEntryDataIndex;
        JumpToMemoryIndex(MemoryHomeEntryDataIndex, true);
    }

    void JumpToMemoryIndex(int dataIndex, bool prepareHomeTest)
    {
        if (IsCheckpointJumpRunning)
            return;
        GameDataManager data = GameDataManager.Instance;
        if (!Application.isPlaying || data == null
            || !data.TryGetOSMemoryData(dataIndex, out _))
        {
            SetJumpStatus("status_invalid_index", "编号不存在，请输入有效的回忆序列号。");
            Debug.LogWarning($"[GameTest] 无法跳转：请在 Play Mode 输入有效的回忆节点编号（当前 {dataIndex}）。", this);
            return;
        }
        if (!IsMemoryIndexAvailable(dataIndex))
        {
            SetJumpStatus("status_demo_unavailable", "Demo 模式仅开放序章、第一章和第二章。");
            return;
        }
        if (memoryJumpRoutine != null)
            StopCoroutine(memoryJumpRoutine);
        GameAutosaveService.Instance?.EnterTestSession();
        SetJumpStatus("status_memory_requested", "正在跳转至回忆 {0}…", false, dataIndex);
        memoryJumpRoutine = StartCoroutine(JumpToMemoryRoutine(dataIndex, prepareHomeTest));
    }

    IEnumerator JumpToMemoryRoutine(int dataIndex, bool prepareHomeTest)
    {
        // A second request can replace a pending jump, including during a scene transition.
        yield return null;
        try
        {
            GameManager manager = GameManager.Instance;
            float deadline = Time.realtimeSinceStartup + 30f;
            while (manager != null && manager.IsTransitioning
                && Time.realtimeSinceStartup < deadline)
                yield return null;

            OS_Handle os = OS_Handle.Instance;
            if (os == null || !os.IsOSVisible)
            {
                if (manager == null || manager.IsTransitioning || SceneLoadManager.Instance == null)
                {
                    SetJumpStatus("status_os_not_ready", "系统尚未就绪，请稍后重试。");
                    Debug.LogWarning("[GameTest] OS 尚未就绪：需要 GameManager 和 SceneLoadManager。", this);
                    yield break;
                }
                manager.SwitchPhase(GamePhase.OSMain);
                while (manager.IsTransitioning && Time.realtimeSinceStartup < deadline)
                    yield return null;
                // Let newly loaded GameUI finish Start and window-pool initialization.
                yield return null;
                os = OS_Handle.Instance;
            }

            if (os == null || !os.IsOSVisible || (manager != null && manager.IsTransitioning)
                || os.MemoryWindowController == null)
            {
                SetJumpStatus("status_os_not_ready", "系统尚未就绪，请稍后重试。");
                Debug.LogWarning("[GameTest] 跳转失败：OS / 回忆窗口未能完成初始化。", this);
                yield break;
            }

            // Demo mode may have changed while the OS scene was loading.
            if (!IsMemoryIndexAvailable(dataIndex))
            {
                SetJumpStatus("status_demo_unavailable", "Demo 模式仅开放序章、第一章和第二章。");
                yield break;
            }

            if (prepareHomeTest)
                PrepareMemoryHomeTest(os);

            // Already-visible OS jumps skip a phase transition, but still release
            // any prologue rain restarted by a debug scene load.
            if (MainGameHandle.Instance != null) MainGameHandle.Instance.SuspendSceneAmbientForOS();
            else MainGameHandle.StopRainAmbient();

            OSMemoryWindowController memory = os.MemoryWindowController;
            memory.DebugJumpToData(dataIndex);
            os.OpenMemoryWindow();
            OSWindowUnit window = memory.GetComponent<OSWindowUnit>();
            if (!window.IsOpen)
            {
                memory.PrepareRuntime();
                SetJumpStatus("status_memory_open_failed", "回忆窗口未能打开，请重试。");
                Debug.LogWarning("[GameTest] 回忆窗口无法打开，请检查任务栏对象池。", this);
                yield break;
            }
            os.NotifyTaskbarProcessPressed(window);
            SetJumpStatus("status_memory_complete", "已跳转至回忆 {0}。", true, dataIndex);
        }
        finally
        {
            memoryJumpRoutine = null;
        }
    }

    static void PrepareMemoryHomeTest(OS_Handle os)
    {
        GameDataManager data = GameDataManager.Instance;
        OSProcessSaveData process = data.OSProcessData;
        if (!process.installedToolProgramIds.Contains(OSToolProgramIds.MemoryRepair01))
            process.installedToolProgramIds.Add(OSToolProgramIds.MemoryRepair01);
        if (!process.installedToolProgramIds.Contains(OSToolProgramIds.MemoryRepair02))
            process.installedToolProgramIds.Add(OSToolProgramIds.MemoryRepair02);

        // Each press starts a fresh local playthrough without clearing other OS progress.
        process.memory.pictureScenario = new OSMemoryPicSaveData();
        process.memory.interactiveSceneDataIndex = OSMemoryProcessSaveData.InvalidDataIndex;
        data.SetCurrentOSMemoryData(OSMemoryData.Chapter01Upgrade02DataIndex);

        // Refresh the installed icon even when the desktop was already open.
        foreach (DesktopProcessUnit unit in os.DesktopProcessUnits)
            if (unit != null && unit.ProcessKey == OSDesktopProcessKeys.Memory)
                os.InitializeDesktopProcessUnit(unit);
    }

    public LanguageState PreviewLanguage => previewLanguage;

    [ShowInInspector, ReadOnly, LabelText("Last Refreshed Texts")]
    public int LastRefreshedTextCount => lastRefreshedTextCount;

    [HorizontalGroup("Language Step")]
    [Button("Previous Language", ButtonSizes.Medium)]
    public void PreviewPreviousLanguage()
    {
        SetPreviewLanguage(GetAdjacentLanguage(-1));
    }

    [HorizontalGroup("Language Step")]
    [Button("Next Language", ButtonSizes.Medium)]
    public void PreviewNextLanguage()
    {
        SetPreviewLanguage(GetAdjacentLanguage(1));
    }

    [Button("Apply Selected Language", ButtonSizes.Medium)]
    public void ApplyPreviewLanguage()
    {
        LocalizationData_SO data = ResolveLocalizationData();
        if (data == null)
        {
            Debug.LogWarning(
                "[GameTest] Localization data is not assigned.",
                this);
            return;
        }

        GameDataManager dataManager = GameDataManager.Instance;
        if (Application.isPlaying && dataManager != null)
            dataManager.SetCurrentLanguage(previewLanguage);

        OSLocalizedText[] localizedTexts =
            UnityEngine.Object.FindObjectsByType<OSLocalizedText>(
                FindObjectsInactive.Include);

#if UNITY_EDITOR
        if (!Application.isPlaying)
            GameTestLocalizationSaveGuard.MarkPreviewed(localizedTexts);
#endif

        if (localizedTexts.Length == 0)
        {
            Debug.LogWarning(
                "[GameTest] No loaded OSLocalizedText components found.",
                this);
        }

        lastRefreshedTextCount = 0;
        for (int i = 0; i < localizedTexts.Length; i++)
        {
            OSLocalizedText localizedText = localizedTexts[i];
            if (localizedText == null)
                continue;

            localizedText.Refresh(data, previewLanguage);
            TMP_Text targetText = localizedText.TargetText;
            if (targetText == null)
                continue;

            lastRefreshedTextCount++;
        }

#if UNITY_EDITOR
        EditorApplication.QueuePlayerLoopUpdate();
        SceneView.RepaintAll();
#endif
    }

    [Button("Restore Simplified Chinese", ButtonSizes.Medium)]
    public void RestoreSimplifiedChinese()
    {
        SetPreviewLanguage(LanguageState.zh_CN);
    }

    LocalizationData_SO ResolveLocalizationData()
    {
        if (localizationData != null)
            return localizationData;

        GameDataManager dataManager = GameDataManager.Instance;
        return dataManager != null ? dataManager.LocalizationData : null;
    }

    LanguageState GetAdjacentLanguage(int offset)
    {
        int currentIndex = Array.IndexOf(Languages, previewLanguage);
        if (currentIndex < 0)
            currentIndex = 0;

        int nextIndex = (currentIndex + offset + Languages.Length)
            % Languages.Length;
        return Languages[nextIndex];
    }

    void SetPreviewLanguage(LanguageState language)
    {
        previewLanguage = language;
        ApplyPreviewLanguage();
    }

#if UNITY_EDITOR
    internal void ResetLocalizationPreviewState()
    {
        previewLanguage = LanguageState.zh_CN;
        lastRefreshedTextCount = 0;
        EditorApplication.QueuePlayerLoopUpdate();
    }
#endif
}

#if UNITY_EDITOR
[InitializeOnLoad]
static class GameTestLocalizationSaveGuard
{
    const string SessionKey =
        "MrTGame.GameTest.LocalizationPreviewScenePaths";

    static readonly HashSet<string> previewedScenePaths =
        new HashSet<string>(StringComparer.Ordinal);
    static readonly Dictionary<ulong, string> pendingScenePaths =
        new Dictionary<ulong, string>();

    static GameTestLocalizationSaveGuard()
    {
        string serialized = SessionState.GetString(
            SessionKey,
            string.Empty);
        string[] paths = serialized.Split(
            new[] { '\n' },
            StringSplitOptions.RemoveEmptyEntries);
        for (int i = 0; i < paths.Length; i++)
            previewedScenePaths.Add(paths[i]);

        EditorSceneManager.sceneSaving -= OnSceneSaving;
        EditorSceneManager.sceneSaving += OnSceneSaving;
        EditorSceneManager.sceneSaved -= OnSceneSaved;
        EditorSceneManager.sceneSaved += OnSceneSaved;
        EditorSceneManager.sceneClosed -= OnSceneClosed;
        EditorSceneManager.sceneClosed += OnSceneClosed;
    }

    internal static void MarkPreviewed(OSLocalizedText[] localizedTexts)
    {
        bool changed = false;
        for (int i = 0; i < localizedTexts.Length; i++)
        {
            OSLocalizedText localizedText = localizedTexts[i];
            if (localizedText == null)
                continue;

            Scene scene = localizedText.gameObject.scene;
            if (!scene.IsValid() || string.IsNullOrEmpty(scene.path))
                continue;

            changed |= previewedScenePaths.Add(scene.path);
        }

        if (changed)
            PersistPreviewedScenePaths();
    }

    static void OnSceneSaving(Scene scene, string path)
    {
        if (Application.isPlaying
            || string.IsNullOrEmpty(scene.path)
            || !previewedScenePaths.Contains(scene.path))
        {
            return;
        }

        LocalizationData_SO data = AssetDatabase.LoadAssetAtPath<
            LocalizationData_SO>("Assets/Data/LocalizationData.asset");
        OSLocalizedText[] localizedTexts =
            UnityEngine.Object.FindObjectsByType<OSLocalizedText>(
                FindObjectsInactive.Include);
        bool restoredAny = false;
        for (int i = 0; i < localizedTexts.Length; i++)
        {
            OSLocalizedText localizedText = localizedTexts[i];
            if (localizedText == null
                || localizedText.gameObject.scene != scene)
            {
                continue;
            }

            localizedText.RestoreAuthoringText(
                data,
                LanguageState.zh_CN);
            TMP_Text targetText = localizedText.TargetText;
            if (targetText == null)
                continue;

            restoredAny = true;
            EditorUtility.SetDirty(localizedText);
            EditorUtility.SetDirty(targetText);
        }

        if (restoredAny)
            EditorSceneManager.MarkSceneDirty(scene);

        pendingScenePaths[scene.handle.GetRawData()] = scene.path;
    }

    static void OnSceneSaved(Scene scene)
    {
        if (!pendingScenePaths.TryGetValue(
                scene.handle.GetRawData(),
                out string previewedPath))
        {
            return;
        }

        pendingScenePaths.Remove(scene.handle.GetRawData());
        previewedScenePaths.Remove(previewedPath);
        previewedScenePaths.Remove(scene.path);
        PersistPreviewedScenePaths();
        ResetGameTestStateIfPreviewFinished();
    }

    static void OnSceneClosed(Scene scene)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            return;

        pendingScenePaths.Remove(scene.handle.GetRawData());
        if (!string.IsNullOrEmpty(scene.path)
            && previewedScenePaths.Remove(scene.path))
        {
            PersistPreviewedScenePaths();
            ResetGameTestStateIfPreviewFinished();
        }
    }

    static void ResetGameTestStateIfPreviewFinished()
    {
        if (previewedScenePaths.Count > 0)
            return;

        GameTest[] gameTests =
            UnityEngine.Object.FindObjectsByType<GameTest>(
                FindObjectsInactive.Include);
        for (int i = 0; i < gameTests.Length; i++)
            gameTests[i]?.ResetLocalizationPreviewState();
    }

    static void PersistPreviewedScenePaths()
    {
        SessionState.SetString(
            SessionKey,
            string.Join("\n", previewedScenePaths));
    }
}
#endif
