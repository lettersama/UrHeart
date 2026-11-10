using System.Collections.Generic;
using DG.Tweening;
using MrTGame.Core;
using TMPro;
using UnityEngine;

/// <summary>
/// Chapter four's private conversation. History is a view of saved visits and
/// playback is mirrored from Live's existing typewriter, including its audio.
/// All choices and continuation go through the story's guarded input methods.
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(31000)]
public sealed class OSChapter04ChatView : MonoBehaviour
{
    static readonly Color Ink = new Color32(0x87, 0x82, 0x7e, 255);
    static readonly Vector2 ExpandedSize = new Vector2(324f, 220f);
    static readonly Vector2 ExpandedPosition = new Vector2(-104f, 12f);
    const float PortraitWidth = 125f, ChatLeft = 127f, AvatarSize = 24f;
    public const string ConversationDate = "3014年2月8日";
    public const string ConversationDateKey = "ui.os.chapter04.conversation_date";
    OSChapter04StoryDirector story;
    OSLinkMessageController messages;
    OSWindowUnit window, hostWindow;
    RectTransform root, content, viewport, footer;
    UnityEngine.UI.ScrollRect scroll;
    UnityEngine.UI.VerticalLayoutGroup rows;
    CanvasGroup originalArea;
    bool originalRaycasts, originalInteractable;
    float originalAlpha;
    TMP_Text template, currentLabel, conversationDateLabel;
    UnityEngine.UI.Button treatmentAction;
    readonly List<UnityEngine.UI.Button> actions = new List<UnityEngine.UI.Button>();
    readonly List<GameObject> rowObjects = new List<GameObject>();
    OSChapter04StoryState observed;
    string signature;
    bool followLatest = true, positioningScroll;
    int renderedVisit = -1;
    bool ownsTemporaryLayout, languageDirty, originalCanMove, originalCanResize;
    UnityEngine.UI.Image portraitImage;
    bool originalPortraitPreserveAspect;
    readonly List<RectPose> originalPoses = new List<RectPose>();
    readonly List<KeyValuePair<GameObject, bool>> hiddenPortraitElements = new List<KeyValuePair<GameObject, bool>>();
    RectTransform portraitRect;
    OSWindowLayoutSaveData hostLayoutBeforeExpansion;
    public bool OwnsLayout { get; private set; }
    public OSWindowUnit HostWindow => hostWindow;
    public RectTransform Root => root;
    public RectTransform Viewport => viewport;
    public TMP_Text CurrentBubble => currentLabel;
    public TMP_Text ConversationDateText => conversationDateLabel;
    public RectTransform PortraitRect => portraitRect;
    public OSWindowLayoutSaveData HostLayoutBeforeExpansion => hostLayoutBeforeExpansion;

    sealed class RectPose
    {
        public RectTransform target;
        public Vector2 min, max, pivot, position, size;
        public Vector3 scale;
        public RectPose(RectTransform value)
        {
            target = value; min = value.anchorMin; max = value.anchorMax; pivot = value.pivot;
            position = value.anchoredPosition; size = value.sizeDelta; scale = value.localScale;
        }
        public void Restore()
        {
            if (target == null) return;
            target.anchorMin = min; target.anchorMax = max; target.pivot = pivot;
            target.sizeDelta = size; target.anchoredPosition = position; target.localScale = scale;
        }
    }

    public static OSChapter04ChatView Attach(OSChapter04StoryDirector owner, OSLinkMessageController controller)
    {
        var view = owner.GetComponent<OSChapter04ChatView>();
        if (view == null) view = owner.gameObject.AddComponent<OSChapter04ChatView>();
        view.story = owner;
        view.messages = controller;
        return view;
    }

    void OnEnable() => GameEvents.LanguageChanged += OnLanguageChanged;
    void OnDisable() { GameEvents.LanguageChanged -= OnLanguageChanged; Release(); }
    void OnDestroy() { GameEvents.LanguageChanged -= OnLanguageChanged; Release(); }
    void OnLanguageChanged(LanguageState language) => languageDirty = true;

