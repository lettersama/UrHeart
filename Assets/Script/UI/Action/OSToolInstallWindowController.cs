using System;
using MrTGame.Core;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

[DisallowMultipleComponent]
[RequireComponent(typeof(OSWindowUnit))]
public sealed class OSToolInstallWindowController : MonoBehaviour
{
    [SerializeField] OSToolWindowController owner;
    [SerializeField] TMP_Text programName;
    [SerializeField] TMP_Text progressText;
    [SerializeField] Image programIcon;
    [SerializeField] Image progressFill;
    [SerializeField, Min(0.1f)] float installationDuration = 3.4f;
    [SerializeField] UnityEvent<float> progressChanged = new UnityEvent<float>();

    OSWindowUnit window;
    OSToolProgramDefinition currentProgram;
    float elapsed;
    bool chapter04Installation;
    bool savedAllowsMove, savedAllowsResize, savedInteractable, savedRaycasts;
    CanvasGroup installationGroup;
    Sprite savedProgramIcon;
    string savedProgramName, savedProgressText;
    Transform savedChapter04Parent;
    Vector2 savedChapter04AnchorMin, savedChapter04AnchorMax, savedChapter04Pivot;
    Vector3 savedChapter04Scale;

    public bool IsInstalling => chapter04Installation || currentProgram != null;
    public bool IsChapter04Installation => chapter04Installation;
    public string DisplayedProgramName => programName != null ? programName.text : string.Empty;
    public float Progress => IsInstalling ? Mathf.Clamp01(elapsed /
        (chapter04Installation ? OSChapter04EntryDirector.InstallationSeconds : installationDuration)) : 0f;
    public event Action<OSToolProgramDefinition, OSToolInstallResult> InstallationFinished;

    void Awake() => BindWindow();

    void OnEnable()
    {
        BindWindow();
        GameEvents.LanguageChanged += HandleLanguageChanged;
        RefreshText();
    }

    void OnDisable() => GameEvents.LanguageChanged -= HandleLanguageChanged;

    void OnDestroy()
    {
        if (window != null)
            window.Closed -= HandleWindowClosed;
    }

    void BindWindow()
    {
        if (window != null)
            return;
        window = GetComponent<OSWindowUnit>();
        window.Closed += HandleWindowClosed;
        // Installers share the ordinary title drag handler, but never expose
        // close/minimize/pin chrome. Chapter-specific dismissal guards still
        // cover taskbar and Escape actions through OSWindowUnit.
        window.ConfigureInteraction(true, window.AllowsResize);
        foreach (var button in GetComponentsInChildren<OSWindowButton>(true))
            if (button.GetComponentInParent<OSWindowUnit>(true) == window) button.gameObject.SetActive(false);
    }

    public bool BeginInstallation(OSToolProgramDefinition program)
    {
        if (program == null || IsInstalling)
            return false;
        BindWindow();
        elapsed = 0f;
        currentProgram = program;
        programIcon.sprite = program.Icon;
        RefreshProgress();
        window.OpenStandalone();
        OS_Handle.Instance?.NotifyWindowPointerPressed(window);
        return true;
    }

    public void CancelInstallation()
    {
        if (chapter04Installation)
        {
            CancelChapter04Installation();
            return;
        }
        currentProgram = null;
        elapsed = 0f;
        if (window != null)
            window.CloseSilently();
    }

    void HandleWindowClosed(OSWindowUnit closedWindow)
    {
        if (chapter04Installation)
        {
            CancelChapter04Installation();
            return;
        }
        CancelInstallation();
        owner?.RequestInputFocus();
    }

    void Update()
    {
        // The entry director owns this clock and its saved progress.
        if (chapter04Installation)
            return;
        OS_Handle os = OS_Handle.Instance;
        if (!IsInstalling || !window.IsOpen || window.IsMinimized
            || (os != null && (!os.IsOSVisible || os.IsSystemInputLocked)))
            return;

        elapsed += Time.unscaledDeltaTime;
        RefreshProgress();
        if (elapsed < installationDuration)
            return;

        OSToolProgramDefinition program = currentProgram;
        currentProgram = null;
        OSToolInstallResult result = GameDataManager.Instance != null
            ? GameDataManager.Instance.TryInstallToolProgram(program.Id)
            : OSToolInstallResult.Unavailable;
        window.CloseSilently();
        if (result == OSToolInstallResult.Success)
            AudioManager.Instance?.PlaySfx("Chapter02_ChoiceConfirm", .45f);
        InstallationFinished?.Invoke(program, result);
        owner?.RequestInputFocus();
    }

    public bool BeginChapter04Installation(float elapsedSeconds = 0f, Sprite icon = null)
    {
        if (currentProgram != null)
            return false;
        BindWindow();
        bool starting = !chapter04Installation;
        if (starting)
        {
            savedAllowsMove = window.AllowsMove;
            savedAllowsResize = window.AllowsResize;
            installationGroup = GetComponent<CanvasGroup>();
            if (installationGroup == null)
                installationGroup = gameObject.AddComponent<CanvasGroup>();
            savedInteractable = installationGroup.interactable;
            savedRaycasts = installationGroup.blocksRaycasts;
            savedProgramIcon = programIcon != null ? programIcon.sprite : null;
            savedProgramName = programName != null ? programName.text : string.Empty;
            savedProgressText = progressText != null ? progressText.text : string.Empty;
            chapter04Installation = true;
            PrepareChapter04Layout();
            window.ConfigureInteraction(true, false);
        }
        elapsed = Mathf.Clamp(elapsedSeconds, 0f, OSChapter04EntryDirector.InstallationSeconds);
        if (programIcon != null)
            programIcon.sprite = icon != null ? icon : OSTVProgramAssets.Icon;
        RefreshProgress();
        // Session restoration may have opened this child while its old Tool
        // parent was inactive. Restore its content after moving to Windows.
        if (starting || !window.IsOpen || window.IsMinimized) window.OpenStandalone();
        // Begin behind the existing windows once. A user's title drag may then
        // bring the installer forward and retain its new position normally.
        if (starting) ApplyChapter04BackgroundPlacement();
        return true;
    }

