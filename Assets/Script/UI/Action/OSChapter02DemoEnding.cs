using MrTGame.Core;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Owns the demo boundary after Chapter 2's thunder. The saved story remains at
/// Chapter03Ready; this separate flag only restores the demo's end card.
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(640)]
public sealed class OSChapter02DemoEnding : MonoBehaviour
{
    const string Prefix = "ui.os.chapter02_ending.";
    const string TemplatePath = "Chapter01EndingOverlay/EndCard";
    const int CanvasOrder = 1203;

    OS_Handle os;
    OSChapter02NightfallPresentation nightfall;
    OSMemoryProcessSaveData observed;
    GameObject card;
    TMP_Text title, message, returnLabel, exitLabel;
    Button returnButton, exitButton;
    OSLinkMessageController suppressedMessages;
    Canvas playerCursorCanvas;
    bool cursorWasEnabled, cursorOverrideSorting;
    int cursorOrder, cursorLayer;
    bool previousSuppression, returning, savePending, saveFailed, warnedTemplate;
    float nextSaveAttempt;

    public bool IsPresenting { get; private set; }
    OSMemoryProcessSaveData State => GameDataManager.Instance?.OSProcessData.memory;
    bool AtBoundary => GameDataManager.Instance?.CurrentChapterIndex == 2
        && State?.chapter02FinalTalkStep == (int)OSChapter02FinalTalkDirector.FinalTalkStep.Chapter03Ready;
    bool SessionAvailable => os != null && os.IsOSVisible
        && GameManager.Instance?.Phase == GamePhase.OSMain
        && GameManager.Instance?.IsTransitioning != true
        && !OSSessionPersistence.IsRestoring && GameSessionFlow.Instance?.IsBusy != true;

    void Awake() => os = GetComponent<OS_Handle>();
    void OnEnable()
    {
        GameEvents.LanguageChanged += RefreshLanguage;
        GameEvents.PhaseChanged += OnPhaseChanged;
    }
    void OnDisable()
    {
        UnbindNightfall();
        HideCard();
        GameEvents.LanguageChanged -= RefreshLanguage;
        GameEvents.PhaseChanged -= OnPhaseChanged;
    }
    void OnDestroy()
    {
        if (card != null) Destroy(card);
    }

    void Update()
    {
        if (os == null) os = GetComponent<OS_Handle>();
        if (!ReferenceEquals(observed, State))
        {
            HideCard();
            observed = State;
            returning = savePending = saveFailed = false;
        }
        BindNightfall();
        if (!GameSetup.IsDemo || !AtBoundary || observed?.chapter02DemoEndReached != true)
        {
            HideCard();
            return;
        }
        // Continue uncovers the OS while the session transaction is still busy.
        // Restore the black presentation first, then enable the card once that
        // transaction finishes, so no bright desktop flashes through its curtain.
        if (!OSSessionPersistence.IsRestoring) PrepareEndedNightfall();
        if (!SessionAvailable)
        {
            HideCard();
            return;
        }
        if (!IsPresenting) ShowCard();
        if (IsPresenting) RaisePlayerCursor();
        PersistCompletion();
    }

    void BindNightfall()
    {
        // FinalTalk creates the presentation before it can fire EndReached.
        // Polling handles scene startup and a component recreated in Play Mode.
        OSChapter02NightfallPresentation next = null;
        if (os != null) os.TryGetComponent(out next);
        if (next == nightfall) return;
        UnbindNightfall();
        nightfall = next;
        if (nightfall != null) nightfall.EndReached += OnEndReached;
    }
    void UnbindNightfall()
    {
        if (nightfall != null) nightfall.EndReached -= OnEndReached;
        nightfall = null;
    }

    void OnEndReached()
    {
        if (!GameSetup.IsDemo || !AtBoundary || State == null) return;
        observed = State;
        if (!observed.chapter02DemoEndReached)
        {
            observed.chapter02DemoEndReached = true;
            savePending = true;
            nextSaveAttempt = 0f;
        }
        if (SessionAvailable) ShowCard();
        PersistCompletion();
    }

