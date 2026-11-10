using System;
using System.Collections.Generic;
using UnityEngine;

namespace MrTGame.Narrative
{
    [Serializable]
    public sealed class OSMemorySubwindowShot
    {
        [SerializeField] OSMemoryShotContent contentPrefab;
        [SerializeField] Vector2 position;
        [SerializeField] Vector2 size = new Vector2(130f, 100f);
        [SerializeField, Min(0f)] float delayAfterPreviousOpen;
        [SerializeField] string openedSfxKey;
        [SerializeField] bool floatCaption;
        [SerializeField, Min(0f)] float captionDuration = 0.8f;
        [SerializeField, Min(0f)] float captionFadeIn = 0.15f;
        [SerializeField, Min(0f)] float captionFadeOut = 0.3f;
        [SerializeField] float captionRise = 12f;

        public OSMemoryShotContent ContentPrefab => contentPrefab;
        public Vector2 Position => position;
        public Vector2 Size => size;
        public float DelayAfterPreviousOpen => delayAfterPreviousOpen;
        public string OpenedSfxKey => openedSfxKey;
        public bool FloatCaption => floatCaption;
        public float CaptionDuration => captionDuration;
        public float CaptionFadeIn => captionFadeIn;
        public float CaptionFadeOut => captionFadeOut;
        public float CaptionRise => captionRise;
    }

    [CreateAssetMenu(menuName = "MrTGame/OS Memory/Subwindow Sequence")]
    public sealed class OSMemorySubwindowSequence : ScriptableObject
    {
        [SerializeField] string sequenceId;
        [Tooltip("All window positions and sizes use this authoring canvas size.")]
        [SerializeField] Vector2 referenceSize = new Vector2(640f, 360f);
        [SerializeField] string requiredToolProgram = OSToolProgramIds.MemoryRepair01;
        [SerializeField] List<OSMemorySubwindowShot> shots = new List<OSMemorySubwindowShot>();
        [SerializeField, Min(0f)] float lastWindowHold = 3f;
        [SerializeField, Min(0f)] float closeInterval = 0.25f;
        [SerializeField, Min(0f)] float closeDuration = 0.32f;
        [Tooltip("Release memory reading at this fraction of the full window sequence. 1 waits for every window to close.")]
        [SerializeField, Range(0f, 1f)] float continueAtProgress = 1f;
        [SerializeField] bool autoAdvance = true;
        [Tooltip("Keep the illustration visible until its owner cancels it. Reading remains unlocked; content controls its own animation loop.")]
        [SerializeField] bool keepOpenUntilCancelled;

        public string SequenceId => sequenceId;
        public Vector2 ReferenceSize => referenceSize;
        public string RequiredToolProgram => requiredToolProgram;
        public IReadOnlyList<OSMemorySubwindowShot> Shots => shots;
        public float LastWindowHold => lastWindowHold;
        public float CloseInterval => closeInterval;
        public float CloseDuration => closeDuration;
        public float ContinueAtProgress => continueAtProgress;
        public bool AutoAdvance => autoAdvance;
        public bool KeepOpenUntilCancelled => keepOpenUntilCancelled;

        public bool Validate(int capacity, out string error)
        {
            error = null;
            if (referenceSize.x <= 0f || referenceSize.y <= 0f
                || !Finite(referenceSize.x) || !Finite(referenceSize.y))
                error = "Reference size must be finite and positive.";
            else if (shots == null || shots.Count == 0 || shots.Count > capacity)
                error = "The sequence must fit the subwindow pool (1-" + capacity + " shots).";
            else if (!Duration(lastWindowHold) || !Duration(closeInterval) || !Duration(closeDuration))
                error = "Hold and close durations must be finite and nonnegative.";
            else if (!Finite(continueAtProgress) || continueAtProgress < 0f || continueAtProgress > 1f)
                error = "Continue progress must be finite and between 0 and 1.";
            else if (keepOpenUntilCancelled && (autoAdvance || continueAtProgress != 0f))
                error = "A persistent illustration must use continue progress 0 and must not auto-advance.";
            else
            {
                for (int i = 0; i < shots.Count; i++)
                {
                    OSMemorySubwindowShot shot = shots[i];
                    if (shot == null || shot.ContentPrefab == null
                        || shot.Size.x <= 0f || shot.Size.y <= 0f
                        || !Finite(shot.Size.x) || !Finite(shot.Size.y)
                        || !Finite(shot.Position.x) || !Finite(shot.Position.y)
                        || !Duration(shot.DelayAfterPreviousOpen)
                        || !Duration(shot.CaptionDuration) || !Duration(shot.CaptionFadeIn)
                        || !Duration(shot.CaptionFadeOut) || !Finite(shot.CaptionRise)
                        || (shot.FloatCaption && (shot.ContentPrefab.FloatingCaption == null
                            || shot.CaptionDuration <= 0f
                            || shot.CaptionFadeIn + shot.CaptionFadeOut > shot.CaptionDuration)))
                    {
                        error = "Shot " + (i + 1) + " has missing content or invalid layout/timing.";
                        break;
                    }
                }
            }
            return error == null;
        }

        static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        static bool Duration(float value) => Finite(value) && value >= 0f;
    }
}
