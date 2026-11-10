using TMPro;
using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(TextMeshProUGUI))]
public sealed class OSNoSignalGlitch : MonoBehaviour
{
    const string ShaderName = "TextMeshPro/OS/No Signal Glitch";
    const string DefaultMessage = "\u65e0\u4fe1\u53f7";
    const string DefaultScrambleCharacters = "#!?%/\\[]{}01";

    static readonly int GlitchTimeId = Shader.PropertyToID("_GlitchTime");
    static readonly int GlitchIntensityId = Shader.PropertyToID("_GlitchIntensity");
    static readonly int GlitchPixelSizeId = Shader.PropertyToID("_GlitchPixelSize");
    static readonly int GlitchBandHeightId = Shader.PropertyToID("_GlitchBandHeight");
    static readonly int GlitchOffsetId = Shader.PropertyToID("_GlitchOffsetPixels");
    static readonly int GlitchDropoutId = Shader.PropertyToID("_GlitchDropout");
    static readonly int GlitchSeedId = Shader.PropertyToID("_GlitchSeed");
    static readonly int ReferenceResolutionId =
        Shader.PropertyToID("_GlitchReferenceResolution");

    [SerializeField] TextMeshProUGUI targetText;

    [Header("Pixel Glitch")]
    [SerializeField, Range(0f, 1f)] float intensity = 0.72f;
    [SerializeField, Range(1f, 4f)] float pixelSize = 1f;
    [SerializeField, Range(1f, 8f)] float bandHeight = 3f;
    [SerializeField, Range(0f, 8f)] float maximumOffsetPixels = 4f;
    [SerializeField, Range(0f, 0.4f)] float dropoutAmount = 0.14f;
    [SerializeField] float randomSeed = 13.37f;
    [SerializeField] Vector2 referenceResolution = new Vector2(640f, 360f);

    [Header("Text Scramble")]
    [SerializeField] bool scrambleText;
    [SerializeField, Min(1)] int scrambleCharacterCount = 5;
    [SerializeField, Min(0.03f)] float scrambleInterval = 0.09f;
    [SerializeField] string scrambleCharacters =
        DefaultScrambleCharacters;

    Material originalSharedMaterial;
    Material runtimeMaterial;
    float effectStartTime;
    float nextScrambleTime;
    uint scrambleState;
    bool missingShaderWasLogged;

    public TextMeshProUGUI TargetText => targetText;
    public float Intensity => intensity;

    void Awake()
    {
        ResolveReferences();
        EnsureDefaultMessage();
    }

    void OnEnable()
    {
        ResolveReferences();
        EnsureDefaultMessage();
        effectStartTime = Time.unscaledTime;
        RestartScramble();
        CreateRuntimeMaterial();
        ApplyMaterialProperties(0f);
    }

    void LateUpdate()
    {
        float currentTime = Time.unscaledTime;
        if (runtimeMaterial != null)
            ApplyMaterialProperties(currentTime - effectStartTime);
        UpdateScrambledText(currentTime, false);
    }

    void OnDisable()
    {
        ReleaseRuntimeMaterial();
    }

    void OnDestroy()
    {
        ReleaseRuntimeMaterial();
    }

    public void SetIntensity(float value)
    {
        intensity = Mathf.Clamp01(value);
        ApplyMaterialProperties(Time.unscaledTime - effectStartTime);
    }

    public void RestartEffect()
    {
        effectStartTime = Time.unscaledTime;
        ApplyMaterialProperties(0f);
        RestartScramble();
    }

    public void ConfigureScrambledText(
        bool enabled,
        int characterCount = 5,
        float interval = 0.09f)
    {
        scrambleText = enabled;
        scrambleCharacterCount = Mathf.Max(1, characterCount);
        scrambleInterval = Mathf.Max(0.03f, interval);
        RestartScramble();
    }

    void ResolveReferences()
    {
        if (targetText == null)
            targetText = GetComponent<TextMeshProUGUI>();
    }

    void EnsureDefaultMessage()
    {
        if (targetText != null && string.IsNullOrEmpty(targetText.text))
            targetText.text = DefaultMessage;
    }

    void RestartScramble()
    {
        scrambleState = unchecked(
            (uint)GetEntityId().GetHashCode() * 747796405u
            + (uint)(Time.unscaledTime * 1000f)
            + 2891336453u);
        nextScrambleTime = Time.unscaledTime;
        UpdateScrambledText(nextScrambleTime, true);
    }

