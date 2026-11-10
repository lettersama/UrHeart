using UnityEngine;
using UnityEngine.UI;

public sealed partial class OSCursorHeartTalkDirector
{
    Image continueTip;

    void RenderContinueTip()
    {
        if (!OwnMessageDisplayed || !HasManualLine) { HideContinueTip(); return; }
        if (continueTip == null)
        {
            var parent = messages.WindowLayout?.transform.Find("MessageArea") as RectTransform;
            if (parent == null) return;
            var node = new GameObject("HeartTalkContinueTip", typeof(RectTransform), typeof(Image));
            node.layer = parent.gameObject.layer;
            var rect = (RectTransform)node.transform;
            rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = new Vector2(.5f, 0f);
            rect.pivot = new Vector2(.5f, .5f);
            rect.sizeDelta = new Vector2(10f, 7f);
            continueTip = node.GetComponent<Image>();
            continueTip.raycastTarget = false;
            continueTip.preserveAspect = true;
        }
        if (continueTip.sprite == null)
        {
            var source = OS_Handle.Instance?.MemoryWindowController?.transform.Find("TextArea/ContinueTip")?.GetComponent<Image>();
            if (source != null)
            {
                continueTip.sprite = source.sprite;
                Color color = source.color; color.a = 1f; continueTip.color = color;
            }
        }
        // Reserve while typing so revealing the arrow never shifts the line.
        messages.WindowLayout.SetTextFooterHeight(15f);
        continueTip.gameObject.SetActive(messageTyped && continueTip.sprite != null
            && !messages.LinkWindow.IsOpening && !messages.LinkWindow.IsClosing);
        continueTip.rectTransform.anchoredPosition = new Vector2(0f, 6f + Mathf.Round(Mathf.Sin(tipTime * 4.4f)));
    }

    void HideContinueTip()
    {
        if (continueTip != null) continueTip.gameObject.SetActive(false);
        if (messages != null && IsOwnEntry(messages.DisplayedEntryId)) messages.WindowLayout?.SetTextFooterHeight(0f);
    }

    void DestroyContinueTip()
    {
        if (continueTip != null) Destroy(continueTip.gameObject);
        continueTip = null;
    }
}
