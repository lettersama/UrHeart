using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

[DisallowMultipleComponent]
[RequireComponent(typeof(RectTransform))]
[RequireComponent(typeof(Canvas))]
[RequireComponent(typeof(CanvasGroup))]
public sealed class OSDesktopProcessDragProxy : MonoBehaviour
{
    const int DragSortingOrder = 32760;

    [SerializeField] Image processIcon;
    [SerializeField] TextMeshProUGUI processName;

    RectTransform _rectTransform;
    Canvas _canvas;
    CanvasGroup _canvasGroup;
    Vector2 _pointerOffset;

    public bool Show(DesktopProcessUnit source, PointerEventData eventData)
    {
        if (source == null || eventData == null)
            return false;

        EnsureReferences();
        if (processIcon == null || processName == null)
            return false;

        RectTransform sourceRect = source.transform as RectTransform;
        RectTransform parentRect = transform.parent as RectTransform;
        if (sourceRect == null || parentRect == null)
            return false;

        CopyImage(source.ProcessIcon, processIcon);
        CopyText(source.ProcessName, processName);

        _rectTransform.anchorMin = new Vector2(0.5f, 0.5f);
        _rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
        _rectTransform.pivot = sourceRect.pivot;
        _rectTransform.sizeDelta = sourceRect.rect.size;
        _rectTransform.localRotation = Quaternion.identity;
        _rectTransform.localScale = Vector3.one;
        _rectTransform.position = sourceRect.position;

        Canvas sourceCanvas = source.ProcessIcon != null
            ? source.ProcessIcon.canvas
            : null;
        if (sourceCanvas != null)
            _canvas.sortingLayerID = sourceCanvas.sortingLayerID;
        _canvas.overrideSorting = true;
        _canvas.sortingOrder = DragSortingOrder;
        _canvasGroup.alpha = 1f;
        _canvasGroup.interactable = false;
        _canvasGroup.blocksRaycasts = false;
        transform.SetAsLastSibling();

        Camera eventCamera = ResolveEventCamera(eventData);
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                parentRect,
                eventData.position,
                eventCamera,
                out Vector2 pointerPosition))
        {
            return false;
        }

        _pointerOffset = _rectTransform.anchoredPosition - pointerPosition;
        gameObject.SetActive(true);
        return true;
    }

    public void Move(PointerEventData eventData)
    {
        if (!gameObject.activeSelf || eventData == null)
            return;

        RectTransform parentRect = transform.parent as RectTransform;
        if (parentRect == null
            || !RectTransformUtility.ScreenPointToLocalPointInRectangle(
                parentRect,
                eventData.position,
                ResolveEventCamera(eventData),
                out Vector2 pointerPosition))
        {
            return;
        }

        _rectTransform.anchoredPosition = pointerPosition + _pointerOffset;
    }

    public void Hide()
    {
        if (gameObject.activeSelf)
            gameObject.SetActive(false);
    }

    void EnsureReferences()
    {
        if (_rectTransform == null)
            _rectTransform = transform as RectTransform;
        if (_canvas == null)
            _canvas = GetComponent<Canvas>();
        if (_canvasGroup == null)
            _canvasGroup = GetComponent<CanvasGroup>();

        if (processIcon == null)
        {
            Transform iconTransform = transform.Find("ProcessIcon");
            processIcon = iconTransform?.GetComponent<Image>();
        }

        if (processIcon == null)
        {
            var iconObject = new GameObject(
                "ProcessIcon",
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Image));
            iconObject.layer = gameObject.layer;
            iconObject.transform.SetParent(transform, false);
            processIcon = iconObject.GetComponent<Image>();
        }

        if (processName == null)
        {
            Transform nameTransform = transform.Find("ProcessName");
            processName = nameTransform?.GetComponent<TextMeshProUGUI>();
        }

        if (processName == null)
        {
            var nameObject = new GameObject(
                "ProcessName",
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(TextMeshProUGUI));
            nameObject.layer = gameObject.layer;
            nameObject.transform.SetParent(transform, false);
            processName = nameObject.GetComponent<TextMeshProUGUI>();
        }

        processIcon.raycastTarget = false;
        processName.raycastTarget = false;
    }

    static void CopyImage(Image source, Image destination)
    {
        if (destination == null)
            return;

        bool hasSource = source != null;
        destination.gameObject.SetActive(
            hasSource && source.gameObject.activeSelf);
        if (!hasSource)
            return;

        CopyRectTransform(source.rectTransform, destination.rectTransform);
        destination.sprite = source.sprite;
        destination.overrideSprite = source.overrideSprite;
        destination.material = source.material;
        destination.color = source.color;
        destination.type = source.type;
        destination.preserveAspect = source.preserveAspect;
        destination.fillCenter = source.fillCenter;
        destination.fillMethod = source.fillMethod;
        destination.fillAmount = source.fillAmount;
        destination.fillClockwise = source.fillClockwise;
        destination.fillOrigin = source.fillOrigin;
        destination.useSpriteMesh = source.useSpriteMesh;
        destination.pixelsPerUnitMultiplier = source.pixelsPerUnitMultiplier;
        destination.maskable = source.maskable;
        destination.raycastTarget = false;
    }

    static void CopyText(TMP_Text source, TextMeshProUGUI destination)
    {
        if (destination == null)
            return;

        bool hasSource = source != null;
        destination.gameObject.SetActive(
            hasSource && source.gameObject.activeSelf);
        if (!hasSource)
            return;

        CopyRectTransform(source.rectTransform, destination.rectTransform);
        destination.text = source.text;
        destination.font = source.font;
        destination.fontSharedMaterial = source.fontSharedMaterial;
        destination.color = source.color;
        destination.fontSize = source.fontSize;
        destination.enableAutoSizing = source.enableAutoSizing;
        destination.fontSizeMin = source.fontSizeMin;
        destination.fontSizeMax = source.fontSizeMax;
        destination.fontStyle = source.fontStyle;
        destination.alignment = source.alignment;
        destination.richText = source.richText;
        destination.overflowMode = source.overflowMode;
        destination.characterSpacing = source.characterSpacing;
        destination.wordSpacing = source.wordSpacing;
        destination.lineSpacing = source.lineSpacing;
        destination.paragraphSpacing = source.paragraphSpacing;
        destination.margin = source.margin;
        destination.maskable = source.maskable;
        destination.raycastTarget = false;
    }

    static void CopyRectTransform(
        RectTransform source,
        RectTransform destination)
    {
        if (source == null || destination == null)
            return;

        destination.anchorMin = source.anchorMin;
        destination.anchorMax = source.anchorMax;
        destination.pivot = source.pivot;
        destination.anchoredPosition = source.anchoredPosition;
        destination.sizeDelta = source.sizeDelta;
        destination.localRotation = source.localRotation;
        destination.localScale = source.localScale;
    }

    static Camera ResolveEventCamera(PointerEventData eventData)
    {
        return eventData.enterEventCamera != null
            ? eventData.enterEventCamera
            : eventData.pressEventCamera;
    }
}
