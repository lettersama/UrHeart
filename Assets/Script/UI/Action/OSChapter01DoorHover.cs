using UnityEngine;
using UnityEngine.EventSystems;

// A quiet visual invitation; the ending itself owns input and progression.
[DisallowMultipleComponent]
public sealed class OSChapter01DoorHover : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    [SerializeField] UnityEngine.UI.Button button;
    [SerializeField] CanvasGroup corners;
    [SerializeField] UnityEngine.UI.Image handleGlint;
    bool inside;
    float enteredAt;

    public void OnPointerEnter(PointerEventData eventData)
    {
        if (button == null || !button.IsInteractable()) return;
        inside = true;enteredAt = Time.unscaledTime;
    }

    public void OnPointerExit(PointerEventData eventData) { inside = false; }

    void Update()
    {
        bool active = inside && button != null && button.IsInteractable();
        if (corners != null)
            corners.alpha = Mathf.MoveTowards(corners.alpha, active ? .8f : 0f, Time.unscaledDeltaTime * 7f);
        if (handleGlint != null)
        {
            float t = (Time.unscaledTime - enteredAt) / .65f;
            float alpha = active && t < 1f ? Mathf.Sin(Mathf.Clamp01(t) * Mathf.PI) * .7f : 0f;
            var tint = handleGlint.color;tint.a = alpha;handleGlint.color = tint;
        }
    }

    void OnDisable()
    {
        inside = false;
        if (corners != null) corners.alpha = 0f;
        if (handleGlint != null) { var tint = handleGlint.color;tint.a = 0f;handleGlint.color = tint; }
    }
}
