using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
[RequireComponent(typeof(RectTransform))]
public sealed class OSLinkMessageWindowLayout : MonoBehaviour
{
    static readonly Vector2 LeftAnchor = Vector2.zero;
    static readonly Vector2 LeftPivot = Vector2.one;
    static readonly Vector2 RightAnchor = new Vector2(1f, 0f);
    static readonly Vector2 RightPivot = Vector2.zero;
    static readonly Vector2 CustomAnchor = new Vector2(0f, 1f);

    [Header("Screen Bounds")]
    [SerializeField] RectTransform screenBounds;

    [Header("Window Layout")]
    [SerializeField, Min(0f)] float preferredWidth = 150f;
    [SerializeField, Min(0f)] float minimumWidth = 60f;
    [SerializeField, Min(0f)] float screenEdgePadding = 2f;
    [SerializeField] Vector2 leftAnchoredPosition = new Vector2(-3f, 90f);
    [SerializeField] Vector2 rightAnchoredPosition = new Vector2(3f, 10f);

    [Header("Text Height")]
    [SerializeField] TMP_Text textPartText;
    [SerializeField, Min(0f)] float baseHeight = 18f;
    [SerializeField, Min(0f)] float extraHeight = 8f;
    [SerializeField, Min(0f)] float heightPerTextLine = 12f;

    [Header("Text + Button Height")]
    [SerializeField] GameObject textWithOptionsPart;
    [SerializeField] TMP_Text textWithOptionsText;
    [SerializeField] RectTransform textWithOptionsTextArea;
    [SerializeField] RectTransform buttonArea;
    [SerializeField, Min(0f)] float textAreaSingleLineHeight = 22f;
    [SerializeField, Min(0f)] float buttonAreaHorizontalInset = 5f;

    [Header("Squeeze Feedback")]
    [SerializeField, Min(0f)] float squeezeHeightIncrease = 1.5f;
    [SerializeField, Min(0f)] float squeezeHeightDuration = 0.1f;

    readonly Vector3[] ownerWorldCorners = new Vector3[4];
    readonly Vector3[] linkWorldCorners = new Vector3[4];

    RectTransform linkWindowRect;
    RectTransform ownerWindowRect;
    RectTransform resolvedScreenBounds;
    OSWindowUnit linkWindow;
    OSCharacterWindowController ownerCharacter;
    bool isDraggingCustomPosition;
    Tween heightTween;
    TMP_FontAsset lastMeasuredFont;
    TMP_Text lastMeasuredTextComponent;
    TMP_Text measurementTextComponent;
    string lastMeasuredText;
    string measurementText;
    float contentHeight;
    float lastMeasuredFontSize;
    float lastMeasuredWidth;
    float lastTargetWidth;
    int lastMeasuredLineCount = 1;
    bool hasContentHeight;
    bool hasLastTargetWidth;
    bool hasMeasurementText;
    bool hasTextMeasurement;
    bool isHeightRaised;
    bool isOnRight;
    float textFooterHeight;
    RectTransform footerReservedTextRect;
    Vector2 textOffsetBeforeFooter;
    UnityEngine.Object bottomDialogueOwner;
    bool ownsBottomTemporaryLayout;
    UnityEngine.Object leftDialogueOwner;
    UnityEngine.Object externalLayoutOwner;

    // A chapter-owned chat can keep the existing playback controls without
    // letting the speech-bubble layout resize its new history viewport.
    public void SetExternalLayoutOwner(UnityEngine.Object owner, bool active)
    {
        if (owner == null || externalLayoutOwner != null && externalLayoutOwner != owner) return;
        if (active) { FinishPositionDrag(); KillHeightTween(); }
        externalLayoutOwner = active ? owner : null;
        if (!active) { hasContentHeight = false; hasLastTargetWidth = false; RefreshLayout(); }
    }

    public void SetTemporaryLeftDialogue(UnityEngine.Object owner, bool enabled)
    {
        if (owner == null || leftDialogueOwner != null && leftDialogueOwner != owner) return;
        ResolveReferences();
        if (enabled) FinishPositionDrag();
        leftDialogueOwner = enabled ? owner : null;
        isOnRight = false;
        hasLastTargetWidth = false;
        ConfigureWindowInteraction();
        RefreshLayout();
    }

