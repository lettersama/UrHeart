using DG.Tweening;
using MrTGame.Core;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
public sealed class OSGiftWindowController : MonoBehaviour
{
    const string ShaderName = "UI/OS/GiftLikeBurst";
    const string LikeSfxKey = "Gift_Get";

    // The thumb pops in as a white silhouette above its rest spot, then drops
    // in whole pixels; the burst fires on the frame it hits (-1 px).
    static readonly int[] LikeDropPixels = { 4, 4, 2, -1, 0 };
    const int LikeFlashSteps = 2;
    const int LikeImpactStep = 3;

    static readonly int BurstTimeId = Shader.PropertyToID("_BurstTime");
    static readonly int RectSizeId = Shader.PropertyToID("_RectSize");

    [Header("References")]
    [SerializeField] OSWindowUnit window;
    [SerializeField] RectTransform sfxArea;
    [SerializeField] Image likeImage;
    [SerializeField] Sprite likeFlashSprite;
    [SerializeField] Image burstImage;
    [SerializeField] Material burstMaterialTemplate;
    [SerializeField] TMP_Text messageText;
    [SerializeField] OSLocalizedText localizedMessage;

    [Header("Like")]
    [SerializeField, Min(0f)] float likeDelay = 0.3f;
    [SerializeField, Min(0.01f)] float likeStepSeconds = 0.04f;

    [Header("Burst")]
    [Tooltip("Seconds of shader time the burst plays; the faint dot grid stays afterwards.")]
    [SerializeField, Min(0.1f)] float burstDuration = 1.1f;
    [SerializeField, Min(0)] int impactJoltPixels = 1;
    [SerializeField, Min(0.01f)] float impactJoltSeconds = 0.05f;

    [Header("Message")]
    [SerializeField, Min(1f)] float textCharactersPerSecond = 24f;
    [SerializeField, Min(0.01f)] float minimumTextDuration = 0.2f;

    Material runtimeMaterial;
    Material originalMaterial;
    Sequence effectSequence;
    Tween messageTween;
    string localizedMessageText = string.Empty;
    bool isMessageRevealed;
    bool hasPlayed;

    Sprite likeSprite;
    Vector2 likeRestPosition;
    Vector2 sfxAreaRestPosition;
    bool hasRestPose;

    void Awake()
    {
        ResolveReferences();
        CreateRuntimeMaterial();
        ResetVisual();
    }

    void OnEnable()
    {
        ResolveReferences();
        BindWindow();
        GameEvents.LanguageChanged -= HandleLanguageChanged;
        GameEvents.LanguageChanged += HandleLanguageChanged;
        CacheLocalizedMessage(true);
    }

    void OnDisable()
    {
        GameEvents.LanguageChanged -= HandleLanguageChanged;
        UnbindWindow();
        KillEffect();
    }

    void OnDestroy()
    {
        UnbindWindow();
        KillEffect();
        if (burstImage != null && burstImage.material == runtimeMaterial)
            burstImage.material = originalMaterial;
        if (runtimeMaterial != null)
            Destroy(runtimeMaterial);
    }

    public void PrepareRuntime()
    {
        ResolveReferences();
        CreateRuntimeMaterial();
        hasPlayed = false;
        isMessageRevealed = false;
        CacheLocalizedMessage(true);
        ResetVisual();
    }

