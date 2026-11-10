using UnityEngine;
using UnityEngine.UI;

// Same breakaway/accelerating fall as the fake claw cabinet. The temporary
// interior layout owns the window, so none of this motion enters its saved pose.
// Before the fall, the third maze's exit opens: Live keeps her "I'll open the
// door" promise on screen. Input is already free; this is presentation only.
[DisallowMultipleComponent]
public sealed class OSCursorExperimentDiscardPresentation : MonoBehaviour
{
    const float DoorOpenSeconds = .3f, DoorHoldSeconds = .25f, Duration = .8f;
    const float DoorSeconds = DoorOpenSeconds + DoorHoldSeconds;
    // Native window coordinates (top-left, y down) of the shared bottom exit
    // marker and the tray rules beneath it.
    static readonly Vector2 ExitTopLeft = new Vector2(121, 169);
    const int ExitWidth = 14, ExitHeight = 6, DoorSlide = 4, GapTop = 175, GapBottom = 183, GapWidth = 10;
    static readonly Color Ink = new Color32(0x87, 0x82, 0x7e, 0xff);
    OSWindowUnit window, settings;
    RectTransform rect;
    CanvasGroup group;
    Vector2 origin;
    Quaternion rotation;
    bool active, hasFocus, originalRaycasts, originalInteractable;
    float elapsed, distance;
    Image exitImage, leftDoor, rightDoor, gap, leftJamb, rightJamb;
    Sprite leftSprite, rightSprite;
    public bool IsFalling => active;
    void OnEnable() => hasFocus = Application.isFocused;
    void OnApplicationFocus(bool focus) => hasFocus = focus;
    void OnDisable() => ResetPresentation();

    public void Begin(OSWindowUnit target)
    {
        ResetPresentation();
        if (target == null || !target.IsOpen) return;
        window = target; rect = window.WindowRect;
        origin = rect.anchoredPosition; rotation = rect.localRotation;
        group = window.GetComponent<CanvasGroup>();
        if (group != null)
        {
            originalRaycasts = group.blocksRaycasts; originalInteractable = group.interactable;
            group.blocksRaycasts = group.interactable = false;
        }
        var canvas = window.GetComponentInParent<Canvas>();
        var canvasRect = canvas != null ? canvas.rootCanvas.transform as RectTransform : null;
        distance = (canvasRect != null ? canvasRect.rect.height : 360f) + rect.rect.height * 1.5f;
        BuildDoor();
        active = true;
        if (exitImage != null) Chapter02Audio.Play("Chapter02_CageDoor", .58f, 1.18f);
        else Chapter02Audio.Play("Chapter02_ShellDrop", .63f, 1.08f);
    }

    void BuildDoor()
    {
        var stage = window.transform.Find("Stage3") as RectTransform;
        foreach (var t in window.GetComponentsInChildren<RectTransform>(true))
            if (stage == null && t.name == "Stage3") stage = t;
        exitImage = stage != null ? stage.Find("Exit")?.GetComponent<Image>() : null;
        if (exitImage == null || exitImage.sprite == null || !exitImage.gameObject.activeInHierarchy) { exitImage = null; return; }
        Sprite source = exitImage.sprite;
        Rect r = source.rect;
        int half = ExitWidth / 2;
        leftSprite = Sprite.Create(source.texture, new Rect(r.x, r.y, half, r.height), new Vector2(0, 1), 1f);
        rightSprite = Sprite.Create(source.texture, new Rect(r.x + half, r.y, r.width - half, r.height), new Vector2(0, 1), 1f);
        // The gap is opaque window background covering the tray rules; jambs frame it.
        gap = MakeImage("ExitDoorGap", stage, null, Color.black);
        leftJamb = MakeImage("ExitDoorJambL", stage, null, Ink);
        rightJamb = MakeImage("ExitDoorJambR", stage, null, Ink);
        leftDoor = MakeImage("ExitDoorL", stage, leftSprite, Color.white);
        rightDoor = MakeImage("ExitDoorR", stage, rightSprite, Color.white);
        leftDoor.rectTransform.sizeDelta = new Vector2(half, ExitHeight);
        rightDoor.rectTransform.sizeDelta = new Vector2(ExitWidth - half, ExitHeight);
        exitImage.enabled = false;
        RenderDoor(0f);
    }

