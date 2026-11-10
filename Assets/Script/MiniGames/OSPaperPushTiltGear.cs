using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// Chapter 2, match 2: a small gear that pops out of the paper-push window's
// top-left corner after two completed rounds. Dragging tilts the whole window.
//   - Appears with a stepped pop, then stays still (including on hover).
//   - Hover: horizontal-resize cursor and the small tip.
//   - The tip also shows once by itself when the gear first appears.
// Only input and visuals live here; the match controller owns pawn movement.
[RequireComponent(typeof(CanvasRenderer))]
public sealed class OSPaperPushTiltGear : MaskableGraphic,
    IPointerEnterHandler, IPointerExitHandler, IPointerDownHandler, IPointerUpHandler,
    IBeginDragHandler, IDragHandler, IEndDragHandler
{
    static readonly Color32 Ink = new Color32(0x87, 0x82, 0x7e, 255);
    static readonly Color32 Black = new Color32(0, 0, 0, 255);
    static readonly string[] Bitmap =
    {
        ".....###.....",
        ".##..###..##.",
        ".###########.",
        "..#########..",
        "..####.####..",
        "#####...#####",
        "#####...#####",
        "#####...#####",
        "..####.####..",
        "..#########..",
        ".###########.",
        ".##..###..##.",
        ".....###.....",
    };
    const float FullDragDistance = 36f, MaximumTilt = 18f, FirstTipSeconds = 5f;
    const string TipKey = "ui.os.paper_push_ch02.tilt_tip", TipFallback = "按住左上齿轮左右拖动，倾斜窗口，让纸偶滑向桌边。";

    OSWindowUnit window;
    RectTransform tip, tipFrame;
    TMP_Text tipLabel;
    bool shown, hovered, held, everShown, used, interactive;
    float popClock = 1f, firstTipLeft, pressPointerX, pressTilt, lastDragClick;
    Camera pressCamera;
    public bool IsHeld => held;
    public float TiltDegrees { get; private set; }
    public float DragDirection => -TiltDegrees / MaximumTilt;
    public void RestoreTilt(float degrees) => TiltDegrees = Mathf.Clamp(degrees, -MaximumTilt, MaximumTilt);

    public static OSPaperPushTiltGear Create(OSWindowUnit window, TMP_Text labelTemplate)
    {
        if (window == null || window.WindowRect == null) return null;
        var root = window.WindowRect;
        var node = new GameObject("Chapter02TiltGear", typeof(RectTransform), typeof(CanvasRenderer));
        node.layer = root.gameObject.layer;
        var rect = (RectTransform)node.transform;
        rect.SetParent(root, false);rect.SetAsLastSibling();
        // Centred one unit outside the corner, so it reads as poking out from behind.
        rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f);rect.pivot = new Vector2(.5f, .5f);
        rect.anchoredPosition = new Vector2(-1f, 1f);rect.sizeDelta = new Vector2(15f, 15f);
        var gear = node.AddComponent<OSPaperPushTiltGear>();
        gear.window = window;
        gear.BuildTip(labelTemplate);
        node.SetActive(false);
        return gear;
    }

    void BuildTip(TMP_Text template)
    {
        var parent = window.WindowRect;
        var box = new GameObject("Chapter02TiltGearTip", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        box.layer = parent.gameObject.layer;
        tip = (RectTransform)box.transform;tip.SetParent(parent, false);
        // Keep the tip below the title bar, inside the default window bounds.
        tip.anchorMin = tip.anchorMax = new Vector2(0f, 1f);tip.pivot = new Vector2(0f, 1f);
        tip.anchoredPosition = new Vector2(8f, -24f);
        var border = box.GetComponent<Image>();border.color = Ink;border.raycastTarget = false;
        var inner = new GameObject("Fill", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        inner.layer = box.layer;
        tipFrame = (RectTransform)inner.transform;tipFrame.SetParent(tip, false);
        tipFrame.anchorMin = Vector2.zero;tipFrame.anchorMax = Vector2.one;
        tipFrame.offsetMin = Vector2.one;tipFrame.offsetMax = -Vector2.one;
        var fill = inner.GetComponent<Image>();fill.color = Black;fill.raycastTarget = false;
        if (template != null)
        {
            // A copy keeps the scene's localisation binding and per-language font switching.
            var copy = Instantiate(template.gameObject, tip, false);
            copy.name = "Label";copy.SetActive(true);
            foreach (var extra in copy.GetComponentsInChildren<Transform>(true))
                if (extra != copy.transform) Destroy(extra.gameObject);
            tipLabel = copy.GetComponent<TMP_Text>();
            var labelRect = tipLabel.rectTransform;
            labelRect.anchorMin = labelRect.anchorMax = labelRect.pivot = new Vector2(.5f, .5f);
            labelRect.anchoredPosition = Vector2.zero;
            tipLabel.raycastTarget = false;
            tipLabel.textWrappingMode = TextWrappingModes.Normal;tipLabel.overflowMode = TextOverflowModes.Overflow;
            tipLabel.alignment = TextAlignmentOptions.Center;tipLabel.enableAutoSizing = false;
            tipLabel.fontSize = 9f;tipLabel.color = Ink;
            OSLocalizedText.SetRuntime(tipLabel, TipKey, TipFallback);
        }
        tip.gameObject.SetActive(false);
    }

    public void Tick(bool show, bool canUse)
    {
        float dt = Mathf.Min(Time.unscaledDeltaTime, .05f);
        interactive = show && canUse;
        if (!interactive && held) EndHold();
        if (show != shown)
        {
            shown = show;
            gameObject.SetActive(show);
            if (show) { popClock = 0f;if (!everShown) { everShown = true;firstTipLeft = FirstTipSeconds; } }
            else { hovered = false;CursorHandle.Instance?.ClearContextState(this); }
        }
        if (!shown) { if (tip != null) tip.gameObject.SetActive(false);return; }
        transform.SetAsLastSibling();

        // Stepped pop: 0 -> 60% -> 120% -> 100%, no blended frames.
        popClock += dt;
        float scale = popClock < .06f ? .6f : popClock < .12f ? 1.2f : 1f;
        rectTransform.localScale = new Vector3(scale, scale, 1f);

        rectTransform.localRotation = Quaternion.identity;

        if (interactive) firstTipLeft = Mathf.Max(0f, firstTipLeft - dt);
        bool tipVisible = !held && popClock >= .12f && (hovered || firstTipLeft > 0f && !used);
        if (tip != null)
        {
            tip.gameObject.SetActive(tipVisible);
            if (tipVisible && tipLabel != null)
            {
                float width = Mathf.Min(220f, window.WindowRect.rect.width - 24f);
                Vector2 size = tipLabel.GetPreferredValues(tipLabel.text, width, 0f);
                tip.sizeDelta = new Vector2(Mathf.Ceil(size.x) + 8f, Mathf.Ceil(size.y) + 4f);
                tipLabel.rectTransform.sizeDelta = new Vector2(Mathf.Ceil(size.x) + 2f, Mathf.Ceil(size.y));
                tip.SetAsLastSibling();
            }
        }
    }

    public void Hide()
    {
        if (held) EndHold();
        shown = hovered = false;
        CursorHandle.Instance?.ClearContextState(this);
        if (tip != null) tip.gameObject.SetActive(false);
        gameObject.SetActive(false);
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        hovered = true;
        CursorHandle.Instance?.SetContextState(this, CursorVisualState.ResizeHorizontal);
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        hovered = false;
        if (!held) CursorHandle.Instance?.ClearContextState(this);
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        if (!interactive || eventData.button != PointerEventData.InputButton.Left) return;
        held = true;pressCamera = eventData.pressEventCamera;
        pressPointerX = PointerX(eventData.position);pressTilt = TiltDegrees;lastDragClick = 0f;
        CursorHandle.Instance?.SetContextState(this, CursorVisualState.Pressed);
    }

    public void OnBeginDrag(PointerEventData eventData) { }

    public void OnDrag(PointerEventData eventData)
    {
        if (!held) return;
        float delta = PointerX(eventData.position) - pressPointerX;
        TiltDegrees = Mathf.Clamp(pressTilt - delta / FullDragDistance * MaximumTilt, -MaximumTilt, MaximumTilt);
        if (interactive && shown && Mathf.Abs(delta - lastDragClick) >= 3f)
        { Chapter02Audio.Play("Chapter02_MetalLatch", .3f, 1.25f, .09f);lastDragClick = delta; }
        if (Mathf.Abs(delta) > .5f) used = true;
    }

    public void OnEndDrag(PointerEventData eventData) => EndHold();
    public void OnPointerUp(PointerEventData eventData) => EndHold();

    void EndHold()
    {
        if (!held) return;
        held = false;
        if (hovered) CursorHandle.Instance?.SetContextState(this, CursorVisualState.ResizeHorizontal);
        else CursorHandle.Instance?.ClearContextState(this);
    }

    float PointerX(Vector2 screen)
    {
        // The parent does not rotate with the window, so a held pointer cannot
        // feed the window's own tilt back into its drag distance.
        RectTransformUtility.ScreenPointToLocalPointInRectangle(window.WindowRect.parent as RectTransform,
            screen, pressCamera, out Vector2 local);
        return local.x;
    }

    protected override void OnDisable()
    {
        EndHold();hovered = false;interactive = false;
        CursorHandle.Instance?.ClearContextState(this);
        if (tip != null) tip.gameObject.SetActive(false);
        base.OnDisable();
    }

    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();
        int size = Bitmap.Length;
        float origin = -size * .5f;
        // A one-unit black rim keeps the ink gear readable over the light window frame.
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                if (Filled(x, y)) continue;
                bool edge = false;
                for (int oy = -1; oy <= 1 && !edge; oy++)
                    for (int ox = -1; ox <= 1 && !edge; ox++) edge = Filled(x + ox, y + oy);
                if (edge) Pixel(vh, origin + x, origin + (size - 1 - y), Black);
            }
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
                if (Filled(x, y)) Pixel(vh, origin + x, origin + (size - 1 - y), Ink);
        // The outer rim beyond the bitmap: one ring of black around the tooth tips.
        for (int i = 0; i < size; i++)
        {
            if (Filled(i, 0)) Pixel(vh, origin + i, origin + size, Black);
            if (Filled(i, size - 1)) Pixel(vh, origin + i, origin - 1, Black);
            if (Filled(0, i)) Pixel(vh, origin - 1, origin + (size - 1 - i), Black);
            if (Filled(size - 1, i)) Pixel(vh, origin + size, origin + (size - 1 - i), Black);
        }
    }

    static bool Filled(int x, int y) => y >= 0 && y < Bitmap.Length && x >= 0 && x < Bitmap[y].Length && Bitmap[y][x] == '#';

    static void Pixel(VertexHelper vh, float x, float y, Color32 tint)
    {
        int first = vh.currentVertCount;
        vh.AddVert(new Vector3(x, y), tint, Vector2.zero);
        vh.AddVert(new Vector3(x, y + 1f), tint, Vector2.zero);
        vh.AddVert(new Vector3(x + 1f, y + 1f), tint, Vector2.zero);
        vh.AddVert(new Vector3(x + 1f, y), tint, Vector2.zero);
        vh.AddTriangle(first, first + 1, first + 2);vh.AddTriangle(first, first + 2, first + 3);
    }

    protected override void OnDestroy()
    {
        CursorHandle.Instance?.ClearContextState(this);
        if (tip != null) Destroy(tip.gameObject);
        base.OnDestroy();
    }
}