    public void SetTemporaryBottomDialogue(UnityEngine.Object owner, bool enabled)
    {
        if (!enabled) { ClearTemporaryBottomDialogue(owner); return; }
        if (owner == null || (bottomDialogueOwner != null && bottomDialogueOwner != owner)) return;
        ResolveReferences();
        if (bottomDialogueOwner == null && linkWindow != null)
        {
            FinishPositionDrag();
            ownsBottomTemporaryLayout = !linkWindow.HasTemporaryOpeningLayout;
            if (ownsBottomTemporaryLayout)
                linkWindow.SetTemporaryOpeningLayout(Vector2.one, linkWindowRect.anchoredPosition, linkWindow.RecordedSize);
        }
        bottomDialogueOwner = owner;
        linkWindow?.ConfigureInteraction(false, false);
        RefreshLayout();
    }

    public void ClearTemporaryBottomDialogue(UnityEngine.Object owner)
    {
        if (owner == null || bottomDialogueOwner != owner) return;
        bottomDialogueOwner = null;
        if (ownsBottomTemporaryLayout) linkWindow?.ClearTemporaryOpeningLayout();
        ownsBottomTemporaryLayout = false;
        ConfigureWindowInteraction();
        RefreshLayout();
    }

    public bool IsOnRight => isOnRight;
    public float TextFooterHeight => textFooterHeight;

    public void SetTextFooterHeight(float height)
    {
        height = Mathf.Max(0f, height);
        if (Mathf.Approximately(textFooterHeight, height))
            return;

        ResolveReferences();
        textFooterHeight = height;
        ApplyTextFooterReservation();
        hasContentHeight = false;
        if (isActiveAndEnabled)
            RefreshLayout();
    }

    public void SetMeasurementText(string text)
    {
        ResolveReferences();
        SetMeasurementText(GetActiveTextComponent(), text);
    }

    public void SetMeasurementText(TMP_Text textComponent, string text)
    {
        string nextText = text ?? string.Empty;
        if (hasMeasurementText
            && measurementTextComponent == textComponent
            && string.Equals(
                measurementText,
                nextText,
                System.StringComparison.Ordinal))
        {
            return;
        }

        measurementText = nextText;
        measurementTextComponent = textComponent;
        hasMeasurementText = true;
        InvalidateTextMeasurement();
        if (isActiveAndEnabled)
            RefreshLayout();
    }

    public void ClearMeasurementText()
    {
        if (!hasMeasurementText)
            return;

        measurementText = null;
        measurementTextComponent = null;
        hasMeasurementText = false;
        InvalidateTextMeasurement();
        if (isActiveAndEnabled)
            RefreshLayout();
    }

    void Awake()
    {
        ResolveReferences();
        SanitizeSettings();
        ConfigureWindowInteraction();
    }

    void OnEnable()
    {
        ResolveReferences();
        SanitizeSettings();
        ConfigureWindowInteraction();
        KillHeightTween();
        InvalidateTextMeasurement();
        hasContentHeight = false;
        hasLastTargetWidth = false;
        isHeightRaised = false;
        RefreshLayout();
    }

    void OnDisable()
    {
        FinishPositionDrag();
        UnbindWindowEvents();
        KillHeightTween();
        SetTextFooterHeight(0f);
        if (hasContentHeight)
            SetHeightImmediate(contentHeight);
        hasLastTargetWidth = false;
        isHeightRaised = false;
    }

    void OnDestroy()
    {
        RestoreTextFooterReservation();
        UnbindWindowEvents();
        KillHeightTween();
    }

    void LateUpdate()
    {
        RefreshLayout();
    }

    void OnTransformParentChanged()
    {
        ResolveReferences();
        if (isActiveAndEnabled)
            RefreshLayout();
    }

