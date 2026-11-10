using UnityEngine;
using UnityEngine.EventSystems;

[DisallowMultipleComponent]
public sealed class OSStateCheckFaultNodePointer : MonoBehaviour,
    IPointerEnterHandler, IPointerExitHandler
{
    OSStateCheckWindowController owner;
    int node;

    public void Initialize(OSStateCheckWindowController controller, int index)
    {
        owner = controller;
        node = index;
    }

    public void OnPointerEnter(PointerEventData eventData) => owner?.HandleFaultNodePointer(node, true);
    public void OnPointerExit(PointerEventData eventData) => owner?.HandleFaultNodePointer(node, false);
    void OnDisable() => owner?.HandleFaultNodePointer(node, false);
}
