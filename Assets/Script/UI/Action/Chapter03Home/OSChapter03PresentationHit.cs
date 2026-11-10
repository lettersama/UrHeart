using System;
using UnityEngine;
using UnityEngine.EventSystems;

// Runtime-only UI event relay. No polling, global input actions, or persistent state.
public sealed class OSChapter03PresentationHit : MonoBehaviour, IPointerClickHandler,
    IBeginDragHandler, IDragHandler, IEndDragHandler, IScrollHandler
{
    public Action<PointerEventData> Clicked, BeganDrag, Dragged, EndedDrag, Scrolled;
    public void OnPointerClick(PointerEventData e) { if (e.button == PointerEventData.InputButton.Left && !e.dragging) Clicked?.Invoke(e); }
    public void OnBeginDrag(PointerEventData e) { if (e.button == PointerEventData.InputButton.Left) BeganDrag?.Invoke(e); }
    public void OnDrag(PointerEventData e) { if (e.button == PointerEventData.InputButton.Left) Dragged?.Invoke(e); }
    public void OnEndDrag(PointerEventData e) { if (e.button == PointerEventData.InputButton.Left) EndedDrag?.Invoke(e); }
    public void OnScroll(PointerEventData e) => Scrolled?.Invoke(e);
}
