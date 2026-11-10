using UnityEngine;
using UnityEngine.UI;

// A short, authored insert beside the warehouse prose. The frames use their
// own clock so quick reading cannot shorten the gesture or the last image.
[DisallowMultipleComponent]
public sealed class OSMemoryWarehouseDoorPresentation : MonoBehaviour
{
    public const int EntryNode = 5000;
    public const int LastNode = 5010;
    public const int ExitNode = 5020;
    public const string SequenceId = "chapter02.warehouse.door";

    [SerializeField] Image frameImage;
    [SerializeField] Sprite[] frames = new Sprite[0];
    [SerializeField] float[] frameDurations = new float[0];
    [SerializeField, Min(0f)] float finalHoldDuration = 2f;

    bool playing;
    float elapsed;
    float animationDuration;
    int frameIndex;
    OSMemoryWindowController owner;

    public bool IsReadyToClose { get; private set; }
    public float Elapsed => elapsed;
    public float AnimationDuration => animationDuration;
    public float FinalHoldDuration => finalHoldDuration;
    public int FrameIndex => frameIndex;
    public static bool ContainsNode(int index) => index >= EntryNode && index <= LastNode;

    void OnEnable() => ResetPresentation();
    void OnDisable() => ResetPresentation();

    // Wired to OSMemoryShotContent: begin after the opening transition, so
    // none of the authored animation is consumed behind the window fade.
    public void PlayOpened()
    {
        ResetPresentation();
        owner = GetComponentInParent<OSMemoryWindowController>();
        if (owner == null) owner = OS_Handle.Instance?.MemoryWindowController;
        owner?.TryPlayChapter02PresentationCue(EntryNode, "door-blocked", "MetalScrape", .45f);
        playing = true;
    }

    public void Release() => ResetPresentation();

    void ResetPresentation()
    {
        playing = false;
        IsReadyToClose = false;
        elapsed = 0f;
        animationDuration = 0f;
        for (int i = 0; i < frames.Length; i++)
            animationDuration += FrameDuration(i);
        SetFrame(0);
    }

    float FrameDuration(int index) => index < frameDurations.Length
        ? Mathf.Max(.01f, frameDurations[index]) : .1f;

    void Update()
    {
        if (!playing || IsReadyToClose)
            return;
        var os = OS_Handle.Instance;
        if (os != null && (!os.IsOSVisible || os.IsSystemInputLocked))
            return;
        float previousElapsed = elapsed;
        elapsed += Time.unscaledDeltaTime;
        if (previousElapsed < 1.5f && elapsed >= 1.5f)
            owner?.TryPlayChapter02PresentationCue(EntryNode, "door-push", "DoorScrape", .65f);
        if (previousElapsed < 3.5f && elapsed >= 3.5f)
            owner?.TryPlayChapter02PresentationCue(EntryNode, "door-dust", "DustFall", .45f);
        float frameEnd = 0f;
        int wanted = Mathf.Max(0, frames.Length - 1);
        for (int i = 0; i < frames.Length; i++)
        {
            frameEnd += FrameDuration(i);
            if (elapsed < frameEnd)
            {
                wanted = i;
                break;
            }
        }
        SetFrame(wanted);
        if (elapsed >= animationDuration + finalHoldDuration)
            IsReadyToClose = true;
    }

    void SetFrame(int index)
    {
        frameIndex = frames.Length == 0 ? -1 : Mathf.Clamp(index, 0, frames.Length - 1);
        if (frameImage != null && frameIndex >= 0)
            frameImage.sprite = frames[frameIndex];
    }
}
