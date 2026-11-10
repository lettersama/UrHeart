using DG.Tweening;
using MrTGame.SideScroll;
using UnityEngine;

[RequireComponent(typeof(BoxCollider2D))]
public abstract class WorldTipEventBase : SceneEventBase
{
    const float ShowYOffset = 0.65f;
    const float ShowRotationOffset = 10f;
    const float ShowDelay = 0.1f;
    const float ShowDuration = 0.3f;
    const float HiddenPoseResetDelay = ShowDelay + ShowDuration;

    WorldTipAnimator _tip;
    Transform _tipBoard;
    Vector3 _shownLocalPosition;
    Vector3 _shownLocalEulerAngles;
    Tween _showMotion;
    Tween _hiddenPoseReset;
    bool _poseInitialized;

    void Awake()
    {
        if (!TryResolveTip())
            Debug.LogWarning($"[{GetType().Name}] A child WorldTipAnimator is required.", this);
    }

    void Start()
    {
        if (!TryResolveTip())
            return;

        _tipBoard = _tip.transform;
        _shownLocalPosition = _tipBoard.localPosition;
        _shownLocalEulerAngles = _tipBoard.localEulerAngles;
        _poseInitialized = true;
        SetHiddenPose();
    }

    protected override void OnPlayerEntered(PlayerController2D player)
    {
        if (!TryResolveTip() || !_poseInitialized)
            return;

        KillHiddenPoseReset();
        _tip.Show();
        AnimateTipBoardIn();
    }

    protected override void OnPlayerExited(PlayerController2D player)
    {
        if (!TryResolveTip() || !_poseInitialized)
            return;

        KillShowMotion();
        _tip.Hide();
        ScheduleHiddenPoseReset();
    }

    void AnimateTipBoardIn()
    {
        KillShowMotion();

        float positionRatio = Mathf.Clamp01(
            Vector3.Distance(_tipBoard.localPosition, _shownLocalPosition) / ShowYOffset);
        float rotationRatio = Mathf.Clamp01(
            Mathf.Abs(Mathf.DeltaAngle(
                _tipBoard.localEulerAngles.z,
                _shownLocalEulerAngles.z)) / ShowRotationOffset);
        float duration = ShowDuration * Mathf.Max(positionRatio, rotationRatio);

        if (duration <= 0.0001f)
        {
            SetShownPose();
            return;
        }

        Sequence sequence = DOTween.Sequence();
        _showMotion = sequence;
        sequence
            .Insert(ShowDelay, _tipBoard
                .DOLocalMove(_shownLocalPosition, duration)
                .SetEase(Ease.OutCubic))
            .Insert(ShowDelay, _tipBoard
                .DOLocalRotate(_shownLocalEulerAngles, duration, RotateMode.Fast)
                .SetEase(Ease.OutCubic))
            .SetLink(gameObject, LinkBehaviour.KillOnDisable)
            .OnComplete(() =>
            {
                if (_showMotion != sequence)
                    return;

                SetShownPose();
                _showMotion = null;
            })
            .OnKill(() =>
            {
                if (_showMotion == sequence)
                    _showMotion = null;
            });
    }

    void ScheduleHiddenPoseReset()
    {
        KillHiddenPoseReset();

        Tween reset = DOVirtual.DelayedCall(
                HiddenPoseResetDelay,
                SetHiddenPose,
                false)
            .SetLink(gameObject, LinkBehaviour.KillOnDisable);
        _hiddenPoseReset = reset;
        reset.OnKill(() =>
        {
            if (_hiddenPoseReset == reset)
                _hiddenPoseReset = null;
        });
    }

    void SetHiddenPose()
    {
        if (!_poseInitialized)
            return;

        _tipBoard.localPosition = _shownLocalPosition + Vector3.down * ShowYOffset;
        _tipBoard.localEulerAngles = _shownLocalEulerAngles +
            Vector3.forward * ShowRotationOffset;
    }

    void SetShownPose()
    {
        _tipBoard.localPosition = _shownLocalPosition;
        _tipBoard.localEulerAngles = _shownLocalEulerAngles;
    }

    void KillShowMotion()
    {
        if (_showMotion == null)
            return;

        Tween showMotion = _showMotion;
        _showMotion = null;
        showMotion.Kill(false);
    }

    void KillHiddenPoseReset()
    {
        if (_hiddenPoseReset == null)
            return;

        Tween hiddenPoseReset = _hiddenPoseReset;
        _hiddenPoseReset = null;
        hiddenPoseReset.Kill(false);
    }

    bool TryResolveTip()
    {
        if (_tip == null)
            _tip = GetComponentInChildren<WorldTipAnimator>(true);

        return _tip != null;
    }

    protected override void OnDisable()
    {
        KillShowMotion();
        KillHiddenPoseReset();
        base.OnDisable();
    }

    void Reset()
    {
        if (!TryGetComponent(out BoxCollider2D trigger))
            return;

        trigger.isTrigger = true;
        trigger.size = new Vector2(3f, 4f);
    }

    void OnValidate()
    {
        if (TryGetComponent(out BoxCollider2D trigger))
            trigger.isTrigger = true;
    }
}