    public void RefreshLayout()
    {
        if (externalLayoutOwner != null) return;
        if (linkWindowRect == null
            || ownerWindowRect == null
            || resolvedScreenBounds == null)
        {
            ResolveReferences();
            if (linkWindowRect == null
                || ownerWindowRect == null
                || resolvedScreenBounds == null)
            {
                return;
            }
        }

        if (bottomDialogueOwner != null)
        {
            // A temporary portrait-and-dialogue composition never overwrites
            // a user-dragged speech-window position or its persisted layout.
            KillHeightTween();
            isHeightRaised = false;
            float scale = Mathf.Abs(resolvedScreenBounds.InverseTransformVector(linkWindowRect.TransformVector(Vector3.right)).x);
            float width = Mathf.Min(244f, (resolvedScreenBounds.rect.width - 2f * screenEdgePadding) / Mathf.Max(.001f, scale));
            linkWindowRect.anchorMin = linkWindowRect.anchorMax = new Vector2(.5f, 0f);
            linkWindowRect.pivot = new Vector2(.5f, 1f);
            linkWindowRect.anchoredPosition = new Vector2(0f, -6f);
            linkWindowRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, width);
            ApplyTextWithOptionsWidth(width);
            RefreshContentHeight();
            return;
        }
        if (leftDialogueOwner != null)
        {
            isOnRight = false;
            GetOwnerHorizontalEdges(out float left, out _);
            ApplyLayout(Mathf.Clamp(GetAvailableLeftWidth(left), minimumWidth, preferredWidth));
            RefreshContentHeight();
            return;
        }
        if (ownerCharacter != null && ownerCharacter.HasCustomLinkMessagePosition)
        {
            CaptureDraggedPosition();
            ApplyCustomLayout();
            return;
        }

        GetOwnerHorizontalEdges(out float ownerLeft, out float ownerRight);
        float availableLeftWidth = GetAvailableLeftWidth(ownerLeft);
        float availableRightWidth = GetAvailableRightWidth(ownerRight);
        float availableCurrentWidth = isOnRight
            ? availableRightWidth
            : availableLeftWidth;
        float availableOppositeWidth = isOnRight
            ? availableLeftWidth
            : availableRightWidth;

        if (availableCurrentWidth < minimumWidth
            && availableOppositeWidth > availableCurrentWidth)
        {
            isOnRight = !isOnRight;
            availableCurrentWidth = availableOppositeWidth;
        }

        float targetWidth = Mathf.Clamp(
            availableCurrentWidth,
            minimumWidth,
            preferredWidth);
        bool isActivelySqueezing = hasLastTargetWidth
            && targetWidth < lastTargetWidth - 0.01f;
        lastTargetWidth = targetWidth;
        hasLastTargetWidth = true;

