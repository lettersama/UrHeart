using DG.Tweening;
using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
[RequireComponent(typeof(Image))]
public sealed class OSGiftLandingDust : MonoBehaviour
{
    const string ShaderName = "UI/OS/GiftLandingDust";

    static readonly int ProgressId = Shader.PropertyToID("_Progress");
    static readonly int RectSizeId = Shader.PropertyToID("_RectSize");

    [SerializeField] Image targetImage;
    [SerializeField] Material materialTemplate;
    [SerializeField, Min(0.05f)] float duration = 0.56f;

    Material runtimeMaterial;
    Material originalMaterial;
    Tween progressTween;

    void Awake()
    {
        ResolveReferences();
        CreateRuntimeMaterial();
        ResetVisual();
    }

    void OnDisable()
    {
        KillTween();
        ResetVisual();
    }

    void OnDestroy()
    {
        KillTween();
        if (targetImage != null && targetImage.material == runtimeMaterial)
            targetImage.material = originalMaterial;
        if (runtimeMaterial != null)
            Destroy(runtimeMaterial);
    }

    public void Play()
    {
        ResolveReferences();
        CreateRuntimeMaterial();
        if (targetImage == null || runtimeMaterial == null)
            return;

        KillTween();
        UpdateRectSize();
        runtimeMaterial.SetFloat(ProgressId, 0f);
        targetImage.raycastTarget = false;
        targetImage.enabled = true;

        Tween tween = DOVirtual.Float(
                0f,
                1f,
                Mathf.Max(0.05f, duration),
                value => runtimeMaterial?.SetFloat(ProgressId, value))
            .SetEase(Ease.Linear)
            .SetUpdate(true)
            .SetLink(gameObject, LinkBehaviour.KillOnDisable);
        progressTween = tween;
        tween.OnComplete(() =>
            {
                if (progressTween != tween)
                    return;

                progressTween = null;
                if (targetImage != null)
                    targetImage.enabled = false;
            })
            .OnKill(() =>
            {
                if (progressTween == tween)
                    progressTween = null;
            });
    }

    public void ResetVisual()
    {
        KillTween();
        if (runtimeMaterial != null)
            runtimeMaterial.SetFloat(ProgressId, 0f);
        if (targetImage != null)
        {
            targetImage.raycastTarget = false;
            targetImage.enabled = false;
        }
    }

    void ResolveReferences()
    {
        if (targetImage == null)
            targetImage = GetComponent<Image>();
    }

    void CreateRuntimeMaterial()
    {
        if (runtimeMaterial != null || targetImage == null)
            return;

        originalMaterial = targetImage.material;
        Material source = materialTemplate;
        if (source == null
            && targetImage.material != null
            && targetImage.material.shader != null
            && targetImage.material.shader.name == ShaderName)
        {
            source = targetImage.material;
        }

        if (source != null)
            runtimeMaterial = new Material(source);
        else
        {
            Shader shader = Shader.Find(ShaderName);
            if (shader != null)
                runtimeMaterial = new Material(shader);
        }

        if (runtimeMaterial == null)
        {
            Debug.LogError("Gift landing dust shader is missing.", this);
            return;
        }

        runtimeMaterial.name = "M_OSGiftLandingDust (Runtime)";
        runtimeMaterial.hideFlags = HideFlags.HideAndDontSave;
        targetImage.material = runtimeMaterial;
        UpdateRectSize();
    }

    void UpdateRectSize()
    {
        if (runtimeMaterial == null || targetImage == null)
            return;

        Rect rect = targetImage.rectTransform.rect;
        runtimeMaterial.SetVector(
            RectSizeId,
            new Vector4(
                Mathf.Max(1f, rect.width),
                Mathf.Max(1f, rect.height),
                0f,
                0f));
    }

    void KillTween()
    {
        if (progressTween == null)
            return;

        Tween tween = progressTween;
        progressTween = null;
        tween.Kill(false);
    }
}
