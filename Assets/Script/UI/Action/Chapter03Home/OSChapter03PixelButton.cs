using UnityEngine;

/// <summary>Normal uGUI Button semantics with a one-logical-pixel press.
/// There are no color tweens or intermediate grey button states.</summary>
public sealed class OSChapter03PixelButton : UnityEngine.UI.Button
{
    RectTransform pixelContent;

    public void SetPixelContent(RectTransform content)
    {
        pixelContent = content;
        transition = Transition.None;
        ApplyPress(currentSelectionState);
    }

    protected override void DoStateTransition(SelectionState state, bool instant)
    {
        // Button continues to own pointer/keyboard submission and onClick.
        ApplyPress(state);
    }

    void ApplyPress(SelectionState state)
    {
        if (pixelContent == null) return;
        pixelContent.anchoredPosition = state == SelectionState.Pressed ? new Vector2(1f, -1f) : Vector2.zero;
    }

    protected override void OnDisable()
    {
        if (pixelContent != null) pixelContent.anchoredPosition = Vector2.zero;
        base.OnDisable();
    }
}
