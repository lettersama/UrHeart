using DG.Tweening;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class WorldTipAnimator : MonoBehaviour
{
    [Header("Renderers")]
    [SerializeField] SpriteRenderer tipBoard;
    [SerializeField] SpriteRenderer tipKey;

    [Header("Timing")]
    [SerializeField, Min(0f)] float transitionDelay = 0.1f;
    [SerializeField, Min(0.01f)] float boardDuration = 0.3f;
    [SerializeField, Min(0.01f)] float keyFadeDuration = 0.2f;
    [SerializeField, Range(0f, 1f)] float keyVisibleAlpha = 1f;
    [SerializeField] bool startVisible;

    static readonly int RevealProperty = Shader.PropertyToID("_Reveal");

    MaterialPropertyBlock _boardProperties;
    Sequence _transition;
    float _boardReveal;
    bool _targetVisible;
    bool _initialized;

    public bool TargetVisible => _targetVisible;

    void Awake()
    {
        if (!TryInitialize())
        {
            enabled = false;
            return;
        }

        SetVisibleImmediate(startVisible);
    }

    public void Show()
    {
        AnimateTo(true);
    }

    public void Hide()
    {
        AnimateTo(false);
    }

    public void SetVisibleImmediate(bool visible)
    {
        if (!TryInitialize())
            return;

        KillTransition();
        _targetVisible = visible;
        SetBoardReveal(visible ? 1f : 0f);
        SetKeyAlpha(visible ? keyVisibleAlpha : 0f);
    }

    void AnimateTo(bool visible)
    {
        if (!TryInitialize())
            return;

        float revealTarget = visible ? 1f : 0f;
        float alphaTarget = visible ? keyVisibleAlpha : 0f;
        bool alreadyTargetingState = _targetVisible == visible;

        if (alreadyTargetingState && _transition != null && _transition.IsActive())
            return;

        if (alreadyTargetingState &&
            Mathf.Approximately(_boardReveal, revealTarget) &&
            Mathf.Approximately(tipKey.color.a, alphaTarget))
            return;

        _targetVisible = visible;
        KillTransition();

        float boardTweenDuration = boardDuration * Mathf.Abs(revealTarget - _boardReveal);
        float alphaRange = Mathf.Max(keyVisibleAlpha, 0.0001f);
        float keyTweenDuration = keyFadeDuration *
            Mathf.Abs(alphaTarget - tipKey.color.a) / alphaRange;

        if (boardTweenDuration <= 0.0001f && keyTweenDuration <= 0.0001f)
        {
            SetBoardReveal(revealTarget);
            SetKeyAlpha(alphaTarget);
            return;
        }

        Sequence sequence = DOTween.Sequence();
        _transition = sequence;

        if (boardTweenDuration > 0.0001f)
        {
            Tween boardTween = DOTween
                .To(() => _boardReveal, SetBoardReveal, revealTarget, boardTweenDuration)
                .SetEase(Ease.Linear);
            sequence.Insert(transitionDelay, boardTween);
        }
        else
        {
            SetBoardReveal(revealTarget);
        }

        if (keyTweenDuration > 0.0001f)
        {
            Tween keyTween = tipKey
                .DOFade(alphaTarget, keyTweenDuration)
                .SetEase(visible ? Ease.OutQuad : Ease.InQuad);
            sequence.Insert(transitionDelay, keyTween);
        }
        else
        {
            SetKeyAlpha(alphaTarget);
        }

        sequence
            .SetLink(gameObject, LinkBehaviour.KillOnDisable)
            .OnComplete(() =>
            {
                if (_transition != sequence)
                    return;

                SetBoardReveal(revealTarget);
                SetKeyAlpha(alphaTarget);
                _transition = null;
            })
            .OnKill(() =>
            {
                if (_transition == sequence)
                    _transition = null;
            });
    }

    bool TryInitialize()
    {
        if (_initialized && _boardProperties != null)
            return true;

        ResolveRendererReferences();
        if (tipBoard == null || tipKey == null)
        {
            Debug.LogError("[WorldTip] TipBord and Tip_E renderers are required.", this);
            return false;
        }

        if (tipBoard.sharedMaterial == null ||
            !tipBoard.sharedMaterial.HasProperty(RevealProperty))
        {
            Debug.LogError("[WorldTip] TipBord material must expose _Reveal.", this);
            return false;
        }

        _boardProperties = new MaterialPropertyBlock();
        tipBoard.GetPropertyBlock(_boardProperties);
        _boardReveal = _boardProperties.HasFloat(RevealProperty)
            ? _boardProperties.GetFloat(RevealProperty)
            : tipBoard.sharedMaterial.GetFloat(RevealProperty);
        _initialized = true;
        return true;
    }

    void ResolveRendererReferences()
    {
        if (tipBoard == null)
            TryGetComponent(out tipBoard);

        if (tipKey != null)
            return;

        Transform keyTransform = transform.Find("Tip_E");
        if (keyTransform != null)
            keyTransform.TryGetComponent(out tipKey);
    }

    void SetBoardReveal(float value)
    {
        _boardReveal = Mathf.Clamp01(value);
        tipBoard.GetPropertyBlock(_boardProperties);
        _boardProperties.SetFloat(RevealProperty, _boardReveal);
        tipBoard.SetPropertyBlock(_boardProperties);
    }

    void SetKeyAlpha(float value)
    {
        Color color = tipKey.color;
        color.a = Mathf.Clamp01(value);
        tipKey.color = color;
    }

    void KillTransition()
    {
        if (_transition == null)
            return;

        Sequence transition = _transition;
        _transition = null;
        transition.Kill(false);
    }

    void OnDisable()
    {
        KillTransition();
    }

    void OnDestroy()
    {
        KillTransition();
    }

    void OnValidate()
    {
        ResolveRendererReferences();
    }
}
