using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

/// <summary>
/// Temporarily replaces ordinary UI modules while a story owns the visual cursor.
/// The EventSystem, physical mouse and debug-console keyboard listener stay active.
/// </summary>
[DefaultExecutionOrder(-400)]
[DisallowMultipleComponent]
public sealed class CursorScriptInputGate : BaseInputModule
{
    readonly List<BaseInputModule> suspendedModules = new List<BaseInputModule>();
    UnityEngine.Object captureOwner;
    bool captured, releasePending, blocking;
    int releaseAfterFrame;

    public bool IsBlocking => blocking;
    public bool IsReleasePending => releasePending;

    public bool Capture(UnityEngine.Object owner)
    {
        if (owner == null || (captured && captureOwner != null && captureOwner != owner)) return false;
        captureOwner = owner;
        captured = true;
        releasePending = false;
        RefreshGate();
        return true;
    }

    public void Release(UnityEngine.Object owner)
    {
        if (!ReferenceEquals(captureOwner, owner)) return;
        captureOwner = null;
        captured = false;
        // A click already held during the capture must never become a UI release.
        releasePending = true;
        releaseAfterFrame = Time.frameCount + 1;
        RefreshGate();
    }

    void Update()
    {
        if (captured && captureOwner == null)
        {
            captured = false;
            captureOwner = null;
            releasePending = true;
            releaseAfterFrame = Time.frameCount + 1;
        }
        if (releasePending && Time.frameCount > releaseAfterFrame && !AnyMouseButtonHeld())
            releasePending = false;
        RefreshGate();
    }

    static bool AnyMouseButtonHeld()
    {
        var mouse = Mouse.current;
        return mouse != null && (mouse.leftButton.isPressed || mouse.rightButton.isPressed
            || mouse.middleButton.isPressed || mouse.forwardButton.isPressed || mouse.backButton.isPressed);
    }

    bool WantsBlock => isActiveAndEnabled && (captured || releasePending)
        && !GameTestConsole.BlocksGameplayInput;

    void RefreshGate()
    {
        bool next = WantsBlock;
        if (next)
        {
            // Recheck enabled modules so a newly enabled UI module cannot leak
            // physical-position clicks while the story cursor is somewhere else.
            foreach (var module in GetComponents<BaseInputModule>())
            {
                if (module == null || module == this || !module.enabled) continue;
                if (!suspendedModules.Contains(module)) suspendedModules.Add(module);
                if (!blocking) ClearSelectedPress();
                module.DeactivateModule();
                // InputSystemUIInputModule.OnDisable resets pointers and sends
                // pointer-exit to the old hover chain before removing its state.
                module.enabled = false;
            }
            blocking = true;
        }
        else
        {
            blocking = false;
            RestoreModules();
        }
    }

    void ClearSelectedPress()
    {
        if (eventSystem == null) return;
        var selected = eventSystem.currentSelectedGameObject;
        if (selected != null)
        {
            // Clear Selectable's pressed appearance without ever sending click,
            // drop or submit. The old physical release is discarded by the gate.
            var data = new PointerEventData(eventSystem)
            {
                button = PointerEventData.InputButton.Left,
                eligibleForClick = false,
                position = Mouse.current != null ? Mouse.current.position.ReadValue() : Vector2.zero
            };
            ExecuteEvents.Execute(selected, data, ExecuteEvents.pointerUpHandler);
        }
        eventSystem.SetSelectedGameObject(null);
    }

    void RestoreModules()
    {
        for (int i = 0; i < suspendedModules.Count; i++)
            if (suspendedModules[i] != null) suspendedModules[i].enabled = true;
        suspendedModules.Clear();
    }

    public override bool ShouldActivateModule() => WantsBlock;
    public override bool IsModuleSupported() => WantsBlock;
    public override void Process() { }

    protected override void OnDisable()
    {
        captured = releasePending = blocking = false;
        captureOwner = null;
        RestoreModules();
        base.OnDisable();
    }
}