    static Image MakeImage(string name, RectTransform parent, Sprite sprite, Color color)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        go.layer = parent.gameObject.layer;
        var rt = (RectTransform)go.transform; rt.SetParent(parent, false);
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0, 1);
        var image = go.GetComponent<Image>(); image.sprite = sprite; image.color = color; image.raycastTarget = false;
        return image;
    }

    static void Place(Image image, float x, float y, float w, float h)
    {
        image.rectTransform.anchoredPosition = new Vector2(Mathf.Round(x), -Mathf.Round(y));
        image.rectTransform.sizeDelta = new Vector2(Mathf.Max(0, Mathf.Round(w)), Mathf.Max(0, Mathf.Round(h)));
    }

    void RenderDoor(float t)
    {
        if (leftDoor == null) return;
        // Ease-out slide: a click, then the halves part and the tray wall opens.
        float open = 1f - (1f - t) * (1f - t) * (1f - t);
        float slide = Mathf.Round(DoorSlide * open);
        Place(leftDoor, ExitTopLeft.x - slide, ExitTopLeft.y, ExitWidth / 2, ExitHeight);
        Place(rightDoor, ExitTopLeft.x + ExitWidth / 2 + slide, ExitTopLeft.y, ExitWidth - ExitWidth / 2, ExitHeight);
        float width = Mathf.Round(GapWidth * open);
        float left = ExitTopLeft.x + ExitWidth / 2 - Mathf.Floor(width / 2);
        Place(gap, left, GapTop, width, GapBottom - GapTop);
        bool framed = width >= 2;
        leftJamb.enabled = rightJamb.enabled = framed;
        Place(leftJamb, left - 1, GapTop + 3, 1, GapBottom - GapTop - 3);
        Place(rightJamb, left + width, GapTop + 3, 1, GapBottom - GapTop - 3);
    }

    void Update()
    {
        var os = OS_Handle.Instance;
        if (settings == null && os != null) settings = os.transform.Find("Windows/WindowUnit_Option")?.GetComponent<OSWindowUnit>();
        if (!active || !hasFocus || os == null || !os.IsOSVisible || OSSessionPersistence.IsRestoring
            || GameSessionFlow.Instance?.IsBusy == true || GameTestConsole.BlocksGameplayInput
            || (settings != null && settings.IsOpen && !settings.IsMinimized)) return;
        Tick(Mathf.Min(Time.unscaledDeltaTime, .05f));
    }

    void Tick(float dt)
    {
        if (!active || rect == null) return;
        float total = (exitImage != null ? DoorSeconds : 0f) + Duration;
        float previousTime = elapsed;
        elapsed = Mathf.Min(total, elapsed + Mathf.Max(0, dt));
        float fallStart = exitImage != null ? DoorSeconds : 0f;
        if (exitImage != null && previousTime < fallStart && elapsed >= fallStart)
            Chapter02Audio.Play("Chapter02_ShellDrop", .63f, 1.08f);
        if (exitImage != null) RenderDoor(Mathf.Clamp01(elapsed / DoorOpenSeconds));
        if (elapsed < fallStart) return;
        float t = (elapsed - fallStart) / Duration;
        rect.anchoredPosition = origin + new Vector2(Mathf.Round(10f * t * t), -Mathf.Round(distance * t * t));
        rect.localRotation = rotation * Quaternion.Euler(0, 0, -7f * t * t);
        if (elapsed < total) return;
        window.PrepareRuntimeClosed();
        ResetPresentation();
    }

    void ClearDoor()
    {
        foreach (var image in new[] { leftDoor, rightDoor, gap, leftJamb, rightJamb })
            if (image != null) Destroy(image.gameObject);
        if (leftSprite != null) Destroy(leftSprite);
        if (rightSprite != null) Destroy(rightSprite);
        if (exitImage != null) exitImage.enabled = true;
        leftDoor = rightDoor = gap = leftJamb = rightJamb = null; leftSprite = rightSprite = null; exitImage = null;
    }

    public void ResetPresentation()
    {
        ClearDoor();
        if (rect != null) { rect.anchoredPosition = origin; rect.localRotation = rotation; }
        if (group != null) { group.blocksRaycasts = originalRaycasts; group.interactable = originalInteractable; }
        rect = null; window = null; group = null; active = false; elapsed = 0f;
    }
#if UNITY_EDITOR
    public void DebugStep(float dt) => Tick(dt);
#endif
}
