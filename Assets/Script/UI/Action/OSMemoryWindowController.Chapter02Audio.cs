using System.Collections.Generic;
using UnityEngine;

// Sound belongs to actual reading transitions, not localization or save reconstruction.
// Animation presenters claim their own cues only after the corresponding node was read.
public sealed partial class OSMemoryWindowController
{
    readonly HashSet<int> chapter02AudioNodes = new HashSet<int>();
    readonly HashSet<string> chapter02AudioClaims = new HashSet<string>();
    readonly List<Chapter02Voice> chapter02Voices = new List<Chapter02Voice>();
    readonly List<Chapter02PendingCue> chapter02Pending = new List<Chapter02PendingCue>();
    OSMemoryProcessSaveData chapter02AudioState;
    OSWindowUnit chapter02AudioSettings;
    Chapter02AudioLoop chapter02QuakeLoop;
    Animator chapter02PackingAnimator;
    float chapter02PackingPosition = -1f, chapter02QuakeFade = -1f;
    bool reconstructingChapter02Audio;

    sealed class Chapter02Voice { public AudioSource source; public AudioClip clip; public string cue; }
    sealed class Chapter02PendingCue { public int node; public string cue; public float gain, delay; }

    public bool Chapter02AudioAvailable
    {
        get
        {
            var os = OS_Handle.Instance;
            if (chapter02AudioSettings == null && os != null)
                chapter02AudioSettings = os.transform.Find("Windows/WindowUnit_Option")?.GetComponent<OSWindowUnit>();
            return Application.isFocused && isActiveAndEnabled && GameDataManager.Instance?.CurrentChapterIndex == 2
                && os != null && os.IsOSVisible && !os.IsSystemInputLocked
                && !OSSessionPersistence.IsRestoring && GameSessionFlow.Instance?.IsBusy != true
                && !GameTestConsole.BlocksGameplayInput
                && !(chapter02AudioSettings != null && chapter02AudioSettings.IsOpen && !chapter02AudioSettings.IsMinimized)
                && window != null && window.IsOpen && !window.IsMinimized && !window.IsClosing;
        }
    }

