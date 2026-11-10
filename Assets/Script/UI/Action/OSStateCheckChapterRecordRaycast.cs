using UnityEngine;
using UnityEngine.EventSystems;

[DisallowMultipleComponent]
public sealed class OSStateCheckChapterRecordRaycast : MonoBehaviour,
    IPointerEnterHandler,
    IPointerExitHandler,
    IPointerClickHandler
{
    OSStateCheckWindowController owner;
    int chapterSequence = -1;
    bool isPointerInside;

    public void Initialize(
        OSStateCheckWindowController targetOwner,
        int targetChapterSequence)
    {
        owner = targetOwner;
        chapterSequence = targetChapterSequence;
    }

    void OnDisable()
    {
        if (!isPointerInside)
            return;

        isPointerInside = false;
        owner?.HandleChapterRecordPointerExit(chapterSequence);
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        if (isPointerInside)
            return;

        isPointerInside = true;
        owner?.HandleChapterRecordPointerEnter(chapterSequence);
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        if (!isPointerInside)
            return;

        isPointerInside = false;
        owner?.HandleChapterRecordPointerExit(chapterSequence);
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        owner?.HandleChapterRecordPointerClick(chapterSequence, eventData);
    }
}
