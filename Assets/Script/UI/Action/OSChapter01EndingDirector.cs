using System.Collections;
using MrTGame.Core;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Video;

// Chapter-local cinematic. It deliberately runs outside the picture window's
// coroutines, which are cancelled when the OS input lock becomes active.
[DisallowMultipleComponent]
public sealed partial class OSChapter01EndingDirector : MonoBehaviour
{
    [SerializeField] OSChapter01EndingData configuration;
    [SerializeField] GameObject overlay;
    [SerializeField] UnityEngine.UI.Image slideImage;
    [SerializeField] OSChapter01EndingPlaceholder placeholder;
    [SerializeField] UnityEngine.UI.Button doorButton;
    [SerializeField] UnityEngine.UI.Image doorImage;
    [SerializeField] RectTransform doorWindow;
    [SerializeField] CanvasGroup doorWindowGroup;
    [SerializeField] TMP_Text caption;
    [SerializeField] UnityEngine.UI.RawImage videoImage;
    [SerializeField] VideoPlayer video;
    [SerializeField] AudioSource videoAudio;
    [SerializeField] UnityEngine.UI.RawImage frozenFrame;
    [SerializeField] Material freezeMaterial;
    [SerializeField] UnityEngine.UI.Image blackFade;
    [SerializeField] GameObject endCard;
    [SerializeField] TMP_Text endTitle;
    [SerializeField] TMP_Text endMessage;
    [SerializeField] UnityEngine.UI.Button returnButton;
    [SerializeField] UnityEngine.UI.Button exitButton;
    [SerializeField] UnityEvent onChapter02Ready;

    OS_Handle os;
    OSChapter01EndingSaveData state;
    Coroutine routine;
    Texture2D capturedFrame;
    RenderTexture videoTexture;
    Material runtimeFreeze;
    OSChapter02RecoveryPresentation recoveryPresentation;
    OSLinkMessageController recoveryMessages;
    OSPaperPushStoryDirector paperPushStory;
    bool recoveryConfirmationPresented;
    float departureMessageDeadline = -1f;
    bool departureAnimationOwned;
    bool keepRecoveryFreezeAnimating;
    float recoveryFreezeStarted;
    bool oldMessageSuppression, waitingDoor, doorAccepted, videoFinished, videoFailed, returning;
    const string Prefix = "ui.os.chapter01_ending.";
    const string RecoverySequence = "chapter02.recovery";
    const string RecoveryBugsEntry = "chapter02_recovery_bugs";
    const string RecoveryDoneEntry = "chapter02_recovery_done";
    const string DepartureEntry = "chapter02_departure_busy";
    const float DepartureMessageSeconds = 3f;
    public bool IsPresenting { get; private set; }
    public OSChapter01EndingPhase Phase => state?.phase ?? OSChapter01EndingPhase.None;
    public OSChapter01EndingData Configuration => configuration;
    public bool IsChapter02LiveDisconnected => GameDataManager.Instance?.CurrentChapterIndex == 2
        && GameDataManager.Instance.OSCharacterInteractionState.activeSequenceId == RecoverySequence
        && GameDataManager.Instance.OSCharacterInteractionState.HasCompletedEntry(DepartureEntry)
        && !GameDataManager.Instance.OSCharacterInteractionState.HasConsumedEntry(ReturnArrivedEntry)
        && !returnSignalPending;

    void Awake() { os = GetComponent<OS_Handle>(); if (overlay != null) overlay.SetActive(false); }
    void OnEnable()
    {
        doorButton?.onClick.AddListener(AcceptDoor);
        returnButton?.onClick.AddListener(ReturnToMainMenu);
        exitButton?.onClick.AddListener(ExitGame);
        GameEvents.LanguageChanged += RefreshLanguage;
        GameEvents.PhaseChanged += OnPhaseChanged;
        BindRecoveryMessages();
        if (video != null) { video.loopPointReached += OnVideoFinished;video.errorReceived += OnVideoError; }
    }
    void OnDisable()
    {
        UnbindRecoveryMessages();
        ResetDeparturePresentation();
        ResetChapter02ReturnPresentation();
        CancelPresentation();
        doorButton?.onClick.RemoveListener(AcceptDoor);
        returnButton?.onClick.RemoveListener(ReturnToMainMenu);
        exitButton?.onClick.RemoveListener(ExitGame);
        GameEvents.LanguageChanged -= RefreshLanguage;
        GameEvents.PhaseChanged -= OnPhaseChanged;
        if (video != null) { video.loopPointReached -= OnVideoFinished;video.errorReceived -= OnVideoError; }
    }

