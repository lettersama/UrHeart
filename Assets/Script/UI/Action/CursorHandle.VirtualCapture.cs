using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

public sealed partial class CursorHandle
{
    CursorScriptInputGate scriptInputGate;
    bool noWarpScriptControl, scriptCaptured, hasScriptRotation;
    UnityEngine.Object virtualCaptureOwner;
    Quaternion rotationBeforeScript;

    /// <summary>Physical input, unaffected by story positioning of the UI cursor.</summary>
    public Vector2 RawMouseDelta => Mouse.current != null ? Mouse.current.delta.ReadValue() : Vector2.zero;
    public Vector2 RawMouseScreenPosition => Mouse.current != null ? Mouse.current.position.ReadValue() : Vector2.zero;
    public bool IsScriptCaptured => IsScriptControlled && scriptCaptured;
    public bool IsPointerInputSuppressed => scriptInputGate != null && scriptInputGate.IsBlocking;

    /// <summary>
    /// Takes ownership of only the front-end cursor. Ordinary UI can be isolated;
    /// the physical pointer is never warped or confined, including at release.
    /// </summary>
    public bool AcquireScriptControlWithoutWarp(UnityEngine.Object owner, bool suppressUiInput = true)
    {
        if (owner == null || (IsScriptControlled && !IsScriptControlledBy(owner))) return false;
        var gate = suppressUiInput ? ResolveScriptInputGate() : null;
        if (suppressUiInput && gate == null) return false;
        if (!AcquireScriptControl(owner)) return false;
        noWarpScriptControl = true;
        virtualCaptureOwner = owner;
        if (suppressUiInput && !gate.Capture(owner))
        {
            ReleaseScriptControl(owner);
            return false;
        }
        scriptCaptured = suppressUiInput;
        return true;
    }

    /// <summary>Toggle UI isolation while retaining visual ownership.</summary>
    public void SetScriptCaptured(UnityEngine.Object owner, bool captured)
    {
        if (!IsScriptControlledBy(owner)) return;
        if (captured)
        {
            var gate = ResolveScriptInputGate();
            if (gate == null || !gate.Capture(owner)) return;
            noWarpScriptControl = true;
            virtualCaptureOwner = owner;
            scriptCaptured = true;
        }
        else
        {
            scriptInputGate?.Release(owner);
            scriptCaptured = false;
        }
    }

    public void SetScriptRotation(UnityEngine.Object owner, float degrees)
    {
        if (!IsScriptControlledBy(owner)) return;
        ResolveReferences();
        if (cursorRect == null) return;
        if (!hasScriptRotation)
        {
            rotationBeforeScript = cursorRect.localRotation;
            hasScriptRotation = true;
        }
        cursorRect.localRotation = rotationBeforeScript * Quaternion.Euler(0f, 0f, degrees);
    }

    public void ReleaseScriptControlWithoutWarp(UnityEngine.Object owner, bool restoreAppearance = true)
    {
        if (!IsScriptControlledBy(owner)) return;
        // Snap the displayed pointer back to the untouched physical position;
        // the gate waits for physical buttons to be released before UI resumes.
        ReleaseScriptControl(owner, restoreAppearance, true);
    }

    CursorScriptInputGate ResolveScriptInputGate()
    {
        var system = EventSystem.current;
        if (system == null) return null;
        if (scriptInputGate != null && scriptInputGate.gameObject == system.gameObject) return scriptInputGate;
        if (scriptInputGate != null) scriptInputGate.Release(virtualCaptureOwner);
        scriptInputGate = system.GetComponent<CursorScriptInputGate>();
        if (scriptInputGate == null) scriptInputGate = system.gameObject.AddComponent<CursorScriptInputGate>();
        return scriptInputGate;
    }

    void TickVirtualCaptureLifetime()
    {
        // Unity-destroyed owners compare equal to null; do not leave either the
        // cursor appearance or ordinary UI stranded when a director disappears.
        if (noWarpScriptControl && scriptedInputOwner == null)
        {
            ClearScriptControlState(true);
            UpdatePointerPosition();
            ApplyVisual(true);
            return;
        }
        if (scriptCaptured && scriptedInputOwner != null)
            ResolveScriptInputGate()?.Capture(scriptedInputOwner);
    }

    void ClearVirtualCaptureState(bool restoreAppearance)
    {
        if (!ReferenceEquals(virtualCaptureOwner, null)) scriptInputGate?.Release(virtualCaptureOwner);
        noWarpScriptControl = false;
        scriptCaptured = false;
        virtualCaptureOwner = null;
        if (hasScriptRotation)
        {
            if (cursorRect != null) cursorRect.localRotation = rotationBeforeScript;
            hasScriptRotation = false;
        }
    }
}
