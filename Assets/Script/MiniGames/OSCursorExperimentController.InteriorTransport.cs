using UnityEngine;

public sealed partial class OSCursorExperimentController
{
    bool interiorTransportActive;
    Vector2 interiorTransportScreenPoint;

    public bool IsInteriorTransportActive => interiorTransportActive && begun
        && phase == ExperimentPhase.Intake;

    /// <summary>Acquire the captured front-end pointer while the view moves to the interior.</summary>
    public void StartInteriorTransport(Vector2 capturedScreenPoint)
    {
        if (!Finite(capturedScreenPoint)) return;
        // Begin records the existing Round 1 / Intake safe checkpoint. Reloading
        // that checkpoint uses the closed-cage hold, never a saved screen tween.
        Begin(capturedScreenPoint, false);
        interiorTransportScreenPoint = capturedScreenPoint;
        interiorTransportActive = true;
        phaseTime = 0f;
        inputWarmup = true;
        RenderPresentation();
        SaveCheckpoint();
    }

    /// <summary>Move only the captured presentation; never warp the physical mouse.</summary>
    public void CarryInteriorPointer(Vector2 screenPoint)
    {
        if (!IsInteriorTransportActive || !Finite(screenPoint)) return;
        interiorTransportScreenPoint = screenPoint;
        RenderPresentation();
    }

    /// <summary>Continue from this exact display point into the ordinary front-door intake.</summary>
    public void FinishInteriorTransport()
    {
        if (!IsInteriorTransportActive) return;
        // Resolve against the final window pose, not the pose at pan start.
        transferOrigin = ScreenToNative(interiorTransportScreenPoint);
        ClearInteriorTransport();
        phaseTime = 0f;
        inputWarmup = true;
        RenderPresentation();
        SaveCheckpoint();
    }

    void ClearInteriorTransport()
    {
        interiorTransportActive = false;
        interiorTransportScreenPoint = Vector2.zero;
    }

    void RenderInteriorTransport()
    {
        // Re-resolve each render so moving the experiment window underneath
        // the claw cannot drag the captured screen point a second time.
        visiblePointer = ScreenToNative(interiorTransportScreenPoint);
        SetCagePointerDepth(false);
        SetPointerSprite(normalPointer);
        PlacePointer(visiblePointer);
        Place(transferRoot, visiblePointer);
        transferRoot.gameObject.SetActive(true);
        SetTransferGrip(true);
        SetCageDoor(0f);
    }
}