    void Update()
    {
        var data = GameDataManager.Instance;
        if (data == null || configuration == null || overlay == null) return;
        var saved = data.OSProcessData.chapter01Ending;
        if (!ReferenceEquals(saved, state))
        {
            ResetDeparturePresentation();
            ResetChapter02ReturnPresentation();
            CancelPresentation();state = saved;returning = false;
            recoveryConfirmationPresented = false;
        }
        BindRecoveryMessages();
        // The repair is a one-time chapter handoff, independent of whether a
        // future Chapter 2 memory node has already been authored.
        if (state.phase == OSChapter01EndingPhase.Chapter02Started)
        {
            if (data.CurrentChapterIndex == 1) data.SetCurrentChapterIndex(2);
            UpdateChapter02Return();
            UpdateChapter02Departure();
            return;
        }
        if (data.CurrentChapterIndex != 1 || os == null || !os.IsOSVisible)
        {
            if (IsPresenting) CancelPresentation();
            returning = false;
            return;
        }
        if (state.phase == OSChapter01EndingPhase.RecoveryAwaitingConfirmation)
        {
            if (!recoveryConfirmationPresented)
                recoveryConfirmationPresented = PresentRecoveryConfirmation();
            return;
        }
        if (keepRecoveryFreezeAnimating && runtimeFreeze != null)
            UpdateRecoveryFreeze();
        if (!IsPresenting && !returning && state.phase != OSChapter01EndingPhase.None && !os.IsSystemInputLocked)
            BeginPresentation();
    }

    public bool TryBeginFromRoomExit(OSMemoryPicController picture, string hotspotId)
    {
        var data = GameDataManager.Instance;
        if (IsPresenting || data?.CurrentChapterIndex != 1 || picture?.scenario == null
            || picture.scenario.scenarioId != "chapter01_home" || picture.CurrentScene?.sceneId != "workshop"
            || hotspotId != "back.workshop" || picture.State?.Has("robot_repaired") != true
            || configuration == null || overlay == null) return false;
        state = data.OSProcessData.chapter01Ending;
        if (state.phase == OSChapter01EndingPhase.RecoveryAwaitingConfirmation
            || state.phase == OSChapter01EndingPhase.Chapter02Started) return false;
        if (state.phase == OSChapter01EndingPhase.None)
        {
            state.phase = OSChapter01EndingPhase.Slides;
            state.slideIndex = 0;
        }
        returning = false;
        BeginPresentation();
        return true;
    }

    void BeginPresentation()
    {
        if (IsPresenting) return;
        IsPresenting = true;
        oldMessageSuppression = os.LinkMessageController?.GlobalMessagesSuppressed == true;
        os.LinkMessageController?.SetGlobalMessagesSuppressed(true);
        os.LinkMessageController?.NotifyOSHidden();
        // The workshop is finished. Close its reading/illustration windows
        // before the recovery reveals the desktop underneath this overlay.
        os.MemoryWindowController?.GetComponent<OSWindowUnit>()?.CloseSilently();
        overlay.SetActive(true);
        overlay.transform.SetAsLastSibling();
        var background = overlay.GetComponent<UnityEngine.UI.Image>();
        if (background != null) background.color = Color.black;
        blackFade.gameObject.SetActive(true);
        endCard.SetActive(false);doorButton.gameObject.SetActive(false);frozenFrame.gameObject.SetActive(false);
        if (doorWindow != null) doorWindow.gameObject.SetActive(false);
        videoImage.gameObject.SetActive(false);SetBlack(1f);
        SetCursorInteractive(false);
        // Keep the player's saved choice; the shared player yields while this lock is active.
        os.GetComponent<OSGramophonePlayer>()?.SuspendForChapterEnding();
        AudioManager.Instance?.StopMusic(.35f);
        AudioManager.Instance?.StopAmbient(.35f);
        // The illustration owns its own foley source, outside the shared mixer channels.
        foreach (var picture in os.GetComponentsInChildren<OSMemoryPicController>(true))
            if (picture.audioSource != null) picture.audioSource.Stop();
        routine = StartCoroutine(Present());
    }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
    public bool DebugBeginAnimation() => DebugBeginEnding(OSChapter01EndingPhase.Animation);

    public bool DebugBeginSlides() => DebugBeginEnding(OSChapter01EndingPhase.Slides);

    bool DebugBeginEnding(OSChapter01EndingPhase startPhase)
    {
        var data = GameDataManager.Instance;
        if (!Application.isPlaying || data?.CurrentChapterIndex != 1 || os == null || !os.IsOSVisible
            || configuration == null || overlay == null || GameSessionFlow.Instance?.IsBusy == true)
            return false;
        GameAutosaveService.Instance?.EnterTestSession();
        CancelPresentation();
        state = data.OSProcessData.chapter01Ending;
        state.phase = startPhase;
        state.slideIndex = 0;state.completed = false;returning = false;
        recoveryConfirmationPresented = false;
        if (data.OSCharacterInteractionState.activeSequenceId == RecoverySequence)
        {
            data.OSCharacterInteractionState.CancelPresentation();
            data.OSCharacterInteractionState.SetEntryConsumed(RecoveryBugsEntry, false);
            data.OSCharacterInteractionState.SetEntryConsumed(RecoveryDoneEntry, false);
        }
        BeginPresentation();
        return IsPresenting;
    }
#endif

