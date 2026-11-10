using System;
using UnityEngine;
using UnityEngine.UI;

// Plays the artist's native full-canvas patches inside the existing Memory
// subwindow. The reading cursor selects a beat; no story or save state is written.
[DisallowMultipleComponent]
public sealed class OSMemoryFaceRepairPresentation : MonoBehaviour
{
    public const int EntryNode = 6960;
    public const int LastNode = 7240;
    public const string SequenceId = "chapter02.makeup";

    [Serializable]
    public sealed class Frame
    {
        public Sprite delta;
        public float seconds;
    }

    [Serializable]
    public sealed class StateClip
    {
        public string id;
        public Sprite patch;
        public Frame[] frames;
    }

    [Serializable]
    public sealed class ActionClip
    {
        public string id;
        public string from;
        public string to;
        public Frame[] frames;
    }

    [SerializeField] Image stateImage;
    [SerializeField] Image frameImage;
    [SerializeField] StateClip[] states = Array.Empty<StateClip>();
    [SerializeField] ActionClip[] actions = Array.Empty<ActionClip>();

    OSMemoryWindowController owner;
    OSWindowUnit settings;
    StateClip state;
    ActionClip action;
    string fulfilledRequest;
    int frame;
    float elapsed;
    float actionAudioSeconds;
    bool opened;
    bool hasFocus;

    public string CurrentStateId => state != null ? state.id : string.Empty;
    public string CurrentActionId => action != null ? action.id : string.Empty;
    public int FrameIndex => frame;
    public bool IsPlayingAction => action != null;
    public static bool ContainsNode(int node) => node >= EntryNode && node <= LastNode;

    void OnEnable()
    {
        owner = GetComponentInParent<OSMemoryWindowController>();
        hasFocus = Application.isFocused;
        Release();
    }

    void OnDisable() => Release();
    void OnApplicationFocus(bool focused) => hasFocus = focused;

    // OSMemoryShotContent invokes these after the shell opens / before reuse.
    public void PlayOpened()
    {
        if (opened) return;
        if (owner == null) owner = OS_Handle.Instance?.MemoryWindowController;
        opened = true;
        string request = GetRequest();
        ActionClip first = FindAction(request);
        SetState(first != null ? first.from : request);
    }

    public void Release()
    {
        opened = false;
        action = null;
        state = null;
        fulfilledRequest = null;
        frame = 0;
        elapsed = 0f;
        actionAudioSeconds = 0f;
        SetImage(frameImage, null);
        SetImage(stateImage, null);
    }

    void Update()
    {
        if (!opened || owner == null || !Available) return;
        string request = GetRequest();
        if (request == null) return;

        // Let an in-flight hand leave the picture before accepting the next beat.
        // Fast reading may skip intermediate beats; it never locks or advances prose.
        if (action == null && request != fulfilledRequest && owner.IsCurrentTextComplete)
        {
            fulfilledRequest = request;
            ActionClip next = FindAction(request);
            if (next == null) SetState(request);
            else
            {
                SetState(next.from);
                action = next;
                frame = 0;
                elapsed = 0f;
                actionAudioSeconds = 0f;
                RenderFrame();
            }
        }

        Frame[] frames = action != null ? action.frames : state?.frames;
        if (frames == null || frames.Length == 0) return;
        float dt = Mathf.Min(Time.unscaledDeltaTime, .1f);
        if (action != null)
        {
            float previousAudioTime = actionAudioSeconds;
            actionAudioSeconds += dt;
            PlayActionContactSounds(previousAudioTime, actionAudioSeconds);
        }
        elapsed += dt;
        while (elapsed >= frames[frame].seconds)
        {
            elapsed -= frames[frame].seconds;
            frame++;
            if (frame >= frames.Length)
            {
                if (action != null)
                {
                    string destination = action.to;
                    action = null;
                    // Atomic patch replacement: the operation layer is cleared
                    // before destination idle, including the mutually exclusive marks.
                    SetState(destination);
                    return;
                }
                frame = 0;
            }
            RenderFrame();
        }
    }

