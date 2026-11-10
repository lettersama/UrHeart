using UnityEngine;
using UnityEngine.UI;

/// <summary>Transient portrait details. The sprite and its animation stay untouched.</summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(900)]
public sealed class OSChapter03PortraitEffects : MonoBehaviour
{
    const float HairclipSeconds = .45f;
    static readonly Vector2Int[] ShakeSteps =
    {
        new Vector2Int(-1, 0), new Vector2Int(1, 1), new Vector2Int(-1, -1),
        new Vector2Int(1, 0), new Vector2Int(0, 1), new Vector2Int(0, -1)
    };

    OSCharacterWindowController owner;
    Image portrait;
    RectTransform target, glint;
    CanvasGroup glintOpacity;
    float pulseRemaining, shakeRemaining, shakeDuration;
    Vector2 appliedOffset, appliedPosition;

    public void Initialize(OSCharacterWindowController controller, Image image)
    {
        if (portrait != image) ResetEffects();
        owner = controller; portrait = image; target = image != null ? image.rectTransform : null;
    }

    public void PulseHairclip()
    {
        if (target == null || !isActiveAndEnabled) return;
        EnsureGlint(); pulseRemaining = HairclipSeconds;
    }

    public void Shake(float seconds)
    {
        if (target == null || !isActiveAndEnabled || float.IsNaN(seconds) || float.IsInfinity(seconds)) return;
        RemoveShakeOffset();
        shakeDuration = shakeRemaining = Mathf.Clamp(seconds, .05f, 1f);
    }

    public void ResetEffects()
    {
        RemoveShakeOffset();
        pulseRemaining = shakeRemaining = 0f;
        if (glint != null) glint.gameObject.SetActive(false);
    }

    void OnDisable() => ResetEffects();
    void OnDestroy()
    {
        ResetEffects();
        if (glint != null) Destroy(glint.gameObject);
    }

    void LateUpdate()
    {
        RemoveShakeOffset();
        if (pulseRemaining <= 0f && shakeRemaining <= 0f) return;
        var story = OS_Handle.Instance?.Chapter03Story;
        if (owner == null || !owner.isActiveAndEnabled || portrait == null
            || !portrait.isActiveAndEnabled || story == null || !story.IsActive)
        { ResetEffects(); return; }
        // Settings, focus loss and session restoration must not consume the beat.
        if (!story.IsAvailable)
        { if (glint != null) glint.gameObject.SetActive(false); return; }

        float dt = Mathf.Min(Time.unscaledDeltaTime, .1f);
        Rect drawing = DrawingRect();
        Vector2 sourceSize = portrait.sprite != null ? portrait.sprite.rect.size : new Vector2(125f, 175f);
        Vector2 pixel = new Vector2(drawing.width / Mathf.Max(1f, sourceSize.x),
            drawing.height / Mathf.Max(1f, sourceSize.y));
        if (pulseRemaining > 0f)
        {
            pulseRemaining = Mathf.Max(0f, pulseRemaining - dt);
            // Checked against StatusAnimations_Normal's 125 x 175 frames: the
            // hairclip sits at (77, 50) from the upper-left. The scene's initial
            // Normal_0 sprite is the same art cropped to 94 x 151 at (63, 27).
            Vector2 clip = sourceSize.x <= 100f
                ? new Vector2(63f / 94f, 1f - 27f / 151f)
                : new Vector2(77f / 125f, 1f - 50f / 175f);
            glint.anchorMin = glint.anchorMax = target.pivot;
            glint.anchoredPosition = drawing.min + Vector2.Scale(drawing.size, clip);
            glint.localScale = new Vector3(pixel.x, pixel.y, 1f);
            float age = HairclipSeconds - pulseRemaining;
            glintOpacity.alpha = Mathf.Min(Mathf.Clamp01(age / .045f), Mathf.Clamp01(pulseRemaining / .3f));
            glint.gameObject.SetActive(pulseRemaining > 0f);
        }
        if (shakeRemaining > 0f)
        {
            shakeRemaining = Mathf.Max(0f, shakeRemaining - dt);
            if (shakeRemaining <= 0f) return;
            int step = Mathf.FloorToInt((shakeDuration - shakeRemaining) / .035f) % ShakeSteps.Length;
            float strength = shakeRemaining > shakeDuration * .45f ? 2f : 1f;
            appliedOffset = Vector2.Scale((Vector2)ShakeSteps[step] * strength, pixel);
            appliedPosition = target.anchoredPosition + appliedOffset;
            target.anchoredPosition = appliedPosition;
        }
    }

    void RemoveShakeOffset()
    {
        // If another animation rewrote the transform, its value is already the
        // new base; do not subtract our previous frame's displacement from it.
        if (target != null && appliedOffset != Vector2.zero
            && (target.anchoredPosition - appliedPosition).sqrMagnitude < .0001f)
            target.anchoredPosition -= appliedOffset;
        appliedOffset = Vector2.zero;
    }

    Rect DrawingRect()
    {
        Rect rect = portrait.GetPixelAdjustedRect();
        if (!portrait.preserveAspect || portrait.sprite == null) return rect;
        Vector2 size = portrait.sprite.rect.size;
        if (size.x <= 0f || size.y <= 0f || rect.width <= 0f || rect.height <= 0f) return rect;
        float aspect = size.x / size.y;
        if (aspect > rect.width / rect.height)
        { float height = rect.width / aspect; rect.y += (rect.height - height) * target.pivot.y; rect.height = height; }
        else
        { float width = rect.height * aspect; rect.x += (rect.width - width) * target.pivot.x; rect.width = width; }
        return rect;
    }

    void EnsureGlint()
    {
        if (glint != null) return;
        var node = new GameObject("Chapter03HairclipGlint", typeof(RectTransform), typeof(CanvasGroup));
        node.layer = target.gameObject.layer;
        glint = (RectTransform)node.transform; glint.SetParent(target, false);
        glint.sizeDelta = new Vector2(9f, 9f);
        glintOpacity = node.GetComponent<CanvasGroup>();
        glintOpacity.interactable = glintOpacity.blocksRaycasts = false;
        GlintPart("Halo", new Vector2(5f, 3f), .25f);
        GlintPart("Horizontal", new Vector2(9f, 1f), .65f);
        GlintPart("Vertical", new Vector2(1f, 7f), .65f);
        GlintPart("Core", new Vector2(3f, 3f), 1f);
        node.SetActive(false);
    }

    void GlintPart(string name, Vector2 size, float opacity)
    {
        var node = new GameObject(name, typeof(RectTransform), typeof(Image));
        node.layer = target.gameObject.layer;
        var rect = (RectTransform)node.transform; rect.SetParent(glint, false);
        rect.sizeDelta = size;
        var image = node.GetComponent<Image>();
        image.color = new Color(1f, 1f, 1f, opacity); image.raycastTarget = false;
    }
}
