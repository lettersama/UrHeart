using UnityEngine;

public sealed partial class OSGramophonePlayer
{
    AudioManager activityEffectsManager;
    AudioSource activityEffectsSource;
    AudioClip activityEffectsClip;
    AudioLowPassFilter activityLowPassFilter;
    AudioDistortionFilter activityDistortionFilter;
    bool previousLowPassEnabled, previousDistortionEnabled;
    float previousCutoff, previousResonance, previousDistortion;

    void RefreshActivityEffects()
    {
        var audio = AudioManager.Instance;
        if (activityEffectsManager != audio)
        {
            RestoreActivityEffects();
            if (activityEffectsManager != null)
                activityEffectsManager.MusicOwnershipChanging -= RestoreActivityEffects;
            activityEffectsManager = audio;
            if (audio != null) audio.MusicOwnershipChanging += RestoreActivityEffects;
        }
        var source = ActivitySource;
        bool active = activityOwner != null && !activityManualOverride && !temporaryMusic
            && !activitySilence && !activitySuspended && state?.wantsPlayback == true
            && source != null && source.isPlaying && audio != null && !audio.HasMusicTreatmentEffect
            && (activityLowPass < 22000f || activityDistortion > 0f);
        if (!active) { RestoreActivityEffects(); return; }
        if (activityEffectsSource != source || activityEffectsClip != source.clip)
        {
            RestoreActivityEffects();
            activityEffectsSource = source;
            activityEffectsClip = source.clip;
            activityLowPassFilter = source.GetComponent<AudioLowPassFilter>();
            if (activityLowPassFilter == null)
            {
                activityLowPassFilter = source.gameObject.AddComponent<AudioLowPassFilter>();
                activityLowPassFilter.enabled = false;
            }
            activityDistortionFilter = source.GetComponent<AudioDistortionFilter>();
            if (activityDistortionFilter == null)
            {
                activityDistortionFilter = source.gameObject.AddComponent<AudioDistortionFilter>();
                activityDistortionFilter.enabled = false;
            }
            previousLowPassEnabled = activityLowPassFilter.enabled;
            previousCutoff = activityLowPassFilter.cutoffFrequency;
            previousResonance = activityLowPassFilter.lowpassResonanceQ;
            previousDistortionEnabled = activityDistortionFilter.enabled;
            previousDistortion = activityDistortionFilter.distortionLevel;
        }
        activityLowPassFilter.enabled = true;
        activityLowPassFilter.cutoffFrequency = activityLowPass;
        activityLowPassFilter.lowpassResonanceQ = 1f;
        activityDistortionFilter.enabled = true;
        activityDistortionFilter.distortionLevel = activityDistortion;
    }

    // AudioManager notifies before its next owner captures these components.
    // This handler only restores properties: it never calls PlayMusic/StopMusic.
    void RestoreActivityEffects()
    {
        if (activityEffectsSource == null) return;
        if (activityLowPassFilter != null)
        {
            activityLowPassFilter.cutoffFrequency = previousCutoff;
            activityLowPassFilter.lowpassResonanceQ = previousResonance;
            activityLowPassFilter.enabled = previousLowPassEnabled;
        }
        if (activityDistortionFilter != null)
        {
            activityDistortionFilter.distortionLevel = previousDistortion;
            activityDistortionFilter.enabled = previousDistortionEnabled;
        }
        // Keep newly created, disabled runtime filters as clean components.
        // Deferred Destroy here could remove a filter just adopted by a new owner.
        activityEffectsSource = null;
        activityEffectsClip = null;
        activityLowPassFilter = null;
        activityDistortionFilter = null;
    }

    void DisposeActivityEffects()
    {
        RestoreActivityEffects();
        if (activityEffectsManager != null)
            activityEffectsManager.MusicOwnershipChanging -= RestoreActivityEffects;
        activityEffectsManager = null;
    }
}