    void ObserveChapter02AudioNode(int node, int previous, bool recordAsTriggered)
    {
        SyncChapter02AudioState();
        if (node != previous) chapter02Pending.Clear();
        if (node == 5970) StopChapter02Cue("CartRoll");
        if (node == 5800)
        {
            StopChapter02Cue("QuakeImpact"); StopChapter02Cue("BeamCrack"); StopChapter02Cue("Debris");
        }
        if (node < 5450 || node >= 5800) { chapter02QuakeFade = -1f; chapter02QuakeLoop?.Stop(); }
        if (OSSessionPersistence.IsRestoring || reconstructingChapter02Audio) return;
        if (!recordAsTriggered || node == previous || GameDataManager.Instance?.CurrentChapterIndex != 2) return;
        chapter02AudioNodes.Add(node);
        if (node == 5780 && previous >= 5450 && previous < 5780) chapter02QuakeFade = 0f;
        if (!Chapter02AudioAvailable) return;

        // Small concrete actions punctuate reading; dialogue and static poses stay quiet.
        switch (node)
        {
            case 4040: StoryCue(node, "PointerGlitch", .22f); break;
            case 4160: StoryCue(node, "PenCap", .60f); break;
            case 4170: StoryCue(node, "Drawer", .58f); break;
            case 4210: case 4430: StoryCue(node, "CupSet", .58f); break;
            case 4320: StoryCue(node, "PaperRustle", .50f); break;
            case 4370:
                StoryCue(node, "TableTap", .58f);
                LaterChapter02Cue(node, "TableTap", .50f, .22f);
                LaterChapter02Cue(node, "CupSet", .50f, .55f); break;
            case 4410: StoryCue(node, "WaterPour", .45f); break;
            case 4460: case 6200: StoryCue(node, "RobotJoint", .55f); break;
            case 4480: StoryCue(node, "Tape", .58f); break;
            case 4690: case 6500: StoryCue(node, "PenWrite", .48f); break;
            case 4780: case 6250: StoryCue(node, "ToolRattle", .52f); break;
            case 4800: case 5040: case 5100: StoryCue(node, "BagRustle", .52f); break;
            case 5010: StoryCue(node, "LightSwitch", .48f); break;
            case 5060: StoryCue(node, "ToolRattle", .48f); break;
            case 5150: case 5170: case 5880: StoryCue(node, "MetalLatch", .58f); break;
            case 5300: StoryCue(node, "ToolRattle", .55f); break;
            case 5310: StoryCue(node, "MetalScrape", .65f); break;
            case 5320: StoryCue(node, "MetalSnap", .78f); break;
            case 5330: StoryCue(node, "DustFall", .50f); break;
            case 5370: case 5380: case 5850: case 6810: case 7360: StoryCue(node, "Cloth", .55f); break;
            case 5450: StoryCue(node, "QuakeImpact", .40f); break;
            case 5460: StoryCue(node, "Packing", .48f); break;
            case 5490: StoryCue(node, "QuakeImpact", .72f); break;
            case 5570: StoryCue(node, "BeamCrack", .78f); break;
            case 5600: StoryCue(node, "PaperRustle", .70f); break;
            case 5610: StoryCue(node, "QuakeImpact", .85f); break;
            case 5620: case 5700: case 5760: StoryCue(node, "Debris", .60f); break;
            case 5710: case 5840: StoryCue(node, "Spark", .58f); break;
            case 5930: case 5960: case 6010: StoryCue(node, "CartRoll", .58f); break;
            case 5970: StoryCue(node, "RobotJoint", .45f); break;
            case 6020: StoryCue(node, "TableTap", .65f); break;
            case 6030: case 6790: StoryCue(node, "LightSwitch", .55f); break;
            case 6120: StoryCue(node, "ToolRattle", .55f); LaterChapter02Cue(node, "Tape", .50f, .65f); break;
            case 6130: case 6640: StoryCue(node, "RobotJoint", .50f); break;
            case 6190: StoryCue(node, "Cloth", .52f); LaterChapter02Cue(node, "LightSwitch", .50f, .65f); break;
            case 6210: case 6260: StoryCue(node, "CupSet", .54f); break;
            case 6220: StoryCue(node, "WaterPour", .55f); break;
            case 6460: StoryCue(node, "Cloth", .48f); break;
            case 6590: StoryCue(node, "ToolRattle", .45f); break;
            case 6600: StoryCue(node, "Knock", .65f); LaterChapter02Cue(node, "Knock", .60f, .32f); break;
            case 6690: StoryCue(node, "Drawer", .54f); break;
            case 6900: StoryCue(node, "BagRustle", .55f); break;
            case 7080: StoryCue(node, "MetalLatch", .45f); break;
            case 7320: StoryCue(node, "Drawer", .48f, 1.15f); break;
            case 7340: StoryCue(node, "MetalLatch", .50f); break;
            case 7430: StoryCue(node, "PaperTear", .65f); LaterChapter02Cue(node, "PaperRustle", .50f, .65f); break;
            case 7460: StoryCue(node, "LightSwitch", .58f); break;
        }
    }

    void StoryCue(int node, string cue, float gain, float pitch = 1f)
        => TryPlayChapter02PresentationCue(node, "story", cue, gain, pitch);

    // A claim survives localization and window reopen. Permission does not survive
    // closing the window or loading a save, so an interrupted visual cannot replay it.
    public bool TryPlayChapter02PresentationCue(int node, string token, string cue, float gain = 1f, float pitch = 1f)
    {
        if (!Chapter02AudioAvailable || !chapter02AudioNodes.Contains(node)) return false;
        if (!chapter02AudioClaims.Add(node + ":" + token)) return false;
        PlayChapter02Owned(cue, gain, pitch);
        return true;
    }

    void PlayChapter02Owned(string cue, float gain, float pitch = 1f)
    {
        AudioSource source = Chapter02Audio.Play("Chapter02_" + cue, gain, pitch);
        if (source != null) chapter02Voices.Add(new Chapter02Voice { source = source, clip = source.clip, cue = cue });
    }

    void LaterChapter02Cue(int node, string cue, float gain, float delay)
        => chapter02Pending.Add(new Chapter02PendingCue { node = node, cue = cue, gain = gain, delay = delay });

    void SyncChapter02AudioState()
    {
        var state = GameDataManager.Instance?.OSProcessData.memory;
        if (ReferenceEquals(chapter02AudioState, state)) return;
        ResetChapter02Audio();
        chapter02AudioState = state;
    }

