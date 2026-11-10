using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Sprites;

// The authored sprite animation tears a little more each tick, starts to
// stutter, then sticks on its last frame. The Glitch phase screenshots that
// stuck frame and holds it still for the red cursor's repair.
public sealed partial class OSChapter01EndingDirector
{
    Material filmGlitch, filmGlitchOriginalMaterial;
    bool filmGlitchBound, filmGlitchStuck;

    bool HasAnimationFrames()
    {
        if (configuration.animationFrames == null) return false;
        foreach (var frame in configuration.animationFrames) if (frame != null) return true;
        return false;
    }

    IEnumerator PlayGlitchedFrames()
    {
        var steps = BuildFrameSchedule(out float total);
        BindFilmGlitch();
        StartFilmAudio();
        slideImage.gameObject.SetActive(true);
        float rate = Mathf.Clamp(configuration.animationGlitchTicksPerSecond, 1f, 30f);
        float clock = 0f, stepEnd = 0f, tickClock = 0f;
        foreach (var step in steps)
        {
            SetFilmFrame(step.frame);
            stepEnd += step.seconds;
            while (clock < stepEnd)
            {
                // Small flickers from the first frame, growing and speeding up:
                // the pattern changes at half the base rate at first, 1.7x by the end.
                float progress = Mathf.Clamp01(clock / total);
                float amount = .14f + .86f * Mathf.Pow(progress, 1.7f);
                SetFilmGlitch(amount, Mathf.Floor(tickClock));
                UpdateFilmAudio(amount, Mathf.Floor(tickClock));
                yield return null;
                clock += Time.unscaledDeltaTime;
                tickClock += Time.unscaledDeltaTime * rate * (.5f + 1.2f * Mathf.Pow(progress, 1.4f));
            }
        }

        float baseTick = Mathf.Floor(tickClock), stick = Mathf.Max(0f, configuration.animationStickSeconds);
        BeginStuckAudio();
        for (float elapsed = 0f; elapsed < stick; elapsed += Time.unscaledDeltaTime)
        {
            float tick = baseTick + Mathf.Floor(elapsed * rate * 2.5f);
            SetFilmGlitch(1f, tick);
            UpdateFilmAudio(1f, tick);
            yield return null;
        }
        // The tick stops here: the torn picture stays exactly as it is, in silence.
        SetFilmGlitch(1f, baseTick + Mathf.Floor(stick * rate * 2.5f));
        StopEndingAudio();
        filmGlitchStuck = true;
    }

    struct FilmStep { public Sprite frame; public float seconds; }

    List<FilmStep> BuildFrameSchedule(out float total)
    {
        var frames = new List<FilmStep>();
        float fallback = 1f / Mathf.Max(1f, configuration.animationFramesPerSecond);
        var authored = configuration.animationFrames;
        var timing = configuration.animationFrameSeconds;
        for (int loop = 0; loop < Mathf.Max(1, configuration.animationLoops); loop++)
            for (int i = 0; i < authored.Length; i++)
                if (authored[i] != null)
                    frames.Add(new FilmStep { frame = authored[i],
                        seconds = timing != null && i < timing.Length && timing[i] > 0f ? timing[i] : fallback });
        float authoredTotal = 0f;
        foreach (var frame in frames) authoredTotal += frame.seconds;

        // From halfway the playback hiccups: a frame holds too long, or the
        // previous frame flashes back for half a beat. Fixed per frame, not random per run.
        var steps = new List<FilmStep>(frames.Count + 8);
        total = 0f;
        for (int i = 0; i < frames.Count; i++)
        {
            var step = frames[i];
            float progress = Mathf.Clamp01(total / Mathf.Max(.01f, authoredTotal));
            if (progress > .5f && Hash(i * 12.31f + 4.7f) < (progress - .5f) * 1.1f)
            {
                float kind = Hash(i * 3.77f + 1.3f);
                if (kind < .5f) step.seconds *= 1.5f + kind * 2f;
                else if (i > 0)
                {
                    var back = new FilmStep { frame = frames[i - 1].frame, seconds = frames[i - 1].seconds * .5f };
                    steps.Add(back);
                    total += back.seconds;
                }
            }
            steps.Add(step);
            total += step.seconds;
        }
        total = Mathf.Max(.01f, total);
        return steps;
    }

    static float Hash(float value)
    {
        float v = Mathf.Sin(value) * 43758.5453f;
        return v - Mathf.Floor(v);
    }

    void BindFilmGlitch()
    {
        filmGlitchStuck = false;
        if (filmGlitchBound || slideImage == null || configuration.animationGlitchShader == null) return;
        filmGlitchOriginalMaterial = slideImage.material;
        filmGlitch = new Material(configuration.animationGlitchShader) { name = "Chapter01EndingFilmGlitch (runtime)" };
        slideImage.material = filmGlitch;
        filmGlitchBound = true;
    }

    void SetFilmFrame(Sprite sprite)
    {
        if (slideImage.sprite != sprite) slideImage.sprite = sprite;
        if (filmGlitch == null || sprite == null) return;
        Vector4 uv = DataUtility.GetOuterUV(sprite);
        filmGlitch.SetVector("_UvRect", new Vector4(uv.x, uv.y, uv.z - uv.x, uv.w - uv.y));
    }

    void SetFilmGlitch(float amount, float tick)
    {
        if (filmGlitch == null) return;
        filmGlitch.SetFloat("_Amount", amount);
        filmGlitch.SetFloat("_Tick", tick);
    }

    void ReleaseFilmGlitch()
    {
        if (filmGlitchBound && slideImage != null) slideImage.material = filmGlitchOriginalMaterial;
        filmGlitchBound = filmGlitchStuck = false;
        filmGlitchOriginalMaterial = null;
        if (filmGlitch != null) Destroy(filmGlitch);
        filmGlitch = null;
    }
}
