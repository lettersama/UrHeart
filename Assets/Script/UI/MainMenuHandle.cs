using System.Collections;
using MrTGame.Core;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Serialization;
using UnityEngine.UI;
using TMPro;

[DisallowMultipleComponent]
[RequireComponent(typeof(Canvas))]
[RequireComponent(typeof(CanvasGroup))]
[RequireComponent(typeof(GraphicRaycaster))]
public sealed class MainMenuHandle : Singleton<MainMenuHandle>
{
    const string DefaultBlurShaderName = "UI/MainMenu/OptionLiveBlur";
    const string MainMenuAmbientKey = "Main_Core";
    const string ThunderSfxKey = "Thunder_ForMainCore";
    const string MainMenuButtonSfxKey = "UI_ComfrimButton";
    const string MainMenuOptionButtonSfxKey = "UI_CancelButton";

    enum LampIgnitionMode
    {
        Direct,
        DoubleFlicker,
        Fade
    }

    [Header("Presentation")]
    [SerializeField] Canvas mainMenuCanvas;
    [SerializeField] CanvasGroup mainMenuCanvasGroup;
    [SerializeField] GraphicRaycaster graphicRaycaster;
    [SerializeField] GameObject mainMenuPanel;
    [SerializeField] GameObject mainMenuButtons;

    [Header("Lamp Loop")]
    [SerializeField] Image mainMenuBackground;
    [SerializeField] Image lampTransitionOverlay;
    [SerializeField] RawImage lampDustOverlay;
    [SerializeField] Sprite lightOffSprite;
    [SerializeField] Sprite lightOnSprite;
    [SerializeField] Sprite completedLightOffSprite;
    [SerializeField] Sprite completedLightOnSprite;
    [SerializeField, Min(0.01f)] float lightOnDurationMin = 8f;
    [SerializeField, Min(0.01f)] float lightOnDurationMax = 12f;
    [SerializeField, Min(0.01f)] float lightOffDurationMin = 3f;
    [SerializeField, Min(0.01f)] float lightOffDurationMax = 4f;
    [SerializeField, Min(0.01f)] float firstFlickerOnDuration = 0.06f;
    [SerializeField, Min(0.01f)] float firstFlickerOffDuration = 0.12f;
    [SerializeField, Min(0.01f)] float secondFlickerOnDuration = 0.08f;
    [SerializeField, Min(0.01f)] float secondFlickerOffDuration = 0.18f;
    [SerializeField, Min(0.01f)] float fadeIgnitionDuration = 0.8f;

    [Header("Thunder Flash")]
    [SerializeField] Image thunderFlash;
    [SerializeField, Min(0.01f)] float thunderIntervalMin = 15f;
    [SerializeField, Min(0.01f)] float thunderIntervalMax = 25f;
    [SerializeField, Range(0f, 1f)] float thunderFirstFlashAlpha = 0.38f;
    [SerializeField, Range(0f, 1f)] float thunderSecondFlashAlpha = 0.72f;
    [SerializeField, Min(0.01f)] float thunderFadeOutDuration = 0.4f;
    [SerializeField, Min(0f)] float thunderSfxDelay = 0.2f;

    [Header("Main Menu Audio")]
    [SerializeField, Min(0f)] float mainMenuAmbientFadeDuration = 1f;

    [Header("Option Panel")]
    [SerializeField] Canvas optionCanvas;
    [SerializeField] GameObject optionPanel;
    [SerializeField] RawImage optionBackground;
    [SerializeField] Material optionBackgroundMaterial;
    [FormerlySerializedAs("screenshotDownsample")]
    [SerializeField, Min(1)] int optionRenderDownsample = 2;
    [FormerlySerializedAs("screenshotBlurRadius")]
    [SerializeField, Min(0f)] float optionBlurRadius = 2f;
    [FormerlySerializedAs("screenshotDarkness")]
    [SerializeField, Range(0f, 1f)] float optionDarkness = 0.1f;

    [Header("Automatic Save Dialog")]
    [SerializeField] GameObject saveDialog;
    [SerializeField] TMP_Text saveDialogTitle;
    [SerializeField] TMP_Text saveDialogMessage;
    [SerializeField] UnityEngine.UI.Button saveDialogAccept;
    [SerializeField] UnityEngine.UI.Button saveDialogCancel;
    [SerializeField] TMP_Text saveDialogAcceptLabel;
    [SerializeField] TMP_Text saveDialogCancelLabel;
    bool confirmingNewGame;
    string saveErrorKey;
    string saveErrorFallback;

    [Header("OS Camera")]
    [SerializeField] string osUiLayerName = "OSUI";
    [SerializeField, Min(0.01f)] float canvasPlaneDistance = 1f;