    IEnumerator Present()
    {
        if (state.phase == OSChapter01EndingPhase.Completed)
        {
            // Legacy Chapter 1 end-card saves replay the finished slides and film
            // as one sequence. Consume Completed here so subsequent saves resume
            // their actual slide/door/film checkpoint without restarting again.
            state.completed = false;
            state.slideIndex = 0;
            state.phase = OSChapter01EndingPhase.Slides;
        }
        yield return new WaitForSecondsRealtime(Mathf.Max(0f, configuration.initialBlackSeconds));
        if (state.phase == OSChapter01EndingPhase.Slides)
        {
            int count = SlideCount;
            for (int i = Mathf.Clamp(state.slideIndex, 0, count - 1); i < count; i++)
            {
                state.slideIndex = i;
                yield return PlaySlide(i);
            }
            state.phase = OSChapter01EndingPhase.Door;
        }
        if (state.phase == OSChapter01EndingPhase.Door)
        {
            HoldFinalSlide();SetBlack(0f);StopSlideAudio();
            PrepareDoor();caption.gameObject.SetActive(false);
            yield return PopUpDoor();
            waitingDoor = true;doorAccepted = false;
            doorButton.interactable = true;
            SetCursorInteractive(true);
            while (!doorAccepted) yield return null;
            waitingDoor = false;
            state.phase = OSChapter01EndingPhase.DoorKnocked;
            SetCursorInteractive(false);
            doorButton.interactable = false;
            if (configuration.doorSound != null) AudioManager.Instance?.PlaySfx(configuration.doorSound, 1f);
            yield return new WaitForSecondsRealtime(Mathf.Max(0f, configuration.doorKnockSeconds));
        }
        if (state.phase == OSChapter01EndingPhase.DoorKnocked)
        {
            // Persist the accepted click, not the transient popup/wipe progress.
            // A resumed save does not demand another click or replay the knock.
            HoldFinalSlide();SetBlack(0f);PrepareDoor();
            if (doorWindowGroup != null) doorWindowGroup.alpha = 1f;
            caption.gameObject.SetActive(false);
            yield return FadeBlack(1f, configuration.doorTransitionSeconds);
            HideDoor();
        }
        // Mid-animation and mid-glitch saves restart the animation, never a blank
        // screenshot or a video seeking to an unprepared frame.
        state.phase = OSChapter01EndingPhase.Animation;
        yield return PlayAnimation();
        StoryAchievementProgress.Record(StoryAchievementMilestone.Chapter01Complete);
        state.phase = OSChapter01EndingPhase.Glitch;
        SetCursorInteractive(false);
        if (video != null) video.Pause();
        if (videoAudio != null) videoAudio.Pause();
        yield return new WaitForEndOfFrame();
        capturedFrame = ScreenCapture.CaptureScreenshotAsTexture();
        frozenFrame.texture = capturedFrame;
        frozenFrame.color = Color.white;
        if (freezeMaterial != null)
        {
            runtimeFreeze = new Material(freezeMaterial);
            runtimeFreeze.SetFloat("_Amount", 0f);runtimeFreeze.SetFloat("_Phase", 0f);
            runtimeFreeze.SetFloat("_ScreenshotGamma", capturedFrame.isDataSRGB ? 0f : 1f);
            frozenFrame.material = runtimeFreeze;
        }
        frozenFrame.gameObject.SetActive(true);
        // Both releases continue through the film and Chapter 2 repair.
        recoveryFreezeStarted = Time.unscaledTime;
        keepRecoveryFreezeAnimating = true;
        UpdateRecoveryFreeze();
        if (configuration.crashSound != null) AudioManager.Instance?.PlaySfx(configuration.crashSound, .65f);
        yield return new WaitForSecondsRealtime(Mathf.Max(.1f, configuration.recoveryFreezeSeconds));
        recoveryPresentation = GetComponent<OSChapter02RecoveryPresentation>();
        if (recoveryPresentation == null)
            recoveryPresentation = gameObject.AddComponent<OSChapter02RecoveryPresentation>();
        yield return recoveryPresentation.Play(os, this, (RectTransform)overlay.transform,
            frozenFrame, caption, RevealRecoveryDesktop, configuration, PresentRecoveryBugs);
        if (recoveryPresentation.Completed)
            CompleteChapter02Recovery();
        else
        {
            // Keep the saved animation checkpoint retryable. Never leave a
            // half-presented desktop with a captured cursor after cancellation.
            routine = null;
            CancelPresentation();
        }
        routine = null;
        yield break;
    }

    void UpdateRecoveryFreeze()
    {
        if (runtimeFreeze == null) return;
        float elapsed = Mathf.Max(0f, Time.unscaledTime - recoveryFreezeStarted);
        // A sprite animation already stuck torn in the screenshot; keep it still.
        runtimeFreeze.SetFloat("_Amount", filmGlitchStuck ? 0f : Mathf.Lerp(.25f, .8f, Mathf.Clamp01(elapsed / .8f)));
        runtimeFreeze.SetFloat("_Phase", elapsed);
    }

