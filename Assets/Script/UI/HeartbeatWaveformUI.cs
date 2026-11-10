using System;
using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
[RequireComponent(typeof(Image))]
public sealed class HeartbeatWaveformUI : MonoBehaviour
{
    public const int WaveformStyleCount = 10;

    const float GridSweepInactiveCenter = -2f;
    const float WaveBaseline = 0.5f;
    const float NormalBeatAmplitudeScale = 0.75f;
    const float NormalBeatSfxVolumeScale = 0.4f;
    const float MinimumBeatSpacing = 0.1f;
    const double BeatTimeEpsilon = 1e-9;
    const int RPeakSampleIndex = 13;
    const float TransitionGrayAmount = 25f / 100f;
    const float TransitionSaturationOffset = 3f / 100f;

    static readonly int WaveTextureId = Shader.PropertyToID("_WaveTex");
    static readonly int VirtualSizeId = Shader.PropertyToID("_VirtualSize");
    static readonly int GridSweepCenterId = Shader.PropertyToID("_GridSweepCenter");
    static readonly int HighlightCoreColorId = Shader.PropertyToID("_HighlightCoreColor");
    static readonly int HighlightTransitionColorId = Shader.PropertyToID("_HighlightTransitionColor");

    static readonly string[] HeartbeatSfxKeys =
    {
        "HeartBeat00",
        "HeartBeat01",
        "HeartBeat02",
        "HeartBeat03",
        "HeartBeat04",
        "HeartBeat05",
    };


    static readonly WaveStyle[] Styles =
    {
        new WaveStyle(0.060f, -0.080f, 0.420f, -0.220f, 0.110f,  0, 2, 0, 4),
        new WaveStyle(0.050f, -0.070f, 0.460f, -0.180f, 0.090f,  0, 2, 0, 4),
        new WaveStyle(0.040f, -0.050f, 0.280f, -0.130f, 0.070f,  0, 2, 0, 4),
        new WaveStyle(0.055f, -0.080f, 0.360f, -0.320f, 0.100f,  0, 2, 0, 4),
        new WaveStyle(0.060f, -0.080f, 0.350f, -0.220f, 0.130f,  0, 3, 0, 5),
        new WaveStyle(0.045f, -0.100f, 0.450f, -0.250f, 0.080f,  0, 2, 0, 3),
        new WaveStyle(0.090f, -0.060f, 0.380f, -0.180f, 0.150f,  0, 2, 1, 5),
        new WaveStyle(0.030f, -0.110f, 0.400f, -0.260f, 0.050f,  0, 2, 0, 3),
        new WaveStyle(0.050f, -0.080f, 0.390f, -0.200f, 0.130f, -1, 2, 2, 5),
        new WaveStyle(0.055f, -0.070f, 0.370f, -0.200f, 0.100f,  1, 2, 0, 4),
    };

    [Header("Pixel Buffer")]
    [SerializeField, Min(16)] int virtualWidth = 120;
    [SerializeField, Min(8)] int virtualHeight = 40;
    [SerializeField, Min(1f)] float pixelsPerSecond = 40f;

    [Header("Timing")]
    [SerializeField] bool useUnscaledTime = true;
    [SerializeField, Min(MinimumBeatSpacing)] float naturalBeatInterval = 1f;
    [SerializeField] int randomSeed;
    [SerializeField] bool avoidImmediateRepeat = true;

    [Header("Grid Sweep")]
    [SerializeField, Min(0.1f)] float gridSweepDuration = 0.8f;
    [SerializeField] Vector2 gridSweepIntervalRange = new Vector2(4f, 6f);

    Image image;
    Material sourceMaterial;
    Material runtimeMaterial;
    Texture2D waveTexture;
    Color32[] samples;
    System.Random styleRandom;
    System.Random sfxRandom;
    System.Random sweepRandom;
    double sampleAccumulator;
    double currentBeatSampleTime;
    double currentBeatDuration;
    float gridSweepTimer;
    float gridSweepProgress;
    bool hasPendingActiveBeat;
    int currentStyleIndex = -1;
    bool isGridSweepActive;
    bool isCurrentBeatActive;
    bool currentBeatSfxPlayed;
    bool beatSfxEnabled;

    public int CurrentStyleIndex => currentStyleIndex;
    public bool IsNextBeatHighlightQueued => hasPendingActiveBeat;
    public int PendingActiveBeatCount => hasPendingActiveBeat ? 1 : 0;
    public bool IsCurrentBeatActive => isCurrentBeatActive;
    public float NaturalBeatInterval => naturalBeatInterval;
    public float TimeSinceLastBeatStart => (float)currentBeatSampleTime;
    public Material RuntimeMaterial => runtimeMaterial;
    bool IsFinalHealEnding => OS_Handle.Instance?.Chapter04Story?.FinalHeal?.IsEnding == true;

