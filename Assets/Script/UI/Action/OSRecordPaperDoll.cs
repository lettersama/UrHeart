using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;

// A presentation-only paper stand. Its bottom pivot never translates; grabbing
// the opaque upper half tilts the illustration around that fixed support.
[DisallowMultipleComponent]
[RequireComponent(typeof(UnityEngine.UI.Image))]
public sealed class OSRecordPaperDoll : MonoBehaviour, ICanvasRaycastFilter,
    IPointerDownHandler, IPointerUpHandler, IInitializePotentialDragHandler,
    IBeginDragHandler, IDragHandler, IEndDragHandler, IScrollHandler, ICancelHandler
{
    public const float RowHeight = 94f;
    public const float MaximumAngle = 30f;
    const float BottomInset = 8f;
    const float SpringFrequency = 14f; // radians/second
    const float SpringDamping = .30f;
    const int NoPointer = int.MinValue;
    static readonly Dictionary<Sprite, byte[]> AlphaMasks = new Dictionary<Sprite, byte[]>();

    OSRecordWindowController owner;
    OSWindowUnit window;
    UnityEngine.UI.ScrollRect scroll;
    UnityEngine.UI.Image dollImage;
    RectTransform dollRect, rowRect;
    byte[] alphaMask;
    int maskWidth, maskHeight, pointerId = NoPointer;
    bool hasFocus = true;
    float angle, angularVelocity, startPointerX, startAngle, grabHeight, lastDragTime;

    public RectTransform DollRect => dollRect;
    public UnityEngine.UI.Image DollImage => dollImage;
    public int BoundRecordIndex { get; private set; } = RecordUnit.InvalidRecordIndex;
    public float Angle => angle;
    public float AngularVelocity => angularVelocity;
    public bool IsDragging => pointerId != NoPointer;
    public bool IsSettled => !IsDragging && angle == 0f && angularVelocity == 0f;

    bool CanInteract => isActiveAndEnabled && hasFocus && owner != null && owner.CanInteract
        && window != null && !window.IsOpening && !window.IsClosing && alphaMask != null;

    void Awake()
    {
        dollRect = (RectTransform)transform;
        dollImage = GetComponent<UnityEngine.UI.Image>();
    }

    void OnEnable() => hasFocus = Application.isFocused;
    void OnDisable() => ResetPose();
    void OnApplicationFocus(bool focused) { hasFocus = focused; if (!focused) ResetPose(); }
    void OnApplicationPause(bool paused) { if (paused) ResetPose(); }

    public void Bind(OSRecordWindowController controller, OSWindowUnit recordWindow, int recordIndex, Sprite sprite)
    {
        if (dollRect == null) Awake();
        bool changed = BoundRecordIndex != recordIndex || dollImage.sprite != sprite;
        owner = controller; window = recordWindow;
        rowRect = dollRect.parent as RectTransform;
        scroll = GetComponentInParent<UnityEngine.UI.ScrollRect>();
        BoundRecordIndex = recordIndex;
        dollImage.sprite = sprite;
        dollImage.color = Color.white;
        dollImage.type = UnityEngine.UI.Image.Type.Simple;
        dollImage.preserveAspect = false;
        dollImage.raycastTarget = true;
        dollRect.anchorMin = dollRect.anchorMax = new Vector2(.5f, 0f);
        // UI pixels follow the source rect, independent of its world-space PPU.
        dollRect.sizeDelta = sprite.rect.size;
        dollRect.anchoredPosition = new Vector2(0f, BottomInset);
        dollRect.localScale = Vector3.one;
        if (changed)
        {
            ResetPose();
            maskWidth = Mathf.RoundToInt(sprite.rect.width);
            maskHeight = Mathf.RoundToInt(sprite.rect.height);
            if (!AlphaMasks.TryGetValue(sprite, out alphaMask))
            {
                alphaMask = ReadAlphaMask(sprite, maskWidth, maskHeight);
                if (alphaMask != null) AlphaMasks.Add(sprite, alphaMask);
            }
            // The source has transparent rows below its feet. Anchor the first
            // opaque row so that padding does not make the support slide sideways.
            int supportRow = 0;
            if (alphaMask != null)
                for (int i = 0; i < alphaMask.Length; i++)
                    if (alphaMask[i] >= 32) { supportRow = i / maskWidth; break; }
            dollRect.pivot = new Vector2(.5f, (float)supportRow / maskHeight);
            ApplyPose();
        }
    }

    public void Clear()
    {
        ResetPose(); BoundRecordIndex = RecordUnit.InvalidRecordIndex;
        alphaMask = null;
        if (dollImage != null) dollImage.sprite = null;
    }

    public void ResetPose()
    {
        pointerId = NoPointer; angle = angularVelocity = 0f;
        ApplyPose();
    }

    void Update()
    {
        if (!CanInteract)
        {
            if (!IsSettled) ResetPose();
            return;
        }
        if (IsDragging)
        {
            if (OS_Handle.Instance != null && !OS_Handle.Instance.IsWindowMouseActive(window)) ResetPose();
            return;
        }
        if (IsSettled) return;
        IntegrateSpring(ref angle, ref angularVelocity, Time.unscaledDeltaTime);
        ApplyPose();
    }

    // Exact solution of x'' + 2*zeta*w*x' + w*w*x = 0. This is stable across
    // frame rates, allows natural overshoot, and has no scripted swing count.
    public static void IntegrateSpring(ref float displacement, ref float velocity, float deltaTime)
    {
        if (deltaTime <= 0f) return;
        float decayRate = SpringDamping * SpringFrequency;
        float frequency = SpringFrequency * Mathf.Sqrt(1f - SpringDamping * SpringDamping);
        float decay = Mathf.Exp(-decayRate * deltaTime);
        float sine = Mathf.Sin(frequency * deltaTime), cosine = Mathf.Cos(frequency * deltaTime);
        float next = decay * (displacement * cosine + (velocity + decayRate * displacement) / frequency * sine);
        velocity = decay * (velocity * cosine
            - (decayRate * velocity + SpringFrequency * SpringFrequency * displacement) / frequency * sine);
        displacement = Mathf.Clamp(next, -MaximumAngle, MaximumAngle);
        if (next != displacement && Mathf.Sign(velocity) == Mathf.Sign(displacement)) velocity = 0f;
        if (Mathf.Abs(displacement) < .04f && Mathf.Abs(velocity) < .3f) displacement = velocity = 0f;
    }

    void ApplyPose()
    {
        if (dollRect == null) return;
        dollRect.anchoredPosition = new Vector2(0f, BottomInset);
        dollRect.localRotation = Quaternion.Euler(0f, 0f, angle);
    }

    public bool IsRaycastLocationValid(Vector2 screenPoint, Camera eventCamera)
    {
        if (!CanInteract || dollRect == null || !RectTransformUtility.ScreenPointToLocalPointInRectangle(
            dollRect, screenPoint, eventCamera, out Vector2 point)) return false;
        Rect rect = dollRect.rect;
        float u = (point.x - rect.xMin) / rect.width, v = (point.y - rect.yMin) / rect.height;
        if (u < 0f || u >= 1f || v < .5f || v >= 1f) return false;
        int x = Mathf.Min(maskWidth - 1, Mathf.FloorToInt(u * maskWidth));
        int y = Mathf.Min(maskHeight - 1, Mathf.FloorToInt(v * maskHeight));
        return alphaMask[y * maskWidth + x] >= 32;
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        if (eventData.button != PointerEventData.InputButton.Left || IsDragging
            || !IsRaycastLocationValid(eventData.position, eventData.pressEventCamera)
            || !RectTransformUtility.ScreenPointToLocalPointInRectangle(rowRect, eventData.position,
                eventData.pressEventCamera, out Vector2 point)) return;
        // Focus without invoking OSWindowUnit's resize/title-drag detection.
        if (OS_Handle.Instance != null) OS_Handle.Instance.NotifyWindowPointerPressed(window);
        else window.BringToFront();
        scroll?.StopMovement();
        pointerId = eventData.pointerId;
        startPointerX = point.x; startAngle = angle;
        grabHeight = Mathf.Max(dollRect.rect.height * .5f, point.y - dollRect.localPosition.y);
        angularVelocity = 0f; lastDragTime = Time.unscaledTime;
    }

    public void OnInitializePotentialDrag(PointerEventData eventData) => eventData.useDragThreshold = false;
    public void OnBeginDrag(PointerEventData eventData) { }

    public void OnDrag(PointerEventData eventData)
    {
        if (eventData.pointerId != pointerId) return;
        if (!CanInteract) { ResetPose(); return; }
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(rowRect, eventData.position,
            eventData.pressEventCamera, out Vector2 point)) return;
        float next = Mathf.Clamp(startAngle - Mathf.Atan2(point.x - startPointerX, grabHeight) * Mathf.Rad2Deg,
            -MaximumAngle, MaximumAngle);
        float elapsed = Mathf.Max(.001f, Time.unscaledTime - lastDragTime);
        angularVelocity = Mathf.Lerp(angularVelocity, Mathf.Clamp((next - angle) / elapsed, -180f, 180f), .65f);
        angle = next; lastDragTime = Time.unscaledTime;
        ApplyPose();
    }

    public void OnPointerUp(PointerEventData eventData) => Release(eventData);
    public void OnEndDrag(PointerEventData eventData) => Release(eventData);
    public void OnCancel(BaseEventData eventData) => ResetPose();

    void Release(PointerEventData eventData)
    {
        if (eventData.button != PointerEventData.InputButton.Left || eventData.pointerId != pointerId) return;
        pointerId = NoPointer;
        // Holding the doll still before letting go should not retain an old flick.
        angularVelocity *= Mathf.Exp(-18f * Mathf.Max(0f, Time.unscaledTime - lastDragTime));
        if (!CanInteract) ResetPose();
    }

    public void OnScroll(PointerEventData eventData)
    {
        if (!IsDragging && scroll != null)
            ExecuteEvents.Execute(scroll.gameObject, eventData, ExecuteEvents.scrollHandler);
    }

    static byte[] ReadAlphaMask(Sprite sprite, int width, int height)
    {
        // The two original sprites are not CPU-readable. Cache only their tiny
        // alpha masks; keep the art/import settings and displayed sprite intact.
        RenderTexture previous = RenderTexture.active, target = null;
        Texture2D copy = null;
        try
        {
            Rect source = sprite.textureRect;
            Vector2 offset = sprite.textureRectOffset;
            int cropWidth = Mathf.RoundToInt(source.width), cropHeight = Mathf.RoundToInt(source.height);
            target = RenderTexture.GetTemporary(cropWidth, cropHeight, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
            target.filterMode = FilterMode.Point;
            Graphics.Blit(sprite.texture, target,
                new Vector2(source.width / sprite.texture.width, source.height / sprite.texture.height),
                new Vector2(source.x / sprite.texture.width, source.y / sprite.texture.height));
            RenderTexture.active = target;
            copy = new Texture2D(cropWidth, cropHeight, TextureFormat.RGBA32, false, true);
            copy.ReadPixels(new Rect(0, 0, cropWidth, cropHeight), 0, 0, false);
            Color32[] pixels = copy.GetPixels32();
            var result = new byte[width * height];
            int left = Mathf.RoundToInt(offset.x), bottom = Mathf.RoundToInt(offset.y);
            for (int y = 0; y < cropHeight; y++)
                for (int x = 0; x < cropWidth; x++)
                {
                    int targetX = x + left, targetY = y + bottom;
                    if (targetX >= 0 && targetX < width && targetY >= 0 && targetY < height)
                        result[targetY * width + targetX] = pixels[y * cropWidth + x].a;
                }
            return result;
        }
        catch (Exception exception)
        {
            Debug.LogWarning("[OS Record] Paper figure alpha mask unavailable; leaving the detail area scrollable. " + exception.Message);
            return null;
        }
        finally
        {
            RenderTexture.active = previous;
            if (target != null) RenderTexture.ReleaseTemporary(target);
            if (copy != null) Destroy(copy);
        }
    }
}
