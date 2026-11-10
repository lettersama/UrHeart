using System.Collections.Generic;
using UnityEngine;

/// <summary>Short, pixel-stepped motions of the complete Live window during the experiment conversations.</summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(510)]
public sealed class OSCursorExperimentLiveReaction : MonoBehaviour
{
    readonly HashSet<string> played = new HashSet<string>(System.StringComparer.Ordinal);
    OSWindowUnit live, messageWindow, settingsWindow;
    string currentReaction;
    float elapsed, duration;
    bool hasFocus = true;

    public bool IsPlaying => currentReaction != null;
    public string CurrentReaction => currentReaction;
    public Vector2 CurrentOffset => live != null ? live.PresentationOffset : Vector2.zero;

    void OnEnable() { hasFocus = Application.isFocused; ResolveReferences(); }
    void OnDisable() { PausePresentation(); UnbindMessageWindow(); }
    void OnDestroy() { PausePresentation(); UnbindMessageWindow(); }
    void OnApplicationFocus(bool focused)
    {
        hasFocus = focused;
        if (!focused) PausePresentation();
    }

    void ResolveReferences()
    {
        if (settingsWindow == null && OS_Handle.Instance != null)
            settingsWindow = OS_Handle.Instance.transform.Find("Windows/WindowUnit_Option")?.GetComponent<OSWindowUnit>();
        OSWindowUnit nextLive = OS_Handle.Instance?.CharacterWindowController?.GetComponent<OSWindowUnit>();
        if (live != nextLive)
        {
            PausePresentation();
            live = nextLive;
        }
        OSWindowUnit nextMessage = OS_Handle.Instance?.LinkMessageController?.LinkWindow;
        if (messageWindow == nextMessage) return;
        UnbindMessageWindow();
        messageWindow = nextMessage;
        if (messageWindow != null) messageWindow.MoveStarted += MessageMoveStarted;
    }

    void UnbindMessageWindow()
    {
        if (messageWindow != null) messageWindow.MoveStarted -= MessageMoveStarted;
        messageWindow = null;
    }

    void MessageMoveStarted(OSWindowUnit source) => PausePresentation();

    bool Available => hasFocus && OS_Handle.Instance?.IsOSVisible == true
        && !OSSessionPersistence.IsRestoring && GameSessionFlow.Instance?.IsBusy != true
        && !GameTestConsole.BlocksGameplayInput && OS_Handle.Instance?.CursorExperiment?.IsSettingsOpen != true
        && (settingsWindow == null || !settingsWindow.IsOpen || settingsWindow.IsMinimized || !settingsWindow.gameObject.activeInHierarchy)
        && OS_Handle.Instance?.IsSystemInputLockedWithoutCursorExperiment != true;

    bool WindowsReady => live != null && live.IsOpen && !live.IsMinimized && !live.IsOpening
        && !live.IsClosing && live.gameObject.activeInHierarchy && !live.IsPointerManipulating
        && messageWindow != null && messageWindow.IsOpen && !messageWindow.IsMinimized
        && !messageWindow.IsOpening && !messageWindow.IsClosing && messageWindow.gameObject.activeInHierarchy
        && !messageWindow.IsPointerManipulating;

    /// <summary>Call once the corresponding LinkMessage is visibly open. Repeated calls and resumes do not replay it.</summary>
    public void PlayReaction(string suffix)
    {
        if (string.IsNullOrEmpty(suffix) || played.Contains(suffix)) return;
        ResolveReferences();
        if (!Available || !WindowsReady) return;
        float nextDuration = ReactionDuration(suffix);
        if (nextDuration < 0f) return;
        PausePresentation();
        if (nextDuration == 0f) { played.Add(suffix); return; }
        if (!live.TrySetPresentationOffset(this, Vector2.zero)) return;
        played.Add(suffix);
        currentReaction = suffix;
        elapsed = 0f;
        duration = nextDuration;
    }

    void Update()
    {
        if (!IsPlaying) return;
        ResolveReferences();
        TickPresentation(Mathf.Min(Time.unscaledDeltaTime, .05f), Available);
    }