        ApplyLayout(targetWidth);
        RefreshContentHeight();
        SetSqueezeHeight(isActivelySqueezing);
    }

    public void RestorePosition()
    {
        isDraggingCustomPosition = false;
        isOnRight = false;
        hasLastTargetWidth = false;
        RefreshLayout();
    }

    public void CaptureDraggedPosition()
    {
        if (isDraggingCustomPosition && linkWindowRect != null && ownerCharacter != null)
            ownerCharacter.SetLinkMessagePosition(linkWindowRect.anchoredPosition, false);
    }

    void ConfigureWindowInteraction()
    {
        if (linkWindow == null)
            return;

        // Message height belongs to the content layout; the title only moves it.
        linkWindow.ConfigureInteraction(bottomDialogueOwner == null && leftDialogueOwner == null
            && externalLayoutOwner == null, false);
        linkWindow.MoveStarted -= HandleMoveStarted;
        linkWindow.MoveStarted += HandleMoveStarted;
        linkWindow.LayoutChanged -= HandleWindowLayoutChanged;
        linkWindow.LayoutChanged += HandleWindowLayoutChanged;
    }

    void UnbindWindowEvents()
    {
        if (linkWindow == null)
            return;

        linkWindow.MoveStarted -= HandleMoveStarted;
        linkWindow.LayoutChanged -= HandleWindowLayoutChanged;
    }

    void HandleMoveStarted(OSWindowUnit source)
    {
        if (source != linkWindow || ownerCharacter == null || linkWindowRect == null)
            return;

        Vector3 topLeft = linkWindowRect.TransformPoint(new Vector3(
            linkWindowRect.rect.xMin, linkWindowRect.rect.yMax, 0f));
        linkWindowRect.anchorMin = CustomAnchor;
        linkWindowRect.anchorMax = CustomAnchor;
        linkWindowRect.pivot = CustomAnchor;
        linkWindowRect.position = topLeft;
        isDraggingCustomPosition = true;
        CaptureDraggedPosition();
        KillHeightTween();
        isHeightRaised = false;
        hasLastTargetWidth = false;
    }

    void HandleWindowLayoutChanged(OSWindowUnit source)
    {
        if (source == linkWindow)
            FinishPositionDrag();
    }

    void FinishPositionDrag()
    {
        if (!isDraggingCustomPosition)
            return;

        CaptureDraggedPosition();
        isDraggingCustomPosition = false;
        ownerCharacter?.CaptureLinkMessagePosition();
    }

    void ApplyCustomLayout()
    {
        linkWindowRect.anchorMin = CustomAnchor;
        linkWindowRect.anchorMax = CustomAnchor;
        linkWindowRect.pivot = CustomAnchor;
        linkWindowRect.anchoredPosition = ownerCharacter.LinkMessagePosition;

        float horizontalScale = Mathf.Abs(resolvedScreenBounds.InverseTransformVector(
            linkWindowRect.TransformVector(Vector3.right)).x);
        float availableWidth = Mathf.Max(1f, resolvedScreenBounds.rect.width - 2f * screenEdgePadding)
            / Mathf.Max(0.001f, horizontalScale);
        float width = Mathf.Min(preferredWidth, availableWidth);
        linkWindowRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, width);
        ApplyTextWithOptionsWidth(width);
        RefreshContentHeight();
        ClampCustomPositionToScreen();
    }

    void ClampCustomPositionToScreen()
    {
        linkWindowRect.GetWorldCorners(linkWorldCorners);
        Vector3 ownerPresentationOffset = GetOwnerPresentationOffset();
        Vector2 minimum = new Vector2(float.PositiveInfinity, float.PositiveInfinity);
        Vector2 maximum = new Vector2(float.NegativeInfinity, float.NegativeInfinity);
        foreach (Vector3 corner in linkWorldCorners)
        {
            Vector2 local = resolvedScreenBounds.InverseTransformPoint(corner - ownerPresentationOffset);
            minimum = Vector2.Min(minimum, local);
            maximum = Vector2.Max(maximum, local);
        }

        Rect bounds = resolvedScreenBounds.rect;
        float xMin = bounds.xMin + screenEdgePadding;
        float xMax = bounds.xMax - screenEdgePadding;
        float yMin = bounds.yMin + screenEdgePadding;
        float yMax = bounds.yMax - screenEdgePadding;
        Vector2 correction = Vector2.zero;
        if (minimum.x < xMin)
            correction.x = xMin - minimum.x;
        else if (maximum.x > xMax)
            correction.x = xMax - maximum.x;
        if (maximum.y - minimum.y > yMax - yMin || maximum.y > yMax)
            correction.y = yMax - maximum.y;
        else if (minimum.y < yMin)
            correction.y = yMin - minimum.y;

        // Keep the requested offset when clamping after an owner move or resize.
        linkWindowRect.position += resolvedScreenBounds.TransformVector(correction);
    }

    void GetOwnerHorizontalEdges(out float ownerLeft, out float ownerRight)
    {
        ownerWindowRect.GetWorldCorners(ownerWorldCorners);
        Vector3 ownerPresentationOffset = GetOwnerPresentationOffset();
        ownerLeft = float.PositiveInfinity;
        ownerRight = float.NegativeInfinity;
        for (int i = 0; i < ownerWorldCorners.Length; i++)
        {
            float localX = resolvedScreenBounds
                .InverseTransformPoint(ownerWorldCorners[i] - ownerPresentationOffset)
                .x;
            ownerLeft = Mathf.Min(ownerLeft, localX);
            ownerRight = Mathf.Max(ownerRight, localX);
        }
    }

    Vector3 GetOwnerPresentationOffset()
    {
        OSWindowUnit owner = ownerCharacter != null ? ownerCharacter.GetComponent<OSWindowUnit>() : null;
        return owner != null ? owner.PresentationWorldOffset : Vector3.zero;
    }

    float GetAvailableLeftWidth(float ownerLeft)
    {
        float linkRight = ownerLeft + leftAnchoredPosition.x;
        float innerScreenLeft = resolvedScreenBounds.rect.xMin
                                + screenEdgePadding;
        return Mathf.Max(
            0f,
            linkRight - innerScreenLeft);
    }

    float GetAvailableRightWidth(float ownerRight)
    {
        float linkLeft = ownerRight + rightAnchoredPosition.x;
        float innerScreenRight = resolvedScreenBounds.rect.xMax
                                 - screenEdgePadding;
        return Mathf.Max(
            0f,
            innerScreenRight - linkLeft);
    }

    void ApplyLayout(float width)
    {
        Vector2 targetAnchor = isOnRight ? RightAnchor : LeftAnchor;
        Vector2 targetPivot = isOnRight ? RightPivot : LeftPivot;
        Vector2 targetPosition = isOnRight
            ? rightAnchoredPosition
            : leftAnchoredPosition;

        if (linkWindowRect.anchorMin != targetAnchor)
            linkWindowRect.anchorMin = targetAnchor;
        if (linkWindowRect.anchorMax != targetAnchor)
            linkWindowRect.anchorMax = targetAnchor;
        if (linkWindowRect.pivot != targetPivot)
            linkWindowRect.pivot = targetPivot;
        if (linkWindowRect.anchoredPosition != targetPosition)
            linkWindowRect.anchoredPosition = targetPosition;

        Vector2 targetSize = linkWindowRect.sizeDelta;
        if (!Mathf.Approximately(targetSize.x, width))
        {
            targetSize.x = width;
            linkWindowRect.sizeDelta = targetSize;
        }

        ApplyTextWithOptionsWidth(width);
    }

    void SetSqueezeHeight(bool shouldRaise)
    {
        if (isHeightRaised == shouldRaise)
            return;

        isHeightRaised = shouldRaise;
        float targetHeight = GetContentHeight()
                             + (shouldRaise ? squeezeHeightIncrease : 0f);
        KillHeightTween();

        if (!Application.isPlaying || squeezeHeightDuration <= 0f)
        {
            SetHeightImmediate(targetHeight);
            return;
        }

        heightTween = DOTween.To(
                () => linkWindowRect != null
                    ? linkWindowRect.sizeDelta.y
                    : targetHeight,
                value => SetHeightImmediate(value),
                targetHeight,
                squeezeHeightDuration)
            .SetEase(Ease.OutSine)
            .SetUpdate(true);
    }

    void RefreshContentHeight()
    {
        bool usesTextWithOptions = IsUsingTextWithOptions();
        ApplyTextFooterReservation();
        int lineCount = GetTextLineCount(GetActiveTextComponent());
        float targetHeight = baseHeight
                             + extraHeight
                             + heightPerTextLine * lineCount
                             + (usesTextWithOptions ? 0f : textFooterHeight);
        if (usesTextWithOptions)
        {
            ApplyTextWithOptionsTextAreaHeight(lineCount);
            targetHeight += GetButtonAreaHeight();
        }

        if (hasContentHeight
            && Mathf.Approximately(contentHeight, targetHeight))
        {
            float expectedHeight = contentHeight
                                   + (isHeightRaised
                                       ? squeezeHeightIncrease
                                       : 0f);
            if (heightTween == null
                && linkWindowRect != null
                && !Mathf.Approximately(
                    linkWindowRect.sizeDelta.y,
                    expectedHeight))
            {
                SetHeightImmediate(expectedHeight);
            }

            return;
        }

        contentHeight = targetHeight;
        hasContentHeight = true;
        KillHeightTween();
        SetHeightImmediate(
            contentHeight + (isHeightRaised ? squeezeHeightIncrease : 0f));
    }

    int GetTextLineCount(TMP_Text textComponent)
    {
        if (textComponent == null)
            return 1;

        RectTransform textRect = textComponent.rectTransform;
        float currentWidth = textRect != null ? textRect.rect.width : 0f;
        bool usesMeasurementText = hasMeasurementText
                                   && measurementTextComponent == textComponent;
        string sourceText = usesMeasurementText
            ? measurementText ?? string.Empty
            : textComponent.text;
        bool needsMeasurement = !hasTextMeasurement
                                || lastMeasuredTextComponent != textComponent
                                || textComponent.havePropertiesChanged
                                || !string.Equals(
                                    lastMeasuredText,
                                    sourceText,
                                    System.StringComparison.Ordinal)
                                || !Mathf.Approximately(
                                    lastMeasuredWidth,
                                    currentWidth)
                                || lastMeasuredFont != textComponent.font
                                || !Mathf.Approximately(
                                    lastMeasuredFontSize,
                                    textComponent.fontSize);
        if (!needsMeasurement)
            return lastMeasuredLineCount;

        string displayText = textComponent.text;
        bool restoreDisplayText = usesMeasurementText
                                  && !string.Equals(
                                      displayText,
                                      sourceText,
                                      System.StringComparison.Ordinal);
        if (restoreDisplayText)
            textComponent.text = sourceText;

        textComponent.ForceMeshUpdate(true);
        lastMeasuredLineCount = Mathf.Max(
            1,
            textComponent.textInfo.lineCount);
        if (restoreDisplayText)
        {
            textComponent.text = displayText;
            textComponent.ForceMeshUpdate(true);
        }

        lastMeasuredTextComponent = textComponent;
        lastMeasuredText = sourceText;
        lastMeasuredWidth = textRect != null ? textRect.rect.width : 0f;
        lastMeasuredFont = textComponent.font;
        lastMeasuredFontSize = textComponent.fontSize;
        hasTextMeasurement = true;
        return lastMeasuredLineCount;
    }

    void ApplyTextWithOptionsWidth(float windowWidth)
    {
        if (textWithOptionsTextArea != null)
        {
            textWithOptionsTextArea.SetSizeWithCurrentAnchors(
                RectTransform.Axis.Horizontal,
                windowWidth);
        }

        if (buttonArea != null)
        {
            buttonArea.SetSizeWithCurrentAnchors(
                RectTransform.Axis.Horizontal,
                Mathf.Max(0f, windowWidth - buttonAreaHorizontalInset));
        }
    }

    void ApplyTextWithOptionsTextAreaHeight(int lineCount)
    {
        if (textWithOptionsTextArea == null)
            return;

        float targetHeight = textAreaSingleLineHeight
                             + heightPerTextLine
                             * Mathf.Max(0, lineCount - 1);
        textWithOptionsTextArea.SetSizeWithCurrentAnchors(
            RectTransform.Axis.Vertical,
            targetHeight);

        Vector2 position = textWithOptionsTextArea.anchoredPosition;
        float topAnchoredY = -targetHeight
                             * (1f - textWithOptionsTextArea.pivot.y);
        if (!Mathf.Approximately(position.y, topAnchoredY))
        {
            position.y = topAnchoredY;
            textWithOptionsTextArea.anchoredPosition = position;
        }
    }

    float GetButtonAreaHeight()
    {
        if (buttonArea == null || !buttonArea.gameObject.activeSelf)
            return 0f;

        LayoutRebuilder.ForceRebuildLayoutImmediate(buttonArea);
        return Mathf.Max(
            0f,
            buttonArea.rect.height,
            LayoutUtility.GetPreferredHeight(buttonArea));
    }

    bool IsUsingTextWithOptions()
    {
        return textWithOptionsPart != null
               && textWithOptionsPart.activeSelf;
    }

    void ApplyTextFooterReservation()
    {
        RectTransform target = textPartText != null ? textPartText.rectTransform : null;
        if (textFooterHeight <= 0f || IsUsingTextWithOptions() || target == null)
        {
            RestoreTextFooterReservation();
            return;
        }

        if (footerReservedTextRect != target)
        {
            RestoreTextFooterReservation();
            footerReservedTextRect = target;
            textOffsetBeforeFooter = target.offsetMin;
        }

        // The ordinary message TMP stretches to its TextPart. Growing the window
        // and raising only its bottom edge preserves both wrapping and body height.
        target.offsetMin = textOffsetBeforeFooter + Vector2.up * textFooterHeight;
    }

    void RestoreTextFooterReservation()
    {
        if (footerReservedTextRect != null)
            footerReservedTextRect.offsetMin = textOffsetBeforeFooter;
        footerReservedTextRect = null;
    }

    TMP_Text GetActiveTextComponent()
    {
        return IsUsingTextWithOptions() && textWithOptionsText != null
            ? textWithOptionsText
            : textPartText;
    }

    float GetContentHeight()
    {
        if (!hasContentHeight)
            RefreshContentHeight();

        return contentHeight;
    }

    void InvalidateTextMeasurement()
    {
        hasTextMeasurement = false;
        lastMeasuredTextComponent = null;
        lastMeasuredText = null;
        lastMeasuredFont = null;
        lastMeasuredLineCount = 1;
    }

    void SetHeightImmediate(float height)
    {
        if (linkWindowRect == null)
            return;

        Vector2 size = linkWindowRect.sizeDelta;
        if (Mathf.Approximately(size.y, height))
            return;

        size.y = height;
        linkWindowRect.sizeDelta = size;
    }

    void KillHeightTween()
    {
        heightTween?.Kill();
        heightTween = null;
    }

    void ResolveReferences()
    {
        linkWindowRect = transform as RectTransform;
        if (linkWindow == null)
            linkWindow = GetComponent<OSWindowUnit>();
        ownerWindowRect = linkWindowRect != null
            ? linkWindowRect.parent as RectTransform
            : null;
        ownerCharacter = ownerWindowRect != null
            ? ownerWindowRect.GetComponent<OSCharacterWindowController>()
            : null;
        resolvedScreenBounds = screenBounds != null
            ? screenBounds
            : ownerWindowRect != null
                ? ownerWindowRect.parent as RectTransform
                : null;

        if (textPartText == null)
        {
            Transform textPart = transform.Find("MessageArea/TextPart");
            textPartText = textPart != null
                ? textPart.GetComponentInChildren<TMP_Text>(true)
                : null;
            InvalidateTextMeasurement();
        }

        Transform messageArea = transform.Find("MessageArea");
        if (textWithOptionsPart == null)
        {
            textWithOptionsPart = messageArea
                ?.Find("Text+ButtonPart")
                ?.gameObject;
        }

        if (textWithOptionsTextArea == null && textWithOptionsPart != null)
        {
            textWithOptionsTextArea = textWithOptionsPart.transform
                .Find("TextArea") as RectTransform;
        }

        if (textWithOptionsText == null
            && textWithOptionsTextArea != null)
        {
            textWithOptionsText = textWithOptionsTextArea
                .GetComponent<TMP_Text>();
        }

        if (buttonArea == null && textWithOptionsTextArea != null)
        {
            buttonArea = textWithOptionsTextArea
                .Find("ButtonArea") as RectTransform;
        }
    }

    void SanitizeSettings()
    {
        preferredWidth = Mathf.Max(0f, preferredWidth);
        minimumWidth = Mathf.Clamp(minimumWidth, 0f, preferredWidth);
        screenEdgePadding = Mathf.Max(0f, screenEdgePadding);
        baseHeight = Mathf.Max(0f, baseHeight);
        extraHeight = Mathf.Max(0f, extraHeight);
        heightPerTextLine = Mathf.Max(0f, heightPerTextLine);
        textAreaSingleLineHeight = Mathf.Max(
            0f,
            textAreaSingleLineHeight);
        buttonAreaHorizontalInset = Mathf.Max(
            0f,
            buttonAreaHorizontalInset);
        squeezeHeightIncrease = Mathf.Max(0f, squeezeHeightIncrease);
        squeezeHeightDuration = Mathf.Max(0f, squeezeHeightDuration);
    }

    void OnValidate()
    {
        SanitizeSettings();
        InvalidateTextMeasurement();
        hasContentHeight = false;
    }
}
