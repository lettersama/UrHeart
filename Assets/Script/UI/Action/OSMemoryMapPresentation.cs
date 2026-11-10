using UnityEngine;
using UnityEngine.UI;

// The map is a resident illustration. Story nodes request later beats, while
// this content owns their minimum timing independently of reading speed.
[DisallowMultipleComponent]
public sealed class OSMemoryMapPresentation : MonoBehaviour
{
    public const int EntryNode = 4630;
    public const int LastMapNode = 4680;
    public const int ExitNode = 4690;

    public enum MapPhase
    {
        Inactive, Paper, HandEntering, FirstTap, AwaitDoubleTap,
        DoubleTap, AwaitRoute, HandLeaving, Route, Arrival, FinalHold, Complete
    }

    [Header("Native 216 x 162 layers")]
    [SerializeField] Image handImage;
    [SerializeField] Sprite handUp;
    [SerializeField] Sprite handDown;
    [SerializeField] Image routeImage;
    [SerializeField] Sprite[] routeFrames = new Sprite[0];
    [SerializeField] Image arrivalImage;
    [SerializeField] Sprite[] arrivalFrames = new Sprite[0];
    [SerializeField] Vector2 handOutsideOffset = new Vector2(36f, -170f);
    [SerializeField] Vector2 handRaisedOffset = new Vector2(0f, 2f);

    [Header("Minimum seconds, unscaled")]
    [SerializeField, Min(.1f)] float paperLead = .8f;
    [SerializeField, Min(.1f)] float handEnterDuration = .65f;
    [SerializeField, Min(.1f)] float firstTapDuration = .65f;
    [SerializeField, Min(.1f)] float doubleTapDuration = 1.18f;
    [SerializeField, Min(.1f)] float handLeaveDuration = .6f;
    [SerializeField, Min(.1f)] float routeDuration = 2.4f;
    [SerializeField, Min(.1f)] float arrivalDuration = .65f;
    [SerializeField, Min(.1f)] float finalHoldDuration = 2f;

    OSMemoryWindowController owner;
    MapPhase phase;
    float phaseStartedAt;
    float openedAt = -1f;
    float readyAt = -1f;
    int requestedStage;
    int routeFrameIndex = -1;
    bool handWasPressed;
    int tapSerial;

    public MapPhase Phase => phase;
    public bool IsReadyToLeave => phase == MapPhase.Complete;
    public int RequestedStage => requestedStage;
    public int RouteFrameIndex => routeFrameIndex;
    public float OpenedAt => openedAt;
    public float ReadyAt => readyAt;
    public float PhaseElapsed => phase == MapPhase.Inactive ? 0f : Time.unscaledTime - phaseStartedAt;
    public float MinimumDuration => paperLead + handEnterDuration + firstTapDuration
        + doubleTapDuration + handLeaveDuration + routeDuration + arrivalDuration + finalHoldDuration;

    public static bool ContainsNode(int node) => node >= EntryNode && node <= LastMapNode;

    void OnEnable()
    {
        owner = GetComponentInParent<OSMemoryWindowController>();
        ResetPresentation();
    }

    void OnDisable() => ResetPresentation();

    // Invoked by OSMemoryShotContent only after the window opening finishes.
    public void PlayOpened()
    {
        if (phase != MapPhase.Inactive)
            return;
        openedAt = Time.unscaledTime;
        if (owner == null) owner = OS_Handle.Instance?.MemoryWindowController;
        owner?.TryPlayChapter02PresentationCue(EntryNode, "map-paper", "PaperRustle", .55f);
        SetPhase(MapPhase.Paper);
        ObserveStory();
    }

    public void Release() => ResetPresentation();

    void ResetPresentation()
    {
        phase = MapPhase.Inactive;
        phaseStartedAt = 0f;
        openedAt = readyAt = -1f;
        requestedStage = 0;
        routeFrameIndex = -1;
        handWasPressed = false;
        tapSerial = 0;
        if (handImage != null)
        {
            handImage.enabled = false;
            handImage.sprite = handUp;
            handImage.rectTransform.anchoredPosition = handOutsideOffset;
        }
        if (routeImage != null) routeImage.enabled = false;
        if (arrivalImage != null) arrivalImage.enabled = false;
    }

    void ObserveStory()
    {
        int node = owner != null && owner.CurrentData != null ? owner.CurrentData.DataIndex : -1;
        if (!ContainsNode(node))
            return;
        int requested = node >= 4670 ? 3 : node >= 4660 ? 2 : node >= 4650 ? 1 : 0;
        requestedStage = Mathf.Max(requestedStage, requested);
    }

