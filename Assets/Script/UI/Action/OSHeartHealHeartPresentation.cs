using UnityEngine;
using UnityEngine.UI;

// Pure presentation: treatment judgement and resource changes remain owned by
// OSHeartHealToolWindowController. Feedback is emitted only from its resolved beats.
[DisallowMultipleComponent]
[RequireComponent(typeof(Animator), typeof(Image))]
public sealed class OSHeartHealHeartPresentation : MonoBehaviour
{
    const float FeedbackLifetime = 0.85f;
    const float FeedbackCooldown = 0.14f;
    static readonly int IdleState = Animator.StringToHash("Base Layer.Idle");

    [SerializeField] Animator heartAnimator;
    [SerializeField] Sprite healingSymbol;
    [SerializeField] Sprite missSymbol;
    [SerializeField] Image[] feedbackImages = new Image[0];

    Vector2[] origins = new Vector2[0];
    Vector2[] distances = new Vector2[0];
    float[] startTimes = new float[0];
    int nextSlot;
    int hitVariation;
    float lastHitTime = float.NegativeInfinity;
    float lastMissTime = float.NegativeInfinity;

    public bool IsConfigured => healingSymbol != null && missSymbol != null
        && feedbackImages != null && feedbackImages.Length >= 3;

    void Awake()
    {
        if (heartAnimator == null) heartAnimator = GetComponent<Animator>();
        PreparePool();
    }

    void OnEnable()
    {
        if (heartAnimator == null) heartAnimator = GetComponent<Animator>();
        PreparePool();
        ResetPresentation();
    }

    void OnDisable() => ClearFeedback();

    void PreparePool()
    {
        int count = feedbackImages != null ? feedbackImages.Length : 0;
        if (origins.Length == count) return;
        origins = new Vector2[count];
        distances = new Vector2[count];
        startTimes = new float[count];
        ClearFeedback();
    }

    public void ShowTreatmentFeedback(bool healed)
    {
        if (!isActiveAndEnabled || !IsConfigured) return;
        PreparePool();
        float now = Time.unscaledTime;
        // A catch-up frame may resolve several hold ticks. Preserve readability
        // without changing how many ticks are actually awarded by the game.
        if (now - (healed ? lastHitTime : lastMissTime) < FeedbackCooldown) return;
        if (healed)
        {
            lastHitTime = now;
            float side = (hitVariation++ & 1) == 0 ? -1f : 1f;
            Emit(healingSymbol, new Vector2(side * 20f, 5f), new Vector2(side * 3f, 15f), now);
            Emit(healingSymbol, new Vector2(-side * 13f, 17f), new Vector2(-side * 2f, 11f), now + 0.08f);
        }
        else
        {
            lastMissTime = now;
            Emit(missSymbol, new Vector2(21f, 7f), new Vector2(0f, -15f), now);
        }
    }

    void Emit(Sprite sprite, Vector2 origin, Vector2 distance, float time)
    {
        int slot = nextSlot;
        nextSlot = (nextSlot + 1) % feedbackImages.Length;
        Image image = feedbackImages[slot];
        if (image == null) return;
        origins[slot] = origin;
        distances[slot] = distance;
        startTimes[slot] = time;
        image.sprite = sprite;
        image.rectTransform.sizeDelta = sprite.rect.size;
        image.rectTransform.anchoredPosition = origin;
        image.color = Color.white;
        image.gameObject.SetActive(true);
    }

    void Update()
    {
        for (int i = 0; i < startTimes.Length; i++)
        {
            Image image = feedbackImages[i];
            if (image == null || !image.gameObject.activeSelf) continue;
            float elapsed = Time.unscaledTime - startTimes[i];
            if (elapsed >= FeedbackLifetime)
            {
                image.gameObject.SetActive(false);
                continue;
            }
            float t = Mathf.Clamp01(elapsed / FeedbackLifetime);
            float move = 1f - (1f - t) * (1f - t);
            Vector2 position = origins[i] + distances[i] * move;
            image.rectTransform.anchoredPosition = new Vector2(Mathf.Round(position.x), Mathf.Round(position.y));
            float alpha = elapsed < 0f ? 0f : 1f - Mathf.InverseLerp(0.55f, FeedbackLifetime, elapsed);
            image.color = new Color(1f, 1f, 1f, alpha);
        }
    }

    public void ResetPresentation()
    {
        ClearFeedback();
        if (heartAnimator == null || !heartAnimator.isActiveAndEnabled
            || heartAnimator.runtimeAnimatorController == null) return;
        foreach (AnimatorControllerParameter parameter in heartAnimator.parameters)
            if (parameter.type == AnimatorControllerParameterType.Trigger)
                heartAnimator.ResetTrigger(parameter.nameHash);
        if (heartAnimator.HasState(0, IdleState))
            heartAnimator.Play(IdleState, 0, 0f);
    }

    void ClearFeedback()
    {
        nextSlot = 0;
        hitVariation = 0;
        lastHitTime = lastMissTime = float.NegativeInfinity;
        if (feedbackImages == null) return;
        foreach (Image image in feedbackImages)
            if (image != null) image.gameObject.SetActive(false);
    }
}
