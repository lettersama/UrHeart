using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Quiet domestic foley for Chapter 3. Sources belong to this director, so pausing
/// them never borrows the shared rain/music channels or a reusable SFX pool voice.
/// Saved beats own one-shot starts; room ambience is reconstructed independently.
/// </summary>
public sealed partial class OSChapter03StoryDirector
{
    sealed class SfxVoice
    {
        public AudioSource source;
        public bool allowInQuiet;
    }

    sealed class SfxLoop
    {
        public AudioSource source;
        public float target;
    }

    sealed class SfxPending
    {
        public string key;
        public float remaining, gain;
    }

    readonly List<SfxVoice> sfxVoices = new List<SfxVoice>();
    readonly List<SfxPending> sfxPending = new List<SfxPending>();
    readonly Dictionary<string, SfxLoop> sfxLoops = new Dictionary<string, SfxLoop>(StringComparer.Ordinal);
    readonly List<string> sfxFinishedLoops = new List<string>();
    readonly Dictionary<string, int> sfxLastVariant = new Dictionary<string, int>(StringComparer.Ordinal);
    // Foley variation must not change which unexplored clue the story randomly suggests.
    readonly System.Random sfxRandom = new System.Random();
    bool sfxPaused;
    string sfxRoom;
    float sfxHallLampLevel = 1f, sfxLastLampFault = float.NegativeInfinity;

#if UNITY_EDITOR
    // Observes actual new one-shots, never restore, pause or UnPause operations.
    public event Action<string, float, float> DebugSfxPlayed;
#endif

    bool CanPlaySfx => IsAvailable && !sfxPaused && !rebinding
        && home?.IsRestoringView != true && !OSSessionPersistence.IsRestoring;

    bool QuietSfxPassage
    {
        get
        {
            if (State == null) return true;
            if (CurrentPhase == Phase.FinalKnock || CurrentPhase == Phase.Letter || CurrentPhase == Phase.Complete
                || view?.IsPlayingRules == true) return true;
            if (State.memoryQueue.Count > 0 && State.memoryQueue[0].type == "effect"
                && State.memoryQueue[0].key == "resolve") return true;
            foreach (var beat in State.liveQueue)
                if (beat.key?.StartsWith("live.breakdown.", StringComparison.Ordinal) == true
                    || beat.key?.StartsWith("os.chapter03.live.breakdown.", StringComparison.Ordinal) == true) return true;
            return messages?.DisplayedEntryId?.StartsWith(LiveId("live.breakdown."), StringComparison.Ordinal) == true
                && (messages.IsPresenting || messages.IsReadingHoldActive || messages.IsWaitingForOption);
        }
    }

    AudioClip FindSfxClip(string key, bool ambient = false)
    {
        var data = GameDataManager.Instance?.AudioData;
        if (data == null || string.IsNullOrEmpty(key)) return null;
        if (ambient && data.AMBIENT != null)
            foreach (var entry in data.AMBIENT)
                if (entry != null && entry.keyid == key && entry.audioclip != null) return entry.audioclip;
        if (data.SFX != null)
            foreach (var entry in data.SFX)
                if (entry != null && entry.keyid == key) return entry.audioclip;
        return null;
    }

    AudioSource PlaySfxCue(string key, float gain = .3f, float pitch = 1f, bool allowInQuiet = false)
    {
        if (!CanPlaySfx || QuietSfxPassage && !allowInQuiet) return null;
        var manager = AudioManager.Instance;
        var clip = FindSfxClip(key);
        if (manager?.SFXMixerGroup == null || clip == null) return null;
        // Completed voices are removed by TickSfx; this cap also limits rapid UI spam.
        if (sfxVoices.Count >= 16) return null;
        var owner = new GameObject("Chapter03Sfx_" + key);
        owner.transform.SetParent(transform, false);
        var source = owner.AddComponent<AudioSource>();
        source.playOnAwake = false; source.loop = false; source.spatialBlend = 0f;
        source.outputAudioMixerGroup = manager.SFXMixerGroup;
        source.clip = clip; source.volume = Mathf.Clamp01(gain); source.pitch = pitch;
        sfxVoices.Add(new SfxVoice { source = source, allowInQuiet = allowInQuiet });
        source.Play();
#if UNITY_EDITOR
        DebugSfxPlayed?.Invoke(key, source.volume, source.pitch);
#endif
        return source;
    }

