using System;
using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public enum DesktopProcessVisualState
{
    Normal,
    New,
    Locked,
    TutorialSuppressed
}

[DisallowMultipleComponent]
[RequireComponent(typeof(Image))]
public sealed class DesktopProcessUnit : MonoBehaviour,
    IPointerEnterHandler,
    IPointerExitHandler,
    IPointerClickHandler,
    IBeginDragHandler,
    IDragHandler,
    IEndDragHandler
{
    static readonly Color NormalIconColor = new Color32(0xFF, 0xFF, 0xFF, 0xFF);
    [SerializeField] Color normalNameColor = new Color32(0x9F, 0x9B, 0x97, 0xFF);
    static readonly Color LockedContentColor = new Color32(0x65, 0x65, 0x65, 0xFF);

    [Header("References")]
    [SerializeField] Image hoverTip;
    [SerializeField] Image processIcon;
    [SerializeField] TMP_Text processName;
    [SerializeField] TMP_Text newTipText;
    [SerializeField] Image newTipImage;
    [SerializeField] Image imageLockTip;

    [Header("State")]
    [SerializeField] string processKey;
    [SerializeField] bool requiresDoubleClick;
    [SerializeField, Min(0f)] float newTipFadeDuration = 0.1f;

    [Header("New Content Hint")]
    [SerializeField, Min(0f)] float newContentBounceHeight = 6f;

    [Header("Hover")]
    [SerializeField, Range(0f, 1f)] float normalAlpha;
    [SerializeField, Range(0f, 1f)] float hoverAlpha = 1f;
    [SerializeField, Min(0f)] float fadeDuration = 0.15f;

    public Image HoverTip => hoverTip;
    public Image ProcessIcon => processIcon;
    public TMP_Text ProcessName => processName;
    public TMP_Text NewTipText => newTipText;
    public Image NewTipImage => newTipImage;
    public Image ImageLockTip => imageLockTip;
    public string ProcessKey => processKey;
    public bool RequiresDoubleClick => requiresDoubleClick;
    public bool UsesPersistentState { get; private set; }
    public bool UsesPersistentNewState => UsesPersistentState;
    public bool IsStateInitialized { get; private set; }
    public bool IsNewStateInitialized => IsStateInitialized;
    public bool HasNewContent { get; private set; }
    public bool IsLocked { get; private set; }
    public bool IsTutorialSuppressed { get; private set; }
    public bool IsInteractable => !IsLocked
        && !IsTutorialSuppressed
        && OS_Handle.Instance?.IsSystemInputLocked != true;
    public DesktopProcessVisualState CurrentState { get; private set; }
    public bool IsTemporarilyUnavailable { get; private set; }
    public bool IsUnavailableHovered => IsTemporarilyUnavailable && pointerInside;
    public RectTransform UnavailableMark => unavailableMark;
    bool pointerInside;
    RectTransform unavailableMark;

    public event Action<DesktopProcessUnit> PointerEntered;
    public event Action<DesktopProcessUnit> Clicked;

    Tween _newTipFadeTween;
    Sequence _newContentBounce;
    RectTransform _newContentBounceRect;
    Vector2 _newContentBounceRestPosition;
    OS_Handle _dragHandle;
    bool _isDragging;
    bool _isDragGhosted;
    float _dragGhostAlpha = 0.35f;
    int _suppressClickFrame = -1;

    void Awake()
    {
        ResolveReferences();
        SetTipAlpha(normalAlpha);
        ApplyVisualState(false);
    }

    void OnEnable()
    {
        ResolveReferences();
        SetTipAlpha(normalAlpha);
        ApplyVisualState(false);
        GetComponentInParent<OS_Handle>(true)
            ?.InitializeDesktopProcessUnit(this);
        GetComponentInParent<OSDesktopProcessGrid>(true)
            ?.EnsureDirectChildrenRegistered();
    }

    void OnDisable()
    {
        pointerInside = false;
        RefreshUnavailableVisual();
        if (_isDragging)
        {
            _isDragging = false;
            _dragHandle?.CancelDesktopProcessDrag(this);
            _dragHandle = null;
        }

        SetDragGhosted(false);
        StopNewContentBounce();
        KillFade();
        KillNewTipFade();
        SetTipAlpha(normalAlpha);
    }

    void OnDestroy()
    {
        StopNewContentBounce();
        KillFade();
        KillNewTipFade();
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        pointerInside = true;
        if (IsTemporarilyUnavailable)
        {
            RefreshUnavailableVisual();
            return;
        }
        if (!IsInteractable)
            return;

        PointerEntered?.Invoke(this);
        FadeTipTo(hoverAlpha);
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        pointerInside = false;
        RefreshUnavailableVisual();
        FadeTipTo(normalAlpha);
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        if (IsTemporarilyUnavailable || !IsInteractable
            || _isDragging
            || _suppressClickFrame == Time.frameCount
            || (requiresDoubleClick && eventData.clickCount < 2)
            || eventData.button != PointerEventData.InputButton.Left)
        {
            return;
        }

        Clicked?.Invoke(this);
    }

    public void SetRequiresDoubleClick(bool required) => requiresDoubleClick = required;

    public void OnBeginDrag(PointerEventData eventData)
    {
        if (IsTemporarilyUnavailable || !IsInteractable
            || eventData.button != PointerEventData.InputButton.Left)
        {
            return;
        }

        // Restore the child before the drag proxy copies its position.
        StopNewContentBounce();
        OS_Handle handle = GetComponentInParent<OS_Handle>(true)
            ?? OS_Handle.Instance;
        if (handle == null
            || !handle.TryBeginDesktopProcessDrag(this, eventData))
        {
            return;
        }

        _dragHandle = handle;
        _isDragging = true;
    }

    public void OnDrag(PointerEventData eventData)
    {
        if (_isDragging)
            _dragHandle?.UpdateDesktopProcessDrag(this, eventData);
    }

    public void OnEndDrag(PointerEventData eventData)
    {
        if (!_isDragging)
            return;

        _suppressClickFrame = Time.frameCount;
        OS_Handle handle = _dragHandle;
        _isDragging = false;
        _dragHandle = null;
        handle?.EndDesktopProcessDrag(this, eventData);
    }

    public void SetDragGhosted(bool isGhosted, float alpha = 0.35f)
    {
        _isDragGhosted = isGhosted;
        _dragGhostAlpha = Mathf.Clamp01(alpha);
        if (_isDragGhosted)
        {
            StopNewContentBounce();
            ApplyDragGhostAlpha();
            return;
        }

        ApplyVisualState(false);
    }

    public void InitializeState(
        string key,
        bool usesPersistentState,
        bool isNew,
        bool isLocked)
    {
        StopNewContentBounce();
        processKey = string.IsNullOrWhiteSpace(key) ? string.Empty : key.Trim();
        UsesPersistentState = usesPersistentState;
        IsStateInitialized = true;
        HasNewContent = isNew;
        IsLocked = isLocked;
        ApplyVisualState(false);
    }

    public void InitializeNewState(
        string key,
        bool usesPersistentState,
        bool isNew)
    {
        InitializeState(key, usesPersistentState, isNew, false);
    }

    public void SetNewState(bool isNew, bool animateWhenClearing = false)
    {
        HasNewContent = isNew;
        ApplyVisualState(animateWhenClearing);
        if (isNew)
            PlayNewContentBounce();
        else
            StopNewContentBounce();
    }

    public void SetLockedState(bool isLocked)
    {
        IsLocked = isLocked;
        ApplyVisualState(false);
    }

    // A temporary story gate still receives hover, unlike a persistent locked app.
    public void SetTemporarilyUnavailable(bool value)
    {
        if (IsTemporarilyUnavailable == value) return;
        IsTemporarilyUnavailable = value;
        KillFade();
        SetTipAlpha(normalAlpha);
        RefreshUnavailableVisual();
    }

    void RefreshUnavailableVisual()
    {
        bool hovered = IsUnavailableHovered && isActiveAndEnabled;
        if (hovered && unavailableMark == null && processIcon != null)
        {
            var root = new GameObject("UnavailableX", typeof(RectTransform));
            root.layer = gameObject.layer;
            unavailableMark = root.GetComponent<RectTransform>();
            unavailableMark.SetParent(processIcon.transform, false);
            unavailableMark.anchorMin = unavailableMark.anchorMax = unavailableMark.pivot = new Vector2(.5f, .5f);
            float size = Mathf.Min(7f, Mathf.Max(3f, Mathf.Min(processIcon.rectTransform.rect.width, processIcon.rectTransform.rect.height) - 2f));
            unavailableMark.sizeDelta = new Vector2(size, size);
            // Thirteen solid square pixels, no font, antialiasing, outline or raycast.
            for (int y = 0; y < 7; y++)
                for (int x = 0; x < 7; x++)
                    if (x == y || x == 6 - y)
                    {
                        var pixel = new GameObject("Pixel", typeof(RectTransform), typeof(UnityEngine.UI.Image));
                        pixel.layer = gameObject.layer;
                        var rect = pixel.GetComponent<RectTransform>();
                        rect.SetParent(unavailableMark, false);
                        rect.anchorMin = new Vector2(x / 7f, y / 7f);
                        rect.anchorMax = new Vector2((x + 1) / 7f, (y + 1) / 7f);
                        rect.offsetMin = rect.offsetMax = Vector2.zero;
                        var ink = pixel.GetComponent<UnityEngine.UI.Image>();
                        ink.color = new Color32(0xDC, 0xD8, 0xD4, 255);
                        ink.raycastTarget = false;
                    }
        }
        if (unavailableMark != null) unavailableMark.gameObject.SetActive(hovered);
        bool suppressed = IsLocked || IsTutorialSuppressed;
        if (processIcon != null) processIcon.color = hovered || suppressed ? LockedContentColor : NormalIconColor;
        if (processName != null) processName.color = hovered ? normalNameColor * new Color(.48f, .48f, .48f, 1f)
            : suppressed ? LockedContentColor : normalNameColor;
    }

    public void SetTutorialSuppressed(bool isSuppressed)
    {
        IsTutorialSuppressed = isSuppressed;
        ApplyVisualState(false);
    }

    void ApplyVisualState(bool animateWhenClearingNewTip)
    {
        ResolveReferences();
        CurrentState = IsLocked
            ? DesktopProcessVisualState.Locked
            : IsTutorialSuppressed
                ? DesktopProcessVisualState.TutorialSuppressed
                : HasNewContent
                    ? DesktopProcessVisualState.New
                    : DesktopProcessVisualState.Normal;

        if (CurrentState != DesktopProcessVisualState.New)
            StopNewContentBounce();

        bool useSuppressedColor = IsLocked || IsTutorialSuppressed;
        Color contentColor = useSuppressedColor
            ? LockedContentColor
            : NormalIconColor;
        if (processIcon != null)
            processIcon.color = contentColor;
        if (processName != null)
        {
            processName.color = useSuppressedColor
                ? LockedContentColor
                : normalNameColor;
        }

        SetLockTipAlpha(IsLocked ? 1f : 0f);
        if (CurrentState == DesktopProcessVisualState.New)
            ShowNewTip();
        else
            HideNewTip(
                animateWhenClearingNewTip
                && !IsLocked
                && !IsTutorialSuppressed);

        if (_isDragGhosted)
            ApplyDragGhostAlpha();

        if (!IsInteractable)
        {
            KillFade();
            SetTipAlpha(normalAlpha);
        }
        RefreshUnavailableVisual();
    }

    void ShowNewTip()
    {
        if (newTipText == null && newTipImage == null)
            return;

        KillNewTipFade();
        SetNewTipActive(true);
        SetNewTipAlpha(1f);
    }

    void PlayNewContentBounce()
    {
        StopNewContentBounce();
        // Restoring an existing NEW badge must never look like a fresh update.
        if (!Application.isPlaying
            || !IsStateInitialized
            || !isActiveAndEnabled
            || OSSessionPersistence.IsRestoring
            || CurrentState != DesktopProcessVisualState.New
            || _isDragging
            || _isDragGhosted
            || processIcon == null
            || !processIcon.gameObject.activeInHierarchy
            || newContentBounceHeight <= 0f)
        {
            return;
        }

        RectTransform icon = processIcon.rectTransform;
        if (icon == transform)
            return;

        // Animate only the image; desktop slots, hitboxes and labels stay put.
        _newContentBounceRect = icon;
        _newContentBounceRestPosition = icon.anchoredPosition;
        float restY = _newContentBounceRestPosition.y;
        Sequence bounce = DOTween.Sequence()
            .SetUpdate(true)
            .SetLink(gameObject, LinkBehaviour.KillOnDisable);
        _newContentBounce = bounce;
        bounce
            .Append(icon.DOAnchorPosY(restY + newContentBounceHeight, 0.16f)
                .SetEase(Ease.OutQuad))
            .Append(icon.DOAnchorPosY(restY, 0.22f)
                .SetEase(Ease.InQuad))
            .Append(icon.DOAnchorPosY(restY + newContentBounceHeight * 0.45f, 0.1f)
                .SetEase(Ease.OutQuad))
            .Append(icon.DOAnchorPosY(restY, 0.16f)
                .SetEase(Ease.InQuad))
            .OnComplete(() => FinishNewContentBounce(bounce))
            .OnKill(() => FinishNewContentBounce(bounce));
    }

    void StopNewContentBounce()
    {
        if (_newContentBounce != null)
        {
            Sequence bounce = _newContentBounce;
            _newContentBounce = null;
            bounce.Kill(false);
        }

        RestoreNewContentBouncePosition();
    }

    void FinishNewContentBounce(Sequence bounce)
    {
        if (_newContentBounce != bounce)
            return;

        _newContentBounce = null;
        RestoreNewContentBouncePosition();
    }

    void RestoreNewContentBouncePosition()
    {
        if (_newContentBounceRect != null)
            _newContentBounceRect.anchoredPosition = _newContentBounceRestPosition;
        _newContentBounceRect = null;
    }

    void HideNewTip(bool animate)
    {
        if (newTipText == null && newTipImage == null)
            return;

        KillNewTipFade();

        float duration = Mathf.Max(0f, newTipFadeDuration);
        if (!animate
            || duration <= 0f
            || !IsNewTipActiveInHierarchy())
        {
            SetNewTipAlpha(0f);
            SetNewTipActive(false);
            return;
        }

        Sequence fade = DOTween.Sequence()
            .SetUpdate(true)
            .SetLink(gameObject, LinkBehaviour.KillOnDisable);
        if (newTipText != null)
        {
            fade.Join(newTipText
                .DOFade(0f, duration)
                .SetEase(Ease.OutSine));
        }
        if (newTipImage != null)
        {
            fade.Join(newTipImage
                .DOFade(0f, duration)
                .SetEase(Ease.OutSine));
        }

        _newTipFadeTween = fade;
        fade.OnComplete(() =>
        {
            if (CurrentState != DesktopProcessVisualState.New)
            {
                SetNewTipActive(false);
            }
        });
        fade.OnKill(() =>
        {
            if (_newTipFadeTween == fade)
                _newTipFadeTween = null;
        });
    }

    void FadeTipTo(float targetAlpha)
    {
        ResolveReferences();
        if (hoverTip == null)
            return;

        hoverTip.DOKill(false);
        float duration = Mathf.Max(0f, fadeDuration);
        if (duration <= 0f)
        {
            SetTipAlpha(targetAlpha);
            return;
        }

        hoverTip
            .DOFade(targetAlpha, duration)
            .SetEase(Ease.OutSine)
            .SetUpdate(true)
            .SetLink(gameObject, LinkBehaviour.KillOnDisable);
    }

    void ResolveReferences()
    {
        if (hoverTip == null)
            hoverTip = GetComponent<Image>();

        if (processIcon == null)
            processIcon = transform.Find("ProcessIcon")?.GetComponent<Image>();

        if (processName == null)
            processName = transform.Find("ProcessName")?.GetComponent<TMP_Text>();

        if (imageLockTip == null)
        {
            Transform lockTip = transform.Find("LockTip")
                ?? transform.Find("ImageLockTip");
            imageLockTip = lockTip?.GetComponent<Image>();
        }

        if (newTipImage == null)
            newTipImage = transform.Find("NewTip02")?.GetComponent<Image>();

        if (processName == null || newTipText == null)
        {
            TMP_Text[] texts = GetComponentsInChildren<TMP_Text>(true);
            for (int i = 0; i < texts.Length; i++)
            {
                TMP_Text text = texts[i];
                if (text == null)
                    continue;

                if (processName == null && text.name == "ProcessName")
                    processName = text;
                else if (newTipText == null && text.name == "NewTip")
                    newTipText = text;

                if (processName != null && newTipText != null)
                    break;
            }
        }
    }

    void KillFade()
    {
        hoverTip?.DOKill(false);
    }

    void KillNewTipFade()
    {
        if (_newTipFadeTween != null)
        {
            Tween fade = _newTipFadeTween;
            _newTipFadeTween = null;
            fade.Kill(false);
        }

        newTipText?.DOKill(false);
        newTipImage?.DOKill(false);
    }

    void SetTipAlpha(float alpha)
    {
        if (hoverTip == null)
            return;

        Color color = hoverTip.color;
        color.a = Mathf.Clamp01(alpha);
        hoverTip.color = color;
    }

    void SetNewTipAlpha(float alpha)
    {
        float clampedAlpha = Mathf.Clamp01(alpha);
        if (newTipText != null)
        {
            Color textColor = newTipText.color;
            textColor.a = clampedAlpha;
            newTipText.color = textColor;
        }

        if (newTipImage != null)
        {
            Color imageColor = newTipImage.color;
            imageColor.a = clampedAlpha;
            newTipImage.color = imageColor;
        }
    }

    bool IsNewTipActiveInHierarchy()
    {
        return (newTipText != null && newTipText.gameObject.activeInHierarchy)
            || (newTipImage != null && newTipImage.gameObject.activeInHierarchy);
    }

    void SetNewTipActive(bool isActive)
    {
        if (newTipText != null)
            newTipText.gameObject.SetActive(isActive);
        if (newTipImage != null)
            newTipImage.gameObject.SetActive(isActive);
    }

    void SetLockTipAlpha(float alpha)
    {
        if (imageLockTip == null)
            return;

        Color color = imageLockTip.color;
        color.a = Mathf.Clamp01(alpha);
        imageLockTip.color = color;
    }

    void ApplyDragGhostAlpha()
    {
        // Badges belong to the same visual as the icon, including scripted drags.
        if (HasNewContent) SetNewTipAlpha(_dragGhostAlpha);
        if (processIcon != null)
        {
            Color iconColor = processIcon.color;
            iconColor.a = _dragGhostAlpha;
            processIcon.color = iconColor;
        }

        if (processName != null)
        {
            Color nameColor = processName.color;
            nameColor.a = _dragGhostAlpha;
            processName.color = nameColor;
        }
    }

    void Reset()
    {
        ResolveReferences();
        SetTipAlpha(normalAlpha);
        ApplyVisualState(false);
    }
}