    void LateUpdate()
    {
        if (story == null || messages == null || !story.IsActive || story.State?.started != true)
        { Release(); return; }
        if (OSSessionPersistence.IsRestoring) return;
        if (!OwnsLayout && !Acquire()) return;
        if (root == null) return;
        if (window.IsOpen && window.IsOpening) window.CompleteOpeningFromScript();
        bool visible = hostWindow.IsOpen && !hostWindow.IsMinimized && !hostWindow.IsOpening && !hostWindow.IsClosing
            && hostWindow.gameObject.activeInHierarchy;
        if (root.gameObject.activeSelf != visible) root.gameObject.SetActive(visible);
        foreach (var element in hiddenPortraitElements)
            if (element.Key != null && element.Key.activeSelf) element.Key.SetActive(false);
        // The original message window remains an active playback carrier. Its
        // entire frame, title and controls are hidden; Live is the only window.
        if (originalArea != null)
        { originalArea.alpha = 0f; originalArea.blocksRaycasts = false; originalArea.interactable = true; }
        if (!visible) return;
        if (!ReferenceEquals(observed, story.State))
        { observed = story.State; signature = null; followLatest = true; renderedVisit = -1; }
        string next = Signature();
        if (next != signature || languageDirty)
        { signature = next; languageDirty = false; Rebuild(); }
        MirrorCurrentText();
        bool input = story.IsAvailable && !story.IsOptionGestureBlocked;
        foreach (var button in actions) if (button != null) button.interactable = input;
        if (treatmentAction != null) treatmentAction.interactable = story.CanOpenFinalHeartHeal;
    }

    bool Acquire()
    {
        window = messages.LinkWindow;
        hostWindow = OS_Handle.Instance?.CharacterWindowController?.GetComponent<OSWindowUnit>();
        template = messages.DisplayedTextLabel;
        if (window == null || hostWindow == null || template == null || hostWindow.WindowRect.parent == null) return false;
        // Hand ownership over before changing any dimensions.
        story.ReleaseDialogueLayout();
        messages.WindowLayout?.SetExternalLayoutOwner(this, true);
        // Retire the old child-window animation before hiding the carrier.
        window.CompleteOpeningFromScript();
        originalCanMove = window.AllowsMove; originalCanResize = window.AllowsResize;
        window.ConfigureInteraction(false, false);
        originalArea = window.GetComponent<CanvasGroup>();
        if (originalArea == null) originalArea = window.gameObject.AddComponent<CanvasGroup>();
        originalAlpha = originalArea.alpha;
        originalRaycasts = originalArea.blocksRaycasts;
        originalInteractable = originalArea.interactable;
        hostWindow.CompleteOpeningFromScript();
        hostLayoutBeforeExpansion = hostWindow.CaptureLayout();
        ownsTemporaryLayout = !hostWindow.HasTemporaryOpeningLayout;
        if (ownsTemporaryLayout)
            hostWindow.SetTemporaryOpeningLayout(ExpandedSize, ExpandedPosition, ExpandedSize);
        else hostWindow.SetOpeningLayout(ExpandedPosition, ExpandedSize);
        hostWindow.ConfigureInteraction(true, false);
        ConfigurePortraitAndChrome();
        if (root == null) Build();
        root.gameObject.SetActive(true);
        OwnsLayout = true;
        signature = null;
        return true;
    }

    public void ReleaseView() => Release();

    void Release()
    {
        if (!OwnsLayout) return;
        OwnsLayout = false;
        if (root != null) root.gameObject.SetActive(false);
        if (originalArea != null)
        {
            originalArea.alpha = originalAlpha;
            originalArea.blocksRaycasts = originalRaycasts;
            originalArea.interactable = originalInteractable;
        }
        if (window != null) window.ConfigureInteraction(originalCanMove, originalCanResize);
        foreach (var pose in originalPoses) pose.Restore();
        originalPoses.Clear();
        foreach (var element in hiddenPortraitElements)
            if (element.Key != null) element.Key.SetActive(element.Value);
        hiddenPortraitElements.Clear();
        if (portraitImage != null) portraitImage.preserveAspect = originalPortraitPreserveAspect;
        if (hostWindow != null && ownsTemporaryLayout) hostWindow.ClearTemporaryOpeningLayout();
        messages?.WindowLayout?.SetExternalLayoutOwner(this, false);
        observed = null; signature = null; followLatest = true; renderedVisit = -1;
    }