    /// <summary>Uses unscaled seconds. Unavailable cancels the current reaction without forgetting that it played.</summary>
    public void TickPresentation(float seconds, bool available)
    {
        if (!IsPlaying) return;
        if (!available || !WindowsReady || !live.OwnsPresentationOffset(this))
        { PausePresentation(); return; }
        if (float.IsNaN(seconds) || float.IsInfinity(seconds) || seconds < 0f) return;
        elapsed = Mathf.Min(duration, elapsed + seconds);
        if (elapsed >= duration) { PausePresentation(); return; }
        Vector2 offset = EvaluateReaction(currentReaction, elapsed / duration);
        if (!live.TrySetPresentationOffset(this, offset)) PausePresentation();
    }

    /// <summary>Settings, focus loss and window operations return exactly to the player's position; no replay on resume.</summary>
    public void PausePresentation()
    {
        if (live != null) live.ClearPresentationOffset(this);
        currentReaction = null;
        elapsed = duration = 0f;
    }

    /// <summary>For a new/restored save binding, rather than a temporary settings/focus pause.</summary>
    public void ResetPresentation()
    {
        PausePresentation();
        played.Clear();
    }

    static float ReactionDuration(string suffix)
    {
        switch (suffix)
        {
            case "caught": return .38f;
            case "third_question": return .46f;
            case "third_return":
            case "third_confirm":
            case "third_deny": return .30f;
            case "study": return .32f;
            case "safety_question":
            case "second_question":
            case "question": return .28f;
            case "safety_confirm":
            case "second_confirm":
            case "not_bug": return .28f;
            case "human": return .34f;
            case "silent": return .30f;
            case "safety_deny":
            case "second_deny":
            case "tether": return .28f;
            case "second_test": return 0f;
            case "third_promise":
            case "third_test": return 0f;
            case "first_test": return 0f;
            default: return -1f;
        }
    }

    static Vector2 EvaluateReaction(string suffix, float t)
    {
        // Integer native-UI offsets only: never scale or rotate the pixel text/window frame.
        switch (suffix)
        {
            case "third_question":
                // A quick startled lift, brief hang, then a small settling jolt.
                // OSWindowUnit rounds this RectTransform offset to native pixels
                // and excludes it from the player's saved window position.
                float lift = t < .18f ? Mathf.Lerp(0, 5, EaseOut(t / .18f))
                    : t < .35f ? 5f
                    : t < .76f ? Mathf.Lerp(5, 0, EaseIn((t - .35f) / .41f))
                    : t < .87f ? Mathf.Lerp(0, -1, (t - .76f) / .11f)
                    : Mathf.Lerp(-1, 0, (t - .87f) / .13f);
                return new Vector2(t < .35f ? Shake(t / .35f, 1f) : 0f, lift);
            case "third_return": return new Vector2(Shake(t, 1f), 0);
            case "third_confirm": return new Vector2(0, Nod(t, 1f));
            case "third_deny": return new Vector2(Shake(t, 1f), 0);
            case "caught":
                return new Vector2(0, t < .34f ? Mathf.Lerp(0, 6, EaseOut(t / .34f))
                    : t < .71f ? Mathf.Lerp(6, 0, EaseIn((t - .34f) / .37f))
                    : t < .84f ? Mathf.Lerp(0, -1, (t - .71f) / .13f)
                    : Mathf.Lerp(-1, 0, (t - .84f) / .16f));
            case "study": return new Vector2(Shake(t, 2f), 0);
            case "safety_question":
            case "second_question":
            case "question": return new Vector2(0, Nod(t, 2f));
            case "safety_confirm":
            case "second_confirm":
            case "not_bug": return new Vector2(Shake(t, 1f), 0);
            case "human": return new Vector2(Shake(t, 2f), Nod(t, -1f));
            case "silent": return new Vector2(0, Nod(t, 1f));
            case "safety_deny":
            case "second_deny":
            case "tether": return new Vector2(0, Nod(t, 2f));
            default: return Vector2.zero;
        }
    }

    static float Nod(float t, float amount) => t < .4f
        ? Mathf.Lerp(0, -amount, EaseOut(t / .4f))
        : Mathf.Lerp(-amount, 0, EaseOut((t - .4f) / .6f));
    static float Shake(float t, float amount) => t < .25f
        ? Mathf.Lerp(0, -amount, EaseOut(t / .25f))
        : t < .62f ? Mathf.Lerp(-amount, amount, (t - .25f) / .37f)
        : Mathf.Lerp(amount, 0, EaseOut((t - .62f) / .38f));
    static float EaseOut(float t) { t = Mathf.Clamp01(t); return 1f - (1f - t) * (1f - t); }
    static float EaseIn(float t) { t = Mathf.Clamp01(t); return t * t; }
}