    void Update()
    {
        if (phase == MapPhase.Inactive || phase == MapPhase.Complete)
            return;
        OS_Handle os = OS_Handle.Instance;
        if (os != null && (!os.IsOSVisible || os.IsSystemInputLocked))
        {
            // A hidden desktop or system transition must not consume the time
            // in which the player is supposed to see the gesture/final plan.
            phaseStartedAt += Time.unscaledDeltaTime;
            return;
        }
        ObserveStory();
        float elapsed = PhaseElapsed;
        switch (phase)
        {
            case MapPhase.Paper:
                if (requestedStage >= 1 && elapsed >= paperLead)
                {
                    handImage.enabled = true;
                    SetPhase(MapPhase.HandEntering);
                }
                break;
            case MapPhase.HandEntering:
                MoveHand(handOutsideOffset, handRaisedOffset, elapsed / handEnterDuration);
                if (elapsed >= handEnterDuration) SetPhase(MapPhase.FirstTap);
                break;
            case MapPhase.FirstTap:
                SetHandPressed(elapsed >= firstTapDuration * .28f && elapsed < firstTapDuration * .64f);
                if (elapsed >= firstTapDuration) SetPhase(MapPhase.AwaitDoubleTap);
                break;
            case MapPhase.AwaitDoubleTap:
                if (requestedStage >= 2) SetPhase(MapPhase.DoubleTap);
                break;
            case MapPhase.DoubleTap:
                float progress = elapsed / doubleTapDuration;
                SetHandPressed((progress >= .15f && progress < .32f) || (progress >= .53f && progress < .70f));
                if (elapsed >= doubleTapDuration) SetPhase(MapPhase.AwaitRoute);
                break;
            case MapPhase.AwaitRoute:
                if (requestedStage >= 3) SetPhase(MapPhase.HandLeaving);
                break;
            case MapPhase.HandLeaving:
                MoveHand(handRaisedOffset, handOutsideOffset, elapsed / handLeaveDuration);
                if (elapsed >= handLeaveDuration)
                {
                    handImage.enabled = false;
                    SetPhase(MapPhase.Route);
                    SetRouteFrame(0);
                }
                break;
            case MapPhase.Route:
                SetRouteFrame(Mathf.FloorToInt(elapsed / routeDuration * routeFrames.Length));
                if (elapsed >= routeDuration)
                {
                    SetPhase(MapPhase.Arrival);
                    SetArrivalFrame(0);
                }
                break;
            case MapPhase.Arrival:
                SetArrivalFrame(Mathf.FloorToInt(elapsed / arrivalDuration * arrivalFrames.Length));
                if (elapsed >= arrivalDuration) SetPhase(MapPhase.FinalHold);
                break;
            case MapPhase.FinalHold:
                if (elapsed >= finalHoldDuration)
                {
                    SetPhase(MapPhase.Complete);
                    readyAt = Time.unscaledTime;
                }
                break;
        }
    }

    void SetPhase(MapPhase next)
    {
        phase = next;
        phaseStartedAt = Time.unscaledTime;
        handWasPressed = false;
        if (next == MapPhase.Route)
            owner?.TryPlayChapter02PresentationCue(4670, "map-route", "PencilStroke", .45f);
    }

    void SetHandPressed(bool pressed)
    {
        if (pressed && !handWasPressed)
        {
            int tap = tapSerial++;
            owner?.TryPlayChapter02PresentationCue(tap == 0 ? 4650 : 4660,
                "map-tap-" + tap, "TableTap", tap == 0 ? .52f : .48f);
        }
        handWasPressed = pressed;
        handImage.sprite = pressed ? handDown : handUp;
        handImage.rectTransform.anchoredPosition = pressed ? Vector2.zero : handRaisedOffset;
    }

    void MoveHand(Vector2 from, Vector2 to, float progress)
    {
        Vector2 position = Vector2.Lerp(from, to, Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(progress)));
        handImage.sprite = handUp;
        // A whole native pixel per step; no blurry subpixel drift on this layer.
        handImage.rectTransform.anchoredPosition = new Vector2(Mathf.Round(position.x), Mathf.Round(position.y));
    }

    void SetRouteFrame(int index)
    {
        if (routeImage == null || routeFrames.Length == 0)
            return;
        routeFrameIndex = Mathf.Clamp(index, 0, routeFrames.Length - 1);
        routeImage.sprite = routeFrames[routeFrameIndex];
        routeImage.enabled = true;
    }

    void SetArrivalFrame(int index)
    {
        if (arrivalImage == null || arrivalFrames.Length == 0)
            return;
        arrivalImage.sprite = arrivalFrames[Mathf.Clamp(index, 0, arrivalFrames.Length - 1)];
        arrivalImage.enabled = true;
    }
}
