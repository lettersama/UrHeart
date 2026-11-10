using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Authored final-treatment data. The runtime reads a resource manifest; the
/// editor QA chart is not used to construct or drive a production treatment.
/// Rhythm keeps the first treatment's serialized beat format and enum values.
/// </summary>
public sealed class OSChapter04FinalHealContent
{
    public const string ResourcePath = "Chapter04/FinalHeal/chart";

    [Serializable]
    public sealed class Note
    {
        // First-section visuals use the unique score id, so a stalled frame
        // cannot resolve an old phrase against its new second-section target.
        // Frozen targets retain phraseId for third-section pairId bindings.
        public string id;
        public string phraseId;
        public string pairId;
        public string text;
        public string textKey;
        public int side;
        public int section;
        // Rhythm and typography are independent: every third-section cut is
        // a Tap, including the lines drawn with Hold or Reinforced typography.
        public int textType = -1;
        public int motherIndex;
        public OSHeartHealBeatData rhythm = new OSHeartHealBeatData();
        // Base duration, before legacy reinforced speed is applied. The JSON
        // uses reinforcedSpeedMultiplier=1 so its heavy notes arrive at the
        // authored time without the old tool's extra approach slowdown.
        public float approachDuration = 2.2f;
        // Sinking words keep the approved frozen composition independently
        // of their real, playable lane beat. Zero uses the rhythm approach.
        public float visualApproachDuration;

        public bool HasText => !string.IsNullOrEmpty(textKey) || !string.IsNullOrEmpty(text);
        public bool IsAnswer => section == 3 && HasText && !string.IsNullOrEmpty(pairId);
        public bool IsFrozenTarget => section == 2 && HasText;
        public bool IsFinal => section == 4;
        public bool IsFirstMother => IsFinal && motherIndex == 1;
        public bool IsLastMother => IsFinal && motherIndex == 2;
        public string VisualId => IsFinal ? "D2" : HasText && section != 1 ? phraseId : id;
        public string LaneId => IsFrozenTarget ? id : VisualId;
        public OSHeartHealBeatType TextBeatType => textType >= 0 ? (OSHeartHealBeatType)textType : BeatType;
        public OSHeartHealBeatType BeatType => rhythm.BeatType;
        public OSHeartHealBeatMotionState MotionState => rhythm.MotionState;
        public float SpawnTime => rhythm.SpawnTime;
        public float ApproachDuration => rhythm.GetApproachDuration(approachDuration);
        public float HitTime => rhythm.GetHitTime(approachDuration);
        public float VisualApproachDuration => visualApproachDuration > 0f ? visualApproachDuration : ApproachDuration;
        public float VisualHitTime => SpawnTime + VisualApproachDuration;
        public float HoldDuration => rhythm.HoldDuration;
        public float HoldFeedbackInterval => rhythm.HoldFeedbackInterval;
        public float EndTime => rhythm.GetEndTime(approachDuration);
    }

    [Serializable]
    public sealed class EndingLine
    {
        public string textKey;
        public string text;
        public float holdSeconds = 1.5f;
    }

    [Serializable]
    public sealed class CreditsData
    {
        public string logoResource;
        public string chineseLogoResource;
        public string studioName;
        public int logoPixelSize = 1;
        public string[] staffKeys = Array.Empty<string>();
        public string[] staffFallback = Array.Empty<string>();
    }

    [Serializable]
    sealed class Source
    {
        public int version = 2;
        public float bpm = 80f;
        public int beatsPerBar = 4;
        public string musicResource;
        public string endingMusicResource;
        public string mamaWaitResource;
        public string bloomMusicResource;
        public string calmLoopResource;
        public string slashResource;
        public float placeholderMusicSeconds = 115f;
        public float sinkStartTime = 51f;
        public float freezeTime = 68.25f;
        public float releaseTime = 74.4f;
        public float firstMotherTime = 105f;
        public float finalNoteTime = 105.75f;
        public float loopStartTime = 111f;
        public float battleDuration = 115f;
        public float bloomLoopStartTime = 5.25f;
        public float endingDelay = 1.2f;
        public Note[] notes = Array.Empty<Note>();
        public EndingLine[] endingLines = Array.Empty<EndingLine>();
        public CreditsData credits = new CreditsData();
    }

    static OSChapter04FinalHealContent instance;
    readonly Source source;
    readonly string parseError;
    readonly List<string> errors;

    OSChapter04FinalHealContent(Source source, string parseError = null)
    {
        this.source = source ?? new Source();
        this.parseError = parseError;
        this.source.notes = this.source.notes ?? Array.Empty<Note>();
        this.source.endingLines = this.source.endingLines ?? Array.Empty<EndingLine>();
        this.source.credits = this.source.credits ?? new CreditsData();
        this.source.credits.staffKeys = this.source.credits.staffKeys ?? Array.Empty<string>();
        this.source.credits.staffFallback = this.source.credits.staffFallback ?? Array.Empty<string>();
        errors = ValidateSource();
    }