    void PlaySfxVariant(string prefix, int count, float gain)
    {
        if (!CanPlaySfx || QuietSfxPassage || count <= 0) return;
        int choice;
        if (count > 1 && sfxLastVariant.TryGetValue(prefix, out int previous))
        { choice = sfxRandom.Next(count - 1); if (choice >= previous) choice++; }
        else choice = sfxRandom.Next(count);
        float pitch = .97f + (float)sfxRandom.NextDouble() * .06f;
        if (PlaySfxCue(prefix + choice, gain, pitch) != null) sfxLastVariant[prefix] = choice;
    }

    void PlaySfxPair(string first, string second, float firstGain = .3f, float secondGain = .3f)
    {
        var source = PlaySfxCue(first, firstGain);
        if (source == null || string.IsNullOrEmpty(second)) return;
        sfxPending.Add(new SfxPending { key = second, gain = secondGain,
            remaining = Mathf.Max(.04f, source.clip.length / Mathf.Abs(source.pitch) - .03f) });
    }

    void PlayStepSfx(bool water)
    {
        if (view?.CurrentRoom == null) return;
        if (water) PlaySfxVariant("Chapter03_StepWet_", 3, .3f);
        else if (view.CurrentRoom.id == "yard") PlaySfxVariant("Chapter03_StepMud_", 3, .3f);
        else PlaySfxVariant("Chapter03_Step_", 4, .3f);
    }

    void PlayTextSfx(string key, bool firstBegin)
    {
        if (!firstBegin || string.IsNullOrEmpty(key)) return;
        switch (key.Replace("os.chapter03.", string.Empty))
        {
            case "interaction.west.bed_door.first":
            case "interaction.west.bed_door.repeat":
            case "interaction.west.bed_door.coffee": PlaySfxCue("Chapter02_Knock", .45f); break;
            case "interaction.kitchen.screwdriver.take":
            case "interaction.store.key.take": PlaySfxCue("MemoryHome_MetalPickup", .35f); break;
            case "interaction.kitchen.drawer": PlaySfxCue("Chapter02_Drawer", .35f); break;
            case "interaction.lab.bed.place_coffee": PlaySfxCue("Chapter02_CupSet", .3f); break;
            case "interaction.kitchen.sink.fill": PlaySfxCue("Chapter02_WaterPour", .32f); break;
            case "interaction.hall.coat": PlaySfxPair("Chapter02_Cloth", "Chapter03_PillboxRattle"); break;
            case "interaction.hall.sofa":
            case "interaction.study.curtain":
            case "interaction.bed.curtain":
            case "interaction.hall.curtain":
            case "interaction.yard.clothesline":
            case "memory.lab.bed.second.cold": PlaySfxCue("Chapter02_Cloth", .3f); break;
            case "interaction.store.toolbox": PlaySfxCue("MemoryHome_Toolbox", .35f); break;
            case "interaction.store.old_leg": PlaySfxCue("Chapter02_RobotJoint", .3f); break;
            case "interaction.yard.gate": PlaySfxCue("Chapter02_MetalLatch", .35f); break;
            case "memory.9060": PlaySfxCue("eaglaxle-electronic-beep-1-453381", .3f); break;
            case "interaction.east.store_door.open": PlaySfxCue("Chapter03_ScrewdriverTurn", .35f); break;
            case "interaction.kitchen.machine.brew": PlaySfxCue("Chapter03_CoffeeBrew", .3f); break;
            case "memory.9300": PlaySfxCue("Chapter03_LabDoorOpen", .35f); break;
            case "memory.9010": PlaySfxCue("Chapter03_MirrorCabinet", .3f); break;
            case "memory.9048": PlaySfxCue("Chapter03_MirrorTouch", .3f); break;
            case "interaction.lab.box": PlaySfxCue("Chapter03_BoxOpen", .3f); break;
            case "interaction.hall.phone": PlaySfxPair("Chapter03_PhoneLift", "Chapter03_PhoneStatic"); break;
            case "interaction.kitchen.fridge": PlaySfxCue("Chapter03_FridgeOpen", .3f); break;
            case "interaction.kitchen.sugar": PlaySfxCue("Chapter03_JarLid", .3f); break;
            case "interaction.bed.wardrobe": PlaySfxCue("Chapter03_Wardrobe", .3f); break;
            case "interaction.store.boxes":
            case "interaction.junk.boxes": PlaySfxCue("Chapter03_Cardboard", .3f); break;
            case "interaction.junk.medicine": PlaySfxCue("Chapter03_PillboxRattle", .3f); break;
            case "interaction.junk.wheelchair": PlaySfxCue("Chapter03_Wheelchair", .3f); break;
            case "interaction.yard.tree": PlaySfxCue("Chapter03_LeavesRain", .3f); break;
        }
    }

