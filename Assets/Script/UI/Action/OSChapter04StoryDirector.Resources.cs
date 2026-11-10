using System;
using MrTGame.Core;
using MrTGame.Systems;
using UnityEngine;

public sealed partial class OSChapter04StoryDirector
{
    public const int TargetHeart = 24;
    public const int TargetFault = 74;
    public const int ResourceWeightBudget = 12;
    const int StoryHeartFloor = OSResourceRuntimeState.LowResourceThreshold + 1;
    const int StoryFaultCeiling = OSResourceRuntimeState.HighFaultThreshold - 1;

    /// <summary>
    /// A notification of a committed story cost. Presentation may animate these
    /// values, but must not apply them again. Weighted beats still notify when
    /// the save is already beyond the target and the actual delta is zero.
    /// </summary>
    public readonly struct ResourceCue
    {
        public string EntryId { get; }
        public int Visit { get; }
        public int HeartWeight { get; }
        public int FaultWeight { get; }
        public int HeartDelta { get; }
        public int FaultDelta { get; }
        public int HeartAfter { get; }
        public int FaultAfter { get; }

        public ResourceCue(string entryId, int visit, int heartWeight, int faultWeight,
            int heartDelta, int faultDelta, int heartAfter, int faultAfter)
        {
            EntryId = entryId; Visit = visit;
            HeartWeight = heartWeight; FaultWeight = faultWeight;
            HeartDelta = heartDelta; FaultDelta = faultDelta;
            HeartAfter = heartAfter; FaultAfter = faultAfter;
        }
    }

    public event Action<ResourceCue> ResourceCueCommitted;

    static int ResourceValue(ResourceType type)
    {
        if (GameDataManager.Instance != null) return GameDataManager.Instance.GetOSResourceValue(type);
        return GameResourceSystem.Instance != null ? Mathf.RoundToInt(GameResourceSystem.Instance.Get(type)) : 0;
    }

    void CaptureResourceBaseline()
    {
        if (State == null || State.resourceBaselineCaptured || !State.started) return;
        State.initialHeart = ResourceValue(ResourceType.Heart);
        State.initialFault = ResourceValue(ResourceType.Fault);
        State.resourceBaselineCaptured = true;
    }

    // Public pure calculation also lets the authoring QA check different third-
    // chapter saves without changing the current play session's resources.
    public static int CalculateHeartTarget(int initial, int cumulativeWeight) =>
        CalculateHeartTarget(initial, cumulativeWeight, ResourceWeightBudget);

    public static int CalculateHeartTarget(int initial, int cumulativeWeight, int budget) => Mathf.RoundToInt(
        Mathf.Lerp(Mathf.Clamp(initial, 0, 100), Mathf.Min(Mathf.Clamp(initial, 0, 100), TargetHeart),
            Mathf.Clamp01(cumulativeWeight / (float)Mathf.Max(1, budget))));

    public static int CalculateFaultTarget(int initial, int cumulativeWeight) =>
        CalculateFaultTarget(initial, cumulativeWeight, ResourceWeightBudget);

    public static int CalculateFaultTarget(int initial, int cumulativeWeight, int budget) => Mathf.RoundToInt(
        Mathf.Lerp(Mathf.Clamp(initial, 0, 100), Mathf.Max(Mathf.Clamp(initial, 0, 100), TargetFault),
            Mathf.Clamp01(cumulativeWeight / (float)Mathf.Max(1, budget))));

    void CommitResources(OSChapter04StoryContent.Beat beat, bool notifyPresentation = true)
    {
        if (State == null || State.currentResourcesCommitted || beat == null || !State.currentTextCompleted) return;
        CaptureResourceBaseline();
        var saved = State;
        if (!saved.currentResourcesPrepared)
        {
            // Save the absolute destinations before raising any resource events.
            // Loading during either resource update can safely finish the other:
            // reaching the same destination twice never charges the cue twice.
            int heartBudget = Mathf.Max(1, Content.HeartWeightBudget);
            int faultBudget = Mathf.Max(1, Content.FaultWeightBudget);
            saved.heartWeightApplied = Mathf.Min(heartBudget, saved.heartWeightApplied + beat.heartWeight);
            saved.faultWeightApplied = Mathf.Min(faultBudget, saved.faultWeightApplied + beat.faultWeight);
            saved.currentHeartTarget = CalculateHeartTarget(saved.initialHeart, saved.heartWeightApplied, heartBudget);
            saved.currentFaultTarget = CalculateFaultTarget(saved.initialFault, saved.faultWeightApplied, faultBudget);
            saved.currentResourcesPrepared = true;
        }
        int heartBefore = ResourceValue(ResourceType.Heart);
        int faultBefore = ResourceValue(ResourceType.Fault);
        // Apply the saved destinations without crossing the real warning lines,
        // including prepared saves authored before these limits existed. A live
        // value already outside a limit is left alone rather than made worse.
        int heartDelta = beat.heartWeight > 0
            ? Mathf.Min(0, Mathf.Max(StoryHeartFloor, saved.currentHeartTarget) - heartBefore) : 0;
        int faultDelta = beat.faultWeight > 0
            ? Mathf.Max(0, Mathf.Min(StoryFaultCeiling, saved.currentFaultTarget) - faultBefore) : 0;
        if (heartDelta != 0) os.ChangeHeart(heartDelta);
        if (!ReferenceEquals(saved, State)) return;
        if (faultDelta != 0) os.ChangeError(faultDelta);
        if (!ReferenceEquals(saved, State)) return;
        saved.currentResourcesCommitted = true;
        if (!notifyPresentation || beat.heartWeight == 0 && beat.faultWeight == 0) return;
        ResourceCueCommitted?.Invoke(new ResourceCue(beat.entryId, saved.currentVisit,
            beat.heartWeight, beat.faultWeight, ResourceValue(ResourceType.Heart) - heartBefore,
            ResourceValue(ResourceType.Fault) - faultBefore, ResourceValue(ResourceType.Heart), ResourceValue(ResourceType.Fault)));
    }
}
