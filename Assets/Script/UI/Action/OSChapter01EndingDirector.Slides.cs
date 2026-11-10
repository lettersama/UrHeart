using System.Collections;
using UnityEngine;
using UnityEngine.Sprites;

public sealed partial class OSChapter01EndingDirector
{
    Material slideTreatment, originalSlideMaterial;
    float slideClock;
    bool slideTreatmentBound;

    IEnumerator PlaySlide(int index)
    {
        var slide = configuration.slides != null && index < configuration.slides.Length
            ? configuration.slides[index] : null;
        ShowSlide(index);
        PrepareSlidePresentation();
        StartSlideAudio(index);
        float duration = Mathf.Max(.1f, slide?.holdSeconds ?? 2.2f);
        float elapsed = 0f;
        // Restoring a shot restarts its visual beat, preserving its saved checkpoint.
        slideClock = 0f;
        for (int i = 0; i < index; i++)
            slideClock += Mathf.Max(.1f, configuration.slides != null && i < configuration.slides.Length
                ? configuration.slides[i]?.holdSeconds ?? 2.2f : 2.2f);
        while (elapsed < duration)
        {
            float progress = Mathf.Clamp01(elapsed / duration);
            UpdateSlideAudio(index, progress);
            if (slide != null)
            {
                SetSlideSprite(GetSlideFrame(slide, progress));
                UpdateSlideTreatment(slide, index, progress);
                UpdateSlideOsd(slide, slideImage.sprite, elapsed);
                float fadeIn = Mathf.Min(slide.fadeInSeconds, duration * .45f);
                float fadeOut = Mathf.Min(slide.fadeOutSeconds, duration * .45f);
                float alphaIn = fadeIn > 0 ? 1f - Mathf.SmoothStep(0, 1, elapsed / fadeIn) : 0;
                float alphaOut = fadeOut > 0 ? Mathf.SmoothStep(0, 1, (elapsed - duration + fadeOut) / fadeOut) : 0;
                SetBlack(Mathf.Max(alphaIn, alphaOut));
            }
            else SetBlack(0);
            yield return null;
            elapsed += Time.unscaledDeltaTime;
            slideClock += Time.unscaledDeltaTime;
        }
        if (slide != null)
        {
            SetSlideSprite(GetSlideFrame(slide, 1f));
            UpdateSlideTreatment(slide, index, 1f);
            UpdateSlideOsd(slide, slideImage.sprite, duration);
            SetBlack(slide.fadeOutSeconds > 0 ? 1f : 0f);
        }
    }

    static Sprite GetSlideFrame(OSChapter01EndingData.Slide slide, float progress)
    {
        if (slide == null) return null;
        var frames = slide.frames;
        if (frames == null || frames.Length == 0) return slide.image;
        int target = Mathf.Min(frames.Length - 1,
            Mathf.FloorToInt(Mathf.Clamp01(progress / Mathf.Max(.1f, slide.framePlaybackFraction)) * frames.Length));
        for (int i = target; i >= 0; i--) if (frames[i] != null) return frames[i];
        return slide.image;
    }

    void SetSlideSprite(Sprite sprite)
    {
        if (slideImage.sprite != sprite) slideImage.sprite = sprite;
        if (slideTreatment == null || sprite == null) return;
        Vector4 uv = DataUtility.GetOuterUV(sprite);
        slideTreatment.SetVector("_UvRect", new Vector4(uv.x, uv.y, uv.z - uv.x, uv.w - uv.y));
    }

    void PrepareSlidePresentation()
    {
        if (!slideTreatmentBound && slideImage != null && configuration.slideShader != null)
        {
            originalSlideMaterial = slideImage.material;
            slideTreatment = new Material(configuration.slideShader) { name = "Chapter01Slides (runtime)" };
            slideImage.material = slideTreatment;
            slideTreatmentBound = true;
            SetSlideSprite(slideImage.sprite);
        }
        // Deliberately silent. The door click owns the next sound.
        // Legacy ambience/foley data is retained for compatibility, never played.
    }