    // These actions depend on the pre-interaction inventory, before their text is queued.
    void PlayThingSfx(string id)
    {
        if (State == null) return;
        switch (id)
        {
            case "store_makeup":
                PlaySfxPair("Chapter03_Zipper", Has("makeup_seen") ? null : "Chapter03_KeyDrop"); break;
            case "kitchen_coffee_jar":
                if (!Has("coffee_beans")) PlaySfxCue("Chapter03_BeansPour", .32f); break;
            case "kitchen_cup":
                if (Has("coffee_brewed") && !Has("coffee_taken")) PlaySfxPair("Chapter02_WaterPour", "MemoryHome_Pickup", .32f);
                else PlaySfxCue("MemoryHome_Pickup", .3f);
                break;
            case "bed_nightstand":
                if (State.items.Contains("coffee")) PlaySfxCue("Chapter02_CupSet", .3f); break;
        }
    }

    void PlayDoorSfx(OSHomeDoor door)
    {
        if (door == null || State == null) return;
        if (door.id == "bed_door" && door.locked && State.items.Contains("key"))
            PlaySfxCue("Chapter03_KeyUnlock", .35f);
        else if (door.id == "junk_door") PlaySfxCue("Chapter03_HingeCreak", .3f);
        else if (door.locked && (door.id == "lab_door" || door.id == "stuck_door"
            || door.id == "front_door" || door.id == "yard_house")) PlaySfxCue("MemoryHome_DoorLocked", .4f);
    }

    void PlayRoomSfx(OSHomeRoom room)
    {
        if (room == null || !CanPlaySfx) return;
        // Animated room swaps report RoomEntered after EnterRoom's rebinding scope
        // has ended. The saved room beat already owns that door sound.
        bool scripted = runtimeMemoryBeat?.type == "room" && runtimeMemoryBeat.begun && runtimeMemoryBeat.key == room.id;
        if (!scripted && !string.IsNullOrEmpty(sfxRoom) && sfxRoom != room.id && State.homeOpened)
            PlaySfxCue("MemoryHome_DoorOpen", .35f);
        sfxRoom = room.id;
    }

    void PlayRoomBeatSfx(OSChapter03StoryBeat beat)
    {
        if (!beat.begun && view?.CurrentRoom != null && view.CurrentRoom.id != beat.key)
            PlaySfxCue("MemoryHome_DoorOpen", .35f);
        beat.begun = true;
    }

    void PlayEffectSfx(OSChapter03StoryBeat beat)
    {
        if (!beat.begun)
        {
            switch (beat.key)
            {
                case "zoom_in": case "zoom_out": PlaySfxCue("Ch01End_ShotSwitch", .25f); break;
                case "live_to_center": PlaySfxCue("Chapter02_CursorCapture", .3f); break;
                case "live_to_home": PlaySfxCue("Chapter02_CursorRelease", .3f); break;
            }
        }
        beat.begun = true;
    }

    void PlayPaperSfx(OSChapter03StoryBeat beat = null)
    {
        if (beat == null || !beat.begun) PlaySfxCue("Chapter02_PaperRustle", .35f);
    }

    // The envelope slides in with a rustle; it is torn open only when both pointers press it.
    void PlayLetterSfx(OSChapter03StoryBeat beat)
    {
        if (!beat.begun && !Has("letter_unfolded") && !Has("letter_collected"))
            PlaySfxCue("Chapter02_PaperRustle", .25f, allowInQuiet: true);
        beat.begun = true;
    }

    public void PlayLetterOpenSfx() => PlaySfxCue("Chapter03_EnvelopeOpen", .35f, allowInQuiet: true);

    public void PlayKeypadPressSfx() => PlaySfxVariant("Chapter03_KeypadPress_", 3, .3f);
    void PlayKeypadResultSfx(bool accepted) => PlaySfxCue(accepted ? "Chapter03_KeypadAccept" : "Chapter03_KeypadError", .3f);

    void PlayItemStowSfx(string id, bool animate)
    {
        if (animate && items != null && items.GetItem(id) == null) PlaySfxCue("Chapter03_ItemStow", .3f);
    }

    // The visual calls at its actual fault, once per fault start rather than once per flicker.
    public void PlayLampFaultSfx(OSHomeView sourceView, string lightId, float faultStart)
    {
        if (sourceView != view || view?.CurrentRoom?.id != "study" || sfxLastLampFault == faultStart) return;
        sfxLastLampFault = faultStart;
        // Paint can run before Home is revealed; do not replay that fault when it becomes visible.
        if (State?.homeOpened != true || !Has("home_revealed")) return;
        PlaySfxCue("Chapter03_LampFlicker", .3f);
    }

