using UnityEngine;

/// <summary>
/// Binding to story.Presentation, as in Docs/Chapter04BackendHandoff.md
/// ("最小绑定示例"). Subscribes, applies the pause value, then reads the current
/// request. appliedRevision survives disable/enable, so closing and reopening
/// the TV never replays the opening. Unity messages are virtual so the view can
/// add its own per-frame work.
/// </summary>
public abstract class OSChapter04TvAdapter : MonoBehaviour
{
    OSChapter04PresentationBridge bridge;
    OSChapter04PresentationBridge previousBridge;
    int appliedRevision;

    protected OSChapter04PresentationBridge Bridge => bridge;

    protected virtual void OnEnable() => TryBind();
    protected virtual void Update() { if (bridge == null) TryBind(); }

#if UNITY_EDITOR
    OSChapter04PresentationBridge debugBridge;
    protected bool HasDebugBridge => debugBridge != null;

    /// <summary>Editor QA only: bind to a standalone bridge instead of the story director's.</summary>
    public void DebugUseBridge(OSChapter04PresentationBridge value)
    {
        bool active = isActiveAndEnabled;
        if (active) OnDisable();
        debugBridge = value;
        if (active) OnEnable();
    }
#endif

    void TryBind()
    {
        OSChapter04PresentationBridge next = null;
#if UNITY_EDITOR
        next = debugBridge;
#endif
        if (next == null && OS_Handle.Instance != null) next = OS_Handle.Instance.Chapter04Story?.Presentation;
        if (next == null) return;
        bridge = next;
        if (!ReferenceEquals(previousBridge, bridge))
        {
            previousBridge = bridge;
            appliedRevision = 0;
        }
        bridge.PresentationRequested += ApplyRequest;
        bridge.PauseChanged += SetAnimationPaused;
        bridge.PresentationCleared += ClearPresentation;
        SetAnimationPaused(bridge.IsPaused);
        if (bridge.IsBound) ApplyRequest(bridge.CurrentRequest);
        else ClearPresentation();
    }

    void ApplyRequest(OSChapter04PresentationBridge.PresentationRequest request)
    {
        if (request.Revision == appliedRevision) return;
        appliedRevision = request.Revision;
        // Must respect the pause value already set.
        StartPresentation(request.StateId, request.IsRestore, request.Revision);
    }

    protected bool ReportPhase(string stateId, int capturedRevision) =>
        bridge != null && bridge.ReportState(stateId, capturedRevision);

    protected virtual void OnDisable()
    {
        if (bridge != null)
        {
            bridge.PresentationRequested -= ApplyRequest;
            bridge.PauseChanged -= SetAnimationPaused;
            bridge.PresentationCleared -= ClearPresentation;
        }
        SetAnimationPaused(true);
        bridge = null;
        // Keep appliedRevision and local progress; reopening does not replay.
    }

    protected abstract void StartPresentation(string stateId, bool restoring, int revision);
    protected abstract void SetAnimationPaused(bool paused);
    protected abstract void ClearPresentation();
}
