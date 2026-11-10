using UnityEngine;

public sealed partial class OSCharacterWindowController
{
    const float PortraitPoseTransitionSeconds = .18f;
    const float PortraitPoseTransitionStart = .08f;
    Material portraitPoseTransitionMaterial;
    float portraitPoseTransitionElapsed;
    bool suppressPortraitPoseTransition;

    // Expressions sharing the resting silhouette can change without a signal break.
    // Hair brushing and taking out the magnifier already contain their own motion.
    static bool NeedsPortraitPoseTransition(OSCharacterExtendedAnimation from, OSCharacterExtendedAnimation to)
    {
        if (from == to || from == OSCharacterExtendedAnimation.OpenLive || to == OSCharacterExtendedAnimation.OpenLive
            || from == OSCharacterExtendedAnimation.Expend00 || to == OSCharacterExtendedAnimation.Expend00)
            return false;
        if (from == OSCharacterExtendedAnimation.MagnifyingGlass00 && to == OSCharacterExtendedAnimation.MagnifyingGlass01)
            return false;
        if (IsRestingPortraitPose(from) && to == OSCharacterExtendedAnimation.MagnifyingGlass00)
            return false;
        return !IsRestingPortraitPose(from) || !IsRestingPortraitPose(to);
    }

    static bool IsRestingPortraitPose(OSCharacterExtendedAnimation animation)
        => animation == OSCharacterExtendedAnimation.None || animation == OSCharacterExtendedAnimation.Nervous
            || animation == OSCharacterExtendedAnimation.Sad || animation == OSCharacterExtendedAnimation.Breakdown00
            || animation == OSCharacterExtendedAnimation.Breakdown02 || animation == OSCharacterExtendedAnimation.Angry00;

    bool CanShowPortraitPoseTransition()
        => isActiveAndEnabled && CurrentState == OSCharacterWindowState.Anima
            && !isTransitioning && !isSignalInActive && !isSignalOutActive && !cameraSignalOwnsAnimation
            && !OSSessionPersistence.IsRestoring
            && (OS_Handle.Instance == null || OS_Handle.Instance.IsOSVisible)
            && characterWindow != null && characterWindow.IsOpen && !characterWindow.IsMinimized
            && !characterWindow.IsOpening && !characterWindow.IsClosing
            && characterImage != null && characterImage.isActiveAndEnabled
            && characterAnimator != null && characterAnimator.isActiveAndEnabled;

    void PlayPortraitPoseTransition(OSCharacterExtendedAnimation from, OSCharacterExtendedAnimation to)
    {
        CancelPortraitPoseTransition();
        if (suppressPortraitPoseTransition || !NeedsPortraitPoseTransition(from, to)
            || !CanShowPortraitPoseTransition() || cameraSignalRuntimeMaterial != null) return;
        // Commit immediately under heavy horizontal breakup. There is no delayed
        // pose callback that can overwrite a newer line, owner, or restored save.
        characterAnimator.Update(0f);
        if (!TryCreateCameraSignalMaterial()) return;
        portraitPoseTransitionMaterial = cameraSignalRuntimeMaterial;
        portraitPoseTransitionElapsed = 0f;
        ConfigureCameraSignalMaterial();
        ApplyCameraSignalProgress(PortraitPoseTransitionStart);
    }

    void TickPortraitPoseTransition(float deltaTime)
    {
        if (portraitPoseTransitionMaterial == null) return;
        if (!CanShowPortraitPoseTransition()) { CancelPortraitPoseTransition(); return; }
        portraitPoseTransitionElapsed += Mathf.Max(0f, deltaTime);
        float progress = Mathf.Clamp01(portraitPoseTransitionElapsed / PortraitPoseTransitionSeconds);
        ApplyCameraSignalProgress(Mathf.Lerp(PortraitPoseTransitionStart, 1f, progress));
        if (progress >= 1f) CancelPortraitPoseTransition();
    }

    void CancelPortraitPoseTransition()
    {
        Material owned = portraitPoseTransitionMaterial;
        portraitPoseTransitionMaterial = null;
        portraitPoseTransitionElapsed = 0f;
        if (owned == null) return;
        if (cameraSignalRuntimeMaterial == owned) ReleaseCameraSignalMaterial();
        else if (Application.isPlaying) Destroy(owned);
        else DestroyImmediate(owned);
    }
}