    public bool IsMainMenuVisible { get; private set; }
    public bool IsOptionPanelVisible => optionPanel != null && optionPanel.activeSelf;
    public bool IsLampOn { get; private set; }
    public bool IsCarnationBackground => !GameSetup.IsDemo
        && GameAutosaveService.Instance?.HasEverCompletedGame == true
        && completedLightOffSprite != null && completedLightOnSprite != null;
    Sprite ActiveLightOffSprite => IsCarnationBackground ? completedLightOffSprite : lightOffSprite;
    Sprite ActiveLightOnSprite => IsCarnationBackground ? completedLightOnSprite : lightOnSprite;

    Coroutine lampLoopRoutine;
    Coroutine thunderLoopRoutine;
    Coroutine thunderSfxRoutine;
    RenderTexture optionBackgroundTexture;
    Camera optionSourceCamera;
    System.Action<RenderTargetIdentifier, CommandBuffer> optionCaptureAction;
    Material runtimeOptionBackgroundMaterial;
    bool hasWarnedMissingOSCamera;
    bool isMainMenuAmbientPlaying;
    bool pendingMainMenuAmbient;

    protected override void Awake()
    {
        base.Awake();
        if (!IsSingletonInstance)
            return;

        ResolveReferences();
        ConfigureCanvasForOSCamera(warnIfMissing: false);
        ConfigureOptionCanvas();
        HideOptionPanelImmediate();
        if (saveDialog != null) saveDialog.SetActive(false);
        ApplyPresentation(false);
    }

    void OnEnable()
    {
        if (!IsSingletonInstance)
            return;

        GameEvents.PhaseChanged += OnPhaseChanged;
        GameEvents.LanguageChanged += OnLanguageChanged;
        if (GameAutosaveService.Instance != null)
            GameAutosaveService.Instance.StatusChanged += RefreshSaveAvailability;
        saveDialogAccept?.onClick.AddListener(HandleSaveDialogAccept);
        saveDialogCancel?.onClick.AddListener(CloseSaveDialog);
    }

    void OnDisable()
    {
        GameEvents.PhaseChanged -= OnPhaseChanged;
        GameEvents.LanguageChanged -= OnLanguageChanged;
        if (GameAutosaveService.Instance != null)
            GameAutosaveService.Instance.StatusChanged -= RefreshSaveAvailability;
        saveDialogAccept?.onClick.RemoveListener(HandleSaveDialogAccept);
        saveDialogCancel?.onClick.RemoveListener(CloseSaveDialog);
        if (saveDialog != null) saveDialog.SetActive(false);
        StopLampLoop();
        StopThunderLoop();
        StopMainMenuAmbient();
        HideOptionPanelImmediate();
        IsMainMenuVisible = false;
        SetMainMenuButtonsVisible(false);
        OSCameraHandle.Instance?.SetPresentationActive(this, false);
    }

    protected override void OnDestroy()
    {
        StopLampLoop();
        StopThunderLoop();
        HideOptionPanelImmediate();

        if (runtimeOptionBackgroundMaterial != null)
        {
            Destroy(runtimeOptionBackgroundMaterial);
            runtimeOptionBackgroundMaterial = null;
        }

        base.OnDestroy();
    }

    void Update()
    {
        if (IsOptionPanelVisible)
            RefreshOptionBackgroundIfNeeded();
        if (pendingMainMenuAmbient && IsMainMenuVisible
            && AudioManager.Instance?.IsBackgroundAudioSuppressed == false)
            PlayMainMenuAmbient();
    }

    public void InitializeMainMenu()
    {
        ResolveReferences();
        ConfigureCanvasForOSCamera();
        ConfigureOptionCanvas();
        HideOptionPanelImmediate();
        ApplyPresentation(true);
        RefreshSaveAvailability();
    }

    public void StartLampLoop()
    {
        ResolveReferences();
        StopLampLoopInternal(resetToOff: false);
        SetLampOn(false);

        if (!IsMainMenuVisible)
            return;

        if (mainMenuBackground == null
            || ActiveLightOffSprite == null
            || ActiveLightOnSprite == null)
        {
            Debug.LogError(
                "[MainMenu] Lamp background or light sprites are missing.",
                this);
            return;
        }

        lampLoopRoutine = StartCoroutine(LampLoop());
    }

    public void StopLampLoop()
    {
        StopLampLoopInternal(resetToOff: true);
    }

    public void StartThunderLoop()
    {
        ResolveReferences();
        StopThunderLoop();

        if (!IsMainMenuVisible)
            return;

        if (thunderFlash == null)
        {
            Debug.LogError("[MainMenu] ThunderFlash image is missing.", this);
            return;
        }

        thunderLoopRoutine = StartCoroutine(ThunderLoop());
    }

    public void StopThunderLoop()
    {
        if (thunderLoopRoutine != null)
        {
            StopCoroutine(thunderLoopRoutine);
            thunderLoopRoutine = null;
        }

        if (thunderSfxRoutine != null)
        {
            StopCoroutine(thunderSfxRoutine);
            thunderSfxRoutine = null;
        }

        SetThunderAlpha(0f);
    }

