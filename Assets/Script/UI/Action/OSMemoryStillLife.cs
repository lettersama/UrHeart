using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Keeps a still memory illustration faintly alive without animating its figures:
/// a closed-eye overlay blinks every few seconds, and an alternate water strip swaps
/// with the original on a fixed beat. Both overlays are opaque two-colour crops that
/// sit exactly over their region of the base image. Unscaled time; restarts on enable.
/// Used by the chapter-two cart pause (memory 5970–6000).
/// </summary>
[DisallowMultipleComponent]
public sealed class OSMemoryStillLife : MonoBehaviour
{
    [SerializeField] Image blinkOverlay;
    [SerializeField] Vector2 blinkInterval = new Vector2(2.8f, 5.2f);
    [SerializeField, Min(.02f)] float blinkSeconds = .14f;
    [SerializeField] Image waterOverlay;
    [SerializeField, Min(.05f)] float waterBeat = .6f;

    float blinkClock, nextBlink, waterClock;
    readonly System.Random random = new System.Random(5970);

    void OnEnable()
    {
        blinkClock = waterClock = 0f;
        nextBlink = NextBlink();
        Show(blinkOverlay, false);
        Show(waterOverlay, false);
    }

    void Update()
    {
        float dt = Mathf.Min(Time.unscaledDeltaTime, .1f);
        blinkClock += dt;
        if (blinkClock >= nextBlink + blinkSeconds) { blinkClock = 0f; nextBlink = NextBlink(); }
        Show(blinkOverlay, blinkClock >= nextBlink);

        waterClock += dt;
        Show(waterOverlay, Mathf.FloorToInt(waterClock / waterBeat) % 2 == 1);
        if (waterClock >= waterBeat * 2f) waterClock -= waterBeat * 2f;
    }

    float NextBlink() => Mathf.Lerp(blinkInterval.x, blinkInterval.y, (float)random.NextDouble());

    static void Show(Image image, bool on)
    {
        if (image != null && image.enabled != on) image.enabled = on;
    }
}
