using System;
using System.Collections.Generic;

/// <summary>Durable narrative progress; the TV renderer does not own this state.</summary>
[Serializable]
public sealed class OSChapter04StoryState
{
    public int version;
    public bool started;
    public bool completed;
    // User dismissal of the chat child is independent of the Live/TV windows.
    public bool dialogueSuspended;
    public string currentEntryId;
    public int currentVisit;
    public bool currentTextCompleted;
    public bool currentCueCommitted;
    public bool currentResourcesPrepared;
    public bool currentResourcesCommitted;
    public int currentHeartTarget;
    public int currentFaultTarget;
    public float currentReadElapsed;
    public string theatreState;
    public bool resourceBaselineCaptured;
    public int initialHeart;
    public int initialFault;
    public int heartWeightApplied;
    public int faultWeightApplied;
    public int griefTier;
    public int griefLineStep;
    public OSChapter04FinalTreatmentStage finalTreatmentStage;
    // Final treatment saves keep a restart point, never a half-played score.
    public bool finalHealInitialHeartCaptured;
    public int finalHealInitialHeart;
    public int finalHealSection;
    public OSChapter04MotherGiftStage motherGiftStage;
    public List<OSChapter04StoryRecord> history = new List<OSChapter04StoryRecord>();

    public void EnsureData()
    {
        if (history == null) history = new List<OSChapter04StoryRecord>();
        version = Math.Max(0, version);
        currentVisit = Math.Max(0, currentVisit);
        heartWeightApplied = Math.Max(0, heartWeightApplied);
        faultWeightApplied = Math.Max(0, faultWeightApplied);
        griefTier = Math.Max(0, Math.Min(3, griefTier));
        griefLineStep = Math.Max(0, Math.Min(3, griefLineStep));
        initialHeart = Math.Max(0, Math.Min(100, initialHeart));
        initialFault = Math.Max(0, Math.Min(100, initialFault));
        finalHealInitialHeart = Math.Max(1, Math.Min(100, finalHealInitialHeart));
        finalHealSection = Math.Max(0, Math.Min(4, finalHealSection));
        // Contiguous, append-only save enums: avoid boxed Enum.IsDefined calls
        // when this state is validated by other chapters' ordinary reads.
        if (finalTreatmentStage < OSChapter04FinalTreatmentStage.None
            || finalTreatmentStage > OSChapter04FinalTreatmentStage.Ending)
            finalTreatmentStage = OSChapter04FinalTreatmentStage.None;
        if (motherGiftStage < OSChapter04MotherGiftStage.None
            || motherGiftStage > OSChapter04MotherGiftStage.Closed)
            motherGiftStage = OSChapter04MotherGiftStage.None;
        if (float.IsNaN(currentReadElapsed) || float.IsInfinity(currentReadElapsed) || currentReadElapsed < 0f)
            currentReadElapsed = 0f;
        // In particular, do not reset a started story or normalize a saved TV
        // transition here. The presentation bridge resolves its restore target.
    }
}

public enum OSChapter04MotherGiftStage
{
    None = 0, KeyOffered = 1, Unlocked = 2, Seen = 3, CloseRequested = 4, Closed = 5
}

public enum OSChapter04FinalTreatmentStage
{
    None = 0,
    Pending = 1,
    Opened = 2,
    Completed = 3,
    // Appended: the numeric values in existing treatment saves must not move.
    WaitingForWarning = 4,
    FailureWarning = 5,
    FinalHeal = 6,
    Ending = 7
}

[Serializable]
public sealed class OSChapter04StoryRecord
{
    public int visit;
    public string entryId;
    public string optionId;
}