    public void SetLampOn(bool isOn)
    {
        ResolveReferences();
        if (!isOn)
            SetLampDustVisible(false);

        if (mainMenuBackground == null)
        {
            IsLampOn = false;
            return;
        }

        Sprite offSprite = ActiveLightOffSprite;
        Sprite onSprite = ActiveLightOnSprite;
        if (offSprite != null)
            mainMenuBackground.sprite = offSprite;

        if (lampTransitionOverlay != null && onSprite != null)
        {
            lampTransitionOverlay.sprite = onSprite;
            lampTransitionOverlay.raycastTarget = false;
            SetLampOverlayAlpha(isOn ? 1f : 0f);
            IsLampOn = isOn;
            SetLampDustVisible(isOn);
            return;
        }

        Sprite targetSprite = isOn ? onSprite : offSprite;
        if (targetSprite == null)
        {
            IsLampOn = false;
            SetLampDustVisible(false);
            return;
        }

        mainMenuBackground.sprite = targetSprite;
        IsLampOn = isOn;
        SetLampDustVisible(isOn);
    }

    public void InitializeOptionPanel()
    {
        ResolveReferences();
        if (!IsMainMenuVisible)
        {
            Debug.LogWarning(
                "[MainMenu] Cannot open the option panel while the main menu is hidden.",
                this);
            return;
        }

        if (optionPanel == null
            || optionBackground == null
            || !ConfigureOptionCanvas())
        {
            Debug.LogError(
                "[MainMenu] OptionPanel requires a RawImage on a separate Option Canvas.",
                this);
            return;
        }

        Camera osCamera = OSCameraHandle.Instance?.Camera;
        if (osCamera == null)
        {
            Debug.LogError(
                "[MainMenu] OS Camera is missing; the live option background cannot be rendered.",
                this);
            return;
        }

        Material backgroundMaterial = ResolveOptionBackgroundMaterial();
        if (backgroundMaterial == null)
            return;

        if (!BeginOptionBackgroundRendering(osCamera))
            return;

        SetMainMenuButtonsVisible(false);
        optionBackground.texture = optionBackgroundTexture;
        optionBackground.material = backgroundMaterial;
        optionBackground.enabled = true;
        optionCanvas.enabled = true;
        optionPanel.SetActive(true);
        SetMainMenuInteraction(false);
        InitializeOptionUI();
    }

    public void UnloadOptionPanel()
    {
        HideOptionPanelImmediate();
    }

    public void InitializeOptionUI()
    {
        RefreshLocalizedTexts(optionCanvas, GameDataManager.Instance);
        optionPanel
            ?.GetComponentInChildren<MainMenuOptionPanel>(true)
            ?.Initialize(this);
    }

    public void OpenContinueGamePanel()
    {
        if (GameSessionFlow.Instance?.ContinueGame() == true)
        {
            SetMainMenuInteraction(false);
            StopMainMenuAmbient();
        }
    }

    public void OpenNewGamePanel()
    {
        if (GameSessionFlow.Instance == null || GameSessionFlow.Instance.IsBusy
            || GameManager.Instance?.Phase != GamePhase.Boot)
            return;
        if (GameAutosaveService.Instance?.HasSave == true)
        {
            confirmingNewGame = true;
            saveErrorKey = null;
            OpenSaveDialog();
            return;
        }
        StartConfirmedNewGame();
    }

    public void ConfirmNewGame()
    {
        if (!confirmingNewGame || saveDialog == null || !saveDialog.activeSelf) return;
        StartConfirmedNewGame();
    }

    void StartConfirmedNewGame()
    {
        CloseSaveDialog();
        if (GameSessionFlow.Instance?.StartNewGame() == true)
        {
            SetMainMenuInteraction(false);
            StopMainMenuAmbient();
        }
    }

    public void RefreshSaveAvailability()
    {
        RefreshBackgroundSprites();
        bool canContinue = GameAutosaveService.Instance?.HasSave == true
            && GameSessionFlow.Instance?.IsBusy != true;
        foreach (var button in GetComponentsInChildren<MainMenuButton>(true))
            if (button.Action == MainMenuButtonAction.ContinueGame)
                button.SetInteractable(canContinue);
    }

    void RefreshBackgroundSprites()
    {
        // Preserve the current fade alpha and lamp timing when the receipt changes.
        Sprite background = lampTransitionOverlay == null && IsLampOn
            ? ActiveLightOnSprite : ActiveLightOffSprite;
        if (mainMenuBackground != null && background != null)
            mainMenuBackground.sprite = background;
        if (lampTransitionOverlay != null && ActiveLightOnSprite != null)
            lampTransitionOverlay.sprite = ActiveLightOnSprite;
    }

