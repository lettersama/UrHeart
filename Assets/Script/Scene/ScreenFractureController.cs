using UnityEngine;
using UnityEngine.Rendering;

[DisallowMultipleComponent]
[RequireComponent(typeof(Volume))]
public sealed class ScreenFractureController : MonoBehaviour
{
    [SerializeField, Range(0f, 1f)]
    float intensity;

    Volume targetVolume;
    ScreenFractureVolume settings;
    float appliedIntensity = -1f;

    public float Intensity
    {
        get => intensity;
        set
        {
            intensity = Mathf.Clamp01(value);
            ApplyIntensity();
        }
    }

    void Awake()
    {
        Initialize();
        ApplyIntensity();
    }

    void LateUpdate()
    {
        if (!Mathf.Approximately(appliedIntensity, intensity))
            ApplyIntensity();
    }

    public void SetIntensity(float value)
    {
        Intensity = value;
    }

    public void Show()
    {
        Intensity = 1f;
    }

    public void Hide()
    {
        Intensity = 0f;
    }

    void Initialize()
    {
        if (settings != null)
            return;

        targetVolume = GetComponent<Volume>();
        VolumeProfile profile = targetVolume.profile;
        if (!profile.TryGet(out settings))
            settings = profile.Add<ScreenFractureVolume>(true);

        settings.active = true;
        settings.intensity.overrideState = true;
    }

    void ApplyIntensity()
    {
        Initialize();
        settings.intensity.value = intensity;
        appliedIntensity = intensity;
    }
}
