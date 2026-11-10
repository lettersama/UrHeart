using UnityEngine;
using UnityEngine.UI;

// Dedicated window music, separate from the SFX pool and the story's music clips.
[DisallowMultipleComponent]
public sealed class OSPaperPushMusic : MonoBehaviour
{
    [SerializeField] OSWindowUnit window;
    [SerializeField] Button toggle;
    [SerializeField] RectTransform disc;
    [SerializeField] Graphic note;
    [SerializeField] GameObject mutedSlash;
    [SerializeField] AudioSource source;
    [SerializeField] AudioClip clip;
    [SerializeField, Range(0f, 1f)] float volume = .7f;
    bool muted, paused, ownsBackgroundMute;
    AudioSource background0, background1;
    bool oldMute0, oldMute1;
    public bool IsMuted => muted;
    public bool IsPlaying => source != null && source.isPlaying;
    public AudioSource Source => source;

    void OnEnable() { toggle?.onClick.AddListener(ToggleMute);UpdateIcon(); }
    void OnDisable()
    { toggle?.onClick.RemoveListener(ToggleMute);if (source != null) source.Stop();ReleaseBackground();paused = false; }

    public void ToggleMute()
    {
        if (OS_Handle.Instance?.IsSystemInputLocked == true) return;
        muted = !muted;
        if (source != null) source.mute = muted;
        UpdateIcon();
    }

    void Update()
    {
        // The shared player owns temporary takeover and restoration. Older scenes
        // without that component retain their previous standalone music behavior.
        if (GetComponent<OSGramophonePlayer>() != null)
        {
            if (!muted && source != null && source.isPlaying && note != null)
                note.rectTransform.Rotate(0f, 0f, -45f * Time.unscaledDeltaTime);
            return;
        }
        var audio = AudioManager.Instance;
        bool visible = window != null && window.IsOpen && !window.IsMinimized
            && OS_Handle.Instance?.IsOSVisible == true && OS_Handle.Instance?.IsSystemInputLocked != true;
        if (!visible || audio == null || clip == null || source == null)
        {
            if (source != null && source.isPlaying) { source.Pause();paused = true; }
            if (window == null || !window.IsOpen) { if (source != null) source.Stop();paused = false; }
            ReleaseBackground();return;
        }
        if (!ownsBackgroundMute)
        {
            // Let existing story music keep its playhead; restore its prior mute flags on exit.
            background0 = audio.MusicSource_00;background1 = audio.MusicSource_01;
            if (background0 != null) { oldMute0 = background0.mute;background0.mute = true; }
            if (background1 != null) { oldMute1 = background1.mute;background1.mute = true; }
            ownsBackgroundMute = true;
        }
        source.outputAudioMixerGroup = audio.MusicMixerGroup;
        source.mute = muted;
        if (paused) { source.UnPause();paused = false; }
        if (!source.isPlaying)
        { source.clip = clip;source.loop = true;source.volume = 0f;source.Play(); }
        source.volume = Mathf.MoveTowards(source.volume, volume, Time.unscaledDeltaTime * 3f);
        if (!muted && note != null) note.rectTransform.Rotate(0f, 0f, -45f * Time.unscaledDeltaTime);
    }

    void UpdateIcon()
    {
        if (mutedSlash != null) mutedSlash.SetActive(muted);
        Color tint = muted ? new Color32(74, 70, 66, 255) : new Color32(201, 190, 173, 255);
        if (note != null)
        {
            note.color = tint;
            if (muted) note.rectTransform.localRotation = Quaternion.identity;
        }
        if (disc != null)
        {
            disc.localRotation = Quaternion.identity;
            var graphic = disc.GetComponent<Graphic>();
            if (graphic != null) graphic.color = tint;
        }
    }

    void ReleaseBackground()
    {
        if (!ownsBackgroundMute) return;
        if (background0 != null) background0.mute = oldMute0;
        if (background1 != null) background1.mute = oldMute1;
        ownsBackgroundMute = false;
    }
}
