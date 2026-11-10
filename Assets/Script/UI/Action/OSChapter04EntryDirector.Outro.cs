using UnityEngine;

public sealed partial class OSChapter04EntryDirector
{
    OSChapter03HomeController outroHome;
    OSMemoryWindowController outroMemory;
    OSWindowUnit outroMemoryWindow, outroHomeWindow;
    OSHomeView outroView;
    OSHomeLayout outroViewLayout;
    OSMemoryProcessSaveData outroMemoryState;
    CanvasGroup outroHomeGroup;
    bool outroHomeCaptured, outroMemoryCaptured, outroViewCaptured;
    bool outroMemoryPoseBaked, outroOwnsTemporaryLayout;
    bool outroMemoryCanMove, outroMemoryCanResize;
    bool outroHomeInteractable, outroHomeRaycasts, outroLampsWereOn, outroMotionWasPaused;
    float outroHomeAlpha, outroPlayerLight, outroPreviousBlackout;
    Vector3 outroHomeScale;
    Vector2 outroMemoryCentreOffset, outroMemoryBasePosition, outroMemoryBakedPosition;

    // The persistent director owns time. Stages: darken, close Home, centre
    // Memory, released desktop. Stage 4 keeps the final text frozen after closure.
    void ApplyOutroPresentation(int stage, float elapsed)
    {
        ResolveOutroReferences();
        if (outroMemory == null || outroMemoryWindow == null) return;

        if (stage < 3 && outroMemoryWindow.IsOpen)
        {
            if (outroMemoryWindow.IsOpening) outroMemoryWindow.CompleteOpeningFromScript();
            if (outroMemory.CurrentData == null) outroMemory.InitializeFromSave();
        }
        CaptureOutroMemory();
        outroMemory.SetChapter04OutroFrozen(true);
        if (stage < 3) outroMemoryWindow.ConfigureInteraction(false, false);

        if (outroHome != null)
        {
            outroHome.PrepareChapter04Outro(stage < 2);
            outroHomeWindow = outroHome.HomeWindow;
            outroView = outroHome.View;
            CaptureOutroView();
        }

        float darken = stage > 0 ? 1f : OutroProgress(elapsed, 2.5f);
        if (outroView != null)
        {
            outroView.SetAllLightsOn(false);
            outroView.SetPlayerLightScale(outroPlayerLight * (1f - darken));
            // Light alone does not erase Raw/self-lit glyphs. This final output
            // pass makes the last frame black even if a saved effect was present.
            outroView.SetPresentationBlackout(Mathf.Lerp(outroPreviousBlackout, 1f, darken));
            outroView.InputEnabled = false;
            outroView.MotionPaused = true;
        }

        float close = stage < 1 ? 0f : stage > 1 ? 1f : OutroProgress(elapsed, .6f);
        ApplyOutroHomeClose(close);
        if (stage >= 3)
        {
            ReleaseOutroMemory();
            return;
        }

        float centre = stage < 2 ? 0f : stage > 2 ? 1f : OutroProgress(elapsed, .8f);
        outroMemoryWindow.TrySetPresentationOffset(this, outroMemoryCentreOffset * centre);
    }

    void ResolveOutroReferences()
    {
        if (outroMemory == null) outroMemory = os?.MemoryWindowController;
        if (outroMemoryWindow == null && outroMemory != null)
            outroMemoryWindow = outroMemory.GetComponent<OSWindowUnit>();
        if (outroHome == null && outroMemory != null)
            outroHome = outroMemory.GetComponent<OSChapter03HomeController>();
    }

    void CaptureOutroMemory()
    {
        if (outroMemoryCaptured || outroMemoryWindow?.WindowRect == null) return;
        var rect = outroMemoryWindow.WindowRect;
        outroMemoryState = State;
        outroMemoryBasePosition = rect.anchoredPosition - outroMemoryWindow.PresentationOffset;
        outroMemoryCanMove = outroMemoryWindow.AllowsMove;
        outroMemoryCanResize = outroMemoryWindow.AllowsResize;
        var parent = rect.parent as RectTransform;
        if (parent != null)
        {
            Vector3 centre = parent.TransformPoint(parent.rect.center);
            Vector3 current = rect.TransformPoint(rect.rect.center);
            outroMemoryCentreOffset = parent.InverseTransformVector(centre - current);
        }
        else outroMemoryCentreOffset = -rect.anchoredPosition;
        outroMemoryCaptured = true;
    }

    void HoldOutroMemoryClosePose()
    {
        if (outroMemoryPoseBaked || outroMemoryWindow == null
            || !outroMemoryWindow.OwnsPresentationOffset(this)) return;
        var rect = outroMemoryWindow.WindowRect;
        outroMemoryBakedPosition = rect.anchoredPosition;
        outroMemoryWindow.ClearPresentationOffset(this);
        // CloseAnimated clears presentation offsets. Keep this displayed pose
        // in the temporary composition so closing cannot jump back to the left.
        if (!outroMemoryWindow.HasTemporaryOpeningLayout)
        {
            outroMemoryWindow.SetTemporaryOpeningLayout(outroMemoryWindow.RecordedSize,
                outroMemoryBakedPosition, outroMemoryWindow.RecordedSize);
            outroOwnsTemporaryLayout = true;
        }
        else rect.anchoredPosition = outroMemoryBakedPosition;
        outroMemoryPoseBaked = true;
    }