    void ConfigurePortraitAndChrome()
    {
        portraitRect = hostWindow.transform.Find("CharacterMask") as RectTransform;
        if (portraitRect != null)
        {
            originalPoses.Add(new RectPose(portraitRect));
            portraitRect.anchorMin = Vector2.zero; portraitRect.anchorMax = new Vector2(0f, 1f);
            portraitRect.offsetMin = new Vector2(1f, 1f);
            portraitRect.offsetMax = new Vector2(1f + PortraitWidth, -16f);
            var anima = portraitRect.Find("AnimaState") as RectTransform;
            if (anima != null)
            {
                HidePortraitElement(anima.Find("Image")?.gameObject);
                originalPoses.Add(new RectPose(anima));
                Stretch(anima, Vector2.zero, Vector2.zero);
                var portrait = anima.Find("Character") as RectTransform;
                if (portrait != null)
                {
                    originalPoses.Add(new RectPose(portrait));
                    portrait.anchorMin = portrait.anchorMax = new Vector2(.5f, 0f);
                    portrait.pivot = new Vector2(.5f, 0f);
                    portrait.anchoredPosition = Vector2.zero;
                    portrait.sizeDelta = new Vector2(125f, 175f);
                    portraitImage = portrait.GetComponent<UnityEngine.UI.Image>();
                    if (portraitImage != null)
                    { originalPortraitPreserveAspect = portraitImage.preserveAspect; portraitImage.preserveAspect = true; }
                }
            }
        }
        HidePortraitElement(hostWindow.transform.Find("PointGroup")?.gameObject);
        var drag = hostWindow.transform.Find("DragArea") as RectTransform;
        if (drag != null)
        {
            originalPoses.Add(new RectPose(drag));
            drag.anchorMin = new Vector2(0f, 1f); drag.anchorMax = Vector2.one;
            drag.offsetMin = new Vector2(1f, -15f); drag.offsetMax = new Vector2(-1f, -1f);
        }
    }

    void HidePortraitElement(GameObject target)
    {
        if (target == null) return;
        hiddenPortraitElements.Add(new KeyValuePair<GameObject, bool>(target, target.activeSelf));
        target.SetActive(false);
    }

    void Build()
    {
        root = Rect("Chapter04PrivateChat", hostWindow.transform);
        Stretch(root, new Vector2(ChatLeft, 1f), new Vector2(-1f, -16f));
        Image(root, Color.black, true);
        var divider = Rect("PortraitDivider", root);
        divider.anchorMin = Vector2.zero; divider.anchorMax = new Vector2(0f, 1f);
        divider.offsetMin = new Vector2(-1f, 0f); divider.offsetMax = Vector2.zero;
        Image(divider, Ink);
        var scroller = Rect("ScrollView", root);
        Stretch(scroller, new Vector2(6f, 32f), new Vector2(-6f, -3f));
        scroll = scroller.gameObject.AddComponent<OSSmoothScrollRect>();
        scroll.horizontal = false; scroll.vertical = true; scroll.inertia = false;
        scroll.movementType = UnityEngine.UI.ScrollRect.MovementType.Clamped;
        scroll.onValueChanged.AddListener(OnScrollPositionChanged);
        viewport = Rect("Viewport", scroller); Stretch(viewport, Vector2.zero, Vector2.zero);
        Image(viewport, Color.black, true);
        viewport.gameObject.AddComponent<UnityEngine.UI.Mask>().showMaskGraphic = false;
        content = Rect("Content", viewport);
        content.anchorMin = new Vector2(0f, 1f); content.anchorMax = Vector2.one;
        content.pivot = new Vector2(.5f, 1f); content.sizeDelta = Vector2.zero;
        rows = content.gameObject.AddComponent<UnityEngine.UI.VerticalLayoutGroup>();
        rows.spacing = 7f; rows.padding = new RectOffset(1, 1, 2, 3);
        rows.childControlWidth = true; rows.childControlHeight = true;
        rows.childForceExpandWidth = true; rows.childForceExpandHeight = false;
        var fitter = content.gameObject.AddComponent<UnityEngine.UI.ContentSizeFitter>();
        fitter.verticalFit = UnityEngine.UI.ContentSizeFitter.FitMode.PreferredSize;
        scroll.content = content; scroll.viewport = viewport;
        footer = Rect("Replies", root);
        footer.anchorMin = Vector2.zero; footer.anchorMax = new Vector2(1f, 0f);
        footer.offsetMin = new Vector2(6f, 5f); footer.offsetMax = new Vector2(-6f, 28f);
        root.SetAsLastSibling();
    }

