using MrTGame.Core;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

[DisallowMultipleComponent]
[RequireComponent(typeof(Image))]
public sealed class MainMenuOptionVolumeControl : MonoBehaviour,
    IPointerDownHandler,
    IBeginDragHandler,
    IDragHandler,
    IEndDragHandler
{
    const float MinHandX = -51f;
    const float MaxHandX = 51f;
    const float VerticalRaycastExpansion = 10f;

    [SerializeField] GameOptionVolumeType volumeType;
    [SerializeField] Image trackImage;
    [SerializeField] RectTransform hand;
    [SerializeField] TMP_Text valueLabel;

    public GameOptionVolumeType VolumeType => volumeType;

    MainMenuOptionPanel owner;
    int currentValue = -1;

    void Awake()
    {
        ResolveReferences();
        ConfigureRaycastArea();
    }

    void OnEnable()
    {
        ResolveReferences();
        ConfigureRaycastArea();
    }

    public void Configure(
        MainMenuOptionPanel panel,
        GameOptionVolumeType type,
        RectTransform handTransform,
        TMP_Text numberLabel)
    {
        owner = panel;
        volumeType = type;
        hand = handTransform;
        valueLabel = numberLabel;
        ResolveReferences();
        ConfigureRaycastArea();
    }

    public void SetValue(int value)
    {
        ResolveReferences();
        int clampedValue = Mathf.Clamp(
            value,
            GameOptionSaveData.MinVolume,
            GameOptionSaveData.MaxVolume);
        currentValue = clampedValue;
        float normalizedValue = clampedValue / 100f;

        if (hand != null)
        {
            Vector2 position = hand.anchoredPosition;
            position.x = Mathf.Lerp(MinHandX, MaxHandX, normalizedValue);
            hand.anchoredPosition = position;
        }

        if (valueLabel != null)
            valueLabel.text = clampedValue.ToString();
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        if (IsLeftButton(eventData))
            UpdateFromPointer(eventData);
    }

    public void OnBeginDrag(PointerEventData eventData)
    {
        if (IsLeftButton(eventData))
            UpdateFromPointer(eventData);
    }

    public void OnDrag(PointerEventData eventData)
    {
        if (IsLeftButton(eventData))
            UpdateFromPointer(eventData);
    }

    public void OnEndDrag(PointerEventData eventData)
    {
    }

    void UpdateFromPointer(PointerEventData eventData)
    {
        RectTransform track = transform as RectTransform;
        if (track == null || eventData == null)
            return;

        Camera eventCamera = eventData.pressEventCamera != null
            ? eventData.pressEventCamera
            : eventData.enterEventCamera;
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                track,
                eventData.position,
                eventCamera,
                out Vector2 localPoint))
        {
            return;
        }

        float normalizedValue = Mathf.InverseLerp(
            MinHandX,
            MaxHandX,
            Mathf.Clamp(localPoint.x, MinHandX, MaxHandX));
        int value = Mathf.RoundToInt(normalizedValue * 100f);
        if (value == currentValue)
            return;

        if (owner == null)
            owner = GetComponentInParent<MainMenuOptionPanel>(true);

        owner?.SetVolume(volumeType, value);
    }

    void ResolveReferences()
    {
        if (trackImage == null)
            trackImage = GetComponent<Image>();

        if (hand == null)
            hand = transform.Find("Hand") as RectTransform;

        if (valueLabel == null && transform.parent != null)
        {
            TMP_Text[] labels = transform.parent
                .GetComponentsInChildren<TMP_Text>(true);
            for (int i = 0; i < labels.Length; i++)
            {
                TMP_Text label = labels[i];
                if (label != null
                    && label.transform != transform.parent
                    && label.transform.parent == transform.parent
                    && label.name.StartsWith("Num"))
                {
                    valueLabel = label;
                    break;
                }
            }
        }
    }

    void ConfigureRaycastArea()
    {
        if (trackImage == null)
            return;

        trackImage.raycastTarget = true;
        trackImage.raycastPadding = new Vector4(
            0f,
            -VerticalRaycastExpansion,
            0f,
            -VerticalRaycastExpansion);
    }

    static bool IsLeftButton(PointerEventData eventData)
    {
        return eventData != null
            && eventData.button == PointerEventData.InputButton.Left;
    }
}
