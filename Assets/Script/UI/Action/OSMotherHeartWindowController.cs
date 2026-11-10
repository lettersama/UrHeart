using System;
using UnityEngine;

/// <summary>A quiet OS window sharing the final treatment's complete heart frames.</summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(OSWindowUnit))]
public sealed class OSMotherHeartWindowController : MonoBehaviour
{
    const string FramePath = "Chapter04/FinalHeal/Heart/heart_3_whole_idle";
    const float FramesPerSecond = 9f;
    readonly Texture2D[] frames = new Texture2D[3];
    OSWindowUnit window;
    UnityEngine.UI.RawImage heartImage;
    float elapsed;
    bool paused, hasFocus = true, loadedFrames;

    public OSWindowUnit Window => window != null ? window : (window = GetComponent<OSWindowUnit>());
    public UnityEngine.UI.RawImage HeartImage => heartImage;

    public static OSMotherHeartWindowController Create(Transform parent, OSWindowUnit frameTemplate)
    {
        if (parent == null) return null;
        var window = OSChapter04SmallWindowFrame.Create("WindowUnit_MotherHeart", parent, frameTemplate,
            new Vector2(200f, 208f), new Vector2(65f, 0f), true, out var body, out _);
        var heartNode = new GameObject("MotherHeart", typeof(RectTransform), typeof(UnityEngine.UI.RawImage));
        heartNode.layer = parent.gameObject.layer;
        var heart = heartNode.GetComponent<UnityEngine.UI.RawImage>();
        heart.rectTransform.SetParent(body, false);
        heart.rectTransform.anchorMin = heart.rectTransform.anchorMax = heart.rectTransform.pivot = new Vector2(.5f, .5f);
        heart.rectTransform.sizeDelta = new Vector2(140f, 172f);
        // DetailsArea is 187 px tall: offset its half-pixel center for this even-sized image.
        heart.rectTransform.anchoredPosition = new Vector2(0f, .5f);
        heart.color = Color.white;
        heart.raycastTarget = false;
        var controller = window.gameObject.AddComponent<OSMotherHeartWindowController>();
        controller.window = window;
        controller.heartImage = heart;
        controller.RenderAt(0f);
        window.PrepareRuntimeClosed();
        return controller;
    }

    public void SetPaused(bool value) => paused = value;

    /// <summary>Set the local animation clock explicitly, including while closed, for preview/QA.</summary>
    public void RenderAt(float seconds)
    {
        if (float.IsNaN(seconds) || float.IsInfinity(seconds)) return;
        elapsed = Mathf.Max(0f, seconds);
        if (!loadedFrames)
        {
            loadedFrames = true;
            for (int i = 0; i < frames.Length; i++)
            {
                frames[i] = Resources.Load<Texture2D>(FramePath + i);
                if (frames[i] != null) frames[i].filterMode = FilterMode.Point;
            }
        }
        if (heartImage != null)
            heartImage.texture = frames[Mathf.FloorToInt((elapsed * FramesPerSecond) % frames.Length)];
    }

    void OnEnable()
    {
        hasFocus = Application.isFocused;
        RenderAt(elapsed);
    }

    void OnApplicationFocus(bool focused) => hasFocus = focused;

    void Update()
    {
        var current = Window;
        if (paused || !hasFocus || current == null || !current.IsOpen || current.IsMinimized
            || current.IsOpening || current.IsClosing || OS_Handle.Instance?.IsOSVisible == false
            || OSSessionPersistence.IsRestoring || GameSessionFlow.Instance?.IsBusy == true
            || GameTestConsole.BlocksGameplayInput) return;
        RenderAt(elapsed + Mathf.Min(Time.unscaledDeltaTime, .1f));
    }
}