    void UpdateScrambledText(float currentTime, bool force)
    {
        if (!scrambleText
            || targetText == null
            || (!force && currentTime < nextScrambleTime))
        {
            return;
        }

        string characters = string.IsNullOrEmpty(scrambleCharacters)
            ? DefaultScrambleCharacters
            : scrambleCharacters;
        char[] buffer = new char[Mathf.Max(1, scrambleCharacterCount)];
        for (int i = 0; i < buffer.Length; i++)
        {
            scrambleState = scrambleState * 1664525u + 1013904223u;
            buffer[i] = characters[(int)(scrambleState % characters.Length)];
        }

        targetText.text = new string(buffer);
        nextScrambleTime = currentTime + Mathf.Max(0.03f, scrambleInterval);
    }

    void CreateRuntimeMaterial()
    {
        if (runtimeMaterial != null || targetText == null)
            return;

        Shader glitchShader = Shader.Find(ShaderName);
        if (glitchShader == null)
        {
            if (!missingShaderWasLogged)
            {
                Debug.LogError(
                    $"Required shader '{ShaderName}' was not found.",
                    this);
                missingShaderWasLogged = true;
            }
            return;
        }

        Material source = targetText.fontSharedMaterial;
        if (source == null)
            return;

        originalSharedMaterial = source;
        runtimeMaterial = new Material(glitchShader)
        {
            name = $"{source.name} (No Signal Runtime)",
            hideFlags = HideFlags.HideAndDontSave,
            renderQueue = source.renderQueue,
            shaderKeywords = source.shaderKeywords
        };
        runtimeMaterial.CopyPropertiesFromMaterial(source);
        targetText.fontSharedMaterial = runtimeMaterial;
        targetText.SetMaterialDirty();
    }

    void ApplyMaterialProperties(float elapsedTime)
    {
        if (runtimeMaterial == null)
            return;

        SetProperties(runtimeMaterial, elapsedTime);

        if (targetText == null || !targetText.isActiveAndEnabled)
            return;

        Material renderingMaterial = targetText.materialForRendering;
        if (renderingMaterial != null
            && renderingMaterial != runtimeMaterial
            && renderingMaterial.shader != null
            && renderingMaterial.shader.name == ShaderName)
        {
            SetProperties(renderingMaterial, elapsedTime);
        }
    }

    void SetProperties(Material material, float elapsedTime)
    {
        material.SetFloat(GlitchTimeId, Mathf.Max(0f, elapsedTime));
        material.SetFloat(GlitchIntensityId, intensity);
        material.SetFloat(GlitchPixelSizeId, Mathf.Max(1f, pixelSize));
        material.SetFloat(GlitchBandHeightId, Mathf.Max(1f, bandHeight));
        material.SetFloat(
            GlitchOffsetId,
            Mathf.Max(0f, maximumOffsetPixels));
        material.SetFloat(GlitchDropoutId, Mathf.Clamp01(dropoutAmount));
        material.SetFloat(GlitchSeedId, randomSeed);
        material.SetVector(
            ReferenceResolutionId,
            new Vector4(
                Mathf.Max(1f, referenceResolution.x),
                Mathf.Max(1f, referenceResolution.y),
                0f,
                0f));
    }

    void ReleaseRuntimeMaterial()
    {
        if (runtimeMaterial == null)
            return;

        if (targetText != null
            && targetText.fontSharedMaterial == runtimeMaterial)
        {
            targetText.fontSharedMaterial = originalSharedMaterial;
            targetText.SetMaterialDirty();
        }

        Material materialToDestroy = runtimeMaterial;
        runtimeMaterial = null;
        originalSharedMaterial = null;

        if (Application.isPlaying)
            Destroy(materialToDestroy);
        else
            DestroyImmediate(materialToDestroy);
    }

    void Reset()
    {
        ResolveReferences();
        EnsureDefaultMessage();
    }

    void OnValidate()
    {
        ResolveReferences();
        intensity = Mathf.Clamp01(intensity);
        pixelSize = Mathf.Clamp(pixelSize, 1f, 4f);
        bandHeight = Mathf.Clamp(bandHeight, 1f, 8f);
        maximumOffsetPixels = Mathf.Clamp(maximumOffsetPixels, 0f, 8f);
        dropoutAmount = Mathf.Clamp(dropoutAmount, 0f, 0.4f);
        scrambleCharacterCount = Mathf.Max(1, scrambleCharacterCount);
        scrambleInterval = Mathf.Max(0.03f, scrambleInterval);
        referenceResolution.x = Mathf.Max(1f, referenceResolution.x);
        referenceResolution.y = Mathf.Max(1f, referenceResolution.y);

        if (Application.isPlaying && isActiveAndEnabled)
        {
            ApplyMaterialProperties(Time.unscaledTime - effectStartTime);
        }
    }
}