    void Awake()
    {
        Initialize();
    }

    void Update()
    {
        AdvanceFrame(useUnscaledTime ? Time.unscaledDeltaTime : Time.deltaTime);
    }

    void AdvanceFrame(float deltaTime)
    {
        if (waveTexture == null || !isActiveAndEnabled
            || deltaTime <= 0f || float.IsNaN(deltaTime) || float.IsInfinity(deltaTime))
            return;

        UpdateGridSweep(deltaTime);
        sampleAccumulator += (double)deltaTime * pixelsPerSecond;

        double requestedSteps = Math.Floor(sampleAccumulator + BeatTimeEpsilon);
        if (requestedSteps < 1d)
            return;

        sampleAccumulator = Math.Max(0d, sampleAccumulator - requestedSteps);
        int steps = (int)Math.Min(requestedSteps, samples.Length);
        double sampleStep = 1d / pixelsPerSecond;

        // Advance offscreen time too, without shifting pixels that will be discarded.
        if (requestedSteps > steps)
            AdvanceBeatTime((requestedSteps - steps) * sampleStep);

        Array.Copy(samples, steps, samples, 0, samples.Length - steps);
        for (int i = samples.Length - steps; i < samples.Length; i++)
        {
            bool isBeatStart = AdvanceBeatTime(sampleStep);
            samples[i] = EncodeSample(
                EvaluateCurrentSample(), isCurrentBeatActive, isBeatStart);
        }

        // Only the latest beat can sound after a hitch; never replay missed beats in a burst.
        TryPlayCurrentBeatSfx();
        UploadSamples();
    }

    void OnDisable()
    {
        hasPendingActiveBeat = false;
    }

    void OnDestroy()
    {
        if (image != null && image.material == runtimeMaterial)
            image.material = sourceMaterial;

        DestroyRuntimeObject(runtimeMaterial);
        DestroyRuntimeObject(waveTexture);
    }

    void OnValidate()
    {
        virtualWidth = Mathf.Max(16, virtualWidth);
        virtualHeight = Mathf.Max(8, virtualHeight);
        pixelsPerSecond = Mathf.Max(1f, pixelsPerSecond);
        naturalBeatInterval = Mathf.Max(MinimumBeatSpacing, naturalBeatInterval);
        gridSweepDuration = Mathf.Max(0.1f, gridSweepDuration);

        float minimumInterval = Mathf.Max(0.1f, gridSweepIntervalRange.x);
        float maximumInterval = Mathf.Max(minimumInterval, gridSweepIntervalRange.y);
        gridSweepIntervalRange = new Vector2(minimumInterval, maximumInterval);
    }

    [ContextMenu("Generate Active Beat")]
    public void HighlightNextBeat()
    {
        GenerateActiveBeat();
    }

    public void GenerateActiveBeat()
    {
        QueueBeat(true);
    }

    public void GenerateNaturalBeat()
    {
        QueueBeat(false);
    }

    float storyBeatStrength = -1f;
    /// <summary>A scripted audible beat starts now without clearing the waveform history.</summary>
    public void GenerateStoryBeat(float strength)
    {
        if (waveTexture == null || !isActiveAndEnabled) return;
        BeginBeat(true);
        storyBeatStrength = Mathf.Clamp(strength, .05f, 2f);
        currentBeatSfxPlayed = true;
        if (!IsFinalHealEnding)
            AudioManager.Instance?.PlaySfx(HeartbeatSfxKeys[0], storyBeatStrength);
    }

    void QueueBeat(bool isActive)
    {
        if (waveTexture == null || !isActiveAndEnabled)
            return;

        // Natural beats are automatic. Requests never interrupt a beat or erase an active request.
        hasPendingActiveBeat |= isActive;
    }

    public void SetNaturalBeatInterval(float seconds)
    {
        if (float.IsNaN(seconds) || float.IsInfinity(seconds))
            return;

        naturalBeatInterval = Mathf.Max(MinimumBeatSpacing, seconds);
    }

    public void SetBeatSfxEnabled(bool isEnabled)
    {
        beatSfxEnabled = isEnabled;
    }

    /// <summary>Stop panel heartbeat tails without touching the treatment's HeartBeat_ForHeal or other effects.</summary>
    public void StopCurrentBeatSounds()
    {
        var audio = AudioManager.Instance;
        var sounds = GameDataManager.Instance?.AudioData?.SFX;
        if (audio == null || sounds == null) return;
        foreach (string key in HeartbeatSfxKeys)
        {
            var clip = sounds.Find(item => item != null && item.keyid == key)?.audioclip;
            if (clip == null) continue;
            foreach (var source in audio.SFXPool)
                if (source != null && source.clip == clip) source.Stop();
        }
    }


