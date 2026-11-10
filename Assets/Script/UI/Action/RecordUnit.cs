using DG.Tweening;
using MrTGame.OS;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

[DisallowMultipleComponent]
[RequireComponent(typeof(CanvasGroup))]
[RequireComponent(typeof(Image))]
public sealed class RecordUnit : MonoBehaviour, IPointerClickHandler
{
    public const int InvalidRecordIndex = -1;
    const string ConfirmButtonSfxKey = "UI_ComfrimButton";

    [Header("References")]
    [SerializeField] CanvasGroup canvasGroup;
    [SerializeField] Image activeImage;
    [SerializeField] Image recordIcon;
    [SerializeField] RectTransform selectionFrame;

    [Header("Visuals")]
    [SerializeField, Range(0f, 1f)] float normalAlpha;
    [SerializeField, Range(0f, 1f)] float activeAlpha = 1f;
    [SerializeField, Min(0f)] float fadeDuration = 0.15f;

    OSRecordWindowController owner;
    RecordData recordData;
    int recordIndex = InvalidRecordIndex;
    Tween fadeTween;
    bool isSelected;

    public int RecordIndex => recordIndex;
    public RecordData Data => recordData;
    public Image RecordIcon => recordIcon;
    public bool IsBound => owner != null && recordData != null;
    public bool IsSelected => isSelected;

    void Awake()
    {
        ResolveReferences();
    }

    void OnEnable()
    {
        ResolveReferences();
        if (IsBound)
            SetCanvasState(1f, true);
    }

    void OnDisable()
    {
        KillFade();
        SetInteraction(false);
    }

    void OnDestroy()
    {
        KillFade();
    }

    public void Initialize(
        OSRecordWindowController targetOwner,
        int targetRecordIndex,
        RecordData targetData,
        bool playFade = true,
        bool showSolidColorWhenIconMissing = false)
    {
        KillFade();
        ResolveReferences();

        owner = targetOwner;
        recordIndex = targetRecordIndex;
        recordData = targetData;

        if (recordIcon != null)
        {
            recordIcon.sprite = targetData != null
                ? targetData.RecordIcon
                : null;
            recordIcon.preserveAspect = true;
            recordIcon.enabled = recordIcon.sprite != null
                || (showSolidColorWhenIconMissing && targetData != null);
        }

        SetSelected(false);
        if (!gameObject.activeSelf)
            gameObject.SetActive(true);

        SetCanvasState(playFade ? 0f : 1f, !playFade);

        if (playFade)
            PlayFadeIn();
    }

    public void Clear()
    {
        KillFade();
        owner = null;
        recordData = null;
        recordIndex = InvalidRecordIndex;

        if (recordIcon != null)
        {
            recordIcon.sprite = null;
            recordIcon.enabled = false;
        }

        SetSelected(false);
        SetCanvasState(0f, false);

        if (gameObject.activeSelf)
            gameObject.SetActive(false);
    }

    public void SetSelected(bool selected)
    {
        isSelected = selected;
        if (selectionFrame != null)
            selectionFrame.gameObject.SetActive(selected);
        if (activeImage == null)
            return;

        Color color = activeImage.color;
        color.a = selected ? activeAlpha : normalAlpha;
        activeImage.color = color;
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        if (eventData.button != PointerEventData.InputButton.Left
            || !IsBound || !owner.CanInteract)
        {
            return;
        }

        AudioManager.Instance?.PlaySfx(ConfirmButtonSfxKey);
        owner.SelectRecord(this);
    }

    void PlayFadeIn()
    {
        if (canvasGroup == null)
            return;

        float duration = Mathf.Max(0f, fadeDuration);
        if (duration <= 0f)
        {
            SetCanvasState(1f, true);
            return;
        }

        fadeTween = canvasGroup
            .DOFade(1f, duration)
            .SetEase(Ease.Linear)
            .SetUpdate(true)
            .SetLink(gameObject, LinkBehaviour.KillOnDisable)
            .OnComplete(() =>
            {
                fadeTween = null;
                SetInteraction(true);
            })
            .OnKill(() => fadeTween = null);
    }

    void ResolveReferences()
    {
        if (canvasGroup == null)
            canvasGroup = GetComponent<CanvasGroup>();

        if (activeImage == null)
            activeImage = GetComponent<Image>();

        if (recordIcon == null)
        {
            recordIcon = transform.Find("Image")?.GetComponent<Image>();
            if (recordIcon == null)
            {
                Image[] images = GetComponentsInChildren<Image>(true);
                for (int i = 0; i < images.Length; i++)
                {
                    if (images[i] != activeImage)
                    {
                        recordIcon = images[i];
                        break;
                    }
                }
            }
        }
    }

    void SetCanvasState(float alpha, bool interactionEnabled)
    {
        if (canvasGroup == null)
            return;

        canvasGroup.alpha = Mathf.Clamp01(alpha);
        SetInteraction(interactionEnabled);
    }

    void SetInteraction(bool enabled)
    {
        if (canvasGroup == null)
            return;

        canvasGroup.interactable = enabled;
        canvasGroup.blocksRaycasts = enabled;
    }

    void KillFade()
    {
        if (fadeTween == null)
            return;

        Tween tween = fadeTween;
        fadeTween = null;
        tween.Kill(false);
    }

    void Reset()
    {
        ResolveReferences();
        SetSelected(false);
    }
}