    string Signature()
    {
        var state = story.State;
        string last = state.history.Count > 0 ? state.history[state.history.Count - 1]?.optionId : null;
        return state.currentVisit + ":" + state.currentEntryId + ":" + state.currentTextCompleted
            + ":" + state.completed + ":" + state.history.Count + ":" + last + ":" + story.FinalTreatmentStage;
    }

    void Rebuild()
    {
        var state = story.State;
        bool showLatest = followLatest || renderedVisit != state.currentVisit;
        float previousOffset = content.anchoredPosition.y;
        scroll.StopMovement();
        foreach (var row in rowObjects) if (row != null) { row.SetActive(false); Destroy(row); }
        rowObjects.Clear(); actions.Clear(); currentLabel = null; treatmentAction = null;
        for (int i = footer.childCount - 1; i >= 0; i--)
        { footer.GetChild(i).gameObject.SetActive(false); Destroy(footer.GetChild(i).gameObject); }
        AddConversationDate();
        var visits = new HashSet<int>();
        foreach (var record in state.history)
        {
            if (record == null || !visits.Add(record.visit)) continue;
            var label = Bubble(story.GetLineText(record.entryId), false);
            if (record.visit == state.currentVisit && !state.completed) currentLabel = label;
            if (!string.IsNullOrEmpty(record.optionId)) Bubble(OptionText(record.entryId, record.optionId), true);
        }
        if (!state.completed && !visits.Contains(state.currentVisit) && !string.IsNullOrEmpty(state.currentEntryId))
        {
            currentLabel = Bubble(story.GetLineText(state.currentEntryId), false);
        }
        var entry = story.CurrentEntry;
        bool choices = !state.completed && state.currentTextCompleted
            && entry?.PartType == OSCharacterInteractionPartType.TextWithOptions;
        bool treatment = state.completed && story.HasFinalTreatmentAccess;
        int count = choices ? entry.Options.Count : state.completed && !treatment ? 0 : 1;
        float footerHeight = 0f;
        if (currentLabel != null)
        {
            var target = currentLabel.transform.parent.GetComponent<UnityEngine.UI.Image>();
            target.raycastTarget = true;
            var next = target.gameObject.AddComponent<UnityEngine.UI.Button>();
            next.transition = UnityEngine.UI.Selectable.Transition.None;
            next.targetGraphic = target;
            next.onClick.AddListener(Advance);
            actions.Add(next);
        }
        for (int i = 0; i < count; i++)
        {
            string optionId = choices ? entry.Options[i].OptionId : null;
            string value = treatment ? OSToolProgramLibrary.Localize("ui.os.heart_heal.title", "心脏治疗")
                : choices ? OptionText(state.currentEntryId, optionId)
                : null;
            var action = Reply(value, i, footerHeight);
            footerHeight += ((RectTransform)action.transform).rect.height + 3f;
            if (treatment)
            { treatmentAction = action; action.onClick.AddListener(() => story.TryOpenFinalHeartHeal()); }
            else if (choices) action.onClick.AddListener(() => story.TrySelectOption(optionId));
            else action.onClick.AddListener(Advance);
            actions.Add(action);
        }
        footerHeight = Mathf.Max(23f, footerHeight);
        footer.offsetMax = new Vector2(-6f, 5f + footerHeight);
        scroll.GetComponent<RectTransform>().offsetMin = new Vector2(6f, footerHeight + 12f);
        positioningScroll = true;
        Canvas.ForceUpdateCanvases();
        UnityEngine.UI.LayoutRebuilder.ForceRebuildLayoutImmediate(content);
        if (showLatest) scroll.verticalNormalizedPosition = 0f;
        else
        {
            Vector2 position = content.anchoredPosition;
            position.y = Mathf.Clamp(previousOffset, 0f, Mathf.Max(0f, content.rect.height - viewport.rect.height));
            content.anchoredPosition = position;
        }
        positioningScroll = false;
        followLatest = showLatest;
        renderedVisit = state.currentVisit;
    }

