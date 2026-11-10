using System;
using UnityEngine;
using UnityEngine.Video;

// Keep the existing values stable for saves made before Chapter 2 was playable.
public enum OSChapter01EndingPhase { None, Slides, Door, Animation, Glitch, Completed, Chapter02Started, RecoveryAwaitingConfirmation, DoorKnocked }

[Serializable]
public sealed class OSChapter01EndingSaveData
{
    public OSChapter01EndingPhase phase;
    public int slideIndex;
    public bool completed;
    public void EnsureData()
    {
        if ((int)phase < 0 || (int)phase > (int)OSChapter01EndingPhase.DoorKnocked)
            phase = OSChapter01EndingPhase.None;
        slideIndex = Mathf.Max(0, slideIndex);
        if (phase == OSChapter01EndingPhase.Chapter02Started
            || phase == OSChapter01EndingPhase.RecoveryAwaitingConfirmation)
            completed = true;
        else
        {
            if (completed) phase = OSChapter01EndingPhase.Completed;
            if (phase == OSChapter01EndingPhase.Completed) completed = true;
        }
    }
}

[CreateAssetMenu(menuName = "MrTGame/Memory/Chapter 01 Ending", fileName = "Chapter01Ending")]
public sealed class OSChapter01EndingData : ScriptableObject
{
    [Serializable]
    public sealed class SlideSound
    {
        public AudioClip clip;
        [Min(0)] public float atSeconds;
        [Range(0, 1)] public float volume = .7f;
    }

    [Serializable]
    public sealed class Slide
    {
        public Sprite image;
        [Tooltip("Optional forward-only frames. The final frame remains visible at the door checkpoint.")]
        public Sprite[] frames = new Sprite[0];
        [Range(.1f, 1f)] public float framePlaybackFraction = .65f;
        [Min(.1f)] public float holdSeconds = 2.2f;
        [Min(0)] public float fadeInSeconds;
        [Min(0)] public float fadeOutSeconds;
        [Range(.1f, 32f)] public float exposure = 1f;
        [Range(0, 8f)] public float exposurePulse;
        [Range(0, .5f)] public float lightStrength;
        public Vector2 lightCenter = new Vector2(.73f, .6f);
        [Range(.05f, 1f)] public float lightRadius = .28f;
        [Tooltip("Legacy presentation data; the silent slideshow does not play audio.")]
        [Range(0, 1)] public float ambience = 1f;
        public SlideSound[] sounds = new SlideSound[0];
        [Tooltip("Used only while image is empty. Placeholder shots: room, corridor, doorway.")]
        public int placeholderShot;
        public string placeholderCaptionKey;
        [Header("Security-camera overlay")]
        [Tooltip("Camera name drawn top-left, e.g. CAM 02. Empty hides the overlay. Digits, spaces and the letters of CAM/REC/IR only.")]
        public string osdCamera;
        [Tooltip("Clock at the start of this shot, in seconds after midnight. It runs while the shot plays.")]
        [Min(0)] public int osdClockSeconds;
        [Tooltip("Adds the IR night-vision mark bottom-right.")]
        public bool osdNightVision;
        [Tooltip("Motion-tracker corners in source pixels, top-left origin. Zero width tracks nothing.")]
        public Rect osdTrack;
    }

    public Slide[] slides = new Slide[0];
    [Header("Fixed-camera slide treatment")]
    public Shader slideShader;
    [Tooltip("Legacy reference only. Slideshow audio is disabled; the door click remains audible.")]
    public AudioClip slideAmbience;
    [Range(0, 1)] public float slideAmbienceVolume = .06f;
    [Header("Door over the final held slide")]
    public Sprite doorImage;
    public Rect doorNormalizedRect = new Rect(.594f, .194f, .244f, .694f);
    [Header("Ending animation: video takes precedence over sprite frames")]
    public VideoClip animationVideo;
    public Sprite[] animationFrames = new Sprite[0];
    [Min(1)] public float animationFramesPerSecond = 12f;
    [Tooltip("Seconds per animation frame, filled from the Aseprite timeline by the editor binder. Empty uses the rate above.")]
    public float[] animationFrameSeconds = new float[0];
    [Min(.1f)] public float animationPlaceholderSeconds = 4f;
    [Header("Sprite animation: horizontal pixel-block glitch that grows until the picture sticks")]
    public Shader animationGlitchShader;
    [Min(1)] public int animationLoops = 1;
    [Range(1f, 30f)] public float animationGlitchTicksPerSecond = 12f;
    [Tooltip("Violent jitter on the last frame before it freezes for the repair.")]
    [Range(0f, 2f)] public float animationStickSeconds = .45f;
    [Header("Presentation timing")]
    [Min(0)] public float initialBlackSeconds = .65f;
    [Min(.01f)] public float doorPopupSeconds = .28f;
    [Min(0)] public float doorKnockSeconds = 1.4f;
    [Min(.01f)] public float demoWipeSeconds = .9f;
    [Min(.01f)] public float doorTransitionSeconds = .45f;
    [Min(.01f)] public float frozenSeconds = .65f;
    [Min(.01f)] public float glitchSeconds = 1.3f;
    [Min(.01f)] public float fadeOutSeconds = 1.1f;
    public AudioClip doorSound;
    public AudioClip crashSound;
    [Header("Chapter 2: repair the frozen desktop (full game only)")]
    [Min(.1f)] public float recoveryFreezeSeconds = 6f;
    [Range(5f, 7f)] public float recoverySwatSeconds = 6f;
    [Header("Ending audio")]
    public AudioClip slideHiss;
    public AudioClip slideFan;
    public AudioClip slideShotSwitch;
    public AudioClip animationMusicBox;
    public AudioClip[] animationGlitchClips = new AudioClip[0];
    public AudioClip animationStuckHum;
    [Range(0, 1)] public float slideHissVolume = .8f;
    [Range(0, 1)] public float slideFanVolume = .9f;
    [Range(0, 1)] public float animationMusicVolume = .85f;
    [Range(0, 1)] public float animationGlitchVolume = .7f;
    [Range(0, 1)] public float animationStuckVolume = .6f;
    [Header("Future chapter handoff")]
    [Tooltip("Optional memory node after Chapter 2's desktop repair. -1 leaves the player on the OS desktop.")]
    [Min(-1)] public int chapter02MemoryIndex = -1;
}
