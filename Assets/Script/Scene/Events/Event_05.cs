using MrTGame.Core;
using UnityEngine;
using UnityEngine.Rendering;

public sealed class Event_05 : WorldTipEventBase
{
    const int FailureEffectDialogueIndex = 2;
    const string GlobalLightingVolumeName = "VFX_GlobalLighting";
    const string OSStateIndex1VolumeName = "OS Volume_ForStateIndex1";
    const string CrowSfxKey = "Event05_CrowCall";
    const string GlitchAmbientKey = "Event05_GlitchAmbient";
    const float GlitchAmbientFadeDuration = 0.1f;

    bool _failureEffectTriggered;

    public override void OnDialogueLineEntered(
        int dialogueIndex,
        EventDialogueData dialogue)
    {
        if (_failureEffectTriggered
            || dialogueIndex != FailureEffectDialogueIndex)
        {
            return;
        }

        _failureEffectTriggered = true;
        PlayerCtrlManager.Instance?.PlayerController?.SetLowHeart(true);
        EnableFailureVolumeEffects();
        PlayFailureAudio();
    }

    protected override void EventAction()
    {
        GameDataManager dataManager = GameDataManager.Instance;
        if (dataManager != null)
            dataManager.SetCurrentStateCheckProgress(1f);
        else
            Debug.LogWarning("[Event_05] GameDataManager is missing.", this);

        OS_Handle osHandle = OS_Handle.Instance;
        if (osHandle != null)
        {
            osHandle.SetCharacterWindowState(
                OSCharacterWindowState.VoidMessage,
                false);
        }
        else
        {
            Debug.LogWarning(
                "[Event_05] OS_Handle is missing; character window state was not set.",
                this);
        }

        bool newStateApplied = osHandle != null
            && osHandle.SetDesktopProcessNew(
                OSDesktopProcessKeys.StateCheck,
                true);
        if (!newStateApplied && dataManager != null)
        {
            dataManager.SetDesktopProcessIsNew(
                OSDesktopProcessKeys.StateCheck,
                true);
        }

        EnableOSStateIndex1Volume();

        GameManager gameManager = GameManager.Instance;
        if (gameManager != null)
            gameManager.SwitchPhase(GamePhase.OSMain);
        else
            Debug.LogWarning("[Event_05] GameManager is missing.", this);
    }

    void EnableFailureVolumeEffects()
    {
        Volume volume = FindGlobalLightingVolume();
        if (volume == null)
        {
            Debug.LogWarning(
                $"[Event_05] Volume '{GlobalLightingVolumeName}' was not found.",
                this);
            return;
        }

        VolumeProfile profile = volume.profile;
        bool vhsEnabled = EnableVolumeComponent<VolFx.VhsVol>(profile);
        bool fractureEnabled = EnableVolumeComponent<ScreenFractureVolume>(profile);
        bool glitchEnabled = EnableVolumeComponent<VolFx.GlitchVol>(profile);
        bool oldMovieEnabled = EnableVolumeComponent<VolFx.OldMovieVol>(profile);
        if (!vhsEnabled || !fractureEnabled || !glitchEnabled || !oldMovieEnabled)
        {
            Debug.LogWarning(
                $"[Event_05] Volume '{GlobalLightingVolumeName}' is missing one or more failure effects.",
                volume);
        }
    }

    Volume FindGlobalLightingVolume()
    {
        return FindVolumeByName(GlobalLightingVolumeName, true);
    }

    void EnableOSStateIndex1Volume()
    {
        Volume volume = FindVolumeByName(OSStateIndex1VolumeName, false);
        if (volume == null)
        {
            Debug.LogWarning(
                $"[Event_05] Volume '{OSStateIndex1VolumeName}' was not found.",
                this);
            return;
        }

        volume.weight = 1f;
    }

    Volume FindVolumeByName(string volumeName, bool requireSameScene)
    {
        Volume[] volumes = Object.FindObjectsByType<Volume>(
            FindObjectsInactive.Include);
        for (int i = 0; i < volumes.Length; i++)
        {
            Volume volume = volumes[i];
            if (volume != null
                && volume.gameObject.name == volumeName
                && (!requireSameScene
                    || volume.gameObject.scene == gameObject.scene))
            {
                return volume;
            }
        }

        return null;
    }

    static bool EnableVolumeComponent<T>(VolumeProfile profile)
        where T : VolumeComponent
    {
        if (profile == null || !profile.TryGet(out T component))
            return false;

        component.active = true;
        return true;
    }

    void PlayFailureAudio()
    {
        AudioManager audioManager = AudioManager.Instance;
        if (audioManager == null)
        {
            Debug.LogWarning("[Event_05] AudioManager is missing.", this);
            return;
        }

        AudioData crowSfx = GameDataManager.Instance
            ?.AudioData
            ?.SFX
            ?.Find(item => item != null && item.keyid == CrowSfxKey);
        if (crowSfx?.audioclip == null)
        {
            Debug.LogWarning(
                $"[Event_05] SFX '{CrowSfxKey}' has no assigned AudioClip.",
                this);
        }

        audioManager.PlaySfx(CrowSfxKey);

        AudioData glitchAmbient = GameDataManager.Instance
            ?.AudioData
            ?.AMBIENT
            ?.Find(item => item != null && item.keyid == GlitchAmbientKey);
        if (glitchAmbient?.audioclip == null)
        {
            Debug.LogWarning(
                $"[Event_05] Ambient '{GlitchAmbientKey}' has no assigned AudioClip.",
                this);
            return;
        }

        audioManager.PlayAmbient(
            glitchAmbient.audioclip,
            GlitchAmbientFadeDuration);
    }
}