    void Advance()
    {
        // A skip can only finish this line. The same click never consumes next.
        if (!story.TryCompleteCurrentText()) story.TryContinue();
    }

    string OptionText(string entryId, string optionId)
    {
        string value = story.GetOptionText(entryId, optionId) ?? string.Empty;
        // Menu numbering is not part of a spoken reply. Keep the authored
        // punctuation after that prefix, including the silence option's dots.
        int start = 0;
        while (start < value.Length && char.IsWhiteSpace(value[start])) start++;
        int end = start;
        while (end < value.Length && value[end] >= '0' && value[end] <= '9') end++;
        return end > start && end < value.Length && value[end] == '.'
            ? value.Substring(end + 1).TrimStart() : value;
    }

    void AddConversationDate()
    {
        // The conversation date is the first history row, so it scrolls away
        // with the messages. Authored historical dates remain narrative data.
        var row = Rect("ConversationDate", content); rowObjects.Add(row.gameObject);
        var size = row.gameObject.AddComponent<UnityEngine.UI.LayoutElement>(); size.preferredHeight = 21f;
        conversationDateLabel = Text("DateText", row, OSToolProgramLibrary.Localize(ConversationDateKey, ConversationDate));
        Stretch(conversationDateLabel.rectTransform, new Vector2(24f, 0f), new Vector2(-24f, 0f));
        conversationDateLabel.fontSize = 9f; conversationDateLabel.alignment = TextAlignmentOptions.Center;
        var leftRule = Rect("LeftRule", row); Top(leftRule, 2f, 10f, 18f, 1f); Image(leftRule, Ink);
        var rightRule = Rect("RightRule", row);
        rightRule.anchorMin = rightRule.anchorMax = new Vector2(1f, .5f); rightRule.pivot = new Vector2(1f, .5f);
        rightRule.anchoredPosition = new Vector2(-2f, 0f); rightRule.sizeDelta = new Vector2(18f, 1f); Image(rightRule, Ink);
    }

    void OnScrollPositionChanged(Vector2 position)
    {
        if (positioningScroll) return;
        float overflow = Mathf.Max(0f, content.rect.height - viewport.rect.height);
        followLatest = overflow <= .5f || position.y * overflow <= .25f;
    }

