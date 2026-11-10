using System;
using DG.Tweening;
using UnityEngine;

// OpenLive is one close-up frame of Live reaching for the camera. Rather than
// holding it still for the whole signal-in, the view moves in whole pixels like
// a camera being adjusted and set down, then a short signal break cuts to her
// stepping back. On the Chapter 1 reconnect she tidies her hair (Expend00) when
// healthy; otherwise the cut lands on the current resource or story pose.
public sealed partial class OSCharacterWindowController
{
    // One offset per step, in UI pixels (x right, y up): picked up and turned,
    // steadied, then set down with a 1px bounce. The last offset holds until the cut.
    static readonly Vector2Int[] OpenLiveCameraPath =
    {
        new Vector2Int(0, 0),
        new Vector2Int(-1, 1),
        new Vector2Int(-3, 2),
        new Vector2Int(-3, 4),
        new Vector2Int(-1, 4),
        new Vector2Int(1, 2),
        new Vector2Int(2, 1),
        new Vector2Int(1, -1),
        new Vector2Int(0, -1),
        new Vector2Int(0, 0),
        new Vector2Int(0, 0),
        new Vector2Int(0, 2),
        new Vector2Int(0, 4),
        new Vector2Int(0, 3)
    };

    [Header("Open Live Camera")]
    [Tooltip("开机特写每一步镜头位移的间隔（秒）。")]
    [SerializeField, Min(0.02f)] float openLiveCameraStepSeconds = 0.07f;
    [Tooltip("特写上信号从乱到稳的时长（秒）。")]
    [SerializeField, Min(0.05f)] float openLiveSignalSeconds = 0.9f;
    [Tooltip("摄像头放稳后停住多久再切走（秒）。")]
    [SerializeField, Min(0f)] float openLiveSettleHoldSeconds = 0.22f;
    [Tooltip("切到退后画面的信号断裂时长（秒）。")]
    [SerializeField, Min(0.05f)] float openLiveCutSeconds = 0.18f;
    [Tooltip("位移倍数，0 关闭镜头晃动。")]
    [SerializeField, Min(0)] int openLiveCameraPixels = 1;

    RectTransform openLiveCameraTarget;
    Vector2 openLiveCameraRest;
    bool openLiveCameraMoved;

    Sequence BuildOpenLiveCameraSequence(Action onCompleted, bool settleWithHairBrush)
    {
        Sequence sequence = DOTween.Sequence();
        float step = Mathf.Max(0.02f, openLiveCameraStepSeconds);
        float cutTime = OpenLiveCameraPath.Length * step
            + Mathf.Max(0f, openLiveSettleHoldSeconds);
        float cutSeconds = Mathf.Max(0.05f, openLiveCutSeconds);

        sequence.Insert(0f, DOVirtual.Float(
                0f,
                1f,
                Mathf.Max(0.05f, Mathf.Min(openLiveSignalSeconds, cutTime)),
                ApplyCameraSignalProgress)
            .SetEase(Ease.OutCubic));

        for (int i = 0; i < OpenLiveCameraPath.Length; i++)
        {
            Vector2Int offset = OpenLiveCameraPath[i];
            sequence.InsertCallback(i * step, () => SetOpenLiveCameraOffset(offset));
        }

        sequence.InsertCallback(cutTime, () => CutFromOpenLive(settleWithHairBrush));
        sequence.Insert(cutTime, DOVirtual.Float(
                PortraitPoseTransitionStart,
                1f,
                cutSeconds,
                ApplyCameraSignalProgress)
            .SetEase(Ease.Linear));

        sequence
            .SetUpdate(true)
            .SetLink(gameObject, LinkBehaviour.KillOnDisable)
            .OnComplete(() =>
            {
                RestoreOpenLiveCamera();
                if (cameraSignalTween != sequence)
                    return;

                cameraSignalTween = null;
                isSignalInActive = false;
                ApplyCameraSignalProgress(1f);
                ReleaseCameraSignalMaterial();
                isTransitioning = false;
                ReleaseCameraSignalAnimation();
                onCompleted?.Invoke();
            })
            .OnKill(() =>
            {
                RestoreOpenLiveCamera();
                if (cameraSignalTween != sequence)
                    return;

                cameraSignalTween = null;
                isSignalInActive = false;
                ReleaseCameraSignalMaterial();
                isTransitioning = false;
                ReleaseCameraSignalAnimation();
            });
        return sequence;
    }

    void CutFromOpenLive(bool settleWithHairBrush)
    {
        RestoreOpenLiveCamera();
        ApplyCameraSignalProgress(PortraitPoseTransitionStart);
        if (currentExtendedAnimation != OSCharacterExtendedAnimation.OpenLive)
            return;

        // Hair brushing only reads right on the healthy idle and when no story
        // beat already owns her pose.
        bool hairBrush = settleWithHairBrush
            && storyAnimationOwner == null
            && TryGetResourceAnimationGroup(out string group)
            && group == "Normal"
            && SetExtendedAnimation(OSCharacterExtendedAnimation.Expend00);
        if (!hairBrush)
            PlayResourceAnimation();

        if (characterAnimator != null && characterAnimator.isActiveAndEnabled)
            characterAnimator.Update(0f);
        ConfigureCameraSignalMaterial();
    }

    void SetOpenLiveCameraOffset(Vector2Int offset)
    {
        if (characterImage == null)
            return;

        RectTransform target = characterImage.rectTransform;
        if (!openLiveCameraMoved || openLiveCameraTarget != target)
        {
            RestoreOpenLiveCamera();
            openLiveCameraTarget = target;
            openLiveCameraRest = target.anchoredPosition;
            openLiveCameraMoved = true;
        }

        int scale = Mathf.Max(0, openLiveCameraPixels);
        target.anchoredPosition = openLiveCameraRest
            + new Vector2(offset.x * scale, offset.y * scale);
    }

    void RestoreOpenLiveCamera()
    {
        if (!openLiveCameraMoved)
            return;

        openLiveCameraMoved = false;
        if (openLiveCameraTarget != null)
            openLiveCameraTarget.anchoredPosition = openLiveCameraRest;
        openLiveCameraTarget = null;
    }
}