    public static OSChapter04FinalHealContent Instance => instance ?? (instance = Load());
    public bool Available => errors.Count == 0;
    public int Version => source.version;
    public float Bpm => source.bpm;
    public int BeatsPerBar => source.beatsPerBar;
    public string MusicResource => source.musicResource;
    public string EndingMusicResource => source.endingMusicResource;
    public string MamaWaitResource => source.mamaWaitResource;
    public string BloomMusicResource => source.bloomMusicResource;
    public string CalmLoopResource => source.calmLoopResource;
    public string SlashResource => source.slashResource;
    public float PlaceholderMusicSeconds => source.placeholderMusicSeconds;
    public float SinkStartTime => source.sinkStartTime;
    public float FreezeTime => source.freezeTime;
    public float ReleaseTime => source.releaseTime;
    public float FirstMotherTime => source.firstMotherTime;
    public float FinalNoteTime => source.finalNoteTime;
    public float LoopStartTime => source.loopStartTime;
    public float BattleDuration => source.battleDuration;
    public float BloomLoopStartTime => source.bloomLoopStartTime;
    public float EndingDelay => source.endingDelay;
    public IReadOnlyList<Note> Notes => source.notes;
    public IReadOnlyList<EndingLine> EndingLines => source.endingLines;
    public CreditsData Credits => source.credits;

    public static void ResetCache() => instance = null;
    public List<string> Validate() => new List<string>(errors);

    static OSChapter04FinalHealContent Load()
    {
        var resource = Resources.Load<TextAsset>(ResourcePath);
        return resource == null
            ? new OSChapter04FinalHealContent(null, "Missing final-treatment resource: " + ResourcePath)
            : FromJson(resource.text);
    }

    public static OSChapter04FinalHealContent FromJson(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return new OSChapter04FinalHealContent(null, "Final-treatment JSON is empty.");
        try
        {
            var parsed = JsonUtility.FromJson<Source>(json);
            return parsed == null
                ? new OSChapter04FinalHealContent(null, "Final-treatment JSON has no object.")
                : new OSChapter04FinalHealContent(parsed);
        }
        catch (ArgumentException exception)
        {
            return new OSChapter04FinalHealContent(null, "Final-treatment JSON: " + exception.Message);
        }
    }

