using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

// Rotate around the window's bottom centre without changing its saved pivot or
// layout. OSWindowUnit removes the temporary offset when capturing a layout.
[DisallowMultipleComponent]
public sealed class OSPaperPushWindowTilt : MonoBehaviour
{
    OSWindowUnit window;
    RectTransform rect;
    Quaternion restRotation = Quaternion.identity;
    bool configured;
    float targetDegrees;
    readonly List<RotatingClip> rotatingClips = new List<RotatingClip>();
    bool rotatingClipsActive;
    sealed class RotatingClip
    {
        public RectMask2D rectangle;
        public Mask stencil;
        public Image addedGraphic;
    }
    public float TiltDegrees { get; private set; }
    public bool WasDragged { get; private set; }
    public RectTransform WindowRect => rect;
    public bool IsScriptedTilt => false;

    public void Configure(OSWindowUnit target)
    {
        if (configured && window == target) { ResetTilt();return; }
        ResetTilt();
        ClearRotatingClips();
        window = target;rect = target != null ? target.WindowRect : null;
        if (rect == null) return;
        restRotation = rect.localRotation;configured = true;
        ResetTilt();
    }

    public void SetHeld(bool holding, float degrees)
    {
        targetDegrees = Mathf.Clamp(degrees, -18f, 18f);
        if (holding && Mathf.Abs(targetDegrees) > .1f) WasDragged = true;
    }

    public void RestoreTilt(float degrees)
    {
        targetDegrees = Mathf.Clamp(degrees, -18f, 18f);
        Tick(0f, true);
    }

    public void Tick(float deltaSeconds, bool allowed)
    {
        if (!allowed || !configured || rect == null) return;
        Quaternion rotated = restRotation * Quaternion.Euler(0f, 0f, targetDegrees);
        Vector3 bottom = Vector3.Scale(new Vector3((.5f - rect.pivot.x) * rect.rect.width,
            -rect.pivot.y * rect.rect.height, 0f), rect.localScale);
        Vector2 offset = restRotation * bottom - rotated * bottom;
        if (!window.TrySetPresentationOffset(this, offset, true)) return;
        rect.localRotation = rotated;
        TiltDegrees = targetDegrees;
        SetRotatingClips(Mathf.Abs(targetDegrees) > .01f);
    }

    // Kept for older callers; new gameplay is driven solely by the gear.
    public void PlayScriptedTilt(float degrees, float seconds) => RestoreTilt(degrees);
    public void ResetTilt(bool resetDragHistory = true)
    {
        if (configured && rect != null) rect.localRotation = restRotation;
        window?.ClearPresentationOffset(this);
        SetRotatingClips(false);
        targetDegrees = TiltDegrees = 0f;
        if (resetDragHistory) WasDragged = false;
    }

    void SetRotatingClips(bool active)
    {
        if (rotatingClipsActive == active) return;
        if (active && rotatingClips.Count == 0 && rect != null)
        {
            // RectMask2D clips in canvas axes and cuts diagonally rotated content.
            // Use the same rectangles as stencil masks only while tilting.
            foreach (var rectangle in rect.GetComponentsInChildren<RectMask2D>(true))
            {
                if (!rectangle.enabled || rectangle.GetComponent<Mask>() != null) continue;
                var graphic = rectangle.GetComponent<Graphic>();
                Image added = null;
                if (graphic == null)
                {
                    added = rectangle.gameObject.AddComponent<Image>();
                    added.raycastTarget = false;added.enabled = false;
                }
                var stencil = rectangle.gameObject.AddComponent<Mask>();
                stencil.enabled = false;stencil.showMaskGraphic = graphic != null;
                rotatingClips.Add(new RotatingClip { rectangle = rectangle, stencil = stencil, addedGraphic = added });
            }
        }
        foreach (var clip in rotatingClips)
        {
            if (clip.rectangle == null || clip.stencil == null) continue;
            if (active) clip.rectangle.enabled = false;
            if (clip.addedGraphic != null && active) clip.addedGraphic.enabled = true;
            clip.stencil.enabled = active;
            if (clip.addedGraphic != null && !active) clip.addedGraphic.enabled = false;
            if (!active) clip.rectangle.enabled = true;
        }
        rotatingClipsActive = active;
    }

    void ClearRotatingClips()
    {
        SetRotatingClips(false);
        foreach (var clip in rotatingClips)
        {
            if (clip.stencil != null) Destroy(clip.stencil);
            if (clip.addedGraphic != null) Destroy(clip.addedGraphic);
        }
        rotatingClips.Clear();
    }
    void OnDisable() => ResetTilt();
    void OnDestroy() { ResetTilt();ClearRotatingClips(); }
}