    void RevealRecoveryDesktop()
    {
        // The recovery owns copies of the frozen image at this point. Keep the
        // transparent overlay's raycast blocker alive until control is returned.
        keepRecoveryFreezeAnimating = false;
        slideImage.gameObject.SetActive(false);
        placeholder.gameObject.SetActive(false);
        caption.gameObject.SetActive(false);
        HideDoor();
        videoImage.gameObject.SetActive(false);
        frozenFrame.gameObject.SetActive(false);
        blackFade.gameObject.SetActive(false);
        endCard.SetActive(false);
        var background = overlay.GetComponent<UnityEngine.UI.Image>();
        if (background != null) { background.color = Color.clear;background.raycastTarget = true; }
    }

    void CompleteChapter02Recovery()
    {
        var data = GameDataManager.Instance;
        if (data == null) return;
        recoveryMessages?.TryCompleteAwaitedEntry(RecoverySequence, RecoveryBugsEntry);
        state.completed = true;
        state.phase = OSChapter01EndingPhase.RecoveryAwaitingConfirmation;
        // Do not stop this coroutine from inside its own completion path.
        routine = null;
        CursorHandle.Instance?.ReleaseScriptControlAtDisplayedPosition(this);
        CancelPresentation();
        recoveryConfirmationPresented = PresentRecoveryConfirmation();
    }

    void BindRecoveryMessages()
    {
        var messages = os != null ? os.LinkMessageController : null;
        if (recoveryMessages == messages) return;
        UnbindRecoveryMessages();
        recoveryMessages = messages;
        if (recoveryMessages == null) return;
        recoveryMessages.OptionSelected += OnRecoveryOptionSelected;
        recoveryMessages.EntryPresentationStarted += OnDeparturePresentationStarted;
        recoveryMessages.EntryTextPlaybackCompleted += OnDepartureTextCompleted;
        recoveryMessages.EntryPresentationStarted += OnReturnEntryStarted;
        recoveryMessages.EntryTextPlaybackCompleted += OnReturnTextCompleted;
        recoveryMessages.EntryPresentationCompleted += OnReturnEntryCompleted;
    }

    void UnbindRecoveryMessages()
    {
        if (recoveryMessages == null) return;
        recoveryMessages.OptionSelected -= OnRecoveryOptionSelected;
        recoveryMessages.EntryPresentationStarted -= OnDeparturePresentationStarted;
        recoveryMessages.EntryTextPlaybackCompleted -= OnDepartureTextCompleted;
        recoveryMessages.EntryPresentationStarted -= OnReturnEntryStarted;
        recoveryMessages.EntryTextPlaybackCompleted -= OnReturnTextCompleted;
        recoveryMessages.EntryPresentationCompleted -= OnReturnEntryCompleted;
        recoveryMessages = null;
    }

    void OnDeparturePresentationStarted(string sequenceId, OSCharacterInteractionEntryData entry)
    {
        if (sequenceId != RecoverySequence || entry?.EntryId != DepartureEntry) return;
        var character = os?.CharacterWindowController;
        if (character != null)
            departureAnimationOwned = character.SetExtendedAnimation(OSCharacterExtendedAnimation.Expend02);
    }

    void OnDepartureTextCompleted(string sequenceId, OSCharacterInteractionEntryData entry)
    {
        if (sequenceId == RecoverySequence && entry?.EntryId == DepartureEntry
            && departureMessageDeadline < 0f)
            departureMessageDeadline = Time.unscaledTime + DepartureMessageSeconds;
    }

    void UpdateChapter02Departure()
    {
        var data = GameDataManager.Instance;
        if (data.CurrentChapterIndex != 2 || os == null || !os.IsOSVisible
            || OSSessionPersistence.IsRestoring || GameSessionFlow.Instance?.IsBusy == true)
            return;
        var interaction = data.OSCharacterInteractionState;
        if (interaction.activeSequenceId != RecoverySequence) return;
        var character = os.CharacterWindowController;
        if (IsChapter02LiveDisconnected)
        {
            // A save made during the flash may still contain the live camera state.
            if (character == null || character.IsSignalOutActive) return;
            if (character.CurrentState != OSCharacterWindowState.VoidMessage)
                character.ApplyStateImmediate(OSCharacterWindowState.VoidMessage);
            ResetDeparturePresentation();
            return;
        }

        // Before this entry existed, confirming the repair completed the sequence.
        // Reopen that old receipt without replaying the repair or its confirmation.
        if (interaction.isCompleted && interaction.HasCompletedEntry(RecoveryDoneEntry)
            && !interaction.HasConsumedEntry(DepartureEntry))
        {
            interaction.isCompleted = false;
            interaction.phase = OSCharacterInteractionSavePhase.WaitingForTrigger;
        }

        // Older sessions could start the chapter through an already-open status
        // window and never hover its desktop icon. Recover the missing departure
        // once reading has started, without closing a window on every update.
        if (!interaction.HasConsumedEntry(DepartureEntry)
            && interaction.HasCompletedEntry(RecoveryDoneEntry)
            && !interaction.HasConsumedEntry(ReturnArrivedEntry) && !data.IsMemoryRepair03Installed
            && data.TryGetOSMemoryChapterProgressRange(2, out var chapter02Range)
            && chapter02Range.Contains(data.CurrentOSMemoryDataIndex)
            && data.CurrentOSMemoryDataIndex <= Chapter02UpgradeGate
            && !os.IsSystemInputLocked && !GameTestConsole.BlocksGameplayInput
            && recoveryMessages != null && !recoveryMessages.IsPresenting
            && !recoveryMessages.GlobalMessagesSuppressed
            && string.IsNullOrEmpty(interaction.presentingEntryId)
            && string.IsNullOrEmpty(interaction.presentingGlobalEntryId)
            && string.IsNullOrEmpty(interaction.pendingEntryId)
            && string.IsNullOrEmpty(interaction.pendingGlobalEntryId)
            && (recoveryMessages.LinkWindow == null
                || (!recoveryMessages.LinkWindow.IsOpen && !recoveryMessages.LinkWindow.IsClosing)))
            recoveryMessages.TryPresentEntry(DepartureEntry);

        if (departureMessageDeadline < 0f || Time.unscaledTime < departureMessageDeadline
            || recoveryMessages == null) return;
        if (!recoveryMessages.TryCompleteAwaitedEntry(RecoverySequence, DepartureEntry)) return;
        departureMessageDeadline = -1f;
        character?.PlaySignalOut();
    }