    public void ObserveLampSfx(OSHomeView sourceView, OSHomeLight light, float strength)
    {
        if (sourceView == view && view?.CurrentRoom?.id == "hall" && light?.kind == OSHomeLightKind.Breathing)
            sfxHallLampLevel = Mathf.Clamp01(strength);
    }

    void SetSfxLoop(string key, float gain)
    {
        if (!sfxLoops.TryGetValue(key, out var loop))
        {
            var manager = AudioManager.Instance;
            var clip = FindSfxClip(key, true);
            if (clip == null || manager?.AmbientMixerGroup == null) return;
            var owner = new GameObject("Chapter03Ambient_" + key);
            owner.transform.SetParent(transform, false);
            var source = owner.AddComponent<AudioSource>();
            source.playOnAwake = false; source.loop = true; source.spatialBlend = 0f;
            source.outputAudioMixerGroup = manager.AmbientMixerGroup;
            source.clip = clip; source.volume = 0f; source.pitch = 1f;
            loop = new SfxLoop { source = source }; sfxLoops.Add(key, loop);
            source.Play();
        }
        loop.target = gain;
    }

    void TickSfx(float dt)
    {
        if (sfxPaused) return;
        bool quiet = QuietSfxPassage;
        for (int i = sfxVoices.Count - 1; i >= 0; i--)
        {
            var voice = sfxVoices[i];
            if (voice.source != null && voice.source.isPlaying && (!quiet || voice.allowInQuiet)) continue;
            if (voice.source != null) { voice.source.Stop(); Destroy(voice.source.gameObject); }
            sfxVoices.RemoveAt(i);
        }
        if (quiet) sfxPending.Clear();
        else for (int i = sfxPending.Count - 1; i >= 0; i--)
        {
            var cue = sfxPending[i]; cue.remaining -= dt;
            if (cue.remaining > 0f) continue;
            sfxPending.RemoveAt(i); PlaySfxCue(cue.key, cue.gain);
        }
        foreach (var loop in sfxLoops.Values) loop.target = 0f;
        string room = view?.CurrentRoom?.id;
        sfxRoom = room;
        if (!quiet && State.homeOpened && Has("home_revealed") && CurrentPhase != Phase.YardReturn)
        {
            switch (room)
            {
                case "study": case "west": case "east": case "bed": SetSfxLoop("Chapter03_RoomTone", .1f); break;
                case "hall": SetSfxLoop("Chapter03_RoomTone", .1f); SetSfxLoop("Chapter03_LampBuzz", .1f * sfxHallLampLevel); break;
                case "kitchen": SetSfxLoop("Chapter03_KitchenClock", .1f); SetSfxLoop("Chapter03_Drip", .1f); break;
                case "bath": SetSfxLoop("Chapter03_Drip", .12f); break;
                case "store": SetSfxLoop("Chapter03_TubeBuzz", .1f); break;
                case "lab": SetSfxLoop("Chapter03_LabHum", .12f); break;
            }
        }
        sfxFinishedLoops.Clear();
        foreach (var pair in sfxLoops)
        {
            var loop = pair.Value;
            if (loop.source == null) { sfxFinishedLoops.Add(pair.Key); continue; }
            loop.source.volume = Mathf.MoveTowards(loop.source.volume, loop.target, dt * .16f);
            if (loop.target > 0f || loop.source.volume > 0f) continue;
            loop.source.Stop(); Destroy(loop.source.gameObject); sfxFinishedLoops.Add(pair.Key);
        }
        foreach (string key in sfxFinishedLoops) sfxLoops.Remove(key);
    }

    void PauseSfx(bool paused)
    {
        if (sfxPaused == paused) return;
        sfxPaused = paused;
        foreach (var voice in sfxVoices)
            if (voice.source != null) { if (paused) voice.source.Pause(); else voice.source.UnPause(); }
        foreach (var loop in sfxLoops.Values)
            if (loop.source != null) { if (paused) loop.source.Pause(); else loop.source.UnPause(); }
    }

    void StopSfx()
    {
        foreach (var voice in sfxVoices)
            if (voice.source != null) { voice.source.Stop(); Destroy(voice.source.gameObject); }
        foreach (var loop in sfxLoops.Values)
            if (loop.source != null) { loop.source.Stop(); Destroy(loop.source.gameObject); }
        sfxVoices.Clear(); sfxLoops.Clear(); sfxPending.Clear(); sfxLastVariant.Clear(); sfxFinishedLoops.Clear();
        sfxRoom = null; sfxHallLampLevel = 1f; sfxLastLampFault = float.NegativeInfinity; sfxPaused = false;
    }
}
