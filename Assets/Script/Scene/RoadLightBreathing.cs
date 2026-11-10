using DG.Tweening;
using UnityEngine;
using UnityEngine.Rendering.Universal;

[DisallowMultipleComponent]
public sealed class RoadLightBreathing : MonoBehaviour
{
    [Header("Target")]
    [SerializeField] Light2D targetLight;

    [Header("Intensity")]
    [SerializeField, Min(0f)] float minIntensity;
    [SerializeField, Min(0f)] float maxIntensity = 4.5f;

    [Header("Timing")]
    [SerializeField, Min(0.01f)] float breathingDuration = 7f;
    [SerializeField, Min(0f)] float restDuration = 4f;
    [SerializeField, Min(0f)] float phaseOffset;

    Sequence _breathingSequence;

    void Awake()
    {
        if (TryResolveTarget())
            return;

        Debug.LogError("[RoadLightBreathing] A child SpotLight with Light2D is required.", this);
        enabled = false;
    }

    void OnEnable()
    {
        if (Application.isPlaying && TryResolveTarget())
            StartBreathing();
    }

    void StartBreathing()
    {
        KillSequence();
        targetLight.intensity = minIntensity;

        float halfBreathingDuration = breathingDuration * 0.5f;
        Tween intensityTween = DOTween
            .To(
                () => targetLight.intensity,
                value => targetLight.intensity = value,
                maxIntensity,
                halfBreathingDuration)
            .SetEase(Ease.InOutSine)
            .SetLoops(2, LoopType.Yoyo);

        Sequence sequence = DOTween.Sequence();
        _breathingSequence = sequence;
        sequence
            .Append(intensityTween)
            .AppendInterval(restDuration)
            .SetLoops(-1, LoopType.Restart)
            .SetLink(gameObject, LinkBehaviour.KillOnDisable)
            .OnKill(() =>
            {
                if (_breathingSequence == sequence)
                    _breathingSequence = null;
            });

        float totalDuration = breathingDuration + restDuration;
        if (totalDuration > 0f && phaseOffset > 0f)
            sequence.Goto(Mathf.Repeat(phaseOffset, totalDuration), true);
    }

    bool TryResolveTarget()
    {
        if (targetLight != null)
            return true;

        Light2D[] childLights = GetComponentsInChildren<Light2D>(true);
        for (int i = 0; i < childLights.Length; i++)
        {
            if (childLights[i].name != "SpotLight")
                continue;

            targetLight = childLights[i];
            return true;
        }

        return false;
    }

    void KillSequence()
    {
        if (_breathingSequence == null)
            return;

        Sequence sequence = _breathingSequence;
        _breathingSequence = null;
        sequence.Kill(false);
    }

    void OnDisable()
    {
        KillSequence();
    }

    void OnDestroy()
    {
        KillSequence();
    }

    void Reset()
    {
        TryResolveTarget();
    }

    void OnValidate()
    {
        maxIntensity = Mathf.Max(minIntensity, maxIntensity);
        breathingDuration = Mathf.Max(0.01f, breathingDuration);
        restDuration = Mathf.Max(0f, restDuration);
        phaseOffset = Mathf.Max(0f, phaseOffset);
        TryResolveTarget();
    }
}
