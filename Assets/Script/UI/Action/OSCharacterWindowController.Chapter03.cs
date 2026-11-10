using UnityEngine;
using UnityEngine.UI;

public sealed partial class OSCharacterWindowController
{
    int chapter03Presence;
    Color chapter03OriginalColor;
    bool chapter03ColorSaved;
    bool chapter03OwnsNoSignal;
    OSChapter03PortraitEffects chapter03PortraitEffects;

    public void SetChapter03Presence(int mode, string status)
    {
        if (mode == 0 && chapter03Presence == 0) return;
        ResolveReferences();
        if (mode != 0 && !chapter03ColorSaved && characterImage != null)
        { chapter03OriginalColor = characterImage.color; chapter03ColorSaved = true; }
        chapter03Presence = mode;
        if (mode == 0)
        {
            if (chapter03OwnsNoSignal)
            {
                chapter03OwnsNoSignal = false;
                if (CurrentState == OSCharacterWindowState.VoidMessage)
                    ApplyStateImmediate(OSCharacterWindowState.Anima);
            }
            if (characterImage != null && chapter03ColorSaved) characterImage.color = chapter03OriginalColor;
            chapter03ColorSaved = false;
            if (topNameLocalizedText != null) topNameLocalizedText.Refresh(OS_Handle.Instance);
            StartAnimaStatusIndicatorPulse(false);
            return;
        }
        EndLinkMessageTyping();
        if (mode == 1)
        {
            chapter03OwnsNoSignal = true;
            if (CurrentState != OSCharacterWindowState.VoidMessage)
            {
                ApplyStateImmediate(OSCharacterWindowState.VoidMessage);
                RestartStateGlitch(OSCharacterWindowState.VoidMessage);
            }
        }
        if (characterImage != null) characterImage.color = mode == 1 ? new Color(.42f, .42f, .42f, 1f) : Color.white;
        StopAnimaStatusIndicatorPulse(false);
        if (animaStatusIndicator != null) animaStatusIndicator.color = new Color(.35f, .35f, .35f, 1f);
        if (topNameText != null) topNameText.text = status ?? string.Empty;
    }

    // The simulated reconnect uses the existing signal flash, without OpenLive's boot pose.
    public bool PlayChapter03SignalIn(System.Action onCompleted = null)
    {
        CancelChapter01Reconnect();
        chapter03OwnsNoSignal = false;
        // Story ownership survives the signal flash. Before the breakdown there
        // is no owner, so the existing resource-driven reconnect still applies.
        PlayResourceAnimation();
        if (isActiveAndEnabled && TryBeginCameraSignalIn(onCompleted, playOpenAnimation: false))
            return true;

        ApplyStateImmediate(OSCharacterWindowState.Anima);
        onCompleted?.Invoke();
        return false;
    }

    public void PulseChapter03Portrait() => GetChapter03PortraitEffects()?.PulseHairclip();

    public void ShakeChapter03Portrait(float seconds = .28f)
        => GetChapter03PortraitEffects()?.Shake(seconds);

    public void ResetChapter03Effects() => chapter03PortraitEffects?.ResetEffects();

    // The mirror close-up (pancake 10-25) copies her portrait and status line while this window hides.
    public Image Chapter03Portrait { get { ResolveReferences(); return characterImage; } }
    public string Chapter03StatusText { get { ResolveReferences(); return topNameText != null ? topNameText.text : null; } }

    /// <summary>Her nose in the portrait, in world space: the mirror lines its reflection up with it.</summary>
    public bool TryGetChapter03FaceWorld(out Vector3 world)
    {
        ResolveReferences(); world = default;
        if (characterImage == null) return false;
        var rect = characterImage.rectTransform;
        Rect drawing = characterImage.GetPixelAdjustedRect();
        Vector2 size = characterImage.sprite != null ? characterImage.sprite.rect.size : new Vector2(125f, 175f);
        if (characterImage.preserveAspect && size.x > 0f && size.y > 0f && drawing.width > 0f && drawing.height > 0f)
        {
            float aspect = size.x / size.y;
            if (aspect > drawing.width / drawing.height)
            { float height = drawing.width / aspect; drawing.y += (drawing.height - height) * rect.pivot.y; drawing.height = height; }
            else
            { float width = drawing.height * aspect; drawing.x += (drawing.width - width) * rect.pivot.x; drawing.width = width; }
        }
        // 125 x 175 frames: nose at (64, 66). The first Normal frame is cropped to 94 x 151 at (14, 23).
        Vector2 nose = size.x <= 100f ? new Vector2(50f / 94f, 1f - 43f / 151f) : new Vector2(64f / 125f, 1f - 66f / 175f);
        world = rect.TransformPoint(drawing.min + Vector2.Scale(drawing.size, nose));
        return true;
    }

    OSChapter03PortraitEffects GetChapter03PortraitEffects()
    {
        ResolveReferences();
        if (characterImage == null) return null;
        if (chapter03PortraitEffects == null)
        {
            chapter03PortraitEffects = characterImage.GetComponent<OSChapter03PortraitEffects>();
            if (chapter03PortraitEffects == null)
                chapter03PortraitEffects = characterImage.gameObject.AddComponent<OSChapter03PortraitEffects>();
            chapter03PortraitEffects.Initialize(this, characterImage);
        }
        return chapter03PortraitEffects;
    }
}