    void ResetDeparturePresentation()
    {
        departureMessageDeadline = -1f;
        var character = os?.CharacterWindowController;
        if (departureAnimationOwned && character != null
            && character.CurrentExtendedAnimation == OSCharacterExtendedAnimation.Expend02)
            character.StopExtendedAnimation();
        departureAnimationOwned = false;
    }

    void PresentRecoveryBugs()
    {
        BindRecoveryMessages();
        if (recoveryMessages == null || !recoveryMessages.ActivateSequence(RecoverySequence)) return;
        recoveryMessages.NotifyOSBecameVisible();
        recoveryMessages.TryPresentEntry(RecoveryBugsEntry);
    }

    bool PresentRecoveryConfirmation()
    {
        if (IsPresenting || GameSessionFlow.Instance?.IsBusy == true
            || os == null || !os.IsOSVisible || os.IsSystemInputLocked
            || GameManager.Instance?.Phase != GamePhase.OSMain) return false;
        BindRecoveryMessages();
        if (recoveryMessages == null) return false;
        recoveryMessages.TryCompleteAwaitedEntry(RecoverySequence, RecoveryBugsEntry);
        // Chapter 1's paper push is played to its seal before chapter 2 can open.
        if (paperPushStory == null) paperPushStory = GetComponent<OSPaperPushStoryDirector>();
        if (paperPushStory != null && paperPushStory.HoldsChapter01Ending)
            return false;
        if (recoveryMessages.DisplayedSequenceId == RecoverySequence
            && recoveryMessages.DisplayedEntryId == RecoveryDoneEntry && recoveryMessages.IsPresenting)
            return true;
        // Let earlier queued commentary finish without restarting it every
        // frame. A late bug line is still completed above before this wait.
        var interaction = GameDataManager.Instance?.OSCharacterInteractionState;
        if (interaction?.activeSequenceId == RecoverySequence
            && (interaction.pendingEntryId == RecoveryDoneEntry
                || interaction.optionCountdownQueue.Exists(item => item != null
                    && item.sequenceId == RecoverySequence && item.entryId == RecoveryDoneEntry)))
            return false;
        // Resume the protected choice before any queued ambient commentary.
        bool suppressed = recoveryMessages.GlobalMessagesSuppressed;
        recoveryMessages.SetGlobalMessagesSuppressed(true);
        bool presented = false;
        if (recoveryMessages.ActivateSequence(RecoverySequence))
        {
            recoveryMessages.NotifyOSBecameVisible();
            recoveryMessages.TryCompleteAwaitedEntry(RecoverySequence, RecoveryBugsEntry);
            if (recoveryMessages.DisplayedSequenceId != RecoverySequence
                || recoveryMessages.DisplayedEntryId != RecoveryDoneEntry || !recoveryMessages.IsPresenting)
                recoveryMessages.TryPresentEntry(RecoveryDoneEntry);
            // TryPresentEntry can accept a queued message. Keep retrying the
            // handoff until the choice is visible, including older stuck saves.
            presented = recoveryMessages.DisplayedSequenceId == RecoverySequence
                && recoveryMessages.DisplayedEntryId == RecoveryDoneEntry && recoveryMessages.IsPresenting;
        }
        recoveryMessages.SetGlobalMessagesSuppressed(suppressed);
        return presented;
    }

    void OnRecoveryOptionSelected(string sequenceId, OSCharacterInteractionEntryData entry,
        OSCharacterInteractionOptionData option)
    {
        var data = GameDataManager.Instance;
        var saved = data?.OSProcessData.chapter01Ending;
        if (sequenceId != RecoverySequence || entry?.EntryId != RecoveryDoneEntry
            || option?.OptionId != "confirm"
            || saved?.phase != OSChapter01EndingPhase.RecoveryAwaitingConfirmation) return;
        // Require a fresh visit to the NEW desktop entry. A status window left
        // open in chapter 1 otherwise bypasses the departure's desktop hover.
        os.StateCheckWindowController?.GetComponent<OSWindowUnit>()?.CloseSilently();
        // OptionSelected runs before the message completes; do not replace its
        // sequence or start memory playback from this callback.
        saved.completed = true;
        saved.phase = OSChapter01EndingPhase.Chapter02Started;
        data.SetCurrentChapterIndex(2);
        os.MarkStateCheckDesktopProcessNew();
        os.StateCheckWindowController?.RefreshFromSave();
        onChapter02Ready?.Invoke();
    }