    bool Available
    {
        get
        {
            var os = OS_Handle.Instance;
            if (settings == null && os != null)
                settings = os.transform.Find("Windows/WindowUnit_Option")?.GetComponent<OSWindowUnit>();
            return hasFocus && os != null && os.IsOSVisible && !os.IsSystemInputLocked
                && !OSSessionPersistence.IsRestoring && GameSessionFlow.Instance?.IsBusy != true
                && !GameTestConsole.BlocksGameplayInput
                && !(settings != null && settings.IsOpen && !settings.IsMinimized);
        }
    }

    // Contact times come from FaceRepair/Sources~/action_timing.lua. They advance
    // with the same paused clock as the hand, never with the prose's reading speed.
    void PlayActionContactSounds(float before, float after)
    {
        switch (action.id)
        {
            case "03_powder_left":
                Contact(6970, .98f, "powder-1", "PowderPat", .60f, before, after);
                Contact(6970, 1.28f, "powder-2", "PowderPat", .52f, before, after);
                Contact(6970, 1.64f, "powder-3", "PowderPat", .50f, before, after);
                Contact(6970, 1.98f, "powder-4", "PowderPat", .45f, before, after);
                break;
            case "04_powder_right":
                Contact(7000, .98f, "powder-1", "PowderPat", .56f, before, after);
                Contact(7000, 1.28f, "powder-2", "PowderPat", .50f, before, after);
                Contact(7000, 1.66f, "powder-3", "PowderPat", .45f, before, after);
                break;
            case "05_brows":
                Contact(7010, 1.1f, "brow-left", "PencilStroke", .50f, before, after);
                Contact(7010, 2.04f, "brow-right", "PencilStroke", .48f, before, after);
                break;
            case "08_cat": Contact(7190, 1.08f, "cat-draw", "PencilStroke", .52f, before, after); break;
            case "09_heart": Contact(7200, 1.04f, "heart-draw", "PencilStroke", .52f, before, after); break;
        }
    }

    void Contact(int node, float time, string token, string cue, float gain, float before, float after)
    {
        if (before < time && after >= time)
            owner?.TryPlayChapter02PresentationCue(node, token, cue, gain);
    }

    string GetRequest()
    {
        int node = owner?.CurrentData != null ? owner.CurrentData.DataIndex : -1;
        if (!ContainsNode(node)) return null;
        if (node < 6970) return "02_filled";
        if (node < 7000) return "03_powder_left";
        if (node < 7010) return "04_powder_right";
        if (node < 7040) return "05_brows";
        if (node < 7070) return "06_tilt";
        if (node < 7190) return "07_return";
        if (node == 7190) return "08_cat";
        if (node == 7200) return "09_heart";
        // A debug jump into a branch previews it, but cannot manufacture a choice.
        // After the merge, only the actual saved choice selects the final mark.
        int pattern = GameDataManager.Instance?.OSProcessData.memory.chapter02MakeupPattern ?? 0;
        return pattern == 1 ? "state:08_cat" : pattern == 2 ? "state:09_heart" : "07_neutral";
    }

    ActionClip FindAction(string id)
    {
        foreach (ActionClip clip in actions)
            if (clip != null && clip.id == id) return clip;
        return null;
    }

    void SetState(string id)
    {
        if (id != null && id.StartsWith("state:", StringComparison.Ordinal)) id = id.Substring(6);
        state = null;
        foreach (StateClip candidate in states)
            if (candidate != null && candidate.id == id) { state = candidate; break; }
        frame = 0;
        elapsed = 0f;
        SetImage(stateImage, state?.patch);
        RenderFrame();
    }

    void RenderFrame()
    {
        Frame[] frames = action != null ? action.frames : state?.frames;
        SetImage(frameImage, frames != null && frames.Length > frame ? frames[frame].delta : null);
    }

    static void SetImage(Image image, Sprite sprite)
    {
        if (image == null) return;
        image.sprite = sprite;
        image.enabled = sprite != null;
    }
}