    TMP_Text Bubble(string value, bool player)
    {
        var row = Rect(player ? "PlayerReply" : "LiveMessage", content); rowObjects.Add(row.gameObject);
        var avatarRect = Rect(player ? "PlayerAvatar" : "LiveAvatar", row);
        avatarRect.anchorMin = avatarRect.anchorMax = new Vector2(player ? 1f : 0f, 1f);
        avatarRect.pivot = new Vector2(player ? 1f : 0f, 1f);
        avatarRect.anchoredPosition = new Vector2(0f, -1f);
        avatarRect.sizeDelta = new Vector2(AvatarSize, AvatarSize);
        var avatar = Image(avatarRect, Color.white);
        var records = GameDataManager.Instance?.RecordSO;
        if (records != null && records.TryGetRecordData(player ? 2 : 3, out var record))
            avatar.sprite = record.RecordIcon;
        avatar.preserveAspect = true;
        avatar.enabled = avatar.sprite != null;

        float available = Mathf.Max(80f, ExpandedSize.x - ChatLeft - 1f - 14f - AvatarSize - 10f);
        var bubble = Rect("Bubble", row);
        bubble.anchorMin = bubble.anchorMax = new Vector2(player ? 1f : 0f, 1f);
        bubble.pivot = new Vector2(player ? 1f : 0f, 1f);
        bubble.anchoredPosition = new Vector2(player ? -AvatarSize - 9f : AvatarSize + 9f, 0f);
        Image(bubble, Ink);
        var fill = Rect("Fill", bubble); Stretch(fill, Vector2.one, -Vector2.one); Image(fill, Color.black);
        var text = Text("Text", bubble, value);
        text.alignment = TextAlignmentOptions.TopLeft;
        text.enableWordWrapping = true;
        text.overflowMode = TextOverflowModes.Overflow;
        text.margin = Vector4.zero;
        float width = Mathf.Clamp(Mathf.Ceil(text.GetPreferredValues(value ?? string.Empty).x) + 12f, 44f, available);
        float height = Mathf.Ceil(text.GetPreferredValues(value ?? string.Empty, width - 12f, 1000f).y) + 12f;
        bubble.sizeDelta = new Vector2(width, Mathf.Max(26f, height));
        Stretch(text.rectTransform, new Vector2(6f, 5f), new Vector2(-6f, -5f));
        AddTail(bubble, player);
        var element = row.gameObject.AddComponent<UnityEngine.UI.LayoutElement>();
        element.preferredHeight = Mathf.Max(AvatarSize, bubble.sizeDelta.y);
        return text;
    }

    static void AddTail(RectTransform bubble, bool player)
    {
        // A small one-pixel stepped outline joins the bubble to its native avatar.
        for (int row = 0; row < 9; row++)
        {
            int reach = 4 - Mathf.Abs(row - 4);
            var fill = Rect("TailFill" + row, bubble);
            fill.anchorMin = fill.anchorMax = new Vector2(player ? 1f : 0f, 1f);
            fill.pivot = new Vector2(player ? 0f : 1f, 1f);
            fill.anchoredPosition = new Vector2(player ? -1f : 1f, -8f - row);
            fill.sizeDelta = new Vector2(reach + 2f, 1f); Image(fill, Color.black);
            var edge = Rect("TailEdge" + row, bubble);
            edge.anchorMin = edge.anchorMax = new Vector2(player ? 1f : 0f, 1f);
            edge.pivot = new Vector2(player ? 0f : 1f, 1f);
            edge.anchoredPosition = new Vector2(player ? reach : -reach, -8f - row);
            edge.sizeDelta = Vector2.one; Image(edge, Ink);
        }
    }

    void MirrorCurrentText()
    {
        if (currentLabel == null || story.State.currentTextCompleted) return;
        TMP_Text source = messages.Chapter04PlaybackLabel;
        bool current = messages.DisplayedEntryId == story.State.currentEntryId && messages.IsPresenting;
        currentLabel.text = current && source != null ? source.text : string.Empty;
        currentLabel.maxVisibleCharacters = current && source != null ? source.maxVisibleCharacters : 0;
        // Grow the current bubble as the original typewriter reveals lines.
        // Reserving a very long paragraph up front would scroll its first words
        // above this compact viewport before the player can read them.
        var bubble = currentLabel.transform.parent as RectTransform;
        var row = bubble != null ? bubble.parent.GetComponent<UnityEngine.UI.LayoutElement>() : null;
        if (bubble == null || row == null) return;
        string visible = currentLabel.text;
        if (current && source != null && source.maxVisibleCharacters < 100000)
        {
            visible = source.GetParsedText();
            visible = visible.Substring(0, Mathf.Clamp(source.maxVisibleCharacters, 0, visible.Length));
        }
        float height = Mathf.Max(26f, Mathf.Ceil(currentLabel.GetPreferredValues(visible, bubble.rect.width - 12f, 1000f).y) + 12f);
        if (Mathf.Approximately(height, row.preferredHeight)) return;
        bubble.sizeDelta = new Vector2(bubble.sizeDelta.x, height);
        row.preferredHeight = height;
        positioningScroll = true;
        UnityEngine.UI.LayoutRebuilder.ForceRebuildLayoutImmediate(content);
        if (followLatest) scroll.verticalNormalizedPosition = 0f;
        positioningScroll = false;
    }

