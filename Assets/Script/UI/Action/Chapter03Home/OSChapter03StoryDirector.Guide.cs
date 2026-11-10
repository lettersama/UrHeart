using System.Collections.Generic;

/// <summary>
/// Guidance while it looks for 主人 (pancake 10-24). The evidence is the shoes, the calendar,
/// the coffee machine and the sugar jar; each interaction ends with a short question about another
/// still unchecked, picked at random. Once all are in it goes back to knock, and the slit under
/// the bed door breathes; back from the yard, a line sends it for the spare key.
/// Only the guiding part of each line is red: the localized text marks it with a color tag.
/// No counters; 「咚」 stays a heartbeat.
/// </summary>
public sealed partial class OSChapter03StoryDirector
{
    static readonly string[] EvidenceFlags = { "shoes_seen", "calendar_seen", "machine_seen", "medicine_seen" };
    static readonly string[] EvidenceHints = { "shoes", "calendar", "machine", "medicine" };
    const string DoubtKey = "memory.evidence.doubt";
    const string HintPrefix = "interaction.west.bed_door.hint.";
    const string NextPrefix = "memory.evidence.next.";
    const string KeyPromptKey = "memory.yard.return.key";

    readonly HashSet<int> guideNodes = new HashSet<int>();
    readonly List<int> openEvidence = new List<int>(4);

    int RandomOpenEvidence()
    {
        openEvidence.Clear();
        for (int i = 0; i < EvidenceFlags.Length; i++) if (!Has(EvidenceFlags[i])) openEvidence.Add(i);
        return openEvidence.Count == 0 ? -1 : openEvidence[UnityEngine.Random.Range(0, openEvidence.Count)];
    }

    /// <summary>A second knock with no answer: from here on it is looking for proof.</summary>
    void RaiseDoubt()
    {
        if (!Once("doubt_raised")) return;
        // The line is new; until its key is deployed the questions alone carry it.
        if (Content.Find(DoubtKey) != null) Say(DoubtKey);
    }

    /// <summary>At the door: the longer question about one unchecked piece.</summary>
    void SayDoorQuestion()
    {
        int i = RandomOpenEvidence();
        if (i >= 0) Say(HintPrefix + EvidenceHints[i]);
    }

    /// <summary>After any genuine evidence interaction: a short question about one still unchecked.</summary>
    void GuideAfterEvidence()
    {
        // A repeated inspection can ask again; restoring a view must never enqueue a hint.
        if (!IsAvailable || rebinding || home?.IsRestoringView == true || CluesReady) return;
        int i = RandomOpenEvidence();
        if (i < 0) return;
        // The short lines are new; until they are deployed the door questions stand in.
        string key = NextPrefix + EvidenceHints[i];
        Say(Content.Find(key) != null ? key : HintPrefix + EvidenceHints[i]);
    }

    /// <summary>Back from the yard: now it is urgent, so the spare key.</summary>
    void PromptForKey()
    {
        if (Content.Find(KeyPromptKey) != null) Say(KeyPromptKey);
    }

    // Memory reds these lines: the part the text tags, or the whole line until it is tagged.
    bool IsGuideNode(int node)
    {
        if (!IsActive || Content == null || !Content.Available) return false;
        if (guideNodes.Count == 0)
        {
            foreach (var hint in EvidenceHints) { AddGuideNode(HintPrefix + hint); AddGuideNode(NextPrefix + hint); }
            AddGuideNode("memory.evidence.return_to_door");
            AddGuideNode(KeyPromptKey);
        }
        return guideNodes.Contains(node);
    }
    void AddGuideNode(string key) { int node = Content.Node(key); if (node >= 0) guideNodes.Add(node); }

    void TickGuide()
    {
        view?.SetLightPulse("west_bed_slit", Has("deduction_done") && !Has("final_knock"));
    }
}
