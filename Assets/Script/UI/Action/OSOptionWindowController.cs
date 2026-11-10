using System;
using DG.Tweening;
using MrTGame.Core;
using TMPro;
using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(OSWindowUnit))]
public sealed class OSOptionWindowController : MonoBehaviour
{
    const float PageFadeDuration = 0.15f;
    const float PageButtonHoverAlpha = 0.3f;
    const float CommandButtonHoverAlpha = 1f;
    const string ConfirmButtonSfxKey = "UI_ComfrimButton";
    const string CancelButtonSfxKey = "UI_CancelButton";
    const float DesktopInset = 2f;

    OSWindowUnit window;
    readonly Vector3[] windowCorners = new Vector3[4];
    CanvasGroup audioPage;
    CanvasGroup screenPage;
    TMP_Text textSpeedLabel;
    OSOptionSwitch fullScreenSwitch;
    OSOptionSwitch postProcessingSwitch;
    OSOptionImageButton audioPageButton;
    OSOptionImageButton screenPageButton;
    OSOptionVolumeControl[] volumeControls =
        new OSOptionVolumeControl[0];
    Tween audioPageTween;
    Tween screenPageTween;
    int currentPageIndex;

    public event Action SaveAndReturnToMainMenuRequested;

    void Awake()
    {
        ResolveReferences();
        ConfigureControls();
        currentPageIndex = 0;
        ApplyPageState(currentPageIndex, false);
    }

    void OnEnable()
    {
        GameEvents.LanguageChanged -= HandleLanguageChanged;
        GameEvents.LanguageChanged += HandleLanguageChanged;
        ResolveReferences();
        ConfigureControls();
        currentPageIndex = 0;
        RefreshFromSave(false);
        ApplyPageState(currentPageIndex, false);
        window.OpenStarted -= HandleWindowOpenStarted;
        window.OpenStarted += HandleWindowOpenStarted;
        KeepWindowAccessible();
    }

    void OnDisable()
    {
        GameEvents.LanguageChanged -= HandleLanguageChanged;
        if (window != null) window.OpenStarted -= HandleWindowOpenStarted;
        KillPageTweens();
    }

    void LateUpdate() => KeepWindowAccessible();

    void HandleWindowOpenStarted(OSWindowUnit opened) => KeepWindowAccessible();

    void KeepWindowAccessible()
    {
        if (window == null || !window.IsOpen || window.IsMinimized
            || window.IsOpening || window.IsClosing)
            return;
        RectTransform rect = window.WindowRect;
        RectTransform desktop = rect != null ? rect.parent as RectTransform : null;
        if (desktop == null || desktop.rect.width <= DesktopInset * 2f
            || desktop.rect.height <= DesktopInset * 2f)
            return;

        // Settings pauses story timers, so its controls must remain reachable even
        // after a saved layout or a viewport change. Use desktop coordinates, not
        // a nested sorting Canvas or physical screen pixels.
        Rect available = desktop.rect;
        available.xMin += DesktopInset;
        available.xMax -= DesktopInset;
        available.yMin += DesktopInset;
        available.yMax -= DesktopInset;
        rect.GetWorldCorners(windowCorners);
        Vector2 min = desktop.InverseTransformPoint(windowCorners[0]);
        Vector2 max = min;
        for (int i = 1; i < windowCorners.Length; i++)
        {
            Vector2 corner = desktop.InverseTransformPoint(windowCorners[i]);
            min = Vector2.Min(min, corner);
            max = Vector2.Max(max, corner);
        }

        // If the desktop is smaller than the window, keep the title controls at
        // the upper right accessible instead of centering them off screen.
        Vector2 correction = new Vector2(
            max.x - min.x <= available.width
                ? Mathf.Clamp(0f, available.xMin - min.x, available.xMax - max.x)
                : available.xMax - max.x,
            max.y - min.y <= available.height
                ? Mathf.Clamp(0f, available.yMin - min.y, available.yMax - max.y)
                : available.yMax - max.y);
        if (correction.sqrMagnitude < 0.0001f)
            return;
        rect.anchoredPosition += correction;
        // Restore opens windows in a temporary stack before restoring their order.
        if (!OSSessionPersistence.IsRestoring)
            GameDataManager.Instance?.SetOSWindowLayout(window.CaptureLayout());
    }