    UnityEngine.UI.Button Reply(string value, int index, float offset)
    {
        float width = ExpandedSize.x - ChatLeft - 13f;
        float height = 20f;
        var rect = Rect("Reply" + index, footer); Top(rect, 0f, offset, width, height);
        var background = Image(rect, Color.black, true);
        // No spoken player reply means the same native-size prompt as Memory,
        // including while Live is typing. Actual choices keep their text.
        bool continuePrompt = value == null;
        var memory = continuePrompt ? OS_Handle.Instance?.MemoryWindowController : null;
        Sprite promptSprite = memory != null ? memory.ContinueTipSprite : null;
        Vector2 promptSize = memory != null ? memory.ContinueTipNativeSize : Vector2.zero;
        var promptPosition = new Vector2(Mathf.Floor((width - promptSize.x) * .5f), Mathf.Floor((20f - promptSize.y) * .5f));
        bool wrapReply = false;
        UnityEngine.UI.Graphic label;
        if (continuePrompt)
            label = ContinuePrompt("ContinueIcon", rect, promptSprite, promptSize, promptPosition, Color.white);
        else
        {
            var text = Text("Label", rect, value); Stretch(text.rectTransform, new Vector2(4, 0), new Vector2(-4, 0));
            text.alignment = TextAlignmentOptions.Center;
            // Keep short replies and the native continue prompt at their old
            // size. Longer authored replies get full-sized wrapped text.
            wrapReply = text.GetPreferredValues(value, float.PositiveInfinity, float.PositiveInfinity).x > width - 8f;
            text.enableWordWrapping = wrapReply;
            if (wrapReply)
                height = Mathf.Max(height, Mathf.Ceil(text.GetPreferredValues(value, width - 8f, float.PositiveInfinity).y) + 6f);
            label = text;
        }
        Top(rect, 0f, offset, width, height);
        var topEdge = Rect("Top", rect); Top(topEdge, 0, 0, width, 1); Image(topEdge, Ink);
        var bottomEdge = Rect("Bottom", rect); Top(bottomEdge, 0, height - 1f, width, 1); Image(bottomEdge, Ink);
        var leftEdge = Rect("Left", rect); Top(leftEdge, 0, 0, 1, height); Image(leftEdge, Ink);
        var rightEdge = Rect("Right", rect); Top(rightEdge, width - 1, 0, 1, height); Image(rightEdge, Ink);
        var fillRect = Rect("HoverFill", rect); Top(fillRect, 1f, 1f, 0f, height - 2f);
        Image(fillRect, Ink);
        fillRect.gameObject.AddComponent<UnityEngine.UI.RectMask2D>();
        // Clip a black copy with the moving fill, keeping both text and the
        // native prompt legible throughout the wipe.
        if (continuePrompt)
            ContinuePrompt("BlackContinueIcon", fillRect, promptSprite, promptSize, promptPosition - Vector2.one, Color.black);
        else
        {
            var filledLabel = Text("BlackLabel", fillRect, value);
            Top(filledLabel.rectTransform, 3f, -1f, width - 8f, height);
            filledLabel.alignment = TextAlignmentOptions.Center;
            filledLabel.enableWordWrapping = wrapReply;
            filledLabel.color = Color.black;
        }
        var button = rect.gameObject.AddComponent<UnityEngine.UI.Button>();
        button.targetGraphic = background;
        button.transition = UnityEngine.UI.Selectable.Transition.None;
        var hover = rect.gameObject.AddComponent<OSChapter04ChatReplyHover>();
        // Memory's sprite already contains its grey pixels; a second grey tint
        // would darken it. The black hover copy still uses the same alpha mask.
        hover.Initialize(label, button, fillRect, continuePrompt ? Color.white : Ink);
        return button;
    }