    public void PlayLikeEffect()
    {
        ResolveReferences();
        CreateRuntimeMaterial();
        if (hasPlayed
            || likeImage == null
            || burstImage == null
            || runtimeMaterial == null)
        {
            return;
        }

        hasPlayed = true;
        KillEffect();
        isMessageRevealed = false;
        CacheLocalizedMessage(true);
        UpdateRectSize();
        SetBurstTime(0f);
        burstImage.enabled = false;
        burstImage.raycastTarget = false;
        HideLike();

        Sequence sequence = DOTween.Sequence();
        effectSequence = sequence;
        float step = Mathf.Max(0.01f, likeStepSeconds);
        float likeStartTime = Mathf.Max(0f, likeDelay);
        float burstStartTime = likeStartTime + step * LikeImpactStep;
        float duration = Mathf.Max(0.1f, burstDuration);

        sequence.InsertCallback(likeStartTime, () =>
        {
            AudioManager.Instance?.PlaySfx(LikeSfxKey);
            ShowLike(true, LikeDropPixels[0]);
        });
        for (int i = 1; i < LikeDropPixels.Length; i++)
        {
            int index = i;
            sequence.InsertCallback(
                likeStartTime + step * index,
                () => ShowLike(index < LikeFlashSteps, LikeDropPixels[index]));
        }

        sequence.InsertCallback(burstStartTime, () =>
        {
            if (burstImage != null)
                burstImage.enabled = true;
            SetBurstTime(0f);
            SetJolt(true);
        });
        sequence.InsertCallback(
            burstStartTime + Mathf.Max(0.01f, impactJoltSeconds),
            () => SetJolt(false));
        sequence.Insert(burstStartTime, DOVirtual.Float(
                0f,
                duration,
                duration,
                SetBurstTime)
            .SetEase(Ease.Linear));
        sequence
            .SetUpdate(true)
            .SetLink(gameObject, LinkBehaviour.KillOnDisable)
            .OnComplete(() =>
            {
                if (effectSequence != sequence)
                    return;

                effectSequence = null;
                // The burst image stays on at its end time: only the faint
                // dot grid is left, as the window's screen texture.
                SetBurstTime(duration);
                RevealMessage();
            })
            .OnKill(() =>
            {
                if (effectSequence == sequence)
                    effectSequence = null;
            });
    }

    public void HandleWindowClosedOnce()
    {
        KillEffect();
        ClearMessage();
    }

    void HandleOpenAnimationCompleted(OSWindowUnit source)
    {
        if (source == window)
            PlayLikeEffect();
    }

    void HandleLanguageChanged(LanguageState language)
    {
        CacheLocalizedMessage(!isMessageRevealed);
        if (!isMessageRevealed)
            return;

        if (messageTween != null)
        {
            Tween tween = messageTween;
            messageTween = null;
            tween.Kill(false);
        }
        messageText?.DOKill(false);
        if (messageText != null)
            messageText.text = localizedMessageText;
    }

    void BindWindow()
    {
        if (window == null)
            return;

        window.OpenAnimationCompleted -= HandleOpenAnimationCompleted;
        window.OpenAnimationCompleted += HandleOpenAnimationCompleted;
    }

    void UnbindWindow()
    {
        if (window != null)
            window.OpenAnimationCompleted -= HandleOpenAnimationCompleted;
    }

    void ResolveReferences()
    {
        if (window == null)
            window = GetComponent<OSWindowUnit>();
        if (sfxArea == null)
            sfxArea = transform.Find("SFXArea") as RectTransform;
        if (likeImage == null && sfxArea != null)
            likeImage = sfxArea.Find("Like")?.GetComponent<Image>();
        if (burstImage == null && sfxArea != null)
        {
            burstImage = sfxArea
                .Find("LikeBurst")
                ?.GetComponent<Image>();
        }
        if (messageText == null && sfxArea != null)
            messageText = sfxArea.Find("Text (TMP)")?.GetComponent<TMP_Text>();
        if (localizedMessage == null && messageText != null)
            localizedMessage = messageText.GetComponent<OSLocalizedText>();

        if (!hasRestPose && likeImage != null && sfxArea != null)
        {
            likeSprite = likeImage.sprite;
            likeRestPosition = likeImage.rectTransform.anchoredPosition;
            sfxAreaRestPosition = sfxArea.anchoredPosition;
            hasRestPose = true;
        }
    }

    void CreateRuntimeMaterial()
    {
        if (runtimeMaterial != null || burstImage == null)
            return;

        originalMaterial = burstImage.material;
        Material source = burstMaterialTemplate;
        if (source == null
            && burstImage.material != null
            && burstImage.material.shader != null
            && burstImage.material.shader.name == ShaderName)
        {
            source = burstImage.material;
        }

        if (source != null)
            runtimeMaterial = new Material(source);
        else
        {
            Shader shader = Shader.Find(ShaderName);
            if (shader != null)
                runtimeMaterial = new Material(shader);
        }

        if (runtimeMaterial == null)
        {
            Debug.LogError("Gift like burst shader is missing.", this);
            return;
        }

        runtimeMaterial.name = "M_OSGiftLikeBurst (Runtime)";
        runtimeMaterial.hideFlags = HideFlags.HideAndDontSave;
        burstImage.material = runtimeMaterial;
        UpdateRectSize();
    }

