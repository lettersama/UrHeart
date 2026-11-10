using System;
using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;

[DisallowMultipleComponent]
[RequireComponent(typeof(RectTransform))]
public sealed class OSLinkMessageSelectUnit : MonoBehaviour,
    IPointerEnterHandler,
    IPointerExitHandler,
    IPointerClickHandler
{
    [Header("References")]
    [SerializeField] TMP_Text selectText;
    [SerializeField] RectTransform activeBackground;

    [Header("Visual State")]
    [SerializeField] Color normalTextColor =
        new Color32(0x9A, 0x98, 0x91, 0xFF);
    [SerializeField] Color activeTextColor =
        new Color32(0xE0, 0xAB, 0x28, 0xFF);
    [SerializeField, Min(0f)] float normalBackgroundWidth;
    [SerializeField, Min(0f)] float activeBackgroundWidth = 142f;
    [SerializeField, Min(0f)] float backgroundHorizontalInset = 3f;
    [SerializeField, Min(0f)] float transitionDuration = 0.15f;
    [SerializeField] Ease transitionEase = Ease.OutQuad;

    Tween visualTween;
    bool isInteractable = true;
    bool isPointerInside;
    UnityEngine.Object scriptHoverOwner;
    bool isScriptHovered;

    public TMP_Text SelectText => selectText;
    public RectTransform ScriptTarget => selectText != null
        ? selectText.rectTransform
        : transform as RectTransform;
    public bool IsInteractable => isInteractable;

    public event Action<OSLinkMessageSelectUnit> Clicked;

    void Awake()
    {
        ResolveReferences();
        SanitizeSettings();
        ApplyVisualImmediate(false);
    }

    void OnEnable()
    {
        ResolveReferences();
        OSLocalizedFontUtility.ApplyCurrent(selectText);
        isPointerInside = false;
        scriptHoverOwner = null;
        isScriptHovered = false;
        ApplyVisualImmediate(false);
    }

    void OnDisable()
    {
        isPointerInside = false;
        scriptHoverOwner = null;
        isScriptHovered = false;
        KillVisualTween();
        ApplyVisualImmediate(false);
    }

    void OnDestroy()
    {
        KillVisualTween();
    }

    void OnRectTransformDimensionsChange()
    {
        if (!isActiveAndEnabled || activeBackground == null)
            return;

        float activeWidth = GetResponsiveActiveWidth();
        AlignBackgroundLeftEdge();
        if (ShouldShowActive())
        {
            KillVisualTween();
            SetBackgroundWidth(activeWidth);
            if (selectText != null)
                selectText.color = activeTextColor;
        }
    }

    public void SetText(string value, MrTGame.Narrative.OSMemoryResourceChange resourceChange = null)
    {
        ResolveReferences();
        OSLocalizedFontUtility.ApplyCurrent(selectText);
        if (selectText != null)
        {
            selectText.text = OSResourceInlineIcons.Format(selectText, value, resourceChange);
            OSOptionRowLayout.Bind(this, selectText);
        }
    }

    public void SetInteractable(bool interactable, bool immediate = false)
    {
        isInteractable = interactable;
        bool shouldBeActive = ShouldShowActive();
        if (immediate || !isActiveAndEnabled || !Application.isPlaying)
            ApplyVisualImmediate(shouldBeActive);
        else
            AnimateVisual(shouldBeActive);
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        if (CursorHandle.Instance?.IsScriptControlled == true)
            return;

        isPointerInside = true;
        if (isInteractable)
            AnimateVisual(true);
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        if (CursorHandle.Instance?.IsScriptControlled == true)
            return;

        isPointerInside = false;
        AnimateVisual(false);
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        if (!isInteractable
            || eventData == null
            || eventData.button != PointerEventData.InputButton.Left
            || CursorHandle.Instance?.IsScriptControlled == true)
        {
            return;
        }

        Clicked?.Invoke(this);
    }

    public bool SetScriptHover(UnityEngine.Object owner, bool hovered)
    {
        if (owner == null)
            return false;

        CursorHandle cursor = CursorHandle.Instance;
        if (cursor == null || !cursor.IsScriptControlledBy(owner))
            return false;

        isPointerInside = false;
        scriptHoverOwner = owner;
        isScriptHovered = hovered;
        if (!isActiveAndEnabled || !Application.isPlaying)
            ApplyVisualImmediate(ShouldShowActive());
        else
            AnimateVisual(ShouldShowActive());
        return true;
    }

    public bool TryInvokeScriptClick(UnityEngine.Object owner)
    {
        CursorHandle cursor = CursorHandle.Instance;
        if (!isInteractable
            || owner == null
            || cursor == null
            || !cursor.IsScriptControlledBy(owner))
        {
            return false;
        }

        Clicked?.Invoke(this);
        return true;
    }

    public void ClearScriptHover()
    {
        scriptHoverOwner = null;
        isScriptHovered = false;
        if (!isActiveAndEnabled || !Application.isPlaying)
            ApplyVisualImmediate(ShouldShowActive());
        else
            AnimateVisual(ShouldShowActive());
    }

    bool ShouldShowActive()
    {
        bool hovered;
        if (scriptHoverOwner != null)
            hovered = isScriptHovered;
        else if (CursorHandle.Instance?.IsScriptControlled == true)
            hovered = false;
        else
            hovered = isPointerInside;
        return isInteractable && hovered;
    }

    void AnimateVisual(bool isActive)
    {
        ResolveReferences();
        KillVisualTween();

        float activeWidth = GetResponsiveActiveWidth();
        AlignBackgroundLeftEdge();
        float targetWidth = isActive
            ? activeWidth
            : normalBackgroundWidth;
        Color targetColor = isActive
            ? activeTextColor
            : normalTextColor;

        if (!Application.isPlaying || transitionDuration <= 0f)
        {
            SetBackgroundWidth(targetWidth);
            if (selectText != null)
                selectText.color = targetColor;
            return;
        }

        Sequence sequence = DOTween.Sequence();
        if (activeBackground != null)
        {
            sequence.Join(DOTween.To(
                () => activeBackground.sizeDelta.x,
                SetBackgroundWidth,
                targetWidth,
                transitionDuration));
        }

        if (selectText != null)
            sequence.Join(selectText.DOColor(targetColor, transitionDuration));

        sequence
            .SetEase(transitionEase)
            .SetUpdate(true)
            .SetLink(gameObject, LinkBehaviour.KillOnDisable);
        visualTween = sequence;
        sequence.OnKill(() => ClearVisualTween(sequence));
        sequence.OnComplete(() => ClearVisualTween(sequence));
    }

    void ApplyVisualImmediate(bool isActive)
    {
        KillVisualTween();
        ResolveReferences();

        float activeWidth = GetResponsiveActiveWidth();
        AlignBackgroundLeftEdge();
        SetBackgroundWidth(
            isActive ? activeWidth : normalBackgroundWidth);
        if (selectText != null)
        {
            selectText.color = isActive
                ? activeTextColor
                : normalTextColor;
        }
    }

    float GetResponsiveActiveWidth()
    {
        RectTransform unitRect = transform as RectTransform;
        if (unitRect == null || unitRect.rect.width <= 0.01f)
            return activeBackgroundWidth;

        return Mathf.Max(0f, unitRect.rect.width - backgroundHorizontalInset);
    }

    void AlignBackgroundLeftEdge()
    {
        if (activeBackground == null)
            return;

        // The dialogue can grow wider than its original 145px option rows.
        // Anchor the reveal to the row edge instead of offsetting from its
        // center by the old 142px background width. Preserve vertical layout.
        Vector2 minimum = activeBackground.anchorMin;
        Vector2 maximum = activeBackground.anchorMax;
        Vector2 pivot = activeBackground.pivot;
        minimum.x = maximum.x = pivot.x = 0f;
        activeBackground.anchorMin = minimum;
        activeBackground.anchorMax = maximum;
        activeBackground.pivot = pivot;
        Vector2 position = activeBackground.anchoredPosition;
        position.x = backgroundHorizontalInset * 0.5f;
        activeBackground.anchoredPosition = position;
    }

    void SetBackgroundWidth(float width)
    {
        if (activeBackground == null)
            return;

        Vector2 size = activeBackground.sizeDelta;
        size.x = Mathf.Max(0f, width);
        activeBackground.sizeDelta = size;
    }

    void ResolveReferences()
    {
        if (selectText == null)
        {
            Transform textTransform = transform.Find("SelectTMP");
            selectText = textTransform != null
                ? textTransform.GetComponent<TMP_Text>()
                : GetComponentInChildren<TMP_Text>(true);
        }

        if (activeBackground != null)
            return;

        for (int i = 0; i < transform.childCount; i++)
        {
            Transform child = transform.GetChild(i);
            if (string.Equals(
                child.name,
                "ActiveBk",
                StringComparison.OrdinalIgnoreCase))
            {
                activeBackground = child as RectTransform;
                break;
            }
        }
    }

    void KillVisualTween()
    {
        if (visualTween == null)
            return;

        Tween tween = visualTween;
        visualTween = null;
        tween.Kill(false);
    }

    void ClearVisualTween(Tween tween)
    {
        if (visualTween == tween)
            visualTween = null;
    }

    void SanitizeSettings()
    {
        normalBackgroundWidth = Mathf.Max(0f, normalBackgroundWidth);
        activeBackgroundWidth = Mathf.Max(0f, activeBackgroundWidth);
        backgroundHorizontalInset = Mathf.Max(0f, backgroundHorizontalInset);
        transitionDuration = Mathf.Max(0f, transitionDuration);
    }

    void Reset()
    {
        ResolveReferences();
        SanitizeSettings();
    }

    void OnValidate()
    {
        ResolveReferences();
        SanitizeSettings();
    }
}