    UnityEngine.UI.Image ContinuePrompt(string name, Transform parent, Sprite sprite, Vector2 size, Vector2 position, Color color)
    {
        var rect = Rect(name, parent); Top(rect, position.x, position.y, size.x, size.y);
        var image = Image(rect, color);
        image.sprite = sprite; image.preserveAspect = true; image.enabled = sprite != null;
        return image;
    }

    TMP_Text Text(string name, Transform parent, string value)
    {
        var rect = Rect(name, parent);
        var text = rect.gameObject.AddComponent<TextMeshProUGUI>();
        text.font = template.font; text.fontSize = Mathf.Clamp(template.fontSize, 9f, 11f);
        text.fontSharedMaterial = template.fontSharedMaterial;
        OSLocalizedFontUtility.ApplyCurrent(text);
        text.text = value ?? string.Empty; text.color = Ink;
        text.raycastTarget = false; text.enableWordWrapping = false;
        return text;
    }

    static RectTransform Rect(string name, Transform parent)
    {
        var node = new GameObject(name, typeof(RectTransform)); node.layer = parent.gameObject.layer;
        var rect = (RectTransform)node.transform; rect.SetParent(parent, false); return rect;
    }
    static UnityEngine.UI.Image Image(RectTransform rect, Color color, bool raycast = false)
    {
        var image = rect.gameObject.AddComponent<UnityEngine.UI.Image>();
        image.color = color; image.raycastTarget = raycast; return image;
    }
    static void Stretch(RectTransform rect, Vector2 min, Vector2 max)
    { rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.offsetMin = min; rect.offsetMax = max; }
    static void Top(RectTransform rect, float x, float y, float width, float height)
    {
        rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f); rect.pivot = new Vector2(0f, 1f);
        rect.anchoredPosition = new Vector2(x, -y); rect.sizeDelta = new Vector2(width, height);
    }
}

// Hover is visual only; Button invokes the existing story input gate.
public sealed class OSChapter04ChatReplyHover : MonoBehaviour,
    UnityEngine.EventSystems.IPointerEnterHandler, UnityEngine.EventSystems.IPointerExitHandler
{
    public const float Duration = .1f;
    UnityEngine.UI.Graphic text;
    UnityEngine.UI.Button button;
    RectTransform fill;
    Color ink;
    Tween tween;
    bool pointerInside, active;
    public float FillAmount { get; private set; }

    public void Initialize(UnityEngine.UI.Graphic label, UnityEngine.UI.Button target, RectTransform highlight, Color normal)
    {
        text = label; button = target; fill = highlight; ink = normal;
        ResetVisual();
    }

    public void OnPointerEnter(UnityEngine.EventSystems.PointerEventData data)
    { pointerInside = true; RefreshVisual(); }
    public void OnPointerExit(UnityEngine.EventSystems.PointerEventData data)
    { pointerInside = false; RefreshVisual(); }

    void LateUpdate() => RefreshVisual();
    void OnDisable() => ResetVisual();
    void OnDestroy() => tween?.Kill();

    void RefreshVisual()
    {
        if (button == null || fill == null) return;
        if (!button.IsActive() || !button.IsInteractable())
        {
            if (active || FillAmount > 0f) ResetVisual(false);
            return;
        }
        if (active == pointerInside) return;
        active = pointerInside;
        tween?.Kill();
        if (!active && text != null) text.color = ink;
        tween = DOTween.To(() => FillAmount, SetFillAmount, active ? 1f : 0f, Duration)
            .SetEase(Ease.Linear).SetUpdate(true)
            .SetLink(gameObject, LinkBehaviour.KillOnDisable)
            .OnKill(() => tween = null);
    }

    void SetFillAmount(float value)
    {
        FillAmount = value;
        if (fill != null && button != null)
        {
            float width = ((RectTransform)button.transform).rect.width - 2f;
            fill.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, Mathf.Max(0f, width) * value);
        }
        if (text != null) text.color = active && value >= 1f ? Color.black : ink;
    }

    void ResetVisual(bool clearPointer = true)
    {
        tween?.Kill(); tween = null;
        if (clearPointer) pointerInside = false;
        active = false;
        SetFillAmount(0f);
    }
}