    void PersistCompletion()
    {
        if (!savePending || !SessionAvailable || Time.unscaledTime < nextSaveAttempt) return;
        var saves = GameAutosaveService.Instance;
        if (saves == null || (!saves.SessionActive && !saves.IsTestSession)) return;
        // Flush the actual snapshot while still in OSMain. Test sessions retain
        // their existing no-write contract; failed writes remain retryable.
        bool succeeded = saves.FlushNow();
        savePending = !succeeded;
        saveFailed = !succeeded;
        nextSaveAttempt = Time.unscaledTime + 5f;
        RefreshText();
    }

    void ShowCard()
    {
        if (IsPresenting || !EnsureCard()) return;
        PrepareEndedNightfall();
        IsPresenting = true;
        returning = false;
        suppressedMessages = os.LinkMessageController;
        if (suppressedMessages != null)
        {
            previousSuppression = suppressedMessages.GlobalMessagesSuppressed;
            suppressedMessages.SetGlobalMessagesSuppressed(true);
            suppressedMessages.NotifyOSHidden();
        }
        RefreshText();
        SetButtonsInteractable(true);
        card.SetActive(true);
        // Enabling an inactive nested Canvas can restore its parent's sorting.
        // Apply the override after activation on every show, above nightfall.
        var cardCanvas = card.GetComponent<Canvas>();
        var parentCanvas = os.GetComponentInParent<Canvas>();
        cardCanvas.overrideSorting = true;
        if (parentCanvas != null) cardCanvas.sortingLayerID = parentCanvas.sortingLayerID;
        cardCanvas.sortingOrder = CanvasOrder;
        card.transform.SetAsLastSibling();
        RaisePlayerCursor();
    }

    void PrepareEndedNightfall()
    {
        if (os == null) return;
        if (nightfall == null)
        {
            _ = os.Chapter02FinalTalk?.Nightfall;
            BindNightfall();
        }
        // The live thunder already set HasEnded. Never reset its rain or replay
        // the restoration on every frame of an existing ending.
        if (nightfall != null && !nightfall.HasEnded) nightfall.RestoreEnded();
    }

    void HideCard()
    {
        IsPresenting = false;
        if (card != null) card.SetActive(false);
        RestorePlayerCursor();
        if (suppressedMessages != null)
        {
            suppressedMessages.SetGlobalMessagesSuppressed(previousSuppression);
            if (os != null && os.IsOSVisible && GameManager.Instance?.Phase == GamePhase.OSMain)
                suppressedMessages.NotifyOSBecameVisible();
        }
        suppressedMessages = null;
    }

    void RaisePlayerCursor()
    {
        var next = CursorHandle.Instance != null ? CursorHandle.Instance.GetComponent<Canvas>() : null;
        if (next != null && next == playerCursorCanvas) return;
        RestorePlayerCursor();
        if (next == null) return;
        playerCursorCanvas = next;
        cursorWasEnabled = next.enabled;
        cursorOverrideSorting = next.overrideSorting;
        cursorOrder = next.sortingOrder;
        cursorLayer = next.sortingLayerID;
        next.enabled = true;
        next.overrideSorting = true;
        next.sortingLayerID = card.GetComponent<Canvas>().sortingLayerID;
        next.sortingOrder = CanvasOrder + 1;
    }

    void RestorePlayerCursor()
    {
        if (playerCursorCanvas != null)
        {
            playerCursorCanvas.enabled = cursorWasEnabled;
            playerCursorCanvas.overrideSorting = cursorOverrideSorting;
            playerCursorCanvas.sortingLayerID = cursorLayer;
            playerCursorCanvas.sortingOrder = cursorOrder;
        }
        playerCursorCanvas = null;
    }