/// <summary>Only the shared OS chrome; neither window starts a story or owns its registration.</summary>
internal static class OSChapter04SmallWindowFrame
{
    internal static OSWindowUnit Create(string name, Transform parent, OSWindowUnit template,
        Vector2 size, Vector2 position, bool withButtons, out RectTransform body, out RectTransform grip)
    {
        var root = new GameObject(name, typeof(RectTransform));
        root.SetActive(false);
        root.layer = parent.gameObject.layer;
        var rect = (RectTransform)root.transform;
        rect.SetParent(parent, false);
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(.5f, .5f);
        rect.sizeDelta = size;

        var border = root.AddComponent<UnityEngine.UI.Image>();
        var source = template != null ? template.transform.Find("Bk")?.GetComponent<UnityEngine.UI.Image>() : null;
        if (source == null && template != null) source = template.GetComponent<UnityEngine.UI.Image>();
        border.color = source != null ? source.color : new Color32(53, 51, 49, 255);
        if (source != null)
        {
            border.sprite = source.sprite;
            border.type = source.type;
            border.material = source.material;
            border.pixelsPerUnitMultiplier = source.pixelsPerUnitMultiplier;
        }
        border.raycastTarget = true;
        var top = Image("DragArea", rect, Color.clear, true);
        grip = top.rectTransform;
        Stretch(grip, new Vector2(0f, 1f), Vector2.one, new Vector2(1f, -15f), new Vector2(-1f, -1f));
        var font = template != null ? template.transform.Find("TopName")?.GetComponent<TMPro.TMP_Text>() : null;
        if (font == null && template != null) font = template.GetComponentInChildren<TMPro.TMP_Text>(true);
        var titleNode = new GameObject("Title", typeof(RectTransform), typeof(TMPro.TextMeshProUGUI));
        titleNode.layer = parent.gameObject.layer;
        titleNode.transform.SetParent(grip, false);
        var title = titleNode.GetComponent<TMPro.TextMeshProUGUI>();
        if (font != null) title.font = font.font;
        title.text = "???";
        title.fontSize = font != null ? Mathf.Clamp(font.fontSize, 8f, 11f) : 10f;
        title.alignment = TMPro.TextAlignmentOptions.MidlineLeft;
        title.color = font != null ? font.color : new Color32(159, 155, 151, 255);
        title.raycastTarget = false;
        title.enableWordWrapping = false;
        Stretch(title.rectTransform, Vector2.zero, Vector2.one,
            new Vector2(4f, 0f), new Vector2(withButtons ? -45f : -4f, 0f));

        body = Image("DetailsArea", rect, new Color32(15, 14, 16, 255)).rectTransform;
        Stretch(body, Vector2.zero, Vector2.one, new Vector2(3f, 3f), new Vector2(-3f, -18f));
        body.gameObject.AddComponent<CanvasGroup>();
        body.gameObject.AddComponent<UnityEngine.UI.RectMask2D>();

        var window = root.AddComponent<OSWindowUnit>();
        // The key is a temporary story window smaller than OSWindowUnit's normal 200x120 minimum.
        if (size.x < 200f || size.y < 120f) window.SetTemporaryOpeningLayout(size, position, size);
        else window.SetOpeningLayout(position, size);
        window.ConfigureInteraction(true, false);
        if (withButtons)
        {
            AddButton(template, grip, window, OSWindowButton.WindowAction.ToggleAlwaysOnTop, -29f);
            AddButton(template, grip, window, OSWindowButton.WindowAction.Minimize, -15f);
            AddButton(template, grip, window, OSWindowButton.WindowAction.Close, -1f);
        }
        return window;
    }

    internal static UnityEngine.UI.Image Image(string name, Transform parent, Color color, bool raycast = false)
    {
        var node = new GameObject(name, typeof(RectTransform), typeof(UnityEngine.UI.Image));
        node.layer = parent.gameObject.layer;
        node.transform.SetParent(parent, false);
        var image = node.GetComponent<UnityEngine.UI.Image>();
        image.color = color;
        image.raycastTarget = raycast;
        return image;
    }

    static void AddButton(OSWindowUnit template, Transform parent, OSWindowUnit window,
        OSWindowButton.WindowAction action, float right)
    {
        if (template == null) return;
        var source = Array.Find(template.GetComponentsInChildren<OSWindowButton>(true),
            button => button.GetComponentInParent<OSWindowUnit>(true) == template && button.Action == action);
        if (source == null)
        {
            Debug.LogWarning("[MotherHeart] Window template has no " + action + " button.");
            return;
        }
        var clone = UnityEngine.Object.Instantiate(source.gameObject, parent, false);
        clone.name = source.name;
        clone.GetComponent<OSWindowButton>().BindToWindow(window);
        Stretch((RectTransform)clone.transform, Vector2.one, Vector2.one,
            new Vector2(right - 14f, -14f), new Vector2(right, 0f));
        clone.SetActive(true);
    }

    static void Stretch(RectTransform rect, Vector2 min, Vector2 max, Vector2 insetMin, Vector2 insetMax)
    {
        rect.anchorMin = min;
        rect.anchorMax = max;
        rect.offsetMin = insetMin;
        rect.offsetMax = insetMax;
    }
}
