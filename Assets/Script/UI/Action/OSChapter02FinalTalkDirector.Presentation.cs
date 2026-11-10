using UnityEngine;

// Presentation only: never writes story, save or resources.
//   Silent beat - after "*不说话" / "*等她说下去" the choice closes and nothing is
//                 said for a moment before her next line opens.
//   Heartbeat   - the whole talk beats a little slower (she has relaxed), easing
//                 in and back out; the comfort line, where she mentions the old
//                 man's heart, ends on one strong beat.
//   Quiet       - IsQuietBeat tells the paper-push bench to stop the red pointer's
//                 bob during the grave, her forgotten face and a silent beat.
//   Nightfall   - from the night line the OS stays dark (OSChapter02NightfallPresentation);
//                 at the end, clicking 状态检查 brings the thunder instead of the window.
// Ticks only from the director's Tick, so focus/settings/console pauses freeze it.
public sealed partial class OSChapter02FinalTalkDirector
{
    public const float SilentBeatSeconds = 1.5f;
    const float RelaxedBeatScale = 1.25f, BeatEaseSeconds = 3f;
    float silentBeat, beatEase, restingBeat = -1f;
    bool heartbeatOwned;
    string strongBeatEntry;
    OSChapter02NightfallPresentation nightfall;
    bool nightfallClickArmed;
    int nightfallReadyFrame = -1;

    public bool IsNightfallTransitionBlockingInput => IsEngaged
        && nightfall != null && nightfall.IsThundering && !nightfall.HasEnded;
    public bool CanLeadToStateCheck => IsEngaged && (IsChapter03EntryReady
        || Step == FinalTalkStep.Goodbye && OwnMessageDisplayed
        && messages.DisplayedEntryId == ReservedEntryId && !messages.LinkWindow.IsOpening
        && !messages.LinkWindow.IsClosing);

    public OSChapter02NightfallPresentation Nightfall
    {
        get
        {
            if (nightfall == null && Application.isPlaying)
            {
                nightfall = GetComponent<OSChapter02NightfallPresentation>();
                if (nightfall == null) nightfall = gameObject.AddComponent<OSChapter02NightfallPresentation>();
            }
            return nightfall;
        }
    }

    /// <summary>
    /// The red pointer starts leading during Goodbye. Early clicks are consumed;
    /// after the dialogue closes a separate click answers with thunder.
    /// </summary>
    public bool TryHandleDesktopProcessClick(DesktopProcessUnit unit)
    {
        if (unit == null || !IsEngaged || Step < FinalTalkStep.Goodbye
            || unit.ProcessKey != OSDesktopProcessKeys.StateCheck) return false;
        if (!IsChapter03EntryReady || !Available || !IsDialogueBoundaryClear || !nightfallClickArmed) return true;
        var night = Nightfall;
        if (night == null || !night.IsNight) return true;
        if (night.IsThundering || night.HasEnded) return true;
        if (!GameSetup.IsDemo)
        {
            if (os.Chapter03Entry?.TryBegin() == true) nightfallClickArmed = false;
            return true;
        }
        if (night.BeginThunder()) nightfallClickArmed = false;
        return true;
    }

    void ArmNightfallClick()
    {
        if (Time.frameCount <= nightfallReadyFrame) return;
        var button = UnityEngine.InputSystem.Mouse.current?.leftButton;
        if (button == null || !button.isPressed && !button.wasPressedThisFrame && !button.wasReleasedThisFrame)
            nightfallClickArmed = true;
    }

    public bool IsSilentBeat => IsEngaged && silentBeat > 0f;
    public bool IsQuietBeat => IsEngaged && (silentBeat > 0f
        || Step == FinalTalkStep.Grave || Step == FinalTalkStep.Forget);

    void BeginSilentBeat() => silentBeat = SilentBeatSeconds;

    // True while the pause still holds the next line back.
    bool TickSilentBeat(float dt)
    {
        if (silentBeat <= 0f) return false;
        silentBeat -= dt;
        return silentBeat > 0f;
    }

    void TickHeartbeat(float dt)
    {
        if (Step >= FinalTalkStep.Night) _ = Nightfall;
        var wave = os?.HeartbeatWaveform;
        if (wave == null) return;
        bool talking = Step >= FinalTalkStep.Opening && Step <= FinalTalkStep.Goodbye;
        if (talking && !heartbeatOwned)
        {
            restingBeat = wave.NaturalBeatInterval; heartbeatOwned = true; beatEase = 0f;
        }
        if (!heartbeatOwned) return;
        beatEase = Mathf.Clamp01(beatEase + (talking ? dt : -dt) / BeatEaseSeconds);
        float u = beatEase * beatEase * (3f - 2f * beatEase);
        wave.SetNaturalBeatInterval(Mathf.Lerp(restingBeat, restingBeat * RelaxedBeatScale, u));
        if (!talking && beatEase <= 0f) { ReleaseHeartbeat(); return; }

        // Once per display of the comfort line, after its last word has typed.
        if (Step != FinalTalkStep.Comfort || !messageTyped || messageId == null
            || messageId == strongBeatEntry) return;
        strongBeatEntry = messageId;
        wave.GenerateActiveBeat();
    }

    void ReleaseHeartbeat()
    {
        if (heartbeatOwned && restingBeat > 0f) os?.HeartbeatWaveform?.SetNaturalBeatInterval(restingBeat);
        heartbeatOwned = false; beatEase = 0f; restingBeat = -1f;
    }

    void ResetTalkPresentation()
    {
        nightfallClickArmed = false;
        nightfallReadyFrame = Time.frameCount;
        silentBeat = 0f; strongBeatEntry = null;
        ReleaseHeartbeat();
    }
}
