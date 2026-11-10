using UnityEngine;
using UnityEngine.EventSystems;

[DisallowMultipleComponent]
public sealed class OSClawMachinePrizeHover : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    OSClawMachineGameWindowController owner;
    void Awake() => owner = GetComponentInParent<OSClawMachineGameWindowController>();
    public void OnPointerEnter(PointerEventData data) => owner?.SetPrizeHovered(true);
    public void OnPointerExit(PointerEventData data) => owner?.SetPrizeHovered(false);
    void OnDisable() => owner?.SetPrizeHovered(false);
}
