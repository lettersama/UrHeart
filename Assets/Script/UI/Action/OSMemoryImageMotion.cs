using System;
using DG.Tweening;
using UnityEngine;

// Content-prefab effect: wired to OSMemoryShotContent's opened/released events.
// Offsets and scales are always relative to the authored image pose.
[DisallowMultipleComponent]
public sealed class OSMemoryImageMotion : MonoBehaviour
{
    [Serializable]
    sealed class Keyframe
    {
        [Min(0f)] public float duration = 1f;
        public Vector2 offset;
        [Min(0.001f)] public float scale = 1f;
        public Ease ease = Ease.InOutSine;
    }

    [SerializeField] RectTransform target;
    [SerializeField] Vector2 initialOffset;
    [SerializeField, Min(0.001f)] float initialScale = 1f;
    [SerializeField] Keyframe[] keyframes = new Keyframe[0];

    Sequence motion;
    RectTransform capturedTarget;
    Vector2 authoredPosition;
    Vector3 authoredScale;

    void OnEnable()
    {
        if (Application.isPlaying)
            PrepareInitialPose();
    }

    void OnDisable() => StopAndRestore();
    void OnDestroy() => StopAndRestore();

    public void Play()
    {
        if (!Application.isPlaying || !isActiveAndEnabled
            || !PrepareInitialPose() || keyframes == null || keyframes.Length == 0)
            return;

        // Validate before creating any tween so malformed content stays in its authored pose.
        foreach (Keyframe frame in keyframes)
        {
            if (frame == null || !Finite(frame.duration) || frame.duration < 0f
                || !Finite(frame.offset.x) || !Finite(frame.offset.y)
                || !Finite(frame.scale) || frame.scale <= 0f)
            {
                StopAndRestore();
                Debug.LogWarning("[Memory Image Motion] Invalid keyframe on " + name, this);
                return;
            }
        }

        Sequence sequence = DOTween.Sequence();
        motion = sequence;
        foreach (Keyframe frame in keyframes)
        {
            sequence.Append(target.DOAnchorPos(authoredPosition + frame.offset, frame.duration)
                .SetEase(frame.ease));
            sequence.Join(target.DOScale(authoredScale * frame.scale, frame.duration)
                .SetEase(frame.ease));
        }
        sequence.SetUpdate(true).OnKill(() =>
        {
            if (motion == sequence)
                motion = null;
        });
    }

    bool PrepareInitialPose()
    {
        StopAndRestore();
        if (target == null || target == transform || !target.IsChildOf(transform)
            || !Finite(initialOffset.x) || !Finite(initialOffset.y)
            || !Finite(initialScale) || initialScale <= 0f)
            return false;

        if (capturedTarget != target)
        {
            capturedTarget = target;
            authoredPosition = target.anchoredPosition;
            authoredScale = target.localScale;
        }
        target.anchoredPosition = authoredPosition + initialOffset;
        target.localScale = authoredScale * initialScale;
        return true;
    }

    public void StopAndRestore()
    {
        Sequence sequence = motion;
        motion = null;
        sequence?.Kill(false);
        if (capturedTarget == null)
            return;
        capturedTarget.anchoredPosition = authoredPosition;
        capturedTarget.localScale = authoredScale;
    }

    static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
}
