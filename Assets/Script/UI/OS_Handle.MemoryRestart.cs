using DG.Tweening;
using UnityEngine;

// Installing the second or third memory upgrade restarts Memory in place:
// a dither darkens the open window, it closes, and it opens again in its new
// form, so the reward reads as the program rebooting instead of vanishing.
public sealed partial class OS_Handle
{
    const float MemoryRestartDarkenSeconds = .56f;
    const float MemoryRestartHoldSeconds = .12f;
    const float MemoryRestartGapSeconds = .18f;

    Sequence memoryRestartSequence;
    OSDitherFill memoryRestartShroud;

    void RestartMemoryWindowAfterUpgrade()
    {
        if (memoryRestartSequence != null && memoryRestartSequence.IsActive())
            return;
        RectTransform frame = memoryWindow != null ? memoryWindow.WindowRect : null;
        if (frame == null || !memoryWindow.IsOpen || memoryWindow.IsMinimized
            || memoryWindow.IsClosing || !memoryWindow.gameObject.activeInHierarchy || !IsOSVisible)
        {
            memoryWindow?.Close();
            return;
        }

        OSWindowUnit front = FrontmostOtherWindow(memoryWindow);
        memoryRestartShroud = OSDitherFill.Create("MemoryRestartShroud", frame, Color.black);
        memoryRestartShroud.Image.raycastTarget = true;
        memoryRestartShroud.transform.SetAsLastSibling();
        memoryRestartShroud.SetLevel(2);

        Sequence sequence = DOTween.Sequence();
        memoryRestartSequence = sequence;
        sequence.Append(DOVirtual.Float(0f, 1f, MemoryRestartDarkenSeconds, t =>
        {
            if (memoryRestartShroud != null)
                memoryRestartShroud.SetLevel(Mathf.RoundToInt(Mathf.Lerp(2f, OSDitherFill.MaxLevel, t)));
        }).SetEase(Ease.Linear));
        sequence.AppendInterval(MemoryRestartHoldSeconds);
        sequence.AppendCallback(() =>
        {
            ClearMemoryRestartShroud();
            memoryWindow.Close();
            // Still open means a story step is holding Memory; it stays as it was.
            if (memoryWindow.IsOpen)
                sequence.Kill();
        });
        sequence.AppendInterval(MemoryRestartGapSeconds);
        sequence.AppendCallback(() =>
        {
            if (!IsOSVisible || IsSystemInputLocked || OSSessionPersistence.IsRestoring
                || GameSessionFlow.Instance?.IsBusy == true || memoryWindow.IsOpen)
                return;
            OpenMemoryWindow();
            // Reopening must not pull Memory over the window the player was using.
            if (front != null && front.IsOpen && !front.IsMinimized)
                front.BringToFront();
        });
        sequence.SetUpdate(true)
            .SetLink(gameObject, LinkBehaviour.KillOnDisable)
            .OnKill(() =>
            {
                ClearMemoryRestartShroud();
                if (memoryRestartSequence == sequence)
                    memoryRestartSequence = null;
            });
    }

    void ClearMemoryRestartShroud()
    {
        if (memoryRestartShroud != null)
            Destroy(memoryRestartShroud.gameObject);
        memoryRestartShroud = null;
    }

    static OSWindowUnit FrontmostOtherWindow(OSWindowUnit window)
    {
        Transform parent = window.transform.parent;
        if (parent == null)
            return null;
        for (int i = parent.childCount - 1; i >= 0; i--)
        {
            Transform child = parent.GetChild(i);
            if (child == window.transform)
                return null;
            if (child.gameObject.activeInHierarchy
                && child.TryGetComponent(out OSWindowUnit other)
                && other.IsOpen && !other.IsMinimized)
                return other;
        }
        return null;
    }
}