    void OnDestroy()
    {
        KillPageTweens();
    }

    public void RefreshFromSave(bool animateSwitches)
    {
        GameDataManager dataManager = GameDataManager.Instance;
        if (dataManager == null)
            return;

        GameOptionSaveData options = dataManager.GameOptions;
        for (int i = 0; i < volumeControls.Length; i++)
        {
            OSOptionVolumeControl control = volumeControls[i];
            if (control == null)
                continue;

            GameOptionVolumeType type = GetVolumeType(i);
            control.SetValue(options.GetVolume(type));
        }

        fullScreenSwitch?.SetState(options.isFullScreen, animateSwitches);
        postProcessingSwitch?.SetState(
            options.isOSPostProcessingEnabled,
            animateSwitches);
        SetTextSpeedLabel(options.textSpeed);
    }

    public void SetVolume(GameOptionVolumeType type, int value)
    {
        GameDataManager dataManager = GameDataManager.Instance;
        if (dataManager == null)
            return;

        int savedValue = dataManager.SetGameOptionVolume(type, value);
        OSOptionVolumeControl control = FindVolumeControl(type);
        control?.SetValue(savedValue);
    }

    public void ToggleSwitch(OSOptionSwitch.SwitchType type)
    {
        GameDataManager dataManager = GameDataManager.Instance;
        if (dataManager == null)
            return;

        AudioManager.Instance?.PlaySfx(CancelButtonSfxKey);

        GameOptionSaveData options = dataManager.GameOptions;
        switch (type)
        {
            case OSOptionSwitch.SwitchType.FullScreen:
                bool fullScreen = !options.isFullScreen;
                dataManager.SetFullScreenOption(fullScreen);
                fullScreenSwitch?.SetState(fullScreen, true);
                break;
            case OSOptionSwitch.SwitchType.OSPostProcessing:
                bool postProcessing =
                    !options.isOSPostProcessingEnabled;
                dataManager.SetOSPostProcessingOption(postProcessing);
                postProcessingSwitch?.SetState(postProcessing, true);
                break;
        }
    }

    public void HandleButton(OSOptionImageButton.ButtonAction action)
    {
        PlayButtonSfx(action);

        switch (action)
        {
            case OSOptionImageButton.ButtonAction.AudioPage:
                SwitchPage(0);
                break;
            case OSOptionImageButton.ButtonAction.ScreenPage:
                SwitchPage(1);
                break;
            case OSOptionImageButton.ButtonAction.PreviousTextSpeed:
                ChangeTextSpeed(-1);
                break;
            case OSOptionImageButton.ButtonAction.NextTextSpeed:
                ChangeTextSpeed(1);
                break;
            case OSOptionImageButton.ButtonAction.SaveAndReturnToMainMenu:
                RequestSaveAndReturnToMainMenu();
                break;
            case OSOptionImageButton.ButtonAction.ExitGame:
                ExitGame();
                break;
        }
    }

    static void PlayButtonSfx(OSOptionImageButton.ButtonAction action)
    {
        string sfxKey;
        switch (action)
        {
            case OSOptionImageButton.ButtonAction.AudioPage:
            case OSOptionImageButton.ButtonAction.ScreenPage:
                sfxKey = ConfirmButtonSfxKey;
                break;

            case OSOptionImageButton.ButtonAction.PreviousTextSpeed:
            case OSOptionImageButton.ButtonAction.NextTextSpeed:
                sfxKey = CancelButtonSfxKey;
                break;

            default:
                return;
        }

        AudioManager.Instance?.PlaySfx(sfxKey);
    }