    void UpdateRectSize()
    {
        if (runtimeMaterial == null || burstImage == null)
            return;

        Rect rect = burstImage.rectTransform.rect;
        runtimeMaterial.SetVector(
            RectSizeId,
            new Vector4(
                Mathf.Max(1f, Mathf.Round(rect.width)),
                Mathf.Max(1f, Mathf.Round(rect.height)),
                0f,
                0f));
    }

    void ShowLike(bool flash, int offsetPixels)
    {
        if (likeImage == null)
            return;

        likeImage.sprite = flash && likeFlashSprite != null
            ? likeFlashSprite
            : likeSprite;
        Color color = likeImage.color;
        color.a = 1f;
        likeImage.color = color;
        likeImage.rectTransform.localScale = Vector3.one;
        likeImage.rectTransform.anchoredPosition =
            likeRestPosition + Vector2.up * offsetPixels;
    }

    void HideLike()
    {
        if (likeImage == null)
            return;

        likeImage.sprite = likeSprite;
        Color color = likeImage.color;
        color.a = 0f;
        likeImage.color = color;
        likeImage.rectTransform.localScale = Vector3.one;
        if (hasRestPose)
            likeImage.rectTransform.anchoredPosition = likeRestPosition;
    }

    void SetJolt(bool active)
    {
        if (sfxArea == null || !hasRestPose)
            return;

        sfxArea.anchoredPosition = sfxAreaRestPosition
            + (active ? Vector2.down * impactJoltPixels : Vector2.zero);
    }

    void ResetVisual()
    {
        KillEffect();
        isMessageRevealed = false;
        ClearMessage();
        SetBurstTime(0f);
        if (burstImage != null)
        {
            burstImage.raycastTarget = false;
            burstImage.enabled = false;
        }
        HideLike();
    }

    void SetBurstTime(float seconds)
    {
        if (runtimeMaterial == null)
            return;

        runtimeMaterial.SetFloat(BurstTimeId, seconds);
    }

    void KillEffect()
    {
        if (messageTween != null)
        {
            Tween tween = messageTween;
            messageTween = null;
            tween.Kill(false);
        }
        messageText?.DOKill(false);
        if (effectSequence != null)
        {
            Sequence sequence = effectSequence;
            effectSequence = null;
            sequence.Kill(false);
        }

        SetJolt(false);
        SetBurstTime(0f);
        if (burstImage != null)
            burstImage.enabled = false;
    }

    void CacheLocalizedMessage(bool clearAfterRefresh)
    {
        if (messageText == null)
            return;

        if (localizedMessage != null)
        {
            localizedMessage.Initialize();
            localizedMessage.Refresh(OS_Handle.Instance);
            localizedMessageText = messageText.text;
        }
        else if (!string.IsNullOrEmpty(messageText.text))
        {
            localizedMessageText = messageText.text;
        }

        if (clearAfterRefresh)
            ClearMessage();
    }

    void RevealMessage()
    {
        if (messageText == null || string.IsNullOrEmpty(localizedMessageText))
            return;

        messageText.DOKill(false);
        messageText.text = string.Empty;
        isMessageRevealed = true;

        float duration = Mathf.Max(
            Mathf.Max(0.01f, minimumTextDuration),
            localizedMessageText.Length
                / Mathf.Max(1f, textCharactersPerSecond));
        Tween tween = messageText
            .DOText(localizedMessageText, duration, true)
            .SetEase(Ease.Linear)
            .SetUpdate(true)
            .SetLink(gameObject, LinkBehaviour.KillOnDisable);
        messageTween = tween;
        tween.OnComplete(() =>
        {
            if (messageTween == tween)
                messageTween = null;
        });
        tween.OnKill(() =>
        {
            if (messageTween == tween)
                messageTween = null;
        });
    }

    void ClearMessage()
    {
        if (messageText == null)
            return;

        messageText.DOKill(false);
        messageText.text = string.Empty;
    }
}
