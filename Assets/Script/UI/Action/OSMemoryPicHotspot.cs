using System.Collections.Generic;
using MrTGame.Narrative;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

[DisallowMultipleComponent]
public sealed class OSMemoryPicHotspot : MonoBehaviour,
    IPointerEnterHandler, IPointerExitHandler, IPointerDownHandler, IPointerUpHandler, IPointerClickHandler,
    IBeginDragHandler, IDragHandler, IEndDragHandler
{
    public string hotspotId;
    [Tooltip("Always-visible direction arrow. Unavailable routes use 0.25 opacity; clicking one also dims its ink.")]
    public bool navigationOnly;
    public Image[] highlightedGraphics = System.Array.Empty<Image>();
    public bool glitchHint = true;
    public Material feedbackMaterial;
    public OSMemoryPicEffect customEffect;
    readonly List<Material> materials = new List<Material>();
    OSMemoryPicController owner;
    OSMemoryPicHotspotData data;
    bool hovered;
    bool dragged;
    bool draggingWire;
    OSMemoryPicWiringView wiring;
    bool unavailableNavigationClicked;
    bool navigationTargetAvailable;
    static readonly Color NavigationColor = new Color32(135, 130, 123, 255);
    static readonly Color UnavailableNavigationColor = new Color32(111, 104, 103, 255);
    float phase;
    public OSMemoryPicHotspotData Data => data;

    public void Bind(OSMemoryPicController controller, OSMemoryPicHotspotData definition)
    {
        owner = controller;
        data = definition;
        wiring = GetComponentInParent<OSMemoryPicWiringView>();
        unavailableNavigationClicked = false;
        phase = (Mathf.Abs(hotspotId.GetHashCode() % 101) / 101f) * 5f;
        if (materials.Count == 0 && feedbackMaterial != null)
        {
            foreach (Image graphic in highlightedGraphics)
            {
                if (graphic == null) continue;
                var material = new Material(feedbackMaterial);
                graphic.material = material;
                materials.Add(material);
            }
        }
        RefreshNavigationColor();
    }

    public void Refresh(OSMemoryPicSaveData state)
    {
        bool visible = navigationOnly || (data != null && data.visibleWhen.Matches(state));
        if (gameObject.activeSelf != visible) gameObject.SetActive(visible);
        RefreshNavigationColor();
        if (visible && hovered) owner.ShowHotspotTip(this);
    }

    void RefreshNavigationColor()
    {
        if (!navigationOnly) return;
        navigationTargetAvailable = owner != null && owner.HasNavigationTarget(data);
        if (navigationTargetAvailable) unavailableNavigationClicked = false;
        foreach (Image graphic in highlightedGraphics)
        {
            if (graphic == null) continue;
            Color tint = graphic.color;
            tint.a = navigationTargetAvailable ? 1f : .25f;
            graphic.color = tint;
        }
        foreach (Material material in materials)
        {
            material.SetFloat("_OverrideInk", 1f);
            material.SetColor("_InkColor", unavailableNavigationClicked ? UnavailableNavigationColor : NavigationColor);
        }
    }

    void Update()
    {
        bool active = owner != null && owner.CanInteract;
        if (navigationOnly && owner != null && owner.HasNavigationTarget(data) != navigationTargetAvailable)
            RefreshNavigationColor();
        if (hovered && !active) ClearHover();
        foreach (Material material in materials)
        {
            bool selected = data != null && data.kind == OSMemoryPicHotspotKind.WirePlug && owner.SelectedWire == data.socketIndex;
            material.SetFloat("_Hover", active && (hovered || selected) ? 1f : 0f);
            material.SetFloat("_Glitch", active && glitchHint && !hovered ? 1f : 0f);
            material.SetFloat("_Clock", Time.unscaledTime + phase);
        }
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        if (owner == null || !owner.CanInteract) return;
        hovered = true;
        owner.ShowHotspotTip(this);
    }
    public void OnPointerExit(PointerEventData eventData) => ClearHover();
    public void OnPointerDown(PointerEventData eventData)
    {
        dragged = false;
        if (eventData.button == PointerEventData.InputButton.Left) owner?.FocusPicture();
        eventData.Use();
    }
    public void OnPointerUp(PointerEventData eventData) => eventData.Use();
    public void OnPointerClick(PointerEventData eventData)
    {
        if (eventData.button == PointerEventData.InputButton.Left && !eventData.dragging && !dragged)
        {
            if (navigationOnly)
            {
                // A busy transition is not an unavailable direction.
                if (owner != null && owner.CanInteract)
                {
                    unavailableNavigationClicked = !owner.HasNavigationTarget(data);
                    RefreshNavigationColor();
                    owner.TryNavigate(hotspotId, customEffect);
                }
            }
            else owner?.TryInteract(hotspotId, customEffect);
        }
        eventData.Use();
    }
    public void OnBeginDrag(PointerEventData eventData)
    {
        if (eventData.button != PointerEventData.InputButton.Left
            || data == null || data.kind != OSMemoryPicHotspotKind.WirePlug || owner == null) return;
        dragged = true;
        eventData.eligibleForClick = false;
        if (owner.SelectedWire != data.socketIndex) owner.TryInteract(hotspotId, customEffect);
        draggingWire = owner.CanInteract && owner.SelectedWire == data.socketIndex;
        if (draggingWire) wiring?.SetDragPointer(eventData.position);
        eventData.Use();
    }
    public void OnDrag(PointerEventData eventData)
    {
        if (!draggingWire) return;
        wiring?.SetDragPointer(eventData.position);
        eventData.Use();
    }
    public void OnEndDrag(PointerEventData eventData)
    {
        if (!draggingWire) return;
        draggingWire = false;
        wiring?.ClearDragPointer();
        var hit = eventData.pointerCurrentRaycast.gameObject;
        var socket = hit != null ? hit.GetComponentInParent<OSMemoryPicHotspot>() : null;
        if (socket != null && socket.owner == owner && socket.data != null
            && socket.data.kind == OSMemoryPicHotspotKind.WireSocket)
            owner.TryInteract(socket.hotspotId, socket.customEffect);
        // Dropping elsewhere keeps the cable held, so click-to-connect still works.
        eventData.Use();
    }
    void ClearHover()
    {
        if (hovered) owner?.HideTip(this);
        hovered = false;
        foreach (Material material in materials) material.SetFloat("_Hover", 0f);
    }
    void OnDisable()
    {
        if (draggingWire) wiring?.ClearDragPointer();
        draggingWire = false;
        ClearHover();
        foreach (Material material in materials) material.SetFloat("_Glitch", 0f);
    }
    void OnDestroy()
    {
        foreach (Material material in materials) Destroy(material);
        materials.Clear();
    }
}