    public void RequestSaveAndReturnToMainMenu()
    {
        SaveAndReturnToMainMenuRequested?.Invoke();
        GameSessionFlow.Instance?.ReturnToMainMenu();
    }

    public void ExitGame()
    {
        if (GameAutosaveService.Instance != null && !GameAutosaveService.Instance.FlushNow())
        {
            AdditionHandle.Instance?.ShowItemAcquiredTip(GetLocalizedText("ui.save.write_failed", "自动存档写入失败，请稍后重试。"));
            return;
        }
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    public string GetLocalizedText(string key, string fallback)
    {
        OS_Handle osHandle = OS_Handle.Instance;
        return osHandle != null
            ? osHandle.GetLocalizedText(key, fallback)
            : fallback;
    }

    void SwitchPage(int pageIndex)
    {
        int clampedIndex = Mathf.Clamp(pageIndex, 0, 1);
        if (currentPageIndex == clampedIndex)
            return;

        currentPageIndex = clampedIndex;
        ApplyPageState(currentPageIndex, true);
    }

    void ApplyPageState(int pageIndex, bool animate)
    {
        KillPageTweens();
        bool showAudio = pageIndex == 0;
        audioPageButton?.SetSelected(showAudio, animate);
        screenPageButton?.SetSelected(!showAudio, animate);
        SetPageInteraction(audioPage, showAudio);
        SetPageInteraction(screenPage, !showAudio);

        if (!animate)
        {
            if (audioPage != null)
                audioPage.alpha = showAudio ? 1f : 0f;
            if (screenPage != null)
                screenPage.alpha = showAudio ? 0f : 1f;
            return;
        }

        audioPageTween = FadePage(
            audioPage,
            showAudio ? 1f : 0f,
            () => audioPageTween = null);
        screenPageTween = FadePage(
            screenPage,
            showAudio ? 0f : 1f,
            () => screenPageTween = null);
    }

    Tween FadePage(CanvasGroup page, float alpha, TweenCallback onComplete)
    {
        if (page == null)
            return null;

        return page
            .DOFade(alpha, PageFadeDuration)
            .SetEase(Ease.Linear)
            .SetUpdate(true)
            .SetLink(gameObject, LinkBehaviour.KillOnDisable)
            .OnKill(onComplete)
            .OnComplete(onComplete);
    }

    static void SetPageInteraction(CanvasGroup page, bool isEnabled)
    {
        if (page == null)
            return;

        page.interactable = isEnabled;
        page.blocksRaycasts = isEnabled;
    }

    void ChangeTextSpeed(int delta)
    {
        GameDataManager dataManager = GameDataManager.Instance;
        if (dataManager == null)
            return;

        int value = dataManager.SetTextSpeedOption(
            dataManager.GameOptions.textSpeed + delta);
        SetTextSpeedLabel(value);
    }

    void SetTextSpeedLabel(int value)
    {
        if (textSpeedLabel != null)
            textSpeedLabel.text = value.ToString();
    }

    void ResolveReferences()
    {
        if (window == null) window = GetComponent<OSWindowUnit>();
        Transform characterMask = transform.Find("CharacterMask");
        if (characterMask == null)
            return;

        Transform pages = characterMask.Find("Pages");
        if (audioPage == null)
            audioPage = pages?.Find("AudioPage")?.GetComponent<CanvasGroup>();
        if (screenPage == null)
            screenPage = pages?.Find("ScreenPage")?.GetComponent<CanvasGroup>();

        if (textSpeedLabel == null && screenPage != null)
        {
            textSpeedLabel = screenPage.transform
                .Find("TextSpeed/Num/Num")
                ?.GetComponent<TMP_Text>();
        }

        if (fullScreenSwitch == null && screenPage != null)
        {
            fullScreenSwitch = screenPage.transform
                .Find("FullScreen/Switch")
                ?.GetComponent<OSOptionSwitch>();
        }

        if (postProcessingSwitch == null && screenPage != null)
        {
            postProcessingSwitch = screenPage.transform
                .Find("PostProcessing/Switch")
                ?.GetComponent<OSOptionSwitch>();
        }

        if (audioPage != null)
        {
            volumeControls = new[]
            {
                FindVolumeControl("MasterVolume"),
                FindVolumeControl("MusicVolume"),
                FindVolumeControl("AmbientVolume"),
                FindVolumeControl("SFXVolume")
            };
        }
    }

    OSOptionVolumeControl FindVolumeControl(string volumeName)
    {
        return audioPage.transform
            .Find(volumeName + "/FloatHand")
            ?.GetComponent<OSOptionVolumeControl>();
    }

    OSOptionVolumeControl FindVolumeControl(GameOptionVolumeType type)
    {
        int index = (int)type;
        return index >= 0 && index < volumeControls.Length
            ? volumeControls[index]
            : null;
    }

    void ConfigureControls()
    {
        Transform characterMask = transform.Find("CharacterMask");
        if (characterMask == null)
            return;

        audioPageButton = ConfigureButton(
            characterMask.Find("PageButtons/AudioPageButton"),
            OSOptionImageButton.ButtonAction.AudioPage,
            PageButtonHoverAlpha);
        screenPageButton = ConfigureButton(
            characterMask.Find("PageButtons/DisPlayPageButton"),
            OSOptionImageButton.ButtonAction.ScreenPage,
            PageButtonHoverAlpha);
        ConfigureButton(
            characterMask.Find("Pages/ScreenPage/TextSpeed/LastButton"),
            OSOptionImageButton.ButtonAction.PreviousTextSpeed,
            CommandButtonHoverAlpha);
        ConfigureButton(
            characterMask.Find("Pages/ScreenPage/TextSpeed/NextButton"),
            OSOptionImageButton.ButtonAction.NextTextSpeed,
            CommandButtonHoverAlpha);
        ConfigureButton(
            characterMask.Find(
                "BottomButtons/SaveGameAndReturnMainMenu"),
            OSOptionImageButton.ButtonAction.SaveAndReturnToMainMenu,
            CommandButtonHoverAlpha);
        ConfigureButton(
            characterMask.Find("BottomButtons/ExitGame"),
            OSOptionImageButton.ButtonAction.ExitGame,
            CommandButtonHoverAlpha);

        fullScreenSwitch?.Configure(
            this,
            OSOptionSwitch.SwitchType.FullScreen);
        postProcessingSwitch?.Configure(
            this,
            OSOptionSwitch.SwitchType.OSPostProcessing);

        for (int i = 0; i < volumeControls.Length; i++)
        {
            volumeControls[i]?.Configure(this, GetVolumeType(i));
        }
    }

    OSOptionImageButton ConfigureButton(
        Transform target,
        OSOptionImageButton.ButtonAction action,
        float hoverAlpha)
    {
        OSOptionImageButton button =
            target?.GetComponent<OSOptionImageButton>();
        button?.Configure(this, action, hoverAlpha);
        return button;
    }

    void HandleLanguageChanged(LanguageState language)
    {
        RefreshFromSave(false);
    }

    static GameOptionVolumeType GetVolumeType(int index)
    {
        switch (index)
        {
            case 0:
                return GameOptionVolumeType.Master;
            case 1:
                return GameOptionVolumeType.Music;
            case 2:
                return GameOptionVolumeType.Ambient;
            case 3:
                return GameOptionVolumeType.Sfx;
            default:
                return GameOptionVolumeType.Master;
        }
    }

    void KillPageTweens()
    {
        if (audioPageTween != null)
        {
            Tween tween = audioPageTween;
            audioPageTween = null;
            tween.Kill(false);
        }

        if (screenPageTween != null)
        {
            Tween tween = screenPageTween;
            screenPageTween = null;
            tween.Kill(false);
        }
    }
}
