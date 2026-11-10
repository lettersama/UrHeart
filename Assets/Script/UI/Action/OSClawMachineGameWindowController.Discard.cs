using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public sealed partial class OSClawMachineGameWindowController
{
    const float ShellFallSeconds = .8f;
    [SerializeField] Sprite compactCarriageSprite;
    bool machineShellDiscarded;
    float shellFallElapsed, shellFallDistance;
    RectTransform fallingShell, originalMotionParent;
    Vector2 originalMotionAnchorMin, originalMotionAnchorMax, originalMotionPivot;
    Vector2 originalMotionSize, originalMotionPosition, originalCarriageSize;
    Vector3 originalMotionScale;
    Quaternion originalMotionRotation;
    int originalMotionSibling;
    Graphic shellRootGraphic;
    bool shellRootGraphicEnabled;
    Sprite originalCarriageSprite;
    readonly List<Transform> shellChildren = new List<Transform>();

    public bool IsMachineShellDiscarded => machineShellDiscarded;

    void DiscardMachineShell(bool animate)
    {
        if (!machineShellDiscarded)
        {
            var motion = TrapMotion;
            if (motion == null) return;
            // The fake cabinet collects all current children. Retire its motor
            // before that reparenting; the exposed return creates its own voice.
            motorAudio?.Dispose();
            motorAudio = null;
            originalMotionParent = motion.parent as RectTransform;
            originalMotionSibling = motion.GetSiblingIndex();
            originalMotionAnchorMin = motion.anchorMin;
            originalMotionAnchorMax = motion.anchorMax;
            originalMotionPivot = motion.pivot;
            originalMotionSize = motion.sizeDelta;
            originalMotionPosition = motion.anchoredPosition;
            originalMotionScale = motion.localScale;
            originalMotionRotation = motion.localRotation;
            originalCarriageSize = carriage.sizeDelta;
            var carriageImage = carriage.GetComponent<Image>();
            originalCarriageSprite = carriageImage != null ? carriageImage.sprite : null;
            // Move the entire coordinate space unchanged, not individual claws.
            // The actual OS window rect never moves, so saves retain its layout.
            motion.SetParent(transform, true);
            var go = new GameObject("DiscardedMachineShell", typeof(RectTransform), typeof(CanvasGroup));
            go.layer = gameObject.layer;
            fallingShell = go.GetComponent<RectTransform>();
            fallingShell.SetParent(transform, false);
            fallingShell.anchorMin = Vector2.zero;
            fallingShell.anchorMax = Vector2.one;
            fallingShell.offsetMin = fallingShell.offsetMax = Vector2.zero;
            var group = go.GetComponent<CanvasGroup>();
            group.blocksRaycasts = false; group.interactable = false;
            shellChildren.Clear();
            foreach (Transform child in transform)
                if (child != motion && child != fallingShell) shellChildren.Add(child);
            foreach (var child in shellChildren) child.SetParent(fallingShell, false);
            fallingShell.SetAsFirstSibling();
            motion.SetAsLastSibling();
            shellRootGraphic = GetComponent<Graphic>();
            if (shellRootGraphic != null)
            {
                shellRootGraphicEnabled = shellRootGraphic.enabled;
                shellRootGraphic.enabled = false;
            }
            // The compact handle is drawn at its display resolution. Resizing
            // the 41x20 carriage made its pixels half the size of the claw's.
            if (carriageImage != null && compactCarriageSprite != null)
            {
                carriageImage.sprite = compactCarriageSprite;
                carriage.sizeDelta = compactCarriageSprite.rect.size;
            }
            // More than the complete canvas height guarantees the prop clears
            // the screen even when the user moved the window to the upper edge.
            var canvas = GetComponentInParent<Canvas>();
            var canvasRect = canvas != null ? canvas.rootCanvas.transform as RectTransform : null;
            shellFallDistance = (canvasRect != null ? canvasRect.rect.height : 360f)
                + Window.WindowRect.rect.height * 1.5f;
            machineShellDiscarded = true;
            shellFallElapsed = 0f;
            if (animate) Chapter02Audio.Play("Chapter02_ShellDrop", .6f, .92f);
        }
        if (!animate)
        {
            shellFallElapsed = ShellFallSeconds;
            if (fallingShell != null) fallingShell.gameObject.SetActive(false);
        }
    }

    void TickDiscardedMachineShell(float dt)
    {
        if (!machineShellDiscarded || fallingShell == null || shellFallElapsed >= ShellFallSeconds) return;
        shellFallElapsed = Mathf.Min(ShellFallSeconds, shellFallElapsed + dt);
        float t = shellFallElapsed / ShellFallSeconds;
        // A brief breakaway followed by acceleration, with a slight loose tilt.
        fallingShell.anchoredPosition = new Vector2(Mathf.Round(10f * t * t), -Mathf.Round(shellFallDistance * t * t));
        fallingShell.localRotation = Quaternion.Euler(0f, 0f, -7f * t * t);
        if (shellFallElapsed >= ShellFallSeconds) fallingShell.gameObject.SetActive(false);
    }

    void RestoreMachineShell()
    {
        if (!machineShellDiscarded) return;
        var motion = TrapMotion;
        // Called only for a fresh, uncaptured game, never while returning to a
        // captured save. Reopening that save keeps just the compact claw.
        foreach (var child in shellChildren)
            if (child != null) child.SetParent(transform, false);
        shellChildren.Clear();
        if (motion != null && originalMotionParent != null)
        {
            motion.SetParent(originalMotionParent, false);
            motion.anchorMin = originalMotionAnchorMin; motion.anchorMax = originalMotionAnchorMax;
            motion.pivot = originalMotionPivot; motion.sizeDelta = originalMotionSize;
            motion.anchoredPosition = originalMotionPosition; motion.localScale = originalMotionScale;
            motion.localRotation = originalMotionRotation; motion.SetSiblingIndex(originalMotionSibling);
        }
        if (carriage != null)
        {
            carriage.sizeDelta = originalCarriageSize;
            carriage.localRotation = Quaternion.identity;
            var carriageImage = carriage.GetComponent<Image>();
            if (carriageImage != null) carriageImage.sprite = originalCarriageSprite;
        }
        if (shellRootGraphic != null) shellRootGraphic.enabled = shellRootGraphicEnabled;
        if (fallingShell != null) Destroy(fallingShell.gameObject);
        fallingShell = null; originalMotionParent = null;
        machineShellDiscarded = false; shellFallElapsed = 0f;
    }
}