    void Initialize()
    {
        image = GetComponent<Image>();
        sourceMaterial = image.material;

        if (sourceMaterial == null
            || !sourceMaterial.HasProperty(WaveTextureId)
            || !sourceMaterial.HasProperty(GridSweepCenterId))
        {
            Debug.LogError("[HeartbeatWaveformUI] A material using MrTGame/UI/PixelHeartbeat is required.", this);
            enabled = false;
            return;
        }

        int seed = randomSeed != 0
            ? randomSeed
            : Guid.NewGuid().GetHashCode();
        styleRandom = new System.Random(seed);
        sfxRandom = new System.Random(unchecked(seed * 16777619 + 31));
        sweepRandom = new System.Random(unchecked(seed * 486187739 + 17));

        waveTexture = new Texture2D(
            virtualWidth,
            1,
            TextureFormat.RGBA32,
            false,
            true)
        {
            name = $"{name}_HeartbeatData",
            filterMode = FilterMode.Point,
            wrapMode = TextureWrapMode.Clamp,
            hideFlags = HideFlags.HideAndDontSave,
        };

        runtimeMaterial = new Material(sourceMaterial)
        {
            name = $"{sourceMaterial.name} (Runtime)",
            hideFlags = HideFlags.HideAndDontSave,
        };
        runtimeMaterial.SetTexture(WaveTextureId, waveTexture);
        runtimeMaterial.SetVector(
            VirtualSizeId,
            new Vector4(virtualWidth, virtualHeight, 0f, 0f));
        runtimeMaterial.SetFloat(GridSweepCenterId, GridSweepInactiveCenter);
        runtimeMaterial.SetColor(
            HighlightTransitionColorId,
            CreateTransitionColor(runtimeMaterial.GetColor(HighlightCoreColorId)));
        image.material = runtimeMaterial;

        samples = new Color32[virtualWidth];
        FillInitialHistory();
        UploadSamples();
        ScheduleNextGridSweep();
    }

    void FillInitialHistory()
    {
        double sampleStep = 1d / pixelsPerSecond;
        sampleAccumulator = 0d;
        hasPendingActiveBeat = false;
        currentStyleIndex = -1;
        BeginBeat(false);

        for (int i = 0; i < samples.Length; i++)
        {
            bool isBeatStart = i == 0 || AdvanceBeatTime(sampleStep);
            samples[i] = EncodeSample(
                EvaluateCurrentSample(), false, isBeatStart);
        }

        currentBeatSfxPlayed = HasReachedCurrentBeatPeak();
    }

    void TryPlayCurrentBeatSfx()
    {
        if (currentBeatSfxPlayed || !HasReachedCurrentBeatPeak())
            return;

        currentBeatSfxPlayed = true;
        PlayCurrentBeatSfx();
    }

    bool HasReachedCurrentBeatPeak()
    {
        if (currentStyleIndex < 0)
            return false;

        double rPeakPhase = (RPeakSampleIndex + Styles[currentStyleIndex].QrsOffset) / 40d;
        return currentBeatSampleTime + BeatTimeEpsilon >= currentBeatDuration * rPeakPhase;
    }

    void PlayCurrentBeatSfx()
    {
        if (OS_Handle.Instance?.Chapter01Ending?.IsPresenting == true || IsFinalHealEnding) return;
        if (!beatSfxEnabled || sfxRandom == null)
            return;

        int sfxIndex = sfxRandom.Next(HeartbeatSfxKeys.Length);
        float volumeScale = isCurrentBeatActive
            ? 1f
            : NormalBeatSfxVolumeScale;
        AudioManager.Instance?.PlaySfx(HeartbeatSfxKeys[sfxIndex], volumeScale);
    }


    float EvaluateCurrentSample()
    {
        float phase = (float)(currentBeatSampleTime / currentBeatDuration);
        float sample = EvaluateStyle(Styles[currentStyleIndex], phase);
        float amplitudeScale = isCurrentBeatActive
            ? 1f
            : NormalBeatAmplitudeScale;
        if (storyBeatStrength >= 0f) amplitudeScale *= storyBeatStrength;
        return WaveBaseline + (sample - WaveBaseline) * amplitudeScale;
    }

