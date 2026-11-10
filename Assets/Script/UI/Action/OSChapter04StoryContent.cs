using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Chapter-four routing only. Text, options and localization remain in the
/// existing interaction data; TV artwork and animation are separate consumers.
/// </summary>
public sealed class OSChapter04StoryContent
{
    public const string EntryPrefix = "chapter04_story_";
    public const string FinalHeartHealAction = "finalHeartHeal";
    public const string FailureWarningAction = "failureWarning";

    [Serializable]
    public sealed class OptionRoute
    {
        public string optionId;
        public string nextEntryId;
    }

    [Serializable]
    public sealed class ResumeRoute
    {
        public string oldEntryId;
        public string nextEntryId;
        public string theatreState;
        public int heartWeightBefore;
        public int faultWeightBefore;
    }

    [Serializable]
    public sealed class Beat
    {
        public string entryId;
        public string signalId;
        // Retained for saves already at this retired line. New story routes
        // must not enter it, and it cannot charge resources or replay a cue.
        public bool legacyResumeOnly;
        public string nextEntryId;
        public string theatreStateAfterText;
        // A date begins a new section and is inherited by following authored beats.
        public string dateLocalizationKey;
        public string dateFallback;
        public int heartWeight;
        public int faultWeight;
        public int griefTier;
        public int griefLineStep;
        // Applied on leaving the final line, after its reading delay / Continue.
        public string endAction;
        // Negative means an explicit TryContinue request from the presentation.
        public float autoAdvanceDelay = -1f;
        public OptionRoute[] options = new OptionRoute[0];
    }

    [Serializable]
    sealed class Source
    {
        public int version = 1;
        public string firstEntryId;
        public string dateLocalizationKey;
        public string dateFallback;
        public int heartWeightBudget = 12;
        public int faultWeightBudget = 12;
        public ResumeRoute[] resumeRoutes = new ResumeRoute[0];
        public Beat[] beats = new Beat[0];
    }

    static OSChapter04StoryContent instance;
    readonly Source source;
    readonly string parseError;
    readonly Dictionary<string, Beat> byId = new Dictionary<string, Beat>(StringComparer.Ordinal);

    OSChapter04StoryContent(Source source, string parseError = null)
    {
        this.source = source ?? new Source();
        this.parseError = parseError;
        if (this.source.beats == null) this.source.beats = new Beat[0];
        foreach (var beat in this.source.beats)
        {
            if (beat == null) continue;
            if (beat.options == null) beat.options = new OptionRoute[0];
            // Keep the first definition. Validation reports duplicates instead
            // of silently selecting whichever one happened to load last.
            if (!string.IsNullOrWhiteSpace(beat.entryId) && !byId.ContainsKey(beat.entryId))
                byId.Add(beat.entryId, beat);
        }
    }

    public static OSChapter04StoryContent Instance => instance ?? (instance = Load());
    public bool Available => parseError == null && !string.IsNullOrWhiteSpace(FirstEntryId)
        && Find(FirstEntryId) != null;
    public int Version => source.version;
    public int HeartWeightBudget => source.heartWeightBudget > 0 ? source.heartWeightBudget : 12;
    public int FaultWeightBudget => source.faultWeightBudget > 0 ? source.faultWeightBudget : 12;
    public string FirstEntryId => source.firstEntryId;
    public string DateLocalizationKey => source.dateLocalizationKey;
    public string DateFallback => source.dateFallback;
    public IReadOnlyList<Beat> Beats => source.beats;

    public Beat Find(string entryId) => entryId != null && byId.TryGetValue(entryId, out var beat) ? beat : null;
    public ResumeRoute FindResumeRoute(string entryId)
    {
        if (source.resumeRoutes != null)
            foreach (var route in source.resumeRoutes)
                if (route != null && route.oldEntryId == entryId) return route;
        return null;
    }
    public static void ResetCache() => instance = null;

    public void GetDate(string entryId, out string key, out string fallback)
    {
        key = DateLocalizationKey;
        fallback = DateFallback;
        if (string.IsNullOrEmpty(entryId)) return;
        foreach (var beat in source.beats)
        {
            if (beat == null) continue;
            if (!string.IsNullOrWhiteSpace(beat.dateLocalizationKey) || !string.IsNullOrWhiteSpace(beat.dateFallback))
            { key = beat.dateLocalizationKey; fallback = beat.dateFallback; }
            if (beat.entryId == entryId) return;
        }
        // Unknown IDs are not associated with the date of the final section.
        key = DateLocalizationKey;
        fallback = DateFallback;
    }

    static OSChapter04StoryContent Load()
    {
        var asset = Resources.Load<TextAsset>("Chapter04/story");
        return asset == null ? new OSChapter04StoryContent(new Source()) : FromJson(asset.text);
    }

    public static OSChapter04StoryContent FromJson(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return new OSChapter04StoryContent(new Source(), "Chapter04 story JSON is empty.");
        try
        {
            var parsed = JsonUtility.FromJson<Source>(json);
            return parsed == null
                ? new OSChapter04StoryContent(new Source(), "Chapter04 story JSON has no object.")
                : new OSChapter04StoryContent(parsed);
        }
        catch (ArgumentException exception)
        {
            return new OSChapter04StoryContent(new Source(), "Chapter04 story JSON: " + exception.Message);
        }
    }

