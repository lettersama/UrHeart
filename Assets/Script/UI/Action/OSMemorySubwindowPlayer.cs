using System;
using System.Collections;
using MrTGame.Narrative;
using UnityEngine;

[DisallowMultipleComponent]
public sealed partial class OSMemorySubwindowPlayer : MonoBehaviour
{
    [SerializeField] RectTransform poolRoot;
    [SerializeField] OSMemorySubwindow[] pool = new OSMemorySubwindow[0];
    Coroutine routine;
    OSMemorySubwindowSequence currentSequence;
    bool continuationReleased;
    float continuationTime;
    int playStartedFrame;
    bool isClosingPersistentDisplay;
    public bool IsPlaying => currentSequence != null;
    public OSMemorySubwindowSequence CurrentSequence => currentSequence;
    public bool IsPersistentDisplay => currentSequence != null && currentSequence.KeepOpenUntilCancelled;
    public bool IsClosingPersistentDisplay => isClosingPersistentDisplay;
    public bool IsBlockingInput => IsPlaying && !continuationReleased;
    public int Capacity => pool.Length;
    public OSMemoryShotContent CurrentContent => IsPlaying && pool.Length > 0 && pool[0] != null
        ? pool[0].Content : null;
    public event Action<OSMemorySubwindowSequence> ContinuationReady;
    public event Action<OSMemorySubwindowSequence> Completed;
    public event Action<OSMemorySubwindowSequence, int> ShotOpened;

    void OnDisable() => Cancel();

    void Update()
    {
        if (!IsPlaying || continuationReleased || currentSequence.ContinueAtProgress >= 1f
            || Time.frameCount == playStartedFrame || Time.unscaledTime < continuationTime)
            return;

        // Release reading without stopping the remaining image/close animations.
        continuationReleased = true;
        ContinuationReady?.Invoke(currentSequence);
    }

    public bool TryPlay(OSMemorySubwindowSequence sequence)
    {
        if (!isActiveAndEnabled || sequence == null || poolRoot == null)
            return false;
        if (!sequence.Validate(pool.Length, out string error))
        {
            Debug.LogError("[OS Memory Subwindows] " + error, sequence);
            return false;
        }
        for (int i = 0; i < pool.Length; i++)
        {
            if (pool[i] == null)
            {
                Debug.LogError("[OS Memory Subwindows] Pool slot " + i + " is missing.", this);
                return false;
            }
        }
        Cancel();
        poolRoot.gameObject.SetActive(true);
        poolRoot.SetAsLastSibling();
        // Preserve the entire composition with uniform scaling on a different canvas size.
        Vector2 available = poolRoot.rect.size;
        float scale = Mathf.Min(available.x / sequence.ReferenceSize.x,
            available.y / sequence.ReferenceSize.y);
        if (scale <= 0f || float.IsNaN(scale) || float.IsInfinity(scale))
        {
            Cancel();
            return false;
        }
        try
        {
            for (int i = 0; i < sequence.Shots.Count; i++)
            {
                OSMemorySubwindowShot shot = sequence.Shots[i];
                var rect = (RectTransform)pool[i].transform;
                rect.localScale = Vector3.one * scale;
                pool[i].Prepare(shot, shot.Position * scale, shot.Size);
            }
        }
        catch (Exception exception)
        {
            Debug.LogException(exception, this);
            Cancel();
            return false;
        }
        currentSequence = sequence;
        // A resident illustration is supplementary from its first frame, including opening.
        continuationReleased = sequence.KeepOpenUntilCancelled;
        playStartedFrame = Time.frameCount;
        continuationTime = Time.unscaledTime + GetDuration(sequence) * sequence.ContinueAtProgress;
        BeginForeground(sequence);
        routine = StartCoroutine(PlaySequence(sequence));
        return true;
    }

    float GetDuration(OSMemorySubwindowSequence sequence)
    {
        float duration = sequence.LastWindowHold + sequence.CloseDuration
            + (sequence.Shots.Count - 1) * sequence.CloseInterval;
        for (int i = 0; i < sequence.Shots.Count; i++)
            duration += sequence.Shots[i].DelayAfterPreviousOpen + pool[i].Window.OpenAnimationDuration;
        return duration;
    }

    IEnumerator PlaySequence(OSMemorySubwindowSequence sequence)
    {
        // Never complete/re-enter the dialogue from inside ShowData's binding stack.
        yield return null;
        for (int i = 0; i < sequence.Shots.Count; i++)
        {
            float delay = sequence.Shots[i].DelayAfterPreviousOpen;
            if (delay > 0f)
                yield return new WaitForSecondsRealtime(delay);
            pool[i].Open();
            ShotOpened?.Invoke(sequence, i);
            while (!pool[i].IsFullyOpen)
                yield return null;
        }
        if (sequence.KeepOpenUntilCancelled)
        {
            // No artificial long hold and no completion/advance callback. The owner
            // retains the open content until the reading cursor leaves its section.
            routine = null;
            yield break;
        }
        if (sequence.LastWindowHold > 0f)
            yield return new WaitForSecondsRealtime(sequence.LastWindowHold);
        yield return CloseSequence(sequence);
    }

    // Supplementary inserts may finish while the reader stays on the same
    // paragraph. Preserve their final image during the existing window fade.
    public void ClosePersistentAnimated()
    {
        if (!IsPersistentDisplay || isClosingPersistentDisplay)
            return;
        if (routine != null)
            StopCoroutine(routine);
        isClosingPersistentDisplay = true;
        routine = StartCoroutine(CloseSequence(currentSequence));
    }

    IEnumerator CloseSequence(OSMemorySubwindowSequence sequence)
    {
        for (int i = 0; i < sequence.Shots.Count; i++)
        {
            pool[i].CloseAnimated(sequence.CloseDuration);
            if (i + 1 < sequence.Shots.Count && sequence.CloseInterval > 0f)
                yield return new WaitForSecondsRealtime(sequence.CloseInterval);
        }
        // Intervals measure start-to-start. The pool remains occupied until every exit finishes.
        for (int i = 0; i < sequence.Shots.Count; i++)
            while (!pool[i].IsClosed)
                yield return null;
        bool notifyContinuation = !continuationReleased;
        continuationReleased = true;
        routine = null;
        currentSequence = null;
        isClosingPersistentDisplay = false;
        // Release before callbacks: continuation can synchronously start a new sequence.
        EndForeground();
        poolRoot.gameObject.SetActive(false);
        if (notifyContinuation)
            ContinuationReady?.Invoke(sequence);
        Completed?.Invoke(sequence);
    }

    public void Cancel()
    {
        EndForeground();
        if (routine != null)
            StopCoroutine(routine);
        routine = null;
        currentSequence = null;
        continuationReleased = false;
        isClosingPersistentDisplay = false;
        foreach (OSMemorySubwindow item in pool)
            if (item != null)
                item.Close();
        if (poolRoot != null)
            poolRoot.gameObject.SetActive(false);
    }
}