    int SlideCount => configuration.slides != null && configuration.slides.Length > 0 ? configuration.slides.Length : 3;
    void ShowSlide(int index)
    {
        var slide = configuration.slides != null && index < configuration.slides.Length ? configuration.slides[index] : null;
        Sprite art = GetSlideFrame(slide, 0f);
        bool hasArt = art != null;
        slideImage.gameObject.SetActive(hasArt);SetSlideSprite(art);
        placeholder.gameObject.SetActive(!hasArt);
        if (!hasArt)
        {
            int shot = slide != null ? slide.placeholderShot : index;
            placeholder.SetShot((OSChapter01EndingPlaceholder.Shot)Mathf.Clamp(shot, 0, 2), 1f);
            string key = slide != null && !string.IsNullOrEmpty(slide.placeholderCaptionKey)
                ? slide.placeholderCaptionKey : Prefix + new[] { "slide_room", "slide_corridor", "slide_doorway" }[Mathf.Clamp(shot, 0, 2)];
            OSLocalizedText.SetRuntime(caption, key, "占位分镜");caption.gameObject.SetActive(true);
        }
        else caption.gameObject.SetActive(false);
    }

    void PrepareDoor()
    {
        if (doorWindow != null)
        {
            doorWindow.gameObject.SetActive(true);
            doorWindow.localScale = Vector3.one;
        }
        else
        {
            var rect = (RectTransform)doorButton.transform;
            var bounds = configuration.doorNormalizedRect;
            rect.anchorMin = bounds.min;rect.anchorMax = bounds.max;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
        }
        doorImage.sprite = configuration.doorImage;
        if (doorWindow != null && configuration.doorImage != null)
        {
            FitDoorLayout();
            doorImage.preserveAspect = true;
        }
        doorImage.color = configuration.doorImage != null ? Color.white : new Color(1f, 1f, 1f, .09f);
        doorButton.gameObject.SetActive(true);doorButton.interactable = false;
    }