    public List<string> Validate(OSCharacterInteractionData_SO data)
    {
        var errors = new List<string>();
        if (parseError != null) { errors.Add(parseError); return errors; }
        // An intentionally empty manifest is valid while the script is being
        // authored. It must not start a story or manufacture production lines.
        if (source.beats.Length == 0 && string.IsNullOrWhiteSpace(FirstEntryId)) return errors;
        if (source.version != 1) errors.Add("Unsupported Chapter04 story version: " + source.version);
        if (string.IsNullOrWhiteSpace(FirstEntryId) || Find(FirstEntryId) == null)
            errors.Add("firstEntryId must name an existing beat.");
        else if (Find(FirstEntryId).legacyResumeOnly)
            errors.Add("firstEntryId cannot be a legacy-only resume beat.");
        if (data == null) errors.Add("OSCharacterInteractionData is missing.");

        var resumedIds = new HashSet<string>(StringComparer.Ordinal);
        if (source.resumeRoutes != null)
            foreach (var route in source.resumeRoutes)
            {
                if (route == null || string.IsNullOrWhiteSpace(route.oldEntryId)
                    || !resumedIds.Add(route.oldEntryId) || Find(route.nextEntryId) == null
                    || Find(route.nextEntryId).legacyResumeOnly || route.oldEntryId == route.nextEntryId
                    || !OSChapter04PresentationBridge.IsLoopState(route.theatreState)
                    || route.heartWeightBefore < 0 || route.heartWeightBefore > HeartWeightBudget
                    || route.faultWeightBefore < 0 || route.faultWeightBefore > FaultWeightBudget)
                    errors.Add("Invalid or duplicate legacy resume mapping.");
            }

        var ids = new HashSet<string>(StringComparer.Ordinal);
        var signals = new HashSet<string>(StringComparer.Ordinal);
        foreach (var beat in source.beats)
        {
            if (beat == null) { errors.Add("Null story beat."); continue; }
            string label = string.IsNullOrWhiteSpace(beat.entryId) ? "<missing entryId>" : beat.entryId;
            if (string.IsNullOrWhiteSpace(beat.entryId) || !beat.entryId.StartsWith(EntryPrefix, StringComparison.Ordinal))
                errors.Add(label + ": entryId must start with " + EntryPrefix);
            if (!ids.Add(beat.entryId ?? string.Empty)) errors.Add(label + ": duplicate entryId.");
            if (string.IsNullOrWhiteSpace(beat.signalId)) errors.Add(label + ": signalId is required.");
            else if (!signals.Add(beat.signalId)) errors.Add(label + ": duplicate signalId " + beat.signalId);
            if (float.IsNaN(beat.autoAdvanceDelay) || float.IsInfinity(beat.autoAdvanceDelay))
                errors.Add(label + ": autoAdvanceDelay must be finite.");
            if (!string.IsNullOrWhiteSpace(beat.theatreStateAfterText)
                && !OSChapter04PresentationBridge.IsStoryCue(beat.theatreStateAfterText))
                errors.Add(label + ": unsupported theatre cue " + beat.theatreStateAfterText);
            if (beat.heartWeight < 0 || beat.faultWeight < 0
                || beat.heartWeight > HeartWeightBudget
                || beat.faultWeight > FaultWeightBudget)
                errors.Add(label + ": resource weights must be between zero and the chapter budget.");
            if (beat.griefTier < 0 || beat.griefTier > 3 || beat.griefLineStep < 0 || beat.griefLineStep > 3)
                errors.Add(label + ": grief tier and line step must be between zero and three.");
            if (beat.legacyResumeOnly && (beat.heartWeight != 0 || beat.faultWeight != 0
                || !string.IsNullOrEmpty(beat.theatreStateAfterText) || !string.IsNullOrEmpty(beat.endAction)
                || !string.IsNullOrEmpty(beat.dateLocalizationKey) || !string.IsNullOrEmpty(beat.dateFallback)))
                errors.Add(label + ": legacy-only resume beats cannot add resources, cues, dates or end actions.");
            if (!string.IsNullOrWhiteSpace(beat.endAction))
            {
                if (beat.endAction != FinalHeartHealAction && beat.endAction != FailureWarningAction)
                    errors.Add(label + ": unsupported endAction " + beat.endAction);
                if (!string.IsNullOrWhiteSpace(beat.nextEntryId) || beat.options.Length != 0)
                    errors.Add(label + ": endAction requires a terminal plain-text beat.");
            }
            ValidateSuccessor(beat.nextEntryId, label, errors, beat.legacyResumeOnly);

            var optionIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var route in beat.options)
            {
                if (route == null) { errors.Add(label + ": null option route."); continue; }
                if (string.IsNullOrWhiteSpace(route.optionId)) errors.Add(label + ": route optionId is required.");
                else if (!optionIds.Add(route.optionId)) errors.Add(label + ": duplicate route " + route.optionId);
                ValidateSuccessor(route.nextEntryId, label + "/" + route.optionId, errors, beat.legacyResumeOnly);
            }
            if (data == null) continue;
            if (!data.TryGetGlobalEntry(beat.entryId, out var entry))
            { errors.Add(label + ": global interaction entry is missing."); continue; }

            int matchingIds = 0, matchingSignals = 0;
            if (data.GlobalEntries != null)
                foreach (var candidate in data.GlobalEntries)
                {
                    if (candidate == null) continue;
                    if (candidate.EntryId == beat.entryId) matchingIds++;
                    if (candidate.TriggerType == OSCharacterInteractionTriggerType.ExternalSignal
                        && candidate.TriggerTargetId == beat.signalId
                        && (candidate.RequiredChapterIndex == 4 || candidate.RequiredChapterIndex < 0)) matchingSignals++;
                }
            if (matchingIds != 1) errors.Add(label + ": interaction entryId is not unique.");
            if (matchingSignals != 1) errors.Add(label + ": external signal does not resolve uniquely in chapter 4.");
            if (entry.RequiredChapterIndex != 4) errors.Add(label + ": RequiredChapterIndex must be 4.");
            if (entry.TriggerType != OSCharacterInteractionTriggerType.ExternalSignal || entry.TriggerTargetId != beat.signalId)
                errors.Add(label + ": ExternalSignal must match signalId.");
            if (!string.IsNullOrWhiteSpace(entry.NextEntryId)) errors.Add(label + ": interaction NextEntryId must be empty.");
            if (entry.AutoCloseDelay != 0f) errors.Add(label + ": AutoCloseDelay must be zero.");
            if (entry.TriggerDelay != 0f) errors.Add(label + ": TriggerDelay must be zero; the story owns timing.");
            if (entry.SelectTimeoutOptionOnInterrupt || entry.AllowUnansweredTimeout || entry.OptionTimeoutDelay > 0f)
                errors.Add(label + ": implicit option selection and timeout are unsupported.");
            if (!string.IsNullOrWhiteSpace(entry.PresentationCueId))
                errors.Add(label + ": PresentationCueId must be empty; presentation is owned by the chapter bridge.");
            if (entry.RequiredHeartHealProcessIndex >= 0)
                errors.Add(label + ": RequiredHeartHealProcessIndex must be negative.");
            if (entry.RequiredCompletedEntryIds != null && entry.RequiredCompletedEntryIds.Count > 0)
                errors.Add(label + ": RequiredCompletedEntryIds must be empty; the manifest owns prerequisites.");
            if (entry.PartType != OSCharacterInteractionPartType.Text
                && entry.PartType != OSCharacterInteractionPartType.TextWithOptions)
                errors.Add(label + ": only Text and TextWithOptions are supported.");
            if (entry.PartType == OSCharacterInteractionPartType.Text && !entry.WaitForExternalCompletion)
                errors.Add(label + ": plain text must WaitForExternalCompletion.");
            if (entry.PartType == OSCharacterInteractionPartType.Text && beat.options.Length > 0)
                errors.Add(label + ": plain text cannot contain option routes.");
            if (entry.PartType == OSCharacterInteractionPartType.Text && entry.Options != null && entry.Options.Count > 0)
                errors.Add(label + ": plain text cannot contain interaction options.");

            var authoredOptions = new HashSet<string>(StringComparer.Ordinal);
            if (entry.Options != null)
                foreach (var option in entry.Options)
                {
                    if (option == null) { errors.Add(label + ": null interaction option."); continue; }
                    if (string.IsNullOrWhiteSpace(option.OptionId) || !authoredOptions.Add(option.OptionId))
                        errors.Add(label + ": interaction optionId is empty or duplicated.");
                    if (!string.IsNullOrWhiteSpace(option.NextEntryId))
                        errors.Add(label + "/" + option.OptionId + ": interaction NextEntryId must be empty.");
                    if (!optionIds.Contains(option.OptionId))
                        errors.Add(label + "/" + option.OptionId + ": option route is missing.");
                }
            foreach (string optionId in optionIds)
                if (!authoredOptions.Contains(optionId)) errors.Add(label + "/" + optionId + ": interaction option is missing.");
            if (entry.PartType == OSCharacterInteractionPartType.TextWithOptions && authoredOptions.Count == 0)
                errors.Add(label + ": TextWithOptions must contain an option.");
        }
        return errors;
    }

    void ValidateSuccessor(string nextEntryId, string label, List<string> errors, bool legacySource)
    {
        // Empty is the terminal edge, not an implicit jump or a fabricated line.
        if (!string.IsNullOrWhiteSpace(nextEntryId) && Find(nextEntryId) == null)
            errors.Add(label + ": successor does not exist: " + nextEntryId);
        else if (!legacySource && Find(nextEntryId)?.legacyResumeOnly == true)
            errors.Add(label + ": new story routes cannot enter legacy-only beat " + nextEntryId);
    }
}