    void UpdateSlideTreatment(OSChapter01EndingData.Slide slide, int index, float progress)
    {
        if (slideTreatment == null) return;
        float p = Mathf.Clamp01(progress);
        float smooth = Mathf.SmoothStep(0f, 1f, p);
        float exposure = slide.exposure + slide.exposurePulse * Mathf.Sin(p * Mathf.PI);
        float light = slide.lightStrength;
        float radius = .68f, pressure = .4f, edge = .12f, shutterIn = .12f, shutterOut = .12f;
        var focus = new Vector2(.58f, .55f);

        // Locked source pixels. Shaped darkness directs the eye through the six shots:
        // workbench, turn, departure, abandoned light, dark door, final hesitation.
        switch (index)
        {
            case 0:
                focus = new Vector2(.67f, .57f);
                radius = Mathf.Lerp(.82f, .6f, smooth);
                pressure = Mathf.Lerp(.32f, .5f, smooth);
                light *= Mathf.Lerp(1.5f, 1f, smooth);
                shutterIn = .4f; shutterOut = .14f;
                break;
            case 1:
                focus = new Vector2(.59f, .51f);
                radius = Mathf.Lerp(.64f, .5f, smooth);
                pressure = .57f;
                exposure *= 1f - .12f * VisualBreath(p, .64f, .15f);
                shutterIn = .11f; shutterOut = .10f;
                break;
            case 2:
                focus = new Vector2(.5f, .39f);
                radius = Mathf.Lerp(.6f, .41f, smooth);
                pressure = .66f;
                edge = .18f;
                shutterIn = .085f; shutterOut = .075f;
                break;
            case 3:
                focus = new Vector2(.71f, .62f);
                radius = Mathf.Lerp(.72f, .23f, smooth);
                pressure = Mathf.Lerp(.4f, .87f, smooth);
                light *= 1f - Mathf.SmoothStep(0f, 1f, p / .65f);
                exposure *= Mathf.Lerp(1f, .68f, smooth);
                edge = Mathf.Lerp(.14f, .34f, smooth);
                shutterIn = .075f; shutterOut = .24f;
                break;
            case 4:
                // This photograph is nearly black. Its independently authored exposure
                // lets the structure emerge without introducing any white flash.
                focus = new Vector2(.49f, .53f);
                radius = Mathf.Lerp(.8f, .52f, smooth);
                pressure = .28f;
                edge = .18f;
                shutterIn = .4f; shutterOut = .22f;
                break;
            default:
                focus = new Vector2(.39f, .6f);
                // Three tightening breaths; the final 0.65 s holds completely still.
                float settle = Mathf.SmoothStep(0f, 1f, p / .875f);
                float breath = .12f * VisualBreath(p, .21f, .13f)
                    + .19f * VisualBreath(p, .49f, .1f)
                    + .27f * VisualBreath(p, .71f, .075f);
                radius = Mathf.Lerp(.73f, .43f, settle) - breath * .18f;
                pressure = Mathf.Lerp(.32f, .61f, settle) + breath * .4f;
                exposure *= (1f - breath) * Mathf.Lerp(1f, .9f, settle);
                edge = Mathf.Lerp(.1f, .24f, settle);
                shutterIn = .27f; shutterOut = 0f;
                break;
        }

        float duration = Mathf.Max(.1f, slide.holdSeconds);
        shutterIn = Mathf.Min(shutterIn, duration * .35f);
        shutterOut = Mathf.Min(shutterOut, duration * .35f);
        float opening = shutterIn > 0f ? 1f - Mathf.SmoothStep(0f, 1f, p * duration / shutterIn) : 0f;
        float closing = shutterOut > 0f ? Mathf.SmoothStep(0f, 1f, (p * duration - duration + shutterOut) / shutterOut) : 0f;
        slideTreatment.SetFloat("_Phase", slideClock);
        slideTreatment.SetFloat("_Exposure", exposure);
        slideTreatment.SetFloat("_LightGain", light);
        slideTreatment.SetVector("_LightCenter", new Vector4(slide.lightCenter.x, slide.lightCenter.y, 0, 0));
        slideTreatment.SetFloat("_LightRadius", slide.lightRadius);
        slideTreatment.SetVector("_FocusCenter", new Vector4(focus.x, focus.y, 0, 0));
        slideTreatment.SetFloat("_FocusRadius", radius);
        slideTreatment.SetFloat("_FocusStrength", pressure);
        slideTreatment.SetFloat("_EdgeShade", edge);
        slideTreatment.SetFloat("_Shutter", Mathf.Max(opening, closing));
        slideTreatment.SetFloat("_ShutterTilt", index == 4 ? -.16f : .12f);
    }

    static float VisualBreath(float progress, float center, float halfWidth)
    {
        float t = Mathf.Clamp01(1f - Mathf.Abs(progress - center) / halfWidth);
        return t * t * (3f - 2f * t);
    }

    void HoldFinalSlide()
    {
        int index = SlideCount - 1;
        ShowSlide(index);
        PrepareSlidePresentation();
        var slide = configuration.slides != null && index < configuration.slides.Length ? configuration.slides[index] : null;
        if (slide != null)
        {
            SetSlideSprite(GetSlideFrame(slide, 1f));
            UpdateSlideTreatment(slide, index, 1f);
            UpdateSlideOsd(slide, slideImage.sprite, slide.holdSeconds, false);
        }
        // Picture, shading, shader clock and camera clock stay still until the player clicks.
    }

    void ReleaseSlidePresentation()
    {
        if (slideTreatmentBound && slideImage != null) slideImage.material = originalSlideMaterial;
        slideTreatmentBound = false;
        originalSlideMaterial = null;
        if (slideTreatment != null) Destroy(slideTreatment);
        slideTreatment = null;
        ReleaseSlideOsd();
        slideClock = 0;
    }
}