    public void SetChapter04Progress(float elapsedSeconds)
    {
        if (!chapter04Installation)
            return;
        elapsed = Mathf.Clamp(elapsedSeconds, 0f, OSChapter04EntryDirector.InstallationSeconds);
        RefreshProgress();
    }

    public void FinishChapter04Installation()
    {
        if (!chapter04Installation)
            return;
        SetChapter04Progress(OSChapter04EntryDirector.InstallationSeconds);
        CancelChapter04Installation();
        AudioManager.Instance?.PlaySfx("Chapter02_ChoiceConfirm", .45f);
    }

    public void CancelChapter04Installation()
    {
        if (!chapter04Installation)
            return;
        // Silent dismissal deliberately does not return focus to the Tool console.
        window?.CloseSilently();
        chapter04Installation = false;
        elapsed = 0f;
        RestoreChapter04Layout();
        window?.ConfigureInteraction(savedAllowsMove, savedAllowsResize);
        if (installationGroup != null)
        {
            installationGroup.interactable = savedInteractable;
            installationGroup.blocksRaycasts = savedRaycasts;
        }
        if (programIcon != null) programIcon.sprite = savedProgramIcon;
        if (programName != null) programName.text = savedProgramName;
        if (progressText != null) progressText.text = savedProgressText;
        if (progressFill != null) progressFill.fillAmount = 0f;
    }

    void PrepareChapter04Layout()
    {
        RectTransform rect = window.WindowRect;
        if (rect == null) return;
        savedChapter04Parent = rect.parent;
        savedChapter04AnchorMin = rect.anchorMin;
        savedChapter04AnchorMax = rect.anchorMax;
        savedChapter04Pivot = rect.pivot;
        savedChapter04Scale = rect.localScale;
        Vector2 size = new Vector2(230f, 150f);
        // Capture the normal Tool-relative layout before changing parents.
        window.SetTemporaryOpeningLayout(size, rect.anchoredPosition, size);
        Transform windowsRoot = GetComponentInParent<OS_Handle>(true)?.transform.Find("Windows");
        if (windowsRoot != null) rect.SetParent(windowsRoot, false);
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(.5f, .5f);
        rect.localScale = Vector3.one;
        RectTransform bounds = rect.parent as RectTransform;
        float width = bounds != null && bounds.rect.width > 0f ? bounds.rect.width : 650f;
        // Memory stays centred; the 230px installer fits the free left column.
        window.SetOpeningLayout(new Vector2(-width * .5f + size.x * .5f + 4f, -10f), size);
    }

    void RestoreChapter04Layout()
    {
        RectTransform rect = window != null ? window.WindowRect : null;
        if (rect == null) return;
        if (savedChapter04Parent != null) rect.SetParent(savedChapter04Parent, false);
        rect.anchorMin = savedChapter04AnchorMin;
        rect.anchorMax = savedChapter04AnchorMax;
        rect.pivot = savedChapter04Pivot;
        rect.localScale = savedChapter04Scale;
        window.ClearTemporaryOpeningLayout();
        savedChapter04Parent = null;
    }

    void ApplyChapter04BackgroundPlacement()
    {
        if (transform.parent != null)
            transform.SetAsFirstSibling();
        if (installationGroup != null)
        {
            installationGroup.interactable = true;
            installationGroup.blocksRaycasts = true;
        }
    }

    void RefreshProgress()
    {
        float progress = Progress;
        if (progressFill != null)
            progressFill.fillAmount = progress;
        RefreshText();
        progressChanged.Invoke(progress);
    }

    void HandleLanguageChanged(LanguageState language) => RefreshText();

    void RefreshText()
    {
        if (chapter04Installation)
        {
            OSLocalizedFontUtility.ApplyCurrent(programName);
            OSLocalizedFontUtility.ApplyCurrent(progressText);
            if (programName != null)
                programName.text = OSToolProgramLibrary.Localize("ui.os.chapter04.installing", "少女安装中")
                    + new string('.', Mathf.FloorToInt(elapsed / .5f) % 4);
            if (progressText != null)
                progressText.text = Mathf.FloorToInt(Progress * 100f) + "%";
            return;
        }
        if (currentProgram == null)
            return;
        OSLocalizedFontUtility.ApplyCurrent(programName);
        OSLocalizedFontUtility.ApplyCurrent(progressText);
        programName.text = currentProgram.DisplayName;
        progressText.text = OSToolProgramLibrary.Localize("ui.os.tool.install.progress", "Installing... {0}%")
            .Replace("{0}", Mathf.FloorToInt(Progress * 100f).ToString());
    }
}
