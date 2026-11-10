using MrTGame.Narrative;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public sealed class OSMemoryPicInventorySlot : MonoBehaviour,
    IPointerEnterHandler, IPointerExitHandler, IPointerDownHandler, IPointerUpHandler, IPointerClickHandler
{
    public Image icon;
    public Image selection;
    OSMemoryPicController owner;
    OSMemoryPicItemData item;
    public string ItemId => item?.itemId;

    public void Bind(OSMemoryPicController controller, OSMemoryPicItemData data, bool selected)
    {
        owner = controller; item = data;
        icon.gameObject.SetActive(item != null);
        selection.gameObject.SetActive(selected);
        if (item == null) return;
        icon.sprite = item.icon;
        icon.rectTransform.sizeDelta = item.icon != null ? item.icon.rect.size : Vector2.zero;
        // The authored icons fit 40x36 and stay at their original pixel density.
        icon.rectTransform.anchoredPosition = Vector2.zero;
    }
    public void OnPointerEnter(PointerEventData e)
    {
        if (item != null) owner?.ShowTip(this, item.labelKey, transform as RectTransform);
    }
    public void OnPointerExit(PointerEventData e) => owner?.HideTip(this);
    public void OnPointerDown(PointerEventData e) { owner?.FocusPicture(); e.Use(); }
    public void OnPointerUp(PointerEventData e) => e.Use();
    public void OnPointerClick(PointerEventData e)
    {
        if (item != null && !e.dragging && e.button == PointerEventData.InputButton.Left)
            owner?.SelectInventoryItem(item.itemId);
        e.Use();
    }
}