    bool EnsureCard()
    {
        if (card != null) return true;
        var template = os != null ? os.transform.Find(TemplatePath) : null;
        if (template == null)
        {
            if (!warnedTemplate)
                Debug.LogWarning("[Chapter 2 Demo] Waiting for the authored end-card template.", this);
            warnedTemplate = true;
            return false;
        }
        // Clone only the visual card, never Chapter 1's director or overlay.
        card = Instantiate(template.gameObject, os.transform, false);
        card.name = "Chapter02DemoEndCard";
        card.SetActive(false);
        var content = card.transform.Find("Window/Content");
        title = content?.Find("Title")?.GetComponent<TMP_Text>();
        message = content?.Find("Thanks")?.GetComponent<TMP_Text>();
        returnButton = content?.Find("ReturnButton")?.GetComponent<Button>();
        exitButton = content?.Find("ExitButton")?.GetComponent<Button>();
        if (title == null || message == null || returnButton == null || exitButton == null)
        {
            Debug.LogError("[Chapter 2 Demo] The authored end-card template is incomplete.", this);
            Destroy(card);
            card = null;
            return false;
        }
        returnLabel = returnButton.GetComponentInChildren<TMP_Text>(true);
        exitLabel = exitButton.GetComponentInChildren<TMP_Text>(true);
        // Replace the event containers, including any serialized old callbacks.
        returnButton.onClick = new Button.ButtonClickedEvent();
        exitButton.onClick = new Button.ButtonClickedEvent();
        returnButton.onClick.AddListener(ReturnToMainMenu);
        exitButton.onClick.AddListener(ExitGame);
        var canvas = card.GetComponent<Canvas>();
        if (canvas == null) canvas = card.AddComponent<Canvas>();
        canvas.overrideSorting = true;
        canvas.sortingOrder = CanvasOrder;
        if (card.GetComponent<GraphicRaycaster>() == null) card.AddComponent<GraphicRaycaster>();
        var group = card.GetComponent<CanvasGroup>();
        if (group == null) group = card.AddComponent<CanvasGroup>();
        group.ignoreParentGroups = true;
        group.alpha = 1f;
        group.interactable = group.blocksRaycasts = true;
        return true;
    }

    void RefreshText()
    {
        OSLocalizedText.SetRuntime(title, Prefix + "demo_title", "Demo内容到此结束");
        OSLocalizedText.SetRuntime(message, saveFailed ? "ui.save.write_failed" : Prefix + "demo_message",
            saveFailed ? "自动存档写入失败，请稍后重试。" : "正式版将在11月免费发布，敬请期待。");
        OSLocalizedText.SetRuntime(returnLabel, Prefix + "return_menu", "返回主菜单");
        OSLocalizedText.SetRuntime(exitLabel, Prefix + "exit", "退出游戏");
    }
    void RefreshLanguage(LanguageState _)
    {
        RefreshText();
        if (card != null)
            foreach (var binding in card.GetComponentsInChildren<OSLocalizedText>(true)) binding.Refresh(os);
    }
    void OnPhaseChanged(GamePhase phase)
    {
        if (phase != GamePhase.OSMain) HideCard();
    }
    void SetButtonsInteractable(bool value)
    {
        if (returnButton != null) returnButton.interactable = value;
        if (exitButton != null) exitButton.interactable = value;
    }
    bool CanLeave => IsPresenting && !returning && SessionAvailable;

    public void ReturnToMainMenu()
    {
        if (!CanLeave) return;
        returning = true;
        SetButtonsInteractable(false);
        if (GameSessionFlow.Instance != null && GameSessionFlow.Instance.ReturnToMainMenu()) return;
        returning = false;
        SetButtonsInteractable(true);
        saveFailed = true;
        RefreshText();
    }

    public void ExitGame()
    {
        if (!CanLeave) return;
        // Match the settings/Chapter 1 exit transaction: save before quitting,
        // and leave both buttons usable when the write fails.
        if (GameAutosaveService.Instance == null || !GameAutosaveService.Instance.FlushNow())
        {
            saveFailed = true;
            RefreshText();
            return;
        }
        returning = true;
        SetButtonsInteractable(false);
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }
}