    public void ShowSaveError(string key, string fallback)
    {
        confirmingNewGame = false;
        saveErrorKey = key;
        saveErrorFallback = fallback;
        OpenSaveDialog();
    }

    void OpenSaveDialog()
    {
        if (saveDialog == null)
        {
            Debug.LogError("[MainMenu] Automatic save dialog is not authored.", this);
            return;
        }
        SetMainMenuButtonsVisible(false);
        SetMainMenuInteraction(true);
        saveDialog.SetActive(true);
        saveDialog.transform.SetAsLastSibling();
        RefreshSaveDialogText();
    }

    void HandleSaveDialogAccept()
    {
        if (GameSessionFlow.Instance?.IsBusy == true) return;
        PlayOptionButtonSfx();
        if (confirmingNewGame) StartConfirmedNewGame();
        else CloseSaveDialog();
    }

    public void CloseSaveDialog()
    {
        if (saveDialog != null) saveDialog.SetActive(false);
        confirmingNewGame = false;
        SetMainMenuButtonsVisible(IsMainMenuVisible && !IsOptionPanelVisible);
        SetMainMenuInteraction(IsMainMenuVisible && !IsOptionPanelVisible);
        RefreshSaveAvailability();
    }

    void RefreshSaveDialogText()
    {
        if (saveDialog == null || !saveDialog.activeSelf) return;
        SetSaveText(saveDialogTitle, confirmingNewGame ? "ui.save.new_title" : "ui.save.error_title",
            confirmingNewGame ? "开始新游戏？" : "自动存档");
        SetSaveText(saveDialogMessage, confirmingNewGame ? "ui.save.new_body" : saveErrorKey,
            confirmingNewGame ? "当前自动存档将被覆盖。语言和游戏设置会保留。" : saveErrorFallback);
        SetSaveText(saveDialogAcceptLabel, confirmingNewGame ? "ui.save.start_new" : "ui.save.ok",
            confirmingNewGame ? "开始新游戏" : "知道了");
        SetSaveText(saveDialogCancelLabel, "ui.save.cancel", "取消");
        if (saveDialogCancel != null) saveDialogCancel.gameObject.SetActive(confirmingNewGame);
        if (saveDialogAccept != null)
        {
            var rect = (RectTransform)saveDialogAccept.transform;
            rect.anchoredPosition = new Vector2(confirmingNewGame ? -79f : 0f, rect.anchoredPosition.y);
        }
    }

    static void SetSaveText(TMP_Text target, string key, string fallback)
    {
        if (target == null) return;
        target.text = GameDataManager.Instance?.GetLocalizedText(key, fallback) ?? fallback;
        OSLocalizedFontUtility.ApplyCurrent(target);
    }

    public void OpenOptionPanel()
    {
        InitializeOptionPanel();
    }