    void ReleaseOutroMemory()
    {
        if (!outroMemoryPoseBaked && outroMemoryWindow.IsOpen && !outroMemoryWindow.IsMinimized)
        {
            // Finish the authored move once. Closing, reopening or dragging after
            // this point belongs to the player, so later frames never recenter it.
            outroMemoryWindow.TrySetPresentationOffset(this, outroMemoryCentreOffset);
            HoldOutroMemoryClosePose();
        }
        outroMemoryWindow.SetPresentationBackground(this, false);
        if (outroMemoryWindow.AllowsMove != outroMemoryCanMove
            || outroMemoryWindow.AllowsResize != outroMemoryCanResize)
            outroMemoryWindow.ConfigureInteraction(outroMemoryCanMove, outroMemoryCanResize);
    }

    void CaptureOutroView()
    {
        if (outroView == null || outroView.Layout == null
            || outroViewCaptured && ReferenceEquals(outroViewLayout, outroView.Layout)) return;
        outroViewLayout = outroView.Layout;
        outroLampsWereOn = outroView.AllLightsOn;
        outroPlayerLight = outroView.PlayerLightScale;
        outroPreviousBlackout = outroView.PresentationBlackout;
        outroMotionWasPaused = outroView.MotionPaused;
        outroViewCaptured = true;
    }

    void ApplyOutroHomeClose(float progress)
    {
        if (outroHomeWindow == null) return;
        if (!outroHomeCaptured)
        {
            outroHomeGroup = outroHomeWindow.GetComponent<CanvasGroup>();
            if (outroHomeGroup == null) outroHomeGroup = outroHomeWindow.gameObject.AddComponent<CanvasGroup>();
            outroHomeAlpha = outroHomeGroup.alpha;
            outroHomeInteractable = outroHomeGroup.interactable;
            outroHomeRaycasts = outroHomeGroup.blocksRaycasts;
            outroHomeScale = outroHomeWindow.transform.localScale;
            outroHomeCaptured = true;
        }
        outroHomeGroup.interactable = false;
        outroHomeGroup.blocksRaycasts = false;
        outroHomeGroup.alpha = outroHomeAlpha * (1f - progress);
        outroHomeWindow.transform.localScale = Vector3.Lerp(outroHomeScale,
            Vector3.Scale(outroHomeScale, new Vector3(.94f, .86f, 1f)), progress);
        if (progress >= 1f && outroHomeWindow.IsOpen) outroHomeWindow.CloseSilently();
    }

    void ResetOutroPresentation()
    {
        if (outroMemoryWindow != null)
        {
            outroMemoryWindow.SetPresentationBackground(this, false);
            outroMemoryWindow.ClearPresentationOffset(this);
            // A new save may already have installed its own layout. Only undo
            // the baked pose while it still belongs to this same save object.
            if (ReferenceEquals(outroMemoryState, State))
            {
                if (outroOwnsTemporaryLayout) outroMemoryWindow.ClearTemporaryOpeningLayout();
                else if (outroMemoryPoseBaked && outroMemoryWindow.WindowRect != null
                    && (outroMemoryWindow.WindowRect.anchoredPosition - outroMemoryBakedPosition).sqrMagnitude < .01f)
                    outroMemoryWindow.WindowRect.anchoredPosition = outroMemoryBasePosition;
            }
            if (outroMemoryCaptured && !outroMemoryWindow.AllowsMove && !outroMemoryWindow.AllowsResize)
                outroMemoryWindow.ConfigureInteraction(outroMemoryCanMove, outroMemoryCanResize);
        }
        outroMemory?.SetChapter04OutroFrozen(false);
        if (outroViewCaptured && outroView != null && ReferenceEquals(outroViewLayout, outroView.Layout))
        {
            outroView.SetAllLightsOn(outroLampsWereOn);
            outroView.SetPlayerLightScale(outroPlayerLight);
            outroView.SetPresentationBlackout(outroPreviousBlackout);
            outroView.MotionPaused = outroMotionWasPaused;
        }
        if (outroHomeCaptured)
        {
            if (outroHomeWindow != null) outroHomeWindow.transform.localScale = outroHomeScale;
            if (outroHomeGroup != null)
            {
                outroHomeGroup.alpha = outroHomeAlpha;
                outroHomeGroup.interactable = outroHomeInteractable;
                outroHomeGroup.blocksRaycasts = outroHomeRaycasts;
            }
        }
        outroHome?.SetChapter04OutroHeld(false);
        outroHomeCaptured = outroMemoryCaptured = outroViewCaptured = false;
        outroMemoryPoseBaked = outroOwnsTemporaryLayout = false;
        outroViewLayout = null;
        outroMemoryState = null;
    }

    static float OutroProgress(float elapsed, float duration)
        => Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / duration));
}