    bool AdvanceBeatTime(double deltaTime)
    {
        currentBeatSampleTime += deltaTime;
        bool isBeatStart = false;
        while (currentBeatSampleTime + BeatTimeEpsilon >= currentBeatDuration)
        {
            double remainder = Math.Max(0d, currentBeatSampleTime - currentBeatDuration);
            BeginBeat(hasPendingActiveBeat);
            hasPendingActiveBeat = false;
            currentBeatSampleTime = remainder;
            isBeatStart = true;
        }

        return isBeatStart;
    }

    void BeginBeat(bool isActive)
    {
        storyBeatStrength = -1f;
        SelectNextStyle();
        isCurrentBeatActive = isActive;
        currentBeatSfxPlayed = false;
        currentBeatSampleTime = 0d;
        currentBeatDuration = naturalBeatInterval;
    }

    void SelectNextStyle()
    {
        int nextStyle = styleRandom.Next(WaveformStyleCount);
        if (avoidImmediateRepeat && nextStyle == currentStyleIndex)
        {
            int offset = styleRandom.Next(1, WaveformStyleCount);
            nextStyle = (nextStyle + offset) % WaveformStyleCount;
        }

        currentStyleIndex = nextStyle;
    }

    Color32 EncodeSample(
        float normalizedHeight,
        bool isHighlighted,
        bool isBeatStart)
    {
        int pixelY = Mathf.RoundToInt(
            Mathf.Clamp01(normalizedHeight) * (virtualHeight - 1));
        byte heightValue = (byte)Mathf.RoundToInt(
            pixelY * 255f / Mathf.Max(virtualHeight - 1, 1));
        byte highlightValue = isHighlighted ? byte.MaxValue : byte.MinValue;
        byte beatStartValue = isBeatStart ? byte.MaxValue : byte.MinValue;
        return new Color32(heightValue, highlightValue, beatStartValue, 255);
    }

    void UploadSamples()
    {
        waveTexture.SetPixels32(samples);
        waveTexture.Apply(false, false);
    }

    void UpdateGridSweep(float deltaTime)
    {
        if (!isGridSweepActive)
        {
            gridSweepTimer -= deltaTime;
            if (gridSweepTimer > 0f)
                return;

            isGridSweepActive = true;
            gridSweepProgress = 0f;
        }

        gridSweepProgress = Mathf.Min(
            gridSweepProgress + deltaTime / gridSweepDuration,
            1f);
        runtimeMaterial.SetFloat(
            GridSweepCenterId,
            Mathf.Lerp(-0.32f, 1.32f, gridSweepProgress));

        if (gridSweepProgress < 1f)
            return;

        isGridSweepActive = false;
        runtimeMaterial.SetFloat(GridSweepCenterId, GridSweepInactiveCenter);
        ScheduleNextGridSweep();
    }

    void ScheduleNextGridSweep()
    {
        float intervalLerp = (float)sweepRandom.NextDouble();
        gridSweepTimer = Mathf.Lerp(
            gridSweepIntervalRange.x,
            gridSweepIntervalRange.y,
            intervalLerp);
    }

    static float EvaluateStyle(WaveStyle style, float phase)
    {
        const float baseline = WaveBaseline;
        const float canonicalSampleCount = 40f;

        const float pStart = 3f / canonicalSampleCount;
        const float pPeak = 6f / canonicalSampleCount;
        const float pEnd = 9f / canonicalSampleCount;

        float rTime = (RPeakSampleIndex + style.QrsOffset) / canonicalSampleCount;
        float qTime = rTime - style.QrsHalfSpan / canonicalSampleCount;
        float qStart = qTime - 1f / canonicalSampleCount;
        float sTime = rTime + style.QrsHalfSpan / canonicalSampleCount;
        float qrsEnd = sTime + 2f / canonicalSampleCount;

        float tPeak = (23f + style.TOffset) / canonicalSampleCount;
        float tHalfWidth = style.THalfWidth / canonicalSampleCount;
        float tStart = tPeak - tHalfWidth;
        float tEnd = tPeak + tHalfWidth;

        if (phase < pStart)
            return baseline;
        if (phase < pPeak)
            return Ramp(phase, pStart, pPeak, baseline, baseline + style.PAmplitude);
        if (phase < pEnd)
            return Ramp(phase, pPeak, pEnd, baseline + style.PAmplitude, baseline);
        if (phase < qStart)
            return baseline;
        if (phase < qTime)
            return Ramp(phase, qStart, qTime, baseline, baseline + style.QAmplitude);
        if (phase < rTime)
            return Ramp(phase, qTime, rTime, baseline + style.QAmplitude, baseline + style.RAmplitude);
        if (phase < sTime)
            return Ramp(phase, rTime, sTime, baseline + style.RAmplitude, baseline + style.SAmplitude);
        if (phase < qrsEnd)
            return Ramp(phase, sTime, qrsEnd, baseline + style.SAmplitude, baseline);
        if (phase < tStart)
            return baseline;
        if (phase < tPeak)
            return Ramp(phase, tStart, tPeak, baseline, baseline + style.TAmplitude);
        if (phase < tEnd)
            return Ramp(phase, tPeak, tEnd, baseline + style.TAmplitude, baseline);

        return baseline;
    }