    List<string> ValidateSource()
    {
        var result = new List<string>();
        if (parseError != null) { result.Add(parseError); return result; }
        if (source.version != 2) result.Add("Unsupported final-treatment version: " + source.version);
        if (!Finite(source.bpm) || source.bpm <= 0f || source.beatsPerBar < 1)
            result.Add("A positive tempo and meter are required.");
        if (!Finite(source.sinkStartTime) || source.sinkStartTime < 0f
            || !Finite(source.freezeTime) || source.freezeTime <= source.sinkStartTime
            || !Finite(source.releaseTime) || source.releaseTime <= source.freezeTime
            || !Finite(source.firstMotherTime) || source.firstMotherTime <= source.releaseTime
            || !Finite(source.finalNoteTime) || source.finalNoteTime <= source.firstMotherTime)
            result.Add("Final-treatment section times must be finite and strictly ordered.");
        if (!Finite(source.loopStartTime) || source.loopStartTime <= source.finalNoteTime
            || !Finite(source.battleDuration) || source.battleDuration <= source.loopStartTime
            || !Finite(source.bloomLoopStartTime) || source.bloomLoopStartTime <= 0f
            || Mathf.Abs(source.loopStartTime - source.finalNoteTime - source.bloomLoopStartTime) > .001f)
            result.Add("The authored battle and Bloom must reach the same calm-loop handoff.");
        if (!Finite(source.placeholderMusicSeconds) || source.placeholderMusicSeconds < source.finalNoteTime
            || !Finite(source.endingDelay) || source.endingDelay < 0f)
            result.Add("The placeholder track must cover the final beat; ending delay must be non-negative.");
        if (source.notes.Length == 0) result.Add("Final-treatment score is empty.");
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var targets = new HashSet<string>(StringComparer.Ordinal);
        var pairs = new HashSet<string>(StringComparer.Ordinal);
        int finalCount = 0, firstMotherCount = 0, lastMotherCount = 0;
        foreach (var note in source.notes)
        {
            if (note == null) { result.Add("Null final-treatment note."); continue; }
            if (string.IsNullOrWhiteSpace(note.id) || !ids.Add(note.id))
                result.Add("Missing or duplicate note id: " + note.id);
            if (note.rhythm == null) { result.Add("Missing rhythm: " + note.id); continue; }
            if (!Enum.IsDefined(typeof(OSHeartHealBeatType), note.BeatType)
                || !Enum.IsDefined(typeof(OSHeartHealBeatType), note.TextBeatType)
                || !Enum.IsDefined(typeof(OSHeartHealBeatMotionState), note.MotionState))
                result.Add("Unknown legacy beat type or motion: " + note.id);
            if (!Finite(note.SpawnTime) || note.SpawnTime < 0f
                || !Finite(note.approachDuration) || note.approachDuration < .1f
                || !Finite(note.HitTime) || !Finite(note.HoldDuration) || note.HoldDuration < 0f
                || !Finite(note.visualApproachDuration) || note.visualApproachDuration < 0f
                || !Finite(note.VisualHitTime)
                || !Finite(note.HoldFeedbackInterval) || note.HoldFeedbackInterval < .02f)
                result.Add("Invalid note timing: " + note.id);
            if (note.section < 1 || note.section > 4 || note.side < 0 || note.side > 4)
                result.Add("Invalid section or side: " + note.id);
            if (!note.IsFinal && note.motherIndex != 0)
                result.Add("Only the two closing words may have a mother index: " + note.id);
            if (note.HasText && (string.IsNullOrWhiteSpace(note.textKey)
                || string.IsNullOrWhiteSpace(note.text) || string.IsNullOrWhiteSpace(note.phraseId)))
                result.Add("Words require a localization key, Chinese fallback and phrase id: " + note.id);
            if (note.section == 1 && note.EndTime > source.sinkStartTime + .001f)
                result.Add("A struggle note crosses the sinking section: " + note.id);
            if (note.IsFrozenTarget)
            {
                if (!targets.Add(note.phraseId)) result.Add("Duplicate frozen target: " + note.phraseId);
                if (note.SpawnTime >= source.freezeTime || note.VisualHitTime <= source.freezeTime)
                    result.Add("Frozen word visuals must still be in flight at FreezeAll: " + note.id);
            }
            if (note.section == 2 && (note.HitTime < source.sinkStartTime || note.EndTime >= source.freezeTime))
                result.Add("Sinking beat lies outside the playable sinking section: " + note.id);
            if (note.section == 3)
            {
                if (!note.IsAnswer || note.SpawnTime < source.releaseTime - .001f || note.EndTime >= source.firstMotherTime
                    || note.BeatType != OSHeartHealBeatType.Tap || note.HoldDuration != 0f)
                    result.Add("Section three contains an invalid answer beat: " + note.id);
                if (!pairs.Add(note.pairId ?? string.Empty)) result.Add("A frozen target is paired twice: " + note.pairId);
            }
            if (note.IsFinal)
            {
                finalCount++;
                if (note.IsFirstMother) firstMotherCount++;
                if (note.IsLastMother) lastMotherCount++;
                float authoredHit = note.IsFirstMother ? source.firstMotherTime : source.finalNoteTime;
                if (!note.HasText || note.side != 4 || note.BeatType != OSHeartHealBeatType.Tap
                    || note.TextBeatType != OSHeartHealBeatType.Reinforced || note.phraseId != "D2"
                    || (!note.IsFirstMother && !note.IsLastMother) || !string.IsNullOrEmpty(note.pairId)
                    || Mathf.Abs(note.HitTime - authoredHit) > .001f)
                    result.Add("The closing words must be two unpaired taps sharing the heavy D2 presentation: " + note.id);
            }
        }
        if (finalCount != 2 || firstMotherCount != 1 || lastMotherCount != 1)
            result.Add("Exactly two ordered closing words are required.");
        if (!targets.SetEquals(pairs)) result.Add("Every frozen target needs exactly one answer pair.");
        if (source.endingLines.Length == 0) result.Add("At least one ending line is required.");
        foreach (var line in source.endingLines)
            if (line == null || string.IsNullOrWhiteSpace(line.textKey) || string.IsNullOrWhiteSpace(line.text)
                || !Finite(line.holdSeconds) || line.holdSeconds < 0f)
                result.Add("An ending line needs a localization key, fallback and non-negative hold.");
        if (source.credits.staffKeys.Length != source.credits.staffFallback.Length)
            result.Add("Credit localization keys and fallbacks must have equal lengths.");
        for (int i = 0; i < source.credits.staffKeys.Length; i++)
            if (string.IsNullOrWhiteSpace(source.credits.staffKeys[i])
                || i >= source.credits.staffFallback.Length || string.IsNullOrWhiteSpace(source.credits.staffFallback[i]))
                result.Add("Credit lines require localization keys and fallbacks.");
        return result;
    }

    static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
}
