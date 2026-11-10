using MrTGame.Narrative;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

[DisallowMultipleComponent]
public sealed class OSMemorySelectUnit : MonoBehaviour,
    IPointerEnterHandler,
    IPointerExitHandler,
    IPointerClickHandler
{
    const string ConfirmButtonSfxKey = "UI_ComfrimButton";

    [SerializeField] TMP_Text label;
    [SerializeField] Color normalColor =
        new Color32(0x55, 0x72, 0x85, 0xFF);
    [SerializeField] Color activeColor =
        new Color32(0xCA, 0x8A, 0x49, 0xFF);
    [SerializeField] Image imageBk;
    [SerializeField] Color disabledColor = new Color32(0xC5, 0x70, 0x70, 0xFF);
    [SerializeField] Color disabledBackgroundColor = new Color32(0x60, 0x22, 0x22, 0xA0);

    Color normalBackgroundColor;
    bool hasBackgroundColor;
    Vector4 normalLabelMargin;
    bool hasLabelMargin;

    OSMemoryWindowController owner;
    OSMemoryReadOptionData option;

    public OSMemoryReadOptionData Option => option;
    public TMP_Text Label => label;
    public bool IsBound => owner != null && option != null;
    public bool IsInteractable => IsBound && owner.IsOptionAvailable(option);

    void Awake()
    {
        ResolveReferences();
        SetActiveVisual(false);
    }

    void OnEnable()
    {
        ResolveReferences();
        SetActiveVisual(false);
    }

    void OnDisable()
    {
        SetActiveVisual(false);
    }

    public void Bind(
        OSMemoryWindowController targetOwner,
        OSMemoryReadOptionData targetOption,
        int displayIndex)
    {
        owner = targetOwner;
        option = targetOption;

        if (!gameObject.activeSelf)
            gameObject.SetActive(true);

        ResolveReferences();
        if (label != null && targetOption != null)
        {
            label.text = OSResourceInlineIcons.Format(label,
                OSOptionLabel.Format(targetOption.Text, displayIndex), targetOption.ResourceChange);
            Vector4 margin = normalLabelMargin;
            if (targetOption.ResourceChange != null && targetOption.ResourceChange.Battery < 0f)
                margin.z += 32f;
            label.margin = margin;
            OSOptionRowLayout.Bind(this, label);
        }

        SetActiveVisual(false);
    }

    public void Clear()
    {
        owner = null;
        option = null;
        SetActiveVisual(false);

        if (gameObject.activeSelf)
            gameObject.SetActive(false);
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        if (IsInteractable)
            SetActiveVisual(true);
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        SetActiveVisual(false);
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        if (eventData.button != PointerEventData.InputButton.Left
            || !IsInteractable)
        {
            return;
        }

        AudioManager.Instance?.PlaySfx(ConfirmButtonSfxKey);
        owner.SelectOption(option);
    }

    void ResolveReferences()
    {
        if (label == null)
            label = GetComponentInChildren<TMP_Text>(true);
        if (label != null && !hasLabelMargin)
        {
            normalLabelMargin = label.margin;
            hasLabelMargin = true;
        }

        if (imageBk == null)
            imageBk = transform.Find("ImageBk")?.GetComponent<Image>();
        if (imageBk == null && Application.isPlaying)
        {
            var background = new GameObject("ImageBk", typeof(RectTransform), typeof(Image));
            background.layer = gameObject.layer;
            background.transform.SetParent(transform, false);
            background.transform.SetAsFirstSibling();
            imageBk = background.GetComponent<Image>();
            imageBk.rectTransform.anchorMin = Vector2.zero;
            imageBk.rectTransform.anchorMax = Vector2.one;
            imageBk.rectTransform.offsetMin = Vector2.zero;
            imageBk.rectTransform.offsetMax = Vector2.zero;
            imageBk.color = Color.clear;
            imageBk.raycastTarget = true;
        }
        if (imageBk != null && !hasBackgroundColor)
        {
            normalBackgroundColor = imageBk.color;
            hasBackgroundColor = true;
        }

        OSLocalizedFontUtility.ApplyCurrent(label);
    }

    void SetActiveVisual(bool isActive)
    {
        bool isDisabled = IsBound && !IsInteractable;
        if (label != null)
            label.color = isDisabled ? disabledColor : isActive ? activeColor : normalColor;
        if (imageBk != null)
            imageBk.color = isDisabled ? disabledBackgroundColor : normalBackgroundColor;
    }

    void Reset()
    {
        ResolveReferences();
        SetActiveVisual(false);
    }
}
