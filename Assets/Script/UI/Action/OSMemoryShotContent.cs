using DG.Tweening;
using MrTGame.Core;
using MrTGame.Narrative;
using TMPro;
using UnityEngine;
using UnityEngine.Events;

// Lives on the authored content prefab. The window shell and dialogue know
// nothing about its Image hierarchy or custom effects.
[DisallowMultipleComponent]
public sealed class OSMemoryShotContent : MonoBehaviour
{
    [SerializeField] TMP_Text floatingCaption;
    [SerializeField] UnityEvent onWindowOpened = new UnityEvent();
    [SerializeField] UnityEvent onReleased = new UnityEvent();

    Sequence captionTween;
    Vector2 captionPosition;
    Color captionColor;
    bool captured;
    bool prepared;
    public TMP_Text FloatingCaption => floatingCaption;

    void OnEnable() => GameEvents.LanguageChanged += HandleLanguageChanged;
    void OnDisable()
    {
        GameEvents.LanguageChanged -= HandleLanguageChanged;
        Release();
    }

    public void Prepare(OSMemorySubwindowShot shot)
    {
        Release();
        if (!captured && floatingCaption != null)
        {
            captionPosition = floatingCaption.rectTransform.anchoredPosition;
            captionColor = floatingCaption.color;
            captured = true;
        }
        RefreshLocalization();
        if (floatingCaption != null)
        {
            floatingCaption.rectTransform.anchoredPosition = captionPosition;
            floatingCaption.color = captionColor;
            if (shot.FloatCaption)
                floatingCaption.alpha = 0f;
        }
        prepared = true;
    }

    public void PlayOpened(OSMemorySubwindowShot shot)
    {
        if (!prepared)
            return;
        if (shot.FloatCaption && floatingCaption != null)
        {
            captionTween = DOTween.Sequence().SetUpdate(true);
            captionTween.Append(floatingCaption.DOFade(captionColor.a, shot.CaptionFadeIn));
            captionTween.Insert(0f, floatingCaption.rectTransform
                .DOAnchorPosY(captionPosition.y + shot.CaptionRise, shot.CaptionDuration)
                .SetEase(Ease.Linear));
            captionTween.Insert(shot.CaptionDuration - shot.CaptionFadeOut,
                floatingCaption.DOFade(0f, shot.CaptionFadeOut));
        }
        onWindowOpened.Invoke();
    }

    public void Release()
    {
        captionTween?.Kill(false);
        captionTween = null;
        if (captured && floatingCaption != null)
        {
            floatingCaption.rectTransform.anchoredPosition = captionPosition;
            floatingCaption.color = captionColor;
        }
        if (!prepared)
            return;
        prepared = false;
        onReleased.Invoke();
    }

    void HandleLanguageChanged(LanguageState language) => RefreshLocalization();

    void RefreshLocalization()
    {
        GameDataManager data = GameDataManager.Instance;
        foreach (OSLocalizedText text in GetComponentsInChildren<OSLocalizedText>(true))
        {
            text.Initialize();
            text.Refresh(data != null ? data.LocalizationData : null,
                data != null ? data.CurrentLanguageState : LanguageState.zh_CN);
        }
    }
}
