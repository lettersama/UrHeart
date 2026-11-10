using System;
using MrTGame.Core;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public sealed partial class OSChapter03StoryDirector
{
    OSCursorReleaseDirector actor;
    HeartbeatWaveformUI waveform;
    OSChapter02NightfallPresentation night;
    float normalBeatInterval = -1f, nextUnevenBeat, revealTime = -1f, lineClock, readReceiptSeconds;
    int readingLine, readingPage = -1, effectStage;
    Vector2 windowFrom, windowTo;
    bool ownsWindowPosition;
    Image batteryFlash;
    float batteryFlashTime, breakdownLineAge;
    string effectEntryId;
    int sobCharactersSeen;

    bool EnsureActor(bool visible = true)
    {
        if (actor == null) actor = os?.CursorRelease;
        if (actor == null || !actor.OwnsScriptActor(this) && !actor.TryAcquireScriptActor(this)) return false;
        actor.ScriptActorSetVisible(this, visible); return true;
    }
    void SetActorPaused(bool paused)
    {
        if (actor != null) actor.ScriptActorSetPaused(this, paused);
        PauseMusic(paused);
        PauseSfx(paused);
    }

    void RefreshLivePresence()
    {
        os.CharacterWindowController?.SetChapter03Presence(Has("reconnecting") ? 2 : State.liveOffline ? 1 : 0,
            Has("reconnecting") ? Text("ui.live.connecting") : State.liveOffline ? Text(readReceiptSeconds > 0f ? "ui.live.read" : "ui.live.offline") : null);
        if (State.liveOffline) EnsureActor(false);
        if (Has("mirror_visible") && !Has("mirror_done") && presentation.MirrorRoot == null)
            RestoreMirrorPresentation();
        if (presentation.MirrorRoot != null && Has("live_moved_center") && !Has("live_back_home")
            && !ownsWindowPosition && liveWindow != null && liveWindow.IsOpen)
        {
            liveWindow.WindowRect.anchoredPosition = MirrorDockPosition();
            liveWindow.RestoreAndBringToFront();
        }
        RefreshMirrorStage();
        // The rain is heard only once the home map has opened (pancake 10-24).
        if (night == null) os.TryGetComponent(out night);
        night?.SetChapter03Rain(view?.CurrentRoom?.id == "yard", IsActive, State.homeOpened);
        if (view != null && view.CurrentRoom != null)
        {
            bool outdoors = view.CurrentRoom.id == "yard";
            view.SetRainEnabled(outdoors);
            presentation.SetRain(outdoors, .85f);
            if (State.homeOpened && !Has("home_revealed"))
            {
                if (revealTime < 0f) revealTime = 0f;
                revealTime += Mathf.Min(Time.unscaledDeltaTime, .1f);
                view.SetAllLightsOn(true); view.SetPlayerVisible(revealTime >= .75f);
                view.SetDongVisible(revealTime >= .75f && !Has("dong_absorbed"));
                view.SetPlayerLightScale(revealTime < .75f ? 0f : Mathf.Clamp01((revealTime - .75f) / .65f));
                if (revealTime > 1.4f) State.Add("home_revealed");
            }
        }
    }

    void RefreshLocalizedPresentation()
    {
        if (presentation == null) return;
        var beat = State.memoryQueue.Count > 0 ? State.memoryQueue[0] : null;
        RenamePaperItems();
        if (IsModalBeat(beat) || !presentation.IsModalOpen) reopenedPaper = null;
        if (reopenedPaper != null)
            presentation.RefreshModalText(PaperTitle(reopenedPaper), Content.PaperBody(reopenedPaper));
        else if (beat != null && presentation.IsModalOpen)
        {
            if (beat.type == "paper") presentation.RefreshModalText(PaperTitle(beat.key), Content.PaperBody(beat.key));
            else if (beat.type == "text" && paperAlongside != null && !paperAlongside.complete)
                presentation.RefreshModalText(PaperTitle(paperAlongside.key), Content.PaperBody(paperAlongside.key));
            else if (beat.type == "letter") presentation.RefreshModalText(PaperTitle("final_letter"), Content.PaperBody("final_letter"));
            else if (beat.type == "keypad") presentation.RefreshModalText(Text("ui.password.title"), Text("ui.password.format"));
            else if (beat.type == "art") presentation.RefreshModalText(ArtTitle(beat.key));
        }
        presentation.RefreshMirrorText(Text("ui.mirror.title"), SavedMirrorIdentity(), SavedMirrorDialogue(), Text("ui.mirror.attention"));
        var samples = new string[7];
        for (int i = 0; i < 6; i++) samples[i] = Text("ui.reasoning.sample." + (i + 1).ToString("00"));
        samples[6] = "0x4D 0x0A 0xFF …";
        var thoughts = new string[5];
        for (int i = 0; i < 5; i++) thoughts[i] = Text("ui.resolve." + (i + 1).ToString("00"));
        presentation.RefreshEffectText(samples, GameDataManager.Instance.GetLocalizedText("os.home.dong.glyph", "咚"), thoughts);
    }

    string SavedMirrorDialogue() => Has("mirror_dialogue_9045") ? Text("memory.9045")
        : Has("mirror_dialogue_9040") ? Text("memory.9040") : string.Empty;

    string SavedMirrorIdentity()
    {
        if (Has("mirror_self")) return Text("ui.mirror.self");
        int node = GameDataManager.Instance.CurrentOSMemoryDataIndex;
        // The current node also covers saves from before mirror_identified existed.
        return Has("mirror_identified") || Has("mirror_dialogue_9040") || Has("mirror_dialogue_9045")
            || node >= 9030 && node <= 9080 ? Text("ui.mirror.master") : string.Empty;
    }

    void RestoreMirrorPresentation()
    {
        if (presentation.MirrorRoot == null)
            presentation.ShowMirror(Text("ui.mirror.title"), SavedMirrorIdentity(), revealEyesFirst: false, attentionText: Text("ui.mirror.attention"));
        // Language refresh can run before this window is rebuilt after a load.
        // Restore both the spoken line and its reflected copy at creation time.
        presentation.RefreshMirrorText(Text("ui.mirror.title"), SavedMirrorIdentity(), SavedMirrorDialogue(), Text("ui.mirror.attention"));
        RefreshMirrorStage();
    }

    void PrepareTextPresentation(string key, bool firstBegin)
    {
        PlayTextSfx(key, firstBegin);
        switch (key.Replace("os.chapter03.", ""))
        {
            case "memory.8090": revealTime = 0f; break;
            case "memory.hall.heartbeat": case "memory.store.heartbeat":
            case "memory.lab.diary13.heartbeat": case "memory.9080": StoryBeat(1f); break;
            case "memory.lab.bed.second.heartbeat": StoryBeat(.18f); break;
            case "memory.west.bed_door.final_knock": AudioManager.Instance?.PlaySfx("Chapter02_Knock"); break;
            case "memory.west.bed_door.panic": StoryBeat(1.3f); break;
            case "memory.9015": BeginMirrorPortraitPrelude(firstBegin); break;
            case "memory.9020":
                bool revealMirror = firstBegin && !Has("mirror_visible");
                State.Add("mirror_visible");
                if (revealMirror) presentation.ShowMirror(Text("ui.mirror.title"), "", attentionText: Text("ui.mirror.attention"));
                else RestoreMirrorPresentation();
                break;
            case "memory.9030":
                State.Add("mirror_identified"); presentation.SetMirrorIdentity(Text("ui.mirror.master")); break;
            case "memory.9040": case "memory.9045":
                State.Add(key.EndsWith("9045", StringComparison.Ordinal) ? "mirror_dialogue_9045" : "mirror_dialogue_9040");
                presentation.SetMirrorDialogue(Text(key)); break;
            case "memory.9048": presentation.PulseMirror(2f); break;
            case "memory.9060": State.Add("mirror_self"); presentation.SetMirrorIdentity(Text("ui.mirror.self")); break;
        }
    }

    bool RunEffect(OSChapter03StoryBeat beat, float dt)
    {
        if (!runtimeMemoryStarted)
        {
            PlayEffectSfx(beat);
            runtimeMemoryStarted = true; effectClock = 0f; effectStage = 0;
            int token = generation;
            Action done = () => { if (token == generation) beat.complete = true; };
            switch (beat.key)
            {
                case "reasoning":
                    // The view dims lamps and 「我」 over its first 0.8 s; 「咚」 stays and beats through the rules.
                    var lines = new string[7]; for (int i = 0; i < 6; i++) lines[i] = Text("ui.reasoning.sample." + (i + 1).ToString("00"));
                    lines[6] = "0x4D 0x0A 0xFF …";
                    presentation.PlayReasoning(lines, beat.remaining, done, GameDataManager.Instance.GetLocalizedText("os.home.dong.glyph", "咚")); break;
                case "yard_arrival":
                    AudioManager.Instance?.PlaySfx("Thunder_ForMainCore", .6f);
                    view.SetAllLightsOn(true); view.SetPlayerVisible(true); view.SetDongVisible(true); view.SetPlayerLightScale(1f);
                    done(); break;
                case "resolve":
                    var thoughts = new string[5]; for (int i = 0; i < thoughts.Length; i++) thoughts[i] = Text("ui.resolve." + (i + 1).ToString("00"));
                    if (CurrentPhase == Phase.YardReturn) PlayMusicCue(YardResolveCue);
                    presentation.PlayThoughts(thoughts, beat.remaining, done, view != null ? view.transform as RectTransform : null); break;
                case "zoom_in": State.Add("yard_zoom"); view?.SetZoom(true); done(); break;
                case "zoom_out": State.Add("yard_zoom_done"); view?.SetZoom(false); done(); break;
                case "absorb_dong":
                    if (Has("dong_absorbed")) done();
                    else view.AbsorbDong(beat.remaining, () =>
                    { if (token != generation) return; State.Add("dong_absorbed"); done(); });
                    break;
                case "reveal_box": State.Add("bed_second_done"); ApplyWorldState(); done(); break;
                case "rebuild_world": ApplyWorldState(); done(); break;
                case "box_pop":
                    ApplyWorldState();
                    if (!rebinding && view?.CurrentRoom?.id == "lab")
                    {
                        view.PopThing("lab_hairclip", "lab_box", beat.remaining);
                        view.PopThing("lab_letter", "lab_box", beat.remaining);
                        PlaySfxCue("Chapter02_PaperRustle", .3f);
                    }
                    done(); break;
                case "close_mirror": State.Add("mirror_done"); presentation.HideMirror(); done(); break;
                case "hairclip": os.CharacterWindowController?.PulseChapter03Portrait(); done(); break;
                case "live_to_center": case "live_to_home":
                    BeginWindowCarry(beat.key == "live_to_center"); break;
                case "mirror_pause": BeginMirrorPause(beat); break;
                case "wait_mirror_resume": RefreshMirrorStage(); break;
                case "closeup_open": case "closeup_close": break; // see the Closeup partial
                case "go_offline":
                    State.liveOffline = true; State.Add("breakdown_quiet"); State.Add("breakdown_music_fading");
                    FadeOutMusic(BreakdownFadeOutSeconds);
                    if (EnsureActor()) actor.ScriptActorMoveTo(this, new Vector2(Screen.width + 40f, Screen.height * .4f), beat.remaining);
                    break;
                case "reconnect":
                    State.liveOffline = false; State.Add("reconnecting");
                    SetLivePortraitCue("reconnect");
                    os.CharacterWindowController?.SetChapter03Presence(2, Text("ui.live.connecting"));
                    if (EnsureActor())
                    {
                        actor.ScriptActorSetPosition(this, new Vector2(Screen.width + 30f, Screen.height * .4f));
                        actor.ScriptActorMoveTo(this, HomeThingPoint("lab_box"), beat.remaining);
                    }
                    os.CharacterWindowController?.PlayChapter03SignalIn(null); break;
                case "failed_typing": SetLivePortraitCue("failed_typing"); break;
                default: Debug.LogError("[Chapter03] Missing effect " + beat.key); done(); break;
            }
        }
        effectClock += dt;
        switch (beat.key)
        {
            case "mirror_pause": return TickMirrorPause();
            case "wait_mirror_resume": return !IsMirrorPlaybackPaused;
            case "live_to_center": case "live_to_home":
                if (liveWindow == null) return true;
                float t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(effectClock / Mathf.Max(.1f, beat.remaining)));
                liveWindow.WindowRect.anchoredPosition = Vector2.Lerp(windowFrom, windowTo, t);
                if (EnsureActor()) actor.ScriptActorSetPosition(this, TitlePoint(liveWindow.WindowRect));
                if (t < 1f) return false;
                actor?.ScriptActorSetPressed(this, false);
                if (beat.key == "live_to_home") { State.Add("live_back_home"); ownsWindowPosition = false; RefreshMirrorStage(); }
                else State.Add("live_moved_center");
                return true;
            // Older saves may still have this beat before line 02. Let the
            // message start; its presentation event now owns the focus cue.
            case "closeup_open": return true;
            case "closeup_close": return RunCloseupClose(beat);
            case "go_offline":
                if (effectClock < beat.remaining) return false;
                EnsureActor(false); return true;
            case "reconnect":
                if (effectClock < beat.remaining) return false;
                State.flags.Remove("reconnecting"); RefreshLivePresence(); return true;
            case "failed_typing":
                int stage = Mathf.FloorToInt(effectClock / .65f);
                if (stage != effectStage)
                {
                    effectStage = stage;
                    if (stage % 2 == 0) os.CharacterWindowController?.BeginLinkMessageTyping();
                    else os.CharacterWindowController?.EndLinkMessageTyping();
                    PulseLiveHairclip();
                    if (stage == 3) OnDrainCue("failed_typing");
                }
                if (effectClock < beat.remaining) return false;
                os.CharacterWindowController?.EndLinkMessageTyping(); return true;
        }
        return beat.complete;
    }

    void BeginWindowCarry(bool toCenter)
    {
        if (liveWindow == null) return;
        if (toCenter) { State.Add("mirror_live_focus"); RefreshMirrorStage(); }
        if (!State.liveHomePositionSaved)
        { State.liveHomePosition = liveWindow.WindowRect.anchoredPosition; State.liveHomePositionSaved = true; }
        ownsWindowPosition = true;
        liveWindow.RestoreAndBringToFront();
        windowFrom = liveWindow.WindowRect.anchoredPosition;
        // Her face onto the reflection's (pancake 10-25).
        if (toCenter && presentation.MirrorRoot != null) windowTo = MirrorDockPosition();
        else windowTo = State.liveHomePosition;
        if (EnsureActor())
        { actor.ScriptActorSetPosition(this, TitlePoint(liveWindow.WindowRect)); actor.ScriptActorSetPressed(this, true); }
    }

    // A click can arrive after the story's Update. Apply both pointer visuals
    // from the presentation's shared press state before that frame is rendered.
    public void SetLetterPressVisual(bool pressed)
    {
        if (CurrentPhase == Phase.Letter) actor?.ScriptActorSetPressed(this, pressed);
    }

    bool RunLetter(OSChapter03StoryBeat beat, float dt)
    {
        if (!runtimeMemoryStarted)
        {
            PlayLetterSfx(beat);
            runtimeMemoryStarted = true; effectClock = lineClock = 0f; readingPage = -1; readingLine = 0; effectStage = 0;
            int token = generation;
            presentation.BeginLetter(PaperTitle("final_letter"), Content.PaperBody("final_letter"), () =>
            {
                if (token != generation) return;
                State.Add("letter_unfolded");
                for (int i = 0; i < State.letterPage; i++) presentation.AdvanceLetterPage();
            }, () =>
            {
                if (token != generation) return;
                State.Add("letter_collected"); beat.complete = true;
                OSStoryRecordUnlocks.RefreshMotherLetter(GameDataManager.Instance, true);
                actor?.ScriptActorSetPosition(this, actor.CompanionScreenPosition);
                actor?.ScriptActorSetPressed(this, false);
                StoryAchievementProgress.Record(StoryAchievementMilestone.MothersLetterRead);
            });
            // Older saves may have stopped during the removed collection animation.
            // Treat that close request as completed without replaying the fold or flight.
            if (Has("letter_collected") || Has("letter_collecting"))
            {
                presentation.HideModal(false); State.Add("letter_collected"); beat.complete = true;
                OSStoryRecordUnlocks.RefreshMotherLetter(GameDataManager.Instance);
                actor?.ScriptActorSetPosition(this, actor.CompanionScreenPosition);
                actor?.ScriptActorSetPressed(this, false);
            }
            else if (Has("letter_unfolded"))
            {
                presentation.RestoreUnfoldedLetter(0);
            }
            EnsureActor();
        }
        if (beat.complete) return true;
        effectClock += dt;
        if (!presentation.IsLetterUnfolded)
        {
            // The red pointer comes onto the seal once the envelope settles and waits there;
            // it presses together with the player's click (pancake 10-26).
            if (presentation.IsLetterEnvelopeReady && presentation.LetterPartnerPoint != null && EnsureActor())
            {
                if (effectStage == 0)
                {
                    effectStage = 1; effectClock = 0f;
                    actor.ScriptActorSetPressed(this, false);
                    actor.ScriptActorMoveTo(this, ScreenPoint(presentation.LetterPartnerPoint), .5f);
                }
                else if (effectClock >= .55f) actor.ScriptActorSetPosition(this, ScreenPoint(presentation.LetterPartnerPoint));
                presentation.SetLetterPartnerPull(Mathf.Clamp01((effectClock - .5f) / .1f));
            }
            // If the player waits, the red pointer taps the seal now and then; alone it cannot open it.
            bool tap = presentation.IsLetterEnvelopeReady && effectStage == 1 && effectClock > 3f
                && (effectClock - 3f) % 2.6f < .14f;
            if (actor != null) actor.ScriptActorSetPressed(this, presentation.IsLetterEnvelopePressed || tap);
        }
        else
        {
            State.letterPage = presentation.LetterPage;
            if (readingPage != presentation.LetterPage)
            { readingPage = presentation.LetterPage; readingLine = -1; lineClock = 0f; }
            lineClock += dt;
            int line = Mathf.Min(Mathf.FloorToInt(lineClock / 2.5f), Mathf.Max(0, presentation.LetterVisibleLineCount - 1));
            if ((line != readingLine || effectStage < 2) && presentation.TryGetLetterLineTarget(line, out var point) && EnsureActor())
            { readingLine = line; effectStage = 2; actor.ScriptActorSetPressed(this, false); actor.ScriptActorMoveTo(this, point, .35f); }
        }
        return false;
    }

    void BeginLiveTextEffect(string id)
    {
        effectEntryId = id; breakdownLineAge = 0f; sobCharactersSeen = 0;
        SetLivePortraitCue(id);
        if (id.StartsWith(LiveId("live.breakdown."), StringComparison.Ordinal)) OnBreakdownLine(id);
        if (id.StartsWith(LiveId("live.breakdown."), StringComparison.Ordinal)
            && id != LiveId("live.breakdown.09") && !Has("breakdown_music_fading"))
        { State.Add("breakdown_music"); PlayMusicCue(BreakdownCue); } // the prelude hands over to the melody
        if (id == LiveId("live.breakdown.08"))
            State.Add("breakdown_quiet");
        if (id == LiveId("live.breakdown.09"))
        { State.Add("breakdown_quiet"); State.Add("breakdown_music_fading"); FadeOutMusic(BreakdownFadeOutSeconds); }
    }

    public void OnChapter03TextProgress(string id, TMP_Text label)
    {
        if (!IsActive || !IsAvailable || label == null) return;
        if (!id.StartsWith(LiveId("live.breakdown."), StringComparison.Ordinal)) return;
        int count = Mathf.Min(label.maxVisibleCharacters, label.textInfo.characterCount);
        for (int i = sobCharactersSeen; i < count; i++)
            if (label.textInfo.characterInfo[i].character == '呜' || label.textInfo.characterInfo[i].character == '嗚')
                ShakeLivePortrait();
        string parsed = label.GetParsedText();
        foreach (string sob in new[] { "sob", "schluchz", "snif", "うぅ", "흑", "흐윽", "У-у" })
        {
            int index = parsed.IndexOf(sob, Mathf.Clamp(sobCharactersSeen - sob.Length + 1, 0, parsed.Length), StringComparison.OrdinalIgnoreCase);
            if (index >= 0 && index + sob.Length > sobCharactersSeen && index + sob.Length <= count)
                ShakeLivePortrait();
        }
        sobCharactersSeen = Mathf.Max(sobCharactersSeen, count);
    }

    void OnLiveTextEffect(string id)
    {
        if (id == LiveId("live.return.06")) StoryBeat(1.5f);
        OnDrainLine(id);
    }

    // Indoors the storm goes on: now and then light leaks round the curtains, the thunder with it
    // (the clap peaks about .3 s in, on the second flash).
    float nextLightning = -1f;
    void TickIndoorLightning()
    {
        float t = Time.unscaledTime;
        bool indoors = view != null && view.CurrentRoom != null && !view.CurrentRoom.outdoors;
        if (!indoors || IsCinematic || !Has("home_revealed") || CurrentPhase == Phase.Yard)
        { nextLightning = -1f; return; }
        if (nextLightning < 0f) { nextLightning = t + UnityEngine.Random.Range(12f, 24f); return; }
        if (t < nextLightning) return;
        view.Lightning(); AudioManager.Instance?.PlaySfx("Thunder_ForMainCore", .3f);
        nextLightning = t + UnityEngine.Random.Range(22f, 40f);
    }

    void TickStoryEffects()
    {
        float dt = Mathf.Min(Time.unscaledDeltaTime, .1f);
        TickIndoorLightning();
        TickGuide();
        breakdownLineAge += dt;
        readReceiptSeconds = Mathf.Max(0f, readReceiptSeconds - dt);
        if (waveform == null) waveform = os.GetComponentInChildren<HeartbeatWaveformUI>(true);
        if (waveform != null && normalBeatInterval < 0f) normalBeatInterval = waveform.NaturalBeatInterval;
        bool unstable = State.liveOffline || CurrentPhase == Phase.FinalKnock
            || CurrentPhase == Phase.Mirror && !Has("breakdown_quiet");
        if (unstable && Time.unscaledTime >= nextUnevenBeat)
        {
            bool accelerating = effectEntryId == LiveId("live.breakdown.05");
            float rules = view != null ? view.RulesBeatInterval : -1f;
            float interval = rules > 0f ? rules : accelerating ? Mathf.Lerp(.85f, .4f, Mathf.Clamp01(breakdownLineAge / 8f))
                : .48f + Mathf.Abs(Mathf.Sin(Time.unscaledTime * 3.71f)) * .46f;
            waveform?.SetNaturalBeatInterval(interval); nextUnevenBeat = Time.unscaledTime + interval;
        }
        else if (!unstable && waveform != null && normalBeatInterval > 0f)
            waveform.SetNaturalBeatInterval(Mathf.MoveTowards(waveform.NaturalBeatInterval, normalBeatInterval, dt * .25f));
        if (batteryFlash != null)
        {
            batteryFlashTime = Mathf.Max(0f, batteryFlashTime - dt);
            batteryFlash.color = new Color(1f, 1f, 1f, batteryFlashTime * 1.4f);
            if (batteryFlashTime <= 0f) { Destroy(batteryFlash.gameObject); batteryFlash = null; }
        }
        TickMusic(dt);
        TickSfx(dt);
        TickCloseup();
        TickDrains(dt);
        if (Has("deduction_done") && !Has("final_knock") && view != null)
            view.SetPlayerLightScale(Mathf.MoveTowards(view.PlayerLightScale, .48f, dt * .1f));
        if (Has("live_returned") && CurrentPhase == Phase.Lab && IsAvailable && EnsureActor())
        {
            Vector2 target = CursorHandle.Instance != null ? CursorHandle.Instance.DisplayedScreenPosition : HomeThingPoint("lab_letter");
            if (!actor.ScriptActorIsMoving && Vector2.Distance(actor.CompanionScreenPosition, target) > 35f)
                actor.ScriptActorMoveTo(this, target + new Vector2(25f, -12f), .5f);
        }
    }

    void StoryBeat(float strength)
    {
        if (waveform == null) waveform = os?.GetComponentInChildren<HeartbeatWaveformUI>(true);
        waveform?.GenerateStoryBeat(strength); view?.BeatDong();
    }
    void PulseBattery()
    {
        foreach (var resource in os.GetComponentsInChildren<OSResourceStateView>(true))
        {
            if (resource.ResourceType != ResourceType.Battery) continue;
            if (batteryFlash != null) Destroy(batteryFlash.gameObject);
            var pulse = new GameObject("Chapter03BatteryFlash", typeof(RectTransform), typeof(Image));
            pulse.layer = resource.gameObject.layer;
            var rect = (RectTransform)pulse.transform; rect.SetParent(resource.transform, false);
            rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.offsetMin = rect.offsetMax = Vector2.zero;
            batteryFlash = pulse.GetComponent<Image>(); batteryFlash.raycastTarget = false;
            batteryFlash.color = new Color(1f, 1f, 1f, .36f); batteryFlashTime = .26f;
            break;
        }
    }
    void PlayHomeStep(bool water) => PlayStepSfx(water);

    Vector2 HomeThingPoint(string id)
    {
        var rect = view?.transform as RectTransform; var thing = view?.CurrentRoom?.FindThing(id);
        if (rect == null || thing == null) return new Vector2(Screen.width * .65f, Screen.height * .5f);
        Vector2 point = new Vector2((thing.tile.x + thing.Width * .5f) * 16f - 120f, 80f - (thing.tile.y + .5f) * 16f);
        return RectTransformUtility.WorldToScreenPoint(CameraFor(rect), rect.TransformPoint(point));
    }
    static Camera CameraFor(RectTransform rect)
    { var canvas = rect?.GetComponentInParent<Canvas>(); return canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : null; }
    static Vector2 ScreenPoint(RectTransform rect) => RectTransformUtility.WorldToScreenPoint(CameraFor(rect), rect.TransformPoint(rect.rect.center));
    static Vector2 TitlePoint(RectTransform rect) => RectTransformUtility.WorldToScreenPoint(CameraFor(rect), rect.TransformPoint(new Vector2(rect.rect.center.x, rect.rect.yMax - 8f)));

    void RestoreExternalPresentation()
    {
        if (ownsWindowPosition && liveWindow != null && observed?.liveHomePositionSaved == true)
            liveWindow.WindowRect.anchoredPosition = observed.liveHomePosition;
        ownsWindowPosition = false;
        RestoreLiveAfterCloseup(); ReleaseLivePose();
        if (actor != null) actor.ReleaseScriptActor(this);
        if (waveform != null && normalBeatInterval > 0f) waveform.SetNaturalBeatInterval(normalBeatInterval);
        normalBeatInterval = -1f;
        StopMusicNow();
        StopSfx();
        if (batteryFlash != null) { Destroy(batteryFlash.gameObject); batteryFlash = null; }
        os?.CharacterWindowController?.ResetChapter03Effects();
        night?.SetChapter03Rain(false, false);
        os?.CharacterWindowController?.SetChapter03Presence(0, null);
        revealTime = -1f; lineClock = 0f;
    }
}
