using System;
using UnityEngine;

/// <summary>
/// Chapter 4's presentation contract. Owns no artwork, animation, story clock or
/// save data. The story director binds saved state and persists StateChanged;
/// a TV renderer consumes CurrentRequest / PresentationRequested and reports
/// automatic transitions with the request revision it originally received.
/// </summary>
[DisallowMultipleComponent]
public sealed class OSChapter04PresentationBridge : MonoBehaviour
{
    public const string CityWalk = "CityWalk";
    public const string CityArrive = "CityArrive";
    public const string DoorLocked = "DoorLocked";
    public const string FoldToLab = "FoldToLab";
    public const string LabRummage = "LabRummage";
    public const string AtDoor = "AtDoor";
    public const string EnterDoor = "EnterDoor";
    public const string LabTidy = "LabTidy";
    public const string MedicineReady = "MedicineReady";
    public const string WastelandWalk = "WastelandWalk";
    public const string SettlementWait = "SettlementWait";
    public const string TentCare = "TentCare";
    public const string NorthSettlement = "NorthSettlement";
    public const string GirlHome = "GirlHome";
    public const string LabNight = "LabNight";
    public const string FlowerWalk = "FlowerWalk";
    public const string FlowerWalkRain = "FlowerWalkRain";
    public const string Grave = "Grave";
    public const string GravePlaceFlowers = "GravePlaceFlowers";
    public const string GraveBreakdown = "GraveBreakdown";

    public readonly struct PresentationRequest
    {
        public string StateId { get; }
        public bool IsRestore { get; }
        public int Revision { get; }

        public PresentationRequest(string stateId, bool isRestore, int revision)
        {
            StateId = stateId;
            IsRestore = isRestore;
            Revision = revision;
        }
    }

    string currentState = string.Empty;
    bool restoring;
    int revision;
    bool paused = true;

    public bool IsBound => !string.IsNullOrEmpty(currentState);
    public string CurrentState => currentState;
    public int Revision => revision;
    public bool IsPaused => paused;

    // Read this after subscribing, including when the TV renderer is created
    // after the story director or re-enabled after its window was closed.
    public PresentationRequest CurrentRequest =>
        new PresentationRequest(currentState, restoring, revision);

    public event Action<PresentationRequest> PresentationRequested;
    public event Action<string> StateChanged;
    public event Action<bool> PauseChanged;
    public event Action PresentationCleared;

    public static bool IsKnownState(string stateId) => stateId == CityWalk
        || stateId == CityArrive || stateId == DoorLocked
        || stateId == FoldToLab || stateId == LabRummage || stateId == AtDoor
        || stateId == EnterDoor || stateId == LabTidy || IsLaterScene(stateId);

    // Semantic targets survive saves before their replacement TV animations exist.
    public static bool IsLaterScene(string stateId) => stateId == MedicineReady
        || stateId == WastelandWalk || stateId == SettlementWait || stateId == TentCare
        || stateId == NorthSettlement || stateId == GirlHome || stateId == LabNight
        || stateId == FlowerWalk || stateId == FlowerWalkRain || stateId == Grave
        || stateId == GravePlaceFlowers || stateId == GraveBreakdown;

    public static bool IsStoryCue(string stateId) => stateId == CityWalk || stateId == CityArrive
        || stateId == EnterDoor || stateId == LabTidy || IsLaterScene(stateId);

    public static bool IsLoopState(string stateId) =>
        stateId == CityWalk || stateId == LabRummage || stateId == AtDoor
        || stateId == LabTidy || IsLaterScene(stateId);

    /// <summary>
    /// There is no player/story boundary inside the arrival-to-lab transition.
    /// A restored transition therefore resolves to its final stable loop.
    /// Unknown state IDs are rejected instead of silently restarting the act.
    /// </summary>
    public static string ResolveRestoredState(string stateId)
    {
        if (!IsKnownState(stateId)) return null;
        if (stateId == CityArrive) return AtDoor;
        return IsLoopState(stateId) ? stateId : LabRummage;
    }

    /// <summary>
    /// Bind once on a new story/save, not on every TV reopen. Fresh CityWalk
    /// requests allow the frontend's opening animation; restore requests put
    /// the scene directly in its loop. Binding preserves the current pause flag.
    /// </summary>
    public bool Bind(string savedState, bool isRestoring)
    {
        if (!IsKnownState(savedState)) return false;
        string next = isRestoring ? ResolveRestoredState(savedState) : savedState;
        PublishRequest(next, isRestoring);
        return true;
    }

    /// <summary>
    /// Story commands are issued after the tagged line finishes typing. A
    /// duplicate command is harmless; automatic transitions belong to the TV
    /// renderer and must use ReportState instead.
    /// </summary>
    public bool Goto(string stateId)
    {
        if (!IsBound || !IsKnownState(stateId)) return false;
        if (stateId == currentState) return true;
        bool allowed = currentState == CityWalk && stateId == CityArrive
            || (currentState == CityArrive || currentState == AtDoor) && stateId == EnterDoor
            || (currentState == EnterDoor || currentState == DoorLocked || currentState == FoldToLab
                || currentState == LabRummage) && stateId == LabTidy
            || (currentState == LabTidy || IsLaterScene(currentState)) && IsLaterScene(stateId);
        if (!allowed) return false;
        PublishRequest(stateId, false);
        return true;
    }

    /// <summary>
    /// The frontend reports each automatic boundary, retaining its original
    /// request revision for the entire chain. Reports never request another
    /// animation. Old callbacks, skipped states and paused reports are rejected.
    /// </summary>
    public bool ReportState(string stateId, int requestRevision)
    {
        if (!IsBound || paused || requestRevision != revision || !IsKnownState(stateId)) return false;
        if (stateId == currentState) return true;
        // DoorLocked is accepted only for legacy renderers/saves.
        if (currentState == CityArrive && stateId == AtDoor)
        { currentState = stateId; restoring = false; StateChanged?.Invoke(currentState); return true; }
        string expected = currentState == CityArrive ? DoorLocked
            : currentState == DoorLocked || currentState == EnterDoor ? FoldToLab
            : currentState == FoldToLab ? LabRummage : null;
        if (stateId != expected) return false;
        currentState = stateId;
        restoring = false;
        StateChanged?.Invoke(currentState);
        return true;
    }

    /// <summary>
    /// The story director pauses for hidden/minimized windows and gameplay
    /// pauses. The renderer must pause its own clocks when this value changes.
    /// Newly created and unbound bridges start paused.
    /// </summary>
    public void SetPaused(bool value)
    {
        if (paused == value) return;
        paused = value;
        PauseChanged?.Invoke(value);
    }

    /// <summary>Invalidate runtime callbacks without modifying any save.</summary>
    public void Unbind()
    {
        AdvanceRevision();
        currentState = string.Empty;
        restoring = false;
        SetPaused(true);
        PresentationCleared?.Invoke();
    }

    void PublishRequest(string stateId, bool isRestore)
    {
        AdvanceRevision();
        currentState = stateId;
        restoring = isRestore;
        int issuedRevision = revision;
        // Persist before a synchronous frontend can report an animation result.
        StateChanged?.Invoke(currentState);
        // A state listener may switch save/session; do not dispatch the stale request.
        if (revision == issuedRevision && IsBound)
            PresentationRequested?.Invoke(CurrentRequest);
    }

    void AdvanceRevision()
    {
        unchecked { revision++; }
        if (revision == 0) revision = 1;
    }
}