    public void ExitGame()
    {
        if (GameAutosaveService.Instance != null && !GameAutosaveService.Instance.FlushNow())
        {
            ShowSaveError("ui.save.write_failed", "自动存档写入失败，请稍后重试。");
            return;
        }
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    public void HandleButton(MainMenuButtonAction action)
    {
        AudioManager.Instance?.PlaySfx(MainMenuButtonSfxKey);

        switch (action)
        {
            case MainMenuButtonAction.ContinueGame:
                OpenContinueGamePanel();
                break;

            case MainMenuButtonAction.StartGame:
                OpenNewGamePanel();
                break;

            case MainMenuButtonAction.Option:
                OpenOptionPanel();
                break;

            case MainMenuButtonAction.ExitGame:
                ExitGame();
                break;

            default:
                Debug.LogError($"[MainMenu] Unsupported button action: {action}.", this);
                break;
        }
    }

    public void PlayOptionButtonSfx()
    {
        AudioManager.Instance?.PlaySfx(MainMenuOptionButtonSfxKey);
    }

    IEnumerator LampLoop()
    {
        while (IsMainMenuVisible)
        {
            yield return new WaitForSecondsRealtime(RandomDuration(
                lightOffDurationMin,
                lightOffDurationMax));

            if (!IsMainMenuVisible)
                break;

            yield return PlayLampIgnition();

            if (!IsMainMenuVisible)
                break;

            yield return new WaitForSecondsRealtime(RandomDuration(
                lightOnDurationMin,
                lightOnDurationMax));

            if (!IsMainMenuVisible)
                break;

            SetLampOn(false);
        }

        lampLoopRoutine = null;
    }

    IEnumerator PlayLampIgnition()
    {
        LampIgnitionMode mode = SelectLampIgnitionMode();
        switch (mode)
        {
            case LampIgnitionMode.DoubleFlicker:
                yield return PlayDoubleFlickerIgnition();
                break;

            case LampIgnitionMode.Fade:
                yield return PlayFadeIgnition();
                break;

            default:
                SetLampOn(true);
                break;
        }
    }

    IEnumerator PlayDoubleFlickerIgnition()
    {
        SetLampOn(true);
        yield return new WaitForSecondsRealtime(firstFlickerOnDuration);
        SetLampOn(false);
        yield return new WaitForSecondsRealtime(firstFlickerOffDuration);

        SetLampOn(true);
        yield return new WaitForSecondsRealtime(secondFlickerOnDuration);
        SetLampOn(false);
        yield return new WaitForSecondsRealtime(secondFlickerOffDuration);

        if (IsMainMenuVisible)
            SetLampOn(true);
    }

    IEnumerator PlayFadeIgnition()
    {
        ResolveReferences();
        if (lampTransitionOverlay == null || ActiveLightOnSprite == null)
        {
            SetLampOn(true);
            yield break;
        }

        SetLampOn(false);
        lampTransitionOverlay.sprite = ActiveLightOnSprite;
        float duration = Mathf.Max(0.01f, fadeIgnitionDuration);
        float elapsed = 0f;
        while (elapsed < duration
            && IsMainMenuVisible
            && lampTransitionOverlay != null)
        {
            elapsed += Time.unscaledDeltaTime;
            float progress = Mathf.Clamp01(elapsed / duration);
            SetLampOverlayAlpha(Mathf.SmoothStep(0f, 1f, progress));
            yield return null;
        }

        if (IsMainMenuVisible)
            SetLampOn(true);
    }

    IEnumerator ThunderLoop()
    {
        while (IsMainMenuVisible)
        {
            yield return new WaitForSecondsRealtime(RandomDuration(
                thunderIntervalMin,
                thunderIntervalMax));

            if (!IsMainMenuVisible)
                break;

            StartThunderSfxDelay();
            yield return FadeThunderTo(thunderFirstFlashAlpha, 0.025f);
            yield return new WaitForSecondsRealtime(0.04f);
            yield return FadeThunderTo(0f, 0.06f);
            yield return new WaitForSecondsRealtime(0.08f);
            yield return FadeThunderTo(thunderSecondFlashAlpha, 0.035f);
            yield return new WaitForSecondsRealtime(0.05f);
            yield return FadeThunderTo(0f, thunderFadeOutDuration);
        }

        SetThunderAlpha(0f);
        thunderLoopRoutine = null;
    }

    void StartThunderSfxDelay()
    {
        if (thunderSfxRoutine != null)
            StopCoroutine(thunderSfxRoutine);

        thunderSfxRoutine = StartCoroutine(PlayThunderSfxDelayed());
    }

    IEnumerator PlayThunderSfxDelayed()
    {
        float delay = Mathf.Max(0f, thunderSfxDelay);
        if (delay > 0f)
            yield return new WaitForSecondsRealtime(delay);

        if (IsMainMenuVisible)
            AudioManager.Instance?.PlaySfx(ThunderSfxKey);

        thunderSfxRoutine = null;
    }

    IEnumerator FadeThunderTo(float targetAlpha, float duration)
    {
        if (thunderFlash == null)
            yield break;

        float startAlpha = thunderFlash.color.a;
        float safeDuration = Mathf.Max(0.001f, duration);
        float elapsed = 0f;
        while (elapsed < safeDuration
            && IsMainMenuVisible
            && thunderFlash != null)
        {
            elapsed += Time.unscaledDeltaTime;
            SetThunderAlpha(Mathf.Lerp(
                startAlpha,
                targetAlpha,
                Mathf.Clamp01(elapsed / safeDuration)));
            yield return null;
        }

        if (IsMainMenuVisible)
            SetThunderAlpha(targetAlpha);
    }

    bool BeginOptionBackgroundRendering(Camera osCamera)
    {
        if (optionSourceCamera != null && optionSourceCamera != osCamera)
            StopOptionBackgroundRendering();

        RecreateOptionBackgroundTextureIfNeeded(force: false);
        if (optionBackgroundTexture == null
            || !optionBackgroundTexture.IsCreated())
        {
            StopOptionBackgroundRendering();
            Debug.LogError(
                "[MainMenu] Failed to allocate the live option background.",
                this);
            return false;
        }

        if (!CameraCaptureBridge.enabled)
        {
            StopOptionBackgroundRendering();
            Debug.LogError(
                "[MainMenu] The active render pipeline does not support camera capture.",
                this);
            return false;
        }

        if (optionSourceCamera == null)
            optionSourceCamera = osCamera;

        if (optionCaptureAction == null)
        {
            optionCaptureAction = CaptureOptionBackground;
            CameraCaptureBridge.AddCaptureAction(
                optionSourceCamera,
                optionCaptureAction);
        }

        return true;
    }

    void RefreshOptionBackgroundIfNeeded()
    {
        if (optionSourceCamera == null)
        {
            HideOptionPanelImmediate();
            return;
        }

        RenderTexture previousTexture = optionBackgroundTexture;
        RecreateOptionBackgroundTextureIfNeeded(force: false);
        if (optionBackgroundTexture == null)
        {
            HideOptionPanelImmediate();
            return;
        }

        if (optionBackgroundTexture != previousTexture)
            optionBackground.texture = optionBackgroundTexture;
    }

    void CaptureOptionBackground(
        RenderTargetIdentifier source,
        CommandBuffer commandBuffer)
    {
        if (commandBuffer == null
            || optionBackgroundTexture == null
            || !optionBackgroundTexture.IsCreated())
        {
            return;
        }

        commandBuffer.Blit(
            source,
            new RenderTargetIdentifier(optionBackgroundTexture));
    }

    void RecreateOptionBackgroundTextureIfNeeded(bool force)
    {
        int downsample = Mathf.Max(1, optionRenderDownsample);
        int width = Mathf.Max(1, Screen.width / downsample);
        int height = Mathf.Max(1, Screen.height / downsample);
        if (!force
            && optionBackgroundTexture != null
            && optionBackgroundTexture.width == width
            && optionBackgroundTexture.height == height
            && optionBackgroundTexture.IsCreated())
        {
            return;
        }

        ReleaseOptionBackgroundTexture();
        optionBackgroundTexture = new RenderTexture(
            width,
            height,
            0,
            RenderTextureFormat.ARGB32,
            RenderTextureReadWrite.Default)
        {
            name = "MainMenu Option Live Background",
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp,
            useMipMap = false,
            autoGenerateMips = false,
            hideFlags = HideFlags.DontSave
        };
        optionBackgroundTexture.Create();
    }

    void PlayMainMenuAmbient()
    {
        if (isMainMenuAmbientPlaying)
            return;

        // The menu can become visible before the previous story's next Update
        // releases its audio lease. Retry once it is free instead of claiming
        // that a playback request rejected by AudioManager has already started.
        pendingMainMenuAmbient = AudioManager.Instance?.IsBackgroundAudioSuppressed == true;
        if (pendingMainMenuAmbient)
            return;

        AudioData ambient = GameDataManager.Instance?.AudioData?.AMBIENT?.Find(
            item => item != null && item.keyid == MainMenuAmbientKey);
        if (ambient?.audioclip == null)
        {
            Debug.LogWarning(
                $"[MainMenu] Ambient '{MainMenuAmbientKey}' is missing.",
                this);
            return;
        }

        AudioManager audioManager = AudioManager.Instance;
        if (audioManager == null)
        {
            Debug.LogWarning(
                "[MainMenu] AudioManager is missing; menu ambient was not played.",
                this);
            return;
        }

        audioManager.PlayAmbient(
            ambient.audioclip,
            Mathf.Max(0f, mainMenuAmbientFadeDuration));
        isMainMenuAmbientPlaying = true;
    }

    void StopMainMenuAmbient()
    {
        pendingMainMenuAmbient = false;
        if (!isMainMenuAmbientPlaying)
            return;

        isMainMenuAmbientPlaying = false;
        AudioManager.Instance?.StopAmbient(
            Mathf.Max(0f, mainMenuAmbientFadeDuration));
    }

    void OnPhaseChanged(GamePhase phase)
    {
        ApplyPresentation(
            GameSetup.UseFormalFlow && phase == GamePhase.Boot);
    }

    void OnLanguageChanged(LanguageState language)
    {
        RefreshLocalization();
    }

    void RefreshLocalization()
    {
        GameDataManager dataManager = GameDataManager.Instance;
        RefreshLocalizedTexts(mainMenuCanvas, dataManager);
        RefreshSaveDialogText();
        if (optionCanvas != mainMenuCanvas)
            RefreshLocalizedTexts(optionCanvas, dataManager);
    }

    static void RefreshLocalizedTexts(Canvas canvas, GameDataManager dataManager)
    {
        if (canvas == null)
            return;

        LocalizationData_SO localizationData = dataManager != null ? dataManager.LocalizationData : null;
        LanguageState language = dataManager != null ? dataManager.CurrentLanguageState : LanguageState.zh_CN;
        OSLocalizedText[] texts =
            canvas.GetComponentsInChildren<OSLocalizedText>(true);
        for (int i = 0; i < texts.Length; i++)
        {
            OSLocalizedText text = texts[i];
            if (text == null)
                continue;

            text.Initialize();
            text.Refresh(localizationData, language);
            if (text.TargetText != null)
                text.TargetText.text = OSPalette.TintRichText(text.TargetText.text);
        }
    }

    void ApplyPresentation(bool visible)
    {
        ResolveReferences();
        if (visible)
        {
            ConfigureCanvasForOSCamera();
            // Loading a session rebuilds GameUI without changing the language.
            // Refresh the new menu before showing its authored default labels.
            RefreshLocalization();
        }
        else
            HideOptionPanelImmediate();

        IsMainMenuVisible = visible;

        if (mainMenuPanel != null)
            mainMenuPanel.SetActive(visible);

        SetMainMenuButtonsVisible(visible && !IsOptionPanelVisible);

        if (mainMenuCanvas != null)
            mainMenuCanvas.enabled = visible;

        if (graphicRaycaster != null)
            graphicRaycaster.enabled = visible;

        if (mainMenuCanvasGroup != null)
            mainMenuCanvasGroup.alpha = visible ? 1f : 0f;

        SetMainMenuInteraction(visible && !IsOptionPanelVisible);

        OSCameraHandle.Instance?.SetPresentationActive(this, visible);

        if (visible)
        {
            RefreshSaveAvailability();
            PlayMainMenuAmbient();
            StartLampLoop();
            StartThunderLoop();
        }
        else
        {
            if (saveDialog != null) saveDialog.SetActive(false);
            StopMainMenuAmbient();
            StopLampLoop();
            StopThunderLoop();
        }
    }

    bool ConfigureCanvasForOSCamera(bool warnIfMissing = true)
    {
        int osUiLayer = LayerMask.NameToLayer(osUiLayerName);
        if (osUiLayer < 0)
        {
            Debug.LogError(
                $"[MainMenu] Layer '{osUiLayerName}' does not exist.",
                this);
            return false;
        }

        SetLayerRecursively(transform, osUiLayer);

        OSCameraHandle cameraHandle = OSCameraHandle.Instance;
        Camera osCamera = cameraHandle != null ? cameraHandle.Camera : null;
        if (osCamera == null)
        {
            if (warnIfMissing && !hasWarnedMissingOSCamera)
            {
                hasWarnedMissingOSCamera = true;
                Debug.LogWarning(
                    "[MainMenu] OSCameraHandle or its Camera is missing; the menu cannot receive OS Volume effects.",
                    this);
            }

            return false;
        }

        hasWarnedMissingOSCamera = false;
        mainMenuCanvas.worldCamera = osCamera;
        mainMenuCanvas.renderMode = RenderMode.ScreenSpaceCamera;
        mainMenuCanvas.planeDistance = canvasPlaneDistance;
        return true;
    }

    bool ConfigureOptionCanvas()
    {
        if (optionCanvas == null && optionPanel != null)
        {
            Canvas parentCanvas = optionPanel.GetComponentInParent<Canvas>(true);
            if (parentCanvas != mainMenuCanvas)
                optionCanvas = parentCanvas;
        }

        if (optionCanvas == null || optionCanvas == mainMenuCanvas)
            return false;

        optionCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
        optionCanvas.worldCamera = null;

        if (optionBackground != null)
            optionBackground.raycastTarget = true;

        return true;
    }

    Material ResolveOptionBackgroundMaterial()
    {
        if (runtimeOptionBackgroundMaterial == null)
        {
            Material source = optionBackgroundMaterial;
            if (source != null)
            {
                runtimeOptionBackgroundMaterial = new Material(source);
            }
            else
            {
                Shader shader = Shader.Find(DefaultBlurShaderName);
                if (shader == null)
                {
                    Debug.LogError(
                        $"[MainMenu] Shader '{DefaultBlurShaderName}' is missing.",
                        this);
                    return null;
                }

                runtimeOptionBackgroundMaterial = new Material(shader);
            }

            runtimeOptionBackgroundMaterial.name = "MainMenu Option Background (Runtime)";
            runtimeOptionBackgroundMaterial.hideFlags = HideFlags.DontSave;
        }

        runtimeOptionBackgroundMaterial.SetFloat(
            "_BlurRadius",
            Mathf.Max(0f, optionBlurRadius));
        runtimeOptionBackgroundMaterial.SetFloat(
            "_Darkness",
            Mathf.Clamp01(optionDarkness));
        return runtimeOptionBackgroundMaterial;
    }

    void ResolveReferences()
    {
        if (mainMenuCanvas == null)
            mainMenuCanvas = GetComponent<Canvas>();

        if (mainMenuCanvasGroup == null)
            mainMenuCanvasGroup = GetComponent<CanvasGroup>();

        if (graphicRaycaster == null)
            graphicRaycaster = GetComponent<GraphicRaycaster>();

        if (mainMenuPanel == null)
            mainMenuPanel = transform.Find("MainMenuPanel")?.gameObject;

        if (mainMenuButtons == null && mainMenuPanel != null)
        {
            mainMenuButtons = mainMenuPanel.transform
                .Find("Buttons")
                ?.gameObject;
        }

        if (mainMenuBackground == null && mainMenuPanel != null)
        {
            mainMenuBackground = mainMenuPanel.transform
                .Find("MainMenuPanelBk")
                ?.GetComponent<Image>();
        }

        if (lampTransitionOverlay == null && mainMenuBackground != null)
        {
            lampTransitionOverlay = mainMenuBackground.transform
                .Find("LampTransitionOverlay")
                ?.GetComponent<Image>();
        }

        if (lampDustOverlay == null && mainMenuBackground != null)
        {
            lampDustOverlay = mainMenuBackground.transform
                .Find("LampDust")
                ?.GetComponent<RawImage>();
        }

        if (lightOffSprite == null && mainMenuBackground != null)
            lightOffSprite = mainMenuBackground.sprite;

        if (thunderFlash == null && mainMenuPanel != null)
        {
            thunderFlash = mainMenuPanel.transform
                .Find("RainScene/ThunderFlash")
                ?.GetComponent<Image>();
        }

        if (optionPanel == null)
        {
            optionPanel = transform.Find("OptionPanel")?.gameObject;
            if (optionPanel == null && optionCanvas != null)
                optionPanel = optionCanvas.transform.Find("OptionPanel")?.gameObject;
        }

        if (optionCanvas == null && optionPanel != null)
        {
            Canvas parentCanvas = optionPanel.GetComponentInParent<Canvas>(true);
            if (parentCanvas != mainMenuCanvas)
                optionCanvas = parentCanvas;
        }

        if (optionBackground == null && optionPanel != null)
        {
            optionBackground = optionPanel.transform
                .Find("RawImage")
                ?.GetComponent<RawImage>();
        }
    }

    void StopLampLoopInternal(bool resetToOff)
    {
        if (lampLoopRoutine != null)
        {
            StopCoroutine(lampLoopRoutine);
            lampLoopRoutine = null;
        }

        if (resetToOff)
            SetLampOn(false);
    }

    static float RandomDuration(float first, float second)
    {
        float minimum = Mathf.Max(0.01f, Mathf.Min(first, second));
        float maximum = Mathf.Max(minimum, Mathf.Max(first, second));
        return Random.Range(minimum, maximum);
    }

    static LampIgnitionMode SelectLampIgnitionMode()
    {
        int roll = Random.Range(0, 10);
        if (roll < 7)
            return LampIgnitionMode.Direct;

        return roll < 9
            ? LampIgnitionMode.DoubleFlicker
            : LampIgnitionMode.Fade;
    }

    void SetLampOverlayAlpha(float alpha)
    {
        if (lampTransitionOverlay == null)
            return;

        Color color = lampTransitionOverlay.color;
        color.a = Mathf.Clamp01(alpha);
        lampTransitionOverlay.color = color;
        lampTransitionOverlay.enabled = color.a > 0f;
    }

    void SetLampDustVisible(bool visible)
    {
        if (lampDustOverlay == null)
            return;

        lampDustOverlay.enabled = visible;
    }

    void SetThunderAlpha(float alpha)
    {
        if (thunderFlash == null)
            return;

        Color color = thunderFlash.color;
        color.a = Mathf.Clamp01(alpha);
        thunderFlash.color = color;
        thunderFlash.enabled = color.a > 0f;
    }

    void HideOptionPanelImmediate()
    {
        if (optionBackground != null)
        {
            optionBackground.texture = null;
            optionBackground.enabled = false;
        }

        if (optionPanel != null)
            optionPanel.SetActive(false);

        if (optionCanvas != null)
            optionCanvas.enabled = false;

        StopOptionBackgroundRendering();
        SetMainMenuButtonsVisible(IsMainMenuVisible);
        SetMainMenuInteraction(IsMainMenuVisible);
    }

    void StopOptionBackgroundRendering()
    {
        if (optionCaptureAction != null)
        {
            CameraCaptureBridge.RemoveCaptureAction(
                optionSourceCamera,
                optionCaptureAction);
        }

        optionCaptureAction = null;
        optionSourceCamera = null;
        ReleaseOptionBackgroundTexture();
    }

    void ReleaseOptionBackgroundTexture()
    {
        if (optionBackgroundTexture == null)
            return;

        if (optionBackgroundTexture.IsCreated())
            optionBackgroundTexture.Release();

        Destroy(optionBackgroundTexture);
        optionBackgroundTexture = null;
    }

    void SetMainMenuInteraction(bool interactable)
    {
        if (mainMenuCanvasGroup == null)
            return;

        mainMenuCanvasGroup.interactable = interactable;
        mainMenuCanvasGroup.blocksRaycasts = interactable;
    }

    void SetMainMenuButtonsVisible(bool visible)
    {
        if (mainMenuButtons != null)
            mainMenuButtons.SetActive(visible);
    }

    static void SetLayerRecursively(Transform root, int layer)
    {
        root.gameObject.layer = layer;
        for (int i = 0; i < root.childCount; i++)
            SetLayerRecursively(root.GetChild(i), layer);
    }
}
