using MrTGame.Narrative;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>Six reusable visible cells. Dragging/wheel pages by a cell; no per-item GameObjects.</summary>
[DisallowMultipleComponent]
public sealed class OSMemoryPicInventory : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler, IScrollHandler
{
    public OSMemoryPicInventorySlot[] slots = new OSMemoryPicInventorySlot[6];
    public Button previous;
    public Button next;
    OSMemoryPicController owner;
    OSMemoryPicScenarioData scenario;
    OSMemoryPicSaveData state;
    int first;
    int lastCount;
    float dragX;
    public int FirstVisible => first;
    public int PoolSize => slots.Length;

    void Awake()
    {
        if (previous != null) previous.onClick.AddListener(Previous);
        if (next != null) next.onClick.AddListener(Next);
    }
    public void Refresh(OSMemoryPicController controller, OSMemoryPicScenarioData data, OSMemoryPicSaveData saved)
    {
        owner = controller; scenario = data; state = saved;
        if (state == null) return;
        if (state.inventory.Count > lastCount) first = Mathf.Max(0, state.inventory.Count - slots.Length);
        lastCount = state.inventory.Count;
        Redraw();
    }
    void Redraw()
    {
        if (state == null) return;
        int maximum = Mathf.Max(0, state.inventory.Count - slots.Length);
        first = Mathf.Clamp(first, 0, maximum);
        for (int i = 0; i < slots.Length; i++)
        {
            var item = first + i < state.inventory.Count ? scenario.FindItem(state.inventory[first + i]) : null;
            slots[i].Bind(owner, item, item != null && item.itemId == state.selectedItemId);
        }
        if (previous != null) previous.interactable = first > 0;
        if (next != null) next.interactable = first < maximum;
        if (previous != null) previous.gameObject.SetActive(first > 0);
        if (next != null) next.gameObject.SetActive(first < maximum);
    }
    public void Previous()
    {
        if (owner == null || !owner.CanInteract) return;
        first--; Redraw(); owner.HideTip(null);
    }
    public void Next()
    {
        if (owner == null || !owner.CanInteract) return;
        first++; Redraw(); owner.HideTip(null);
    }
    public void OnBeginDrag(PointerEventData eventData)
    {
        dragX = eventData.position.x;
        owner?.HideTip(null);
    }
    public void OnDrag(PointerEventData eventData)
    {
        float scale = GetComponentInParent<Canvas>().scaleFactor;
        float delta = (eventData.position.x - dragX) / Mathf.Max(.01f, scale);
        if (Mathf.Abs(delta) >= 26f)
        {
            if (delta < 0) Next(); else Previous();
            dragX = eventData.position.x;
        }
        eventData.Use();
    }
    public void OnEndDrag(PointerEventData eventData) => eventData.Use();
    public void OnScroll(PointerEventData eventData)
    {
        if (eventData.scrollDelta.x < 0 || eventData.scrollDelta.y < 0) Next(); else Previous();
        eventData.Use();
    }
}