    void FitDoorLayout()
    {
        float height = doorWindow.rect.height;
        float photoHeight = Mathf.Max(1f, height - 24f);
        float photoWidth = photoHeight * configuration.doorImage.rect.width / configuration.doorImage.rect.height;
        float width = Mathf.Ceil(photoWidth + 6f);
        doorWindow.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, width);
        SetDoorRect(doorWindow.Find("Content"), new Vector2(0, -9), new Vector2(width - 2, height - 20));
        SetDoorRect(doorButton.transform, Vector2.zero, new Vector2(photoWidth, photoHeight));
        SetDoorRect(doorImage.transform, Vector2.zero, new Vector2(photoWidth, photoHeight));
        var chrome = doorWindow.Find("Chrome");
        SetDoorRect(chrome, Vector2.zero, new Vector2(width, height));
        if (chrome != null)
        {
            var title = chrome.Find("TitleBar");
            SetDoorRect(title, new Vector2(0, height * .5f - 10), new Vector2(width - 2, 18));
            SetDoorRect(chrome.Find("Divider"), new Vector2(0, height * .5f - 18.5f), new Vector2(width - 2, 1));
            if (title != null)
            {
                SetDoorRect(title.Find("Title"), new Vector2(-16, 0), new Vector2(width - 40, 16));
                SetDoorRect(title.Find("Minimize"), new Vector2(width * .5f - 23, 0), new Vector2(12, 12));
                SetDoorRect(title.Find("Close"), new Vector2(width * .5f - 9, 0), new Vector2(12, 12));
            }
        }
        // Hover decorations are siblings of ImageBK under the button.
        var corners = doorButton.transform.Find("HoverCorners");
        SetDoorRect(corners, Vector2.zero, new Vector2(photoWidth, photoHeight));
        if (corners != null)
            for (int x = -1; x <= 1; x += 2)
                for (int y = -1; y <= 1; y += 2)
                {
                    SetDoorRect(corners.Find("Horizontal" + x + "_" + y),
                        new Vector2(x * (photoWidth * .5f - 4), y * (photoHeight * .5f - 1)), new Vector2(7, 1));
                    SetDoorRect(corners.Find("Vertical" + x + "_" + y),
                        new Vector2(x * (photoWidth * .5f - 1), y * (photoHeight * .5f - 4)), new Vector2(1, 7));
                }
        SetDoorRect(doorButton.transform.Find("HandleGlint"),
            new Vector2(photoWidth * .31f, -photoHeight * .02f), new Vector2(14, 1));
    }

    static void SetDoorRect(Transform target, Vector2 position, Vector2 size)
    {
        var rect = target as RectTransform;
        if (rect == null) return;
        rect.anchorMin = rect.anchorMax = rect.pivot = Vector2.one * .5f;
        rect.anchoredPosition = position;rect.sizeDelta = size;
    }

    IEnumerator PopUpDoor()
    {
        if (doorWindow == null) yield break;
        float elapsed = 0f, duration = Mathf.Max(.01f, configuration.doorPopupSeconds);
        while (elapsed < duration)
        {
            float t = Mathf.Clamp01(elapsed / duration);
            float eased = 1f - Mathf.Pow(1f - t, 3f);
            float scale = t < .72f
                ? Mathf.Lerp(.78f, 1.018f, 1f - Mathf.Pow(1f - t / .72f, 3f))
                : Mathf.Lerp(1.018f, 1f, Mathf.SmoothStep(0f, 1f, (t - .72f) / .28f));
            doorWindow.localScale = Vector3.one * scale;
            if (doorWindowGroup != null) doorWindowGroup.alpha = eased;
            elapsed += Time.unscaledDeltaTime;
            yield return null;
        }
        doorWindow.localScale = Vector3.one;
        if (doorWindowGroup != null) doorWindowGroup.alpha = 1f;
    }

    void HideDoor()
    {
        doorButton.gameObject.SetActive(false);
        if (doorWindow != null) doorWindow.gameObject.SetActive(false);
    }

    IEnumerator WipeBlackFromTop()
    {
        float elapsed = 0f, duration = Mathf.Max(.01f, configuration.demoWipeSeconds);
        blackFade.color = Color.black;
        var rect = blackFade.rectTransform;
        while (elapsed < duration)
        {
            float t = Mathf.Clamp01(elapsed / duration);
            rect.anchorMin = new Vector2(0f, 1f - t);
            rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
            elapsed += Time.unscaledDeltaTime;
            yield return null;
        }
        SetBlack(1f);
    }

    public void AcceptDoor()
    {
        if (IsPresenting && waitingDoor && state.phase == OSChapter01EndingPhase.Door && !doorAccepted)
            doorAccepted = true;
    }

    IEnumerator PlayAnimation()
    {
        ReleaseSlidePresentation();
        slideImage.gameObject.SetActive(false);placeholder.gameObject.SetActive(false);
        caption.gameObject.SetActive(false);SetBlack(0f);
        if (configuration.animationVideo != null && video != null)
        {
            videoFinished = videoFailed = false;
            var clip = configuration.animationVideo;
            int width = Mathf.Clamp((int)clip.width, 16, 1920);
            int height = Mathf.Max(16, Mathf.RoundToInt(width * (float)clip.height / Mathf.Max(1f, clip.width)));
            videoTexture = new RenderTexture(width, height, 0) { name = "Chapter01EndingVideo" };
            videoTexture.Create();video.targetTexture = videoTexture;videoImage.texture = videoTexture;
            video.clip = clip;video.isLooping = false;video.playOnAwake = false;
            video.timeUpdateMode = VideoTimeUpdateMode.UnscaledGameTime;
            video.renderMode = VideoRenderMode.RenderTexture;
            video.audioOutputMode = VideoAudioOutputMode.AudioSource;
            video.controlledAudioTrackCount = 1;
            if (clip.audioTrackCount > 0 && videoAudio != null)
            {
                videoAudio.outputAudioMixerGroup = AudioManager.Instance?.MusicMixerGroup;
                video.EnableAudioTrack(0, true);video.SetTargetAudioSource(0, videoAudio);
            }
            video.Prepare();float deadline = Time.unscaledTime + 10f;
            while (!video.isPrepared && !videoFailed && Time.unscaledTime < deadline) yield return null;
            if (video.isPrepared && !videoFailed)
            {
                videoImage.gameObject.SetActive(true);video.Play();
                deadline = Time.unscaledTime + Mathf.Max(1f, (float)clip.length) + 5f;
                while (!videoFinished && !videoFailed && Time.unscaledTime < deadline) yield return null;
                if (!videoFailed && videoFinished) yield break;
            }
            Debug.LogWarning("[Chapter 01 Ending] Video unavailable; using the animation placeholder.", this);
            video.Stop();videoImage.gameObject.SetActive(false);
        }
        if (HasAnimationFrames())
        {
            yield return PlayGlitchedFrames();
            yield break;
        }
        placeholder.gameObject.SetActive(true);SetCaption("animation_placeholder", "结尾动画占位");
        float elapsed = 0f, duration = Mathf.Max(.1f, configuration.animationPlaceholderSeconds);
        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            placeholder.SetShot(OSChapter01EndingPlaceholder.Shot.Animation, Mathf.Clamp01(elapsed / duration));
            yield return null;
        }
    }

    void OnVideoFinished(VideoPlayer _) { videoFinished = true;video.Pause(); }
    void OnVideoError(VideoPlayer _, string message) { videoFailed = true;Debug.LogWarning("[Chapter 01 Ending] " + message, this); }
    IEnumerator FadeBlack(float target, float seconds)
    {
        float start = blackFade.color.a, elapsed = 0f;
        float duration = Mathf.Max(.01f, seconds);
        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;SetBlack(Mathf.Lerp(start, target, elapsed / duration));yield return null;
        }
        SetBlack(target);
    }
    void SetBlack(float alpha)
    {
        var rect = blackFade.rectTransform;
        rect.anchorMin = Vector2.zero;rect.anchorMax = Vector2.one;
        rect.offsetMin = rect.offsetMax = Vector2.zero;
        blackFade.color = new Color(0f, 0f, 0f, alpha);
        if (frozenFrame.gameObject.activeSelf)
            frozenFrame.color = new Color(1f, 1f, 1f, 1f - alpha);
    }
    void SetCaption(string suffix, string fallback)
    {
        caption.gameObject.SetActive(true);OSLocalizedText.SetRuntime(caption, Prefix + suffix, fallback);
    }
    void SetCursorInteractive(bool interactive)
    {
        var cursor = CursorHandle.Instance;
        if (interactive) cursor?.ReleaseScriptControl(this);
        else if (cursor != null && cursor.AcquireScriptControl(this)) cursor.SetScriptAppearance(this, 1f, Color.clear);
    }
    void ShowEndCard()
    {
        SetBlack(1f);endCard.SetActive(true);SetEndButtonsInteractable(true);
        RefreshEndCardText();
        SetCursorInteractive(true);
    }
    void RefreshEndCardText()
    {
        if (endTitle != null)
            OSLocalizedText.SetRuntime(endTitle, Prefix + (GameSetup.IsDemo ? "demo_title" : "chapter_complete_title"),
                GameSetup.IsDemo ? "Demo内容到此结束" : "第一章结束");
        if (endMessage != null)
            OSLocalizedText.SetRuntime(endMessage, Prefix + (GameSetup.IsDemo ? "demo_update" : "chapter_complete_body"),
                GameSetup.IsDemo ? "第二章内容将在26.10.20新品节发布。" : "感谢游玩。");
    }
    void SetEndButtonsInteractable(bool value)
    {
        if (returnButton != null) returnButton.interactable = value;
        if (exitButton != null) exitButton.interactable = value;
    }
    public void ReturnToMainMenu()
    {
        if (!IsPresenting || state?.phase != OSChapter01EndingPhase.Completed || returning || GameManager.Instance == null) return;
        if (GameSessionFlow.Instance != null)
        {
            if (!GameSessionFlow.Instance.ReturnToMainMenu()) return;
        }
        else GameManager.Instance.OpenMainMenu();
        returning = true;SetEndButtonsInteractable(false);
    }
    public void ExitGame()
    {
        if (!IsPresenting || state?.phase != OSChapter01EndingPhase.Completed || returning
            || GameSessionFlow.Instance?.IsBusy == true) return;
        if (GameAutosaveService.Instance != null && !GameAutosaveService.Instance.FlushNow())
        {
            if (endMessage != null)
                OSLocalizedText.SetRuntime(endMessage, "ui.save.write_failed", "自动存档写入失败，请稍后重试。");
            return;
        }
        returning = true;SetEndButtonsInteractable(false);
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }
    void OnPhaseChanged(GamePhase phase)
    {
        if (phase == GamePhase.OSMain) return;
        CancelPresentation();
        ResetDeparturePresentation();
        ResetChapter02ReturnPresentation();
    }
    void RefreshLanguage(LanguageState _)
    {
        if (overlay == null) return;
        foreach (var text in overlay.GetComponentsInChildren<OSLocalizedText>(true)) text.Refresh(os);
    }
    void CancelPresentation()
    {
        if (routine != null) { StopCoroutine(routine);routine = null; }
        recoveryPresentation?.Cancel();
        keepRecoveryFreezeAnimating = false;
        bool wasPresenting = IsPresenting;
        IsPresenting = false;waitingDoor = doorAccepted = false;
        CursorHandle.Instance?.ReleaseScriptControl(this);
        if (overlay != null) overlay.SetActive(false);
        if (blackFade != null) blackFade.gameObject.SetActive(true);
        ReleaseMedia();
        if (wasPresenting && os?.LinkMessageController != null)
        {
            os.LinkMessageController.SetGlobalMessagesSuppressed(oldMessageSuppression);
            if (os.IsOSVisible && GameManager.Instance?.Phase == GamePhase.OSMain)
                os.LinkMessageController.NotifyOSBecameVisible();
        }
    }
    void ReleaseMedia()
    {
        ReleaseSlidePresentation();
        ReleaseFilmGlitch();
        StopEndingAudio();
        if (video != null) { video.Stop();video.targetTexture = null; }
        if (videoAudio != null) videoAudio.Stop();
        if (videoImage != null) { videoImage.texture = null;videoImage.gameObject.SetActive(false); }
        if (videoTexture != null) { videoTexture.Release();Destroy(videoTexture);videoTexture = null; }
        if (frozenFrame != null) { frozenFrame.texture = null;frozenFrame.material = null;frozenFrame.gameObject.SetActive(false); }
        if (capturedFrame != null) { Destroy(capturedFrame);capturedFrame = null; }
        if (runtimeFreeze != null) { Destroy(runtimeFreeze);runtimeFreeze = null; }
    }
}