    void TickChapter02Audio()
    {
        SyncChapter02AudioState();
        if (!Chapter02AudioAvailable)
        {
            chapter02QuakeLoop?.Stop();
            StopChapter02Voices();
            chapter02Pending.Clear();
            chapter02PackingPosition = -1f;
            return;
        }
        int node = currentData != null ? currentData.DataIndex : -1;
        float dt = Mathf.Min(Time.unscaledDeltaTime, .05f);
        for (int i = chapter02Pending.Count - 1; i >= 0; i--)
        {
            var pending = chapter02Pending[i];
            if (pending.node != node) { chapter02Pending.RemoveAt(i); continue; }
            pending.delay -= dt;
            if (pending.delay > 0f) continue;
            PlayChapter02Owned(pending.cue, pending.gain);
            chapter02Pending.RemoveAt(i);
        }
        chapter02Voices.RemoveAll(v => v.source == null || v.source.clip != v.clip || !v.source.isPlaying);

        float rumble = node >= 5460 && node < 5490 ? .15f : node >= 5490 && node < 5780 ? .27f : 0f;
        if (chapter02QuakeFade >= 0f && node >= 5780 && node < 5800)
        {
            chapter02QuakeFade += dt;
            rumble = .27f * Mathf.Clamp01(1f - chapter02QuakeFade / 2f);
            if (chapter02QuakeFade >= 2f) chapter02QuakeFade = -1f;
        }
        if (rumble > 0f)
        {
            if (chapter02QuakeLoop == null) chapter02QuakeLoop = new Chapter02AudioLoop(this);
            chapter02QuakeLoop.Set("Chapter02_QuakeRumble", rumble);
        }
        else chapter02QuakeLoop?.Stop();
        TickChapter02PackingAudio(node);
    }

    void TickChapter02PackingAudio(int node)
    {
        var content = subwindowPlayer != null ? subwindowPlayer.CurrentContent : null;
        if (node != 5400 || content == null || !content.gameObject.activeInHierarchy)
        {
            chapter02PackingAnimator = null; chapter02PackingPosition = -1f; return;
        }
        if (chapter02PackingAnimator == null)
            chapter02PackingAnimator = content.GetComponentInChildren<Animator>();
        if (chapter02PackingAnimator == null || !chapter02PackingAnimator.isActiveAndEnabled) return;
        AnimatorStateInfo state = chapter02PackingAnimator.GetCurrentAnimatorStateInfo(0);
        if (!state.IsName("Collecting")) { chapter02PackingPosition = -1f; return; }
        float position = state.normalizedTime * 7.71f;
        float previous = chapter02PackingPosition;
        chapter02PackingPosition = position;
        // Read the animation clock. A resume or skipped frame never catches up old sounds.
        if (previous < 0f || position < previous || position - previous > .25f) return;
        int cycle = Mathf.FloorToInt(position / 7.71f);
        for (int c = Mathf.Max(0, cycle - 1); c <= cycle; c++)
        {
            float start = c * 7.71f;
            if ((previous < start + 1.65f && position >= start + 1.65f)
                || (previous < start + 3.7f && position >= start + 3.7f))
                PlayChapter02Owned("Packing", .45f);
            if (previous < start + 5.2f && position >= start + 5.2f)
                PlayChapter02Owned("PaperRustle", .45f);
        }
    }

    void StopChapter02Voices()
    {
        foreach (var voice in chapter02Voices)
            if (voice.source != null && voice.source.clip == voice.clip) voice.source.Stop();
        chapter02Voices.Clear();
    }

    void StopChapter02Cue(string cue)
    {
        for (int i = chapter02Voices.Count - 1; i >= 0; i--)
        {
            var voice = chapter02Voices[i];
            if (voice.cue != cue) continue;
            if (voice.source != null && voice.source.clip == voice.clip) voice.source.Stop();
            chapter02Voices.RemoveAt(i);
        }
    }

    void SuspendChapter02Audio()
    {
        chapter02AudioNodes.Clear();
        chapter02Pending.Clear();
        chapter02QuakeLoop?.Stop();
        chapter02PackingAnimator = null; chapter02PackingPosition = -1f;
        StopChapter02Voices();
    }

    void ResetChapter02Audio()
    {
        SuspendChapter02Audio();
        chapter02AudioClaims.Clear();
        chapter02QuakeFade = -1f;
    }

    void DisposeChapter02Audio()
    {
        ResetChapter02Audio();
        chapter02QuakeLoop?.Dispose();
        chapter02QuakeLoop = null;
    }
}
