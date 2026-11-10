using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// A terminal row the player can click to type its command into the prompt.
/// The command is only filled in; Enter still runs it, so the terminal keeps
/// its typed feel while nobody has to copy a command by hand.
/// </summary>
[DisallowMultipleComponent]
public sealed class OSToolCommandLink : MonoBehaviour,
    IPointerEnterHandler, IPointerExitHandler, IPointerClickHandler
{
    static readonly Color HoverInk = Color.white;

    OSToolProgramLibrary owner;
    string command;
    Graphic[] inks;
    Color[] restInks;
    Graphic hitArea;
    bool hitAreaWasTarget;
    bool hovered;

    public static void Attach(Component row, Graphic hitArea, OSToolProgramLibrary owner, string command, params Graphic[] inks)
    {
        if (row == null || hitArea == null || owner == null || string.IsNullOrEmpty(command))
            return;
        var link = row.GetComponent<OSToolCommandLink>();
        if (link == null)
            link = row.gameObject.AddComponent<OSToolCommandLink>();
        else
            link.Release();
        link.owner = owner;
        link.command = command;
        link.hitArea = hitArea;
        link.hitAreaWasTarget = hitArea.raycastTarget;
        hitArea.raycastTarget = true;
        link.inks = inks;
        link.restInks = new Color[inks.Length];
        link.enabled = true;
    }

    public static void Detach(Component row)
    {
        var link = row != null ? row.GetComponent<OSToolCommandLink>() : null;
        if (link != null)
            link.Release();
    }

    // Pooled terminal rows are reused for other output; a released link does nothing.
    public void Release()
    {
        SetHovered(false);
        if (hitArea != null)
            hitArea.raycastTarget = hitAreaWasTarget;
        owner = null;
        command = null;
        hitArea = null;
        inks = null;
        restInks = null;
        enabled = false;
    }

    bool IsLive => enabled && owner != null && !string.IsNullOrEmpty(command);

    void OnDisable() => SetHovered(false);

    void LateUpdate()
    {
        if (hovered && (!IsLive || !owner.CanFillCommand))
            SetHovered(false);
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        if (!IsLive || !owner.CanFillCommand)
            return;
        SetHovered(true);
        owner.RequestInstallationHint();
    }

    public void OnPointerExit(PointerEventData eventData) => SetHovered(false);

    public void OnPointerClick(PointerEventData eventData)
    {
        if (eventData.button != PointerEventData.InputButton.Left || eventData.dragging
            || !IsLive || !owner.CanFillCommand)
            return;
        owner.FillCommand(command);
    }

    void SetHovered(bool value)
    {
        if (hovered == value)
            return;
        hovered = value;
        if (inks == null)
            return;
        for (int i = 0; i < inks.Length; i++)
        {
            if (inks[i] == null)
                continue;
            // Read the resting ink at hover time; decoration may have recoloured the row.
            if (value)
                restInks[i] = inks[i].color;
            inks[i].color = value ? HoverInk : restInks[i];
        }
    }
}
