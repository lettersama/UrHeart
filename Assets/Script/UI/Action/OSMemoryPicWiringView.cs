using System;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>Chapter-local click/drag cables, rendered by the HeartBitPanel cable component.</summary>
[DisallowMultipleComponent]
public sealed class OSMemoryPicWiringView : MonoBehaviour
{
    [Serializable]
    public sealed class Cable
    {
        public int index;
        public RectTransform start;
        public RectTransform end;
        public Vector2 socket;
        public float length;
        public OSUICableGraphic graphic;
    }

    public Cable[] cables = Array.Empty<Cable>();
    OSMemoryPicController owner;
    Canvas parentCanvas;
    Vector2? dragPointer;

    public void SetDragPointer(Vector2 position) { dragPointer = position; Refresh(); }
    public void ClearDragPointer() => dragPointer = null;
    void OnDisable() => ClearDragPointer();

    public void Bind(OSMemoryPicController controller)
    {
        owner = controller;
        parentCanvas = GetComponentInParent<Canvas>();
        foreach (Cable cable in cables)
        {
            cable.graphic.ConfigureFixedLength(cable.start, cable.end, cable.length);
            cable.graphic.color = new Color32(165, 39, 55, 255);
        }
        Refresh();
    }

    void LateUpdate() => Refresh();

    public void Refresh()
    {
        if (owner == null || owner.State == null) return;
        foreach (Cable cable in cables)
        {
            bool connected = owner.State.Has("wire_" + cable.index);
            bool held = owner.CanInteract && owner.SelectedWire == cable.index;
            bool visible = connected || held;
            if (visible)
            {
                Vector2 target = cable.socket;
                if (!connected)
                {
                    target = cable.start.anchoredPosition;
                    if ((dragPointer.HasValue || Mouse.current != null) && parentCanvas != null)
                    {
                        Camera camera = parentCanvas.renderMode == RenderMode.ScreenSpaceOverlay
                            ? null : parentCanvas.worldCamera;
                        RectTransformUtility.ScreenPointToLocalPointInRectangle(
                            (RectTransform)cable.end.parent, dragPointer ?? Mouse.current.position.ReadValue(), camera, out target);
                    }
                }
                cable.end.anchoredPosition = ConstrainEndpoint(cable, target);
            }
            if (cable.graphic.gameObject.activeSelf != visible)
                cable.graphic.gameObject.SetActive(visible);
        }
    }

    public static Vector2 ConstrainEndpoint(Cable cable, Vector2 desired)
    {
        Vector2 start = cable.start.anchoredPosition;
        return start + Vector2.ClampMagnitude(desired - start, cable.length);
    }
}
