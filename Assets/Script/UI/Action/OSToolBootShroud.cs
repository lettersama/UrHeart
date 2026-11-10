using UnityEngine;

/// <summary>
/// While the toolbox boots for the first time, the rest of the OS sinks into a
/// dither and only the toolbox stays lit, as if the machine is handing it the
/// screen. Four dithered pieces surround the window and follow it if dragged.
/// A speaking Live is lifted above this layer and stays readable.
/// </summary>
[DisallowMultipleComponent]
public sealed class OSToolBootShroud : MonoBehaviour
{
    const int AboveParent = 1;
    const int DimLevel = 11;
    const float StepInSeconds = .05f;
    const float StepOutSeconds = .04f;

    readonly Vector3[] corners = new Vector3[4];
    readonly OSDitherFill[] pieces = new OSDitherFill[4];
    RectTransform window;
    Canvas canvas;
    Canvas parentCanvas;
    int level;
    int target;
    float stepTimer;

    public static OSToolBootShroud Create(OSWindowUnit owner)
    {
        RectTransform frame = owner != null ? owner.WindowRect : null;
        Canvas parent = frame != null && frame.parent != null ? frame.parent.GetComponentInParent<Canvas>() : null;
        if (parent == null)
            return null;
        Canvas root = parent.rootCanvas;

        var node = new GameObject("ToolBootShroud", typeof(RectTransform));
        node.layer = root.gameObject.layer;
        var rect = (RectTransform)node.transform;
        rect.SetParent(root.transform, false);
        rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
        rect.offsetMin = rect.offsetMax = Vector2.zero;
        rect.SetAsLastSibling();

        var shroud = node.AddComponent<OSToolBootShroud>();
        shroud.window = frame;
        shroud.parentCanvas = parent;
        shroud.canvas = node.AddComponent<Canvas>();
        for (int i = 0; i < shroud.pieces.Length; i++)
        {
            OSDitherFill piece = OSDitherFill.Create("Dim" + i, rect, Color.black);
            var pieceRect = (RectTransform)piece.transform;
            pieceRect.anchorMin = pieceRect.anchorMax = Vector2.zero;
            pieceRect.pivot = Vector2.zero;
            shroud.pieces[i] = piece;
        }
        node.SetActive(false);
        return shroud;
    }

    public bool IsClear => level == 0 && target == 0;

    public void Show()
    {
        if (target == DimLevel && gameObject.activeSelf)
            return;
        target = DimLevel;
        if (!gameObject.activeSelf)
        {
            stepTimer = 0f;
            gameObject.SetActive(true);
        }
    }

    public void Hide(bool fade)
    {
        target = 0;
        if (!fade)
            ApplyLevel(0);
        if (level == 0 && gameObject.activeSelf)
            gameObject.SetActive(false);
    }

    void LateUpdate()
    {
        if (window == null || parentCanvas == null)
        {
            Hide(false);
            return;
        }

        canvas.overrideSorting = true;
        canvas.sortingLayerID = parentCanvas.sortingLayerID;
        canvas.sortingOrder = parentCanvas.sortingOrder + AboveParent;
        LayoutHole();

        if (level != target)
        {
            stepTimer -= Time.unscaledDeltaTime;
            if (stepTimer <= 0f)
            {
                stepTimer = level < target ? StepInSeconds : StepOutSeconds;
                ApplyLevel(level + (level < target ? 1 : -1));
            }
        }
        if (level == 0 && target == 0)
            gameObject.SetActive(false);
    }

    void ApplyLevel(int value)
    {
        level = Mathf.Clamp(value, 0, DimLevel);
        foreach (OSDitherFill piece in pieces)
            piece.SetLevel(level);
    }

    // Bottom and top span the screen; left and right fill between them.
    void LayoutHole()
    {
        var self = (RectTransform)transform;
        Rect area = self.rect;
        window.GetWorldCorners(corners);
        Vector2 min = self.InverseTransformPoint(corners[0]);
        Vector2 max = self.InverseTransformPoint(corners[2]);
        min = new Vector2(Mathf.Clamp(Mathf.Round(min.x), area.xMin, area.xMax), Mathf.Clamp(Mathf.Round(min.y), area.yMin, area.yMax));
        max = new Vector2(Mathf.Clamp(Mathf.Round(max.x), min.x, area.xMax), Mathf.Clamp(Mathf.Round(max.y), min.y, area.yMax));

        Place(pieces[0], area.xMin, area.yMin, area.xMax, min.y);
        Place(pieces[1], area.xMin, max.y, area.xMax, area.yMax);
        Place(pieces[2], area.xMin, min.y, min.x, max.y);
        Place(pieces[3], max.x, min.y, area.xMax, max.y);

        void Place(OSDitherFill piece, float x0, float y0, float x1, float y1)
        {
            var r = (RectTransform)piece.transform;
            r.anchoredPosition = new Vector2(x0 - area.xMin, y0 - area.yMin);
            r.sizeDelta = new Vector2(Mathf.Max(0f, x1 - x0), Mathf.Max(0f, y1 - y0));
        }
    }
}
