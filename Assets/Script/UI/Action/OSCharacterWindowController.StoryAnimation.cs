using UnityEngine;

public sealed partial class OSCharacterWindowController
{
    object storyAnimationOwner;
    OSCharacterExtendedAnimation storyAnimationRequest;
    OSCharacterExtendedAnimation storyAnimation;

    /// <summary>
    /// 剧情持有表情，直到同一导演释放。隐藏、无信号和资源变化不清除意图；
    /// 好奇00自动续接01后，同一请求不会重新掏放大镜。
    /// </summary>
    public bool SetStoryAnimation(object owner, OSCharacterExtendedAnimation animation,
        bool replayEntry = false, bool instant = false)
    {
        if (owner == null) return false;
        if (animation == OSCharacterExtendedAnimation.None)
            return ReleaseStoryAnimation(owner, instant);
        int index = (int)animation;
        bool workAnimation = animation == OSCharacterExtendedAnimation.Expend01
            || animation == OSCharacterExtendedAnimation.Expend02;
        if (!workAnimation && (index < (int)OSCharacterExtendedAnimation.Nervous
            || index >= ExtendedAnimationNames.Length)) return false;

        ResolveReferences();
        CacheExtendedAnimationParameters();
        if (extendedAnimationParameterController != null
            && (extendedAnimationParameterMask & (1 << index)) == 0) return false;

        if (!ReferenceEquals(storyAnimationOwner, owner)
            || storyAnimationRequest != animation || replayEntry)
        {
            storyAnimationOwner = owner;
            storyAnimationRequest = storyAnimation = animation;
        }
        bool previousSuppression = suppressPortraitPoseTransition;
        suppressPortraitPoseTransition |= instant;
        if (instant) CancelPortraitPoseTransition();
        try { ApplyStoryAnimation(); }
        finally { suppressPortraitPoseTransition = previousSuppression; }
        if (replayEntry && !cameraSignalOwnsAnimation && characterAnimator != null
            && characterAnimator.isActiveAndEnabled
            && currentExtendedAnimation == animation)
        {
            int state = Animator.StringToHash(ResourceAnimationPrefix + ExtendedAnimationNames[index]);
            if (characterAnimator.HasState(0, state)) characterAnimator.Play(state, 0, 0f);
        }
        return true;
    }

    /// <summary>旧导演退出时只能释放自己，不能清掉下一章刚接管的表情。</summary>
    public bool ReleaseStoryAnimation(object owner, bool instant = false)
    {
        if (owner == null || !ReferenceEquals(storyAnimationOwner, owner)) return false;
        bool heldAnimation = currentExtendedAnimation == storyAnimation;
        storyAnimationOwner = null;
        storyAnimationRequest = storyAnimation = OSCharacterExtendedAnimation.None;
        if (heldAnimation && !cameraSignalOwnsAnimation)
        {
            bool previousSuppression = suppressPortraitPoseTransition;
            suppressPortraitPoseTransition |= instant;
            if (instant) CancelPortraitPoseTransition();
            try { PlayResourceAnimation(); }
            finally { suppressPortraitPoseTransition = previousSuppression; }
        }
        return true;
    }

    bool ApplyStoryAnimation()
    {
        if (storyAnimationOwner == null) return false;
        // OpenLive owns only the signal-in interval; release restores the story pose.
        if (cameraSignalOwnsAnimation) return true;
        if (currentExtendedAnimation != storyAnimation)
            SetExtendedAnimation(storyAnimation);
        else
        {
            CacheExtendedAnimationParameters();
            if (extendedAnimationParametersPending) ApplyExtendedAnimationParameters();
        }
        return true;
    }
}