    static float Ramp(
        float phase,
        float startPhase,
        float endPhase,
        float startValue,
        float endValue)
    {
        float progress = Mathf.InverseLerp(startPhase, endPhase, phase);
        return Mathf.Lerp(startValue, endValue, progress);
    }

    static Color CreateTransitionColor(Color sourceColor)
    {
        Color.RGBToHSV(sourceColor, out float hue, out float saturation, out float value);
        saturation = Mathf.Clamp01(saturation + TransitionSaturationOffset);
        value *= 1f - TransitionGrayAmount;

        Color transitionColor = Color.HSVToRGB(hue, saturation, value, true);
        transitionColor.a = sourceColor.a;
        return transitionColor;
    }

    static void DestroyRuntimeObject(UnityEngine.Object target)
    {
        if (target == null)
            return;

        if (Application.isPlaying)
            Destroy(target);
        else
            DestroyImmediate(target);
    }

    readonly struct WaveStyle
    {
        public readonly float PAmplitude;
        public readonly float QAmplitude;
        public readonly float RAmplitude;
        public readonly float SAmplitude;
        public readonly float TAmplitude;
        public readonly int QrsOffset;
        public readonly int QrsHalfSpan;
        public readonly int TOffset;
        public readonly int THalfWidth;

        public WaveStyle(
            float pAmplitude,
            float qAmplitude,
            float rAmplitude,
            float sAmplitude,
            float tAmplitude,
            int qrsOffset,
            int qrsHalfSpan,
            int tOffset,
            int tHalfWidth)
        {
            PAmplitude = pAmplitude;
            QAmplitude = qAmplitude;
            RAmplitude = rAmplitude;
            SAmplitude = sAmplitude;
            TAmplitude = tAmplitude;
            QrsOffset = qrsOffset;
            QrsHalfSpan = qrsHalfSpan;
            TOffset = tOffset;
            THalfWidth = tHalfWidth;
        }
    }
}

#if UNITY_EDITOR
[UnityEditor.CustomEditor(typeof(HeartbeatWaveformUI))]
sealed class HeartbeatWaveformUIEditor : UnityEditor.Editor
{
    UnityEditor.Editor runtimeMaterialEditor;
    bool showRuntimeMaterial = true;

    void OnDisable()
    {
        if (runtimeMaterialEditor != null)
            UnityEngine.Object.DestroyImmediate(runtimeMaterialEditor);
    }

    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();
        UnityEditor.EditorGUILayout.Space();

        HeartbeatWaveformUI waveform = (HeartbeatWaveformUI)target;
        using (new UnityEditor.EditorGUI.DisabledScope(!Application.isPlaying))
        {
            if (GUILayout.Button("Test Active Beat"))
                waveform.HighlightNextBeat();
        }

        DrawRuntimeMaterial(waveform);
    }

    void DrawRuntimeMaterial(HeartbeatWaveformUI waveform)
    {
        UnityEditor.EditorGUILayout.Space();
        showRuntimeMaterial = UnityEditor.EditorGUILayout.Foldout(
            showRuntimeMaterial,
            "Runtime Material (Live)",
            true);

        if (!showRuntimeMaterial)
            return;

        Material material = waveform.RuntimeMaterial;
        if (!Application.isPlaying || material == null)
        {
            UnityEditor.EditorGUILayout.HelpBox(
                "Enter Play Mode to edit the material instance used by the waveform.",
                UnityEditor.MessageType.Info);
            return;
        }

        using (new UnityEditor.EditorGUILayout.VerticalScope(UnityEditor.EditorStyles.helpBox))
        {
            using (new UnityEditor.EditorGUI.DisabledScope(true))
            {
                UnityEditor.EditorGUILayout.ObjectField(
                    "Active Material",
                    material,
                    typeof(Material),
                    false);
            }

            if (GUILayout.Button("Select Runtime Material"))
                UnityEditor.Selection.activeObject = material;

            UnityEditor.Editor.CreateCachedEditor(
                material,
                typeof(UnityEditor.MaterialEditor),
                ref runtimeMaterialEditor);
            runtimeMaterialEditor.OnInspectorGUI();
        }
    }
}
#endif
