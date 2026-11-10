using System;
using System.Collections;
using System.Collections.Generic;
using MrTGame.Core;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// The ending director owns the input lock, cursor and save checkpoint throughout.
// This component owns only disposable visuals and the temporary work animation.
[DisallowMultipleComponent]
public sealed class OSChapter02RecoveryPresentation : MonoBehaviour
{
    [SerializeField] Sprite[] flyFrames = new Sprite[2];
    [SerializeField] Sprite squashedSprite;
    [SerializeField] Sprite swatterSprite;
    [SerializeField] AudioClip repairSound;
    [SerializeField] AudioClip swatSound;

    sealed class Bug
    {
        public Image image;
        public Vector2 home, hitPosition;
        public float seed, hitTime = -1f;
    }
    sealed class Block
    {
        public RectTransform rect;
        public Vector2 origin;
        public float delay, speed, drift;
    }

    static readonly Color Paper = new Color32(135, 130, 126, 255);
    static readonly Color ControlledRed = new Color32(210, 47, 43, 255);
    // Hold the middle of the handle (15 px from the bottom), not its tip.
    // The authored mesh head is centered at y=31 in the 24x40 sprite.
    static readonly Vector2 SwatterGripPivot = new Vector2(.5f, .375f);
    const float SwatterHeadOffset = 16f;
    readonly List<Bug> bugs = new List<Bug>();
    readonly List<Block> blocks = new List<Block>();
    RectTransform root, bugLayer, blockLayer, panel;
    Image swatter;
    Canvas canvas;
    CursorHandle cursor;
    UnityEngine.Object cursorOwner;
    OSCharacterWindowController character;
    Material heldGlitch;
    float clock, phaseStart, blockStart = -1f, scale = 1f;
    bool running, ownsWork, carryingSwatter;
    Vector2 swatterGrip;
    float swatterAngle;
    Chapter02AudioLoop bugAudio;
    public bool Completed { get; private set; }
    public string Stage { get; private set; } = "idle";
    public bool IsConfigured => flyFrames != null && flyFrames.Length >= 2
        && flyFrames[0] != null && flyFrames[1] != null
        && squashedSprite != null && swatterSprite != null;

    public IEnumerator Play(OS_Handle os, UnityEngine.Object owner, RectTransform overlay,
        RawImage frozenFrame, TMP_Text fontTemplate, Action revealDesktop, OSChapter01EndingData configuration,
        Action onSwattingStarted = null)
    {
        Cancel();Completed = false;
        cursor = CursorHandle.Instance;cursorOwner = owner;
        if (cursor == null || !cursor.AcquireScriptControl(owner) || !IsConfigured)
        {
            Debug.LogError("[Chapter 2 Recovery] Cursor or authored bug sprites are missing.", this);
            yield break;
        }

        running = true;clock = 0f;blockStart = -1f;
        scale = Mathf.Clamp(configuration.recoverySwatSeconds, 5f, 7f) / 6f;
        heldGlitch = frozenFrame.material;
        phaseStart = heldGlitch != null ? heldGlitch.GetFloat("_Phase") : 0f;
        // The captured frame already contains CRT processing. Both it and the
        // repair sit on overlay canvases, so neither passes through CRT twice.
        root = Rect("Chapter02RecoveryRuntime", null);
        UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(root.gameObject, gameObject.scene);
        canvas = root.gameObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;canvas.sortingOrder = 950;
        var scaler = root.gameObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(640f, 360f);scaler.matchWidthOrHeight = .5f;
        root.gameObject.AddComponent<GraphicRaycaster>();
        root.gameObject.AddComponent<Image>().color = Color.clear;
        cursor.SetScriptOverlayPresentation(owner, true);
        blockLayer = Rect("FallingScreenBlocks", root);Stretch(blockLayer);
        blockLayer.gameObject.AddComponent<RectMask2D>();
        bugLayer = Rect("Bugs", root);Stretch(bugLayer);
        Canvas.ForceUpdateCanvases();

        Stage = "repair_window";
        cursor.SetScriptAppearance(owner, 1.45f, ControlledRed);
        var button = CreateRepairWindow(fontTemplate);
        yield return ExpandWindow(.42f);
        yield return Pause(.24f);
        Vector2 buttonPosition = ScreenPoint((RectTransform)button.transform);
        yield return MoveHuman(buttonPosition + new Vector2(7f, -3f) * ScreenScale, .65f, 1f);
        yield return MoveHuman(buttonPosition, .16f, -.25f);
        yield return Pause(.12f);
        cursor.SetScriptPressed(owner, true);
        button.targetGraphic.color = Paper * .65f;
        yield return Pause(.10f);
        cursor.SetScriptPressed(owner, false);
        PlaySound(repairSound, .5f, "Chapter02_RepairBreak", .8f);

        // Split the existing full-screen screenshot before hiding it; every tile
        // samples its original UV region and shares the live glitch material.
        CreateScreenBlocks(frozenFrame);
        revealDesktop?.Invoke();
        panel.gameObject.SetActive(false);
        SpawnBugs();
        character = os.CharacterWindowController;
        if (character != null)
        {
            os.OpenCharacterWindowForReconnect(.15f);
            character.ShowAnimaState(false);
            ownsWork = character.SetExtendedAnimation(OSCharacterExtendedAnimation.Expend01);
        }
        Stage = "screen_falling";blockStart = clock;
        yield return Pause(1.65f);
        blockLayer.gameObject.SetActive(false);

        Stage = "swatting";
        onSwattingStarted?.Invoke();
        float huntStart = clock;
        // Leave by the right edge, pause to pick up the swatter, then return.
        Vector2 outside = LocalToScreen(new Vector2(root.rect.xMax + 48f, root.rect.height * .08f));
        yield return MoveHuman(outside, .55f * scale, -.5f);
        yield return Pause(.18f * scale);
        swatter = MakeImage("FlySwatter", bugLayer, swatterSprite);
        swatter.rectTransform.sizeDelta = new Vector2(24f, 40f);
        swatter.rectTransform.pivot = SwatterGripPivot;
        carryingSwatter = true;swatterAngle = -18f;
        yield return MoveHuman(LocalToScreen(new Vector2(root.rect.width * .28f, root.rect.height * .05f)), .6f * scale, .55f);
        yield return Pause(.12f * scale);
        for (int i = 0; i < bugs.Count; i++)
        {
            var bug = bugs[i];
            swatterAngle = i % 2 == 0 ? -16f : 14f;
            Vector2 predicted = FlightPosition(bug, clock + .55f * scale);
            yield return MoveHuman(LocalToScreen(predicted - Vector2.up * SwatterHeadOffset), .48f * scale, i % 2 == 0 ? 1f : -.8f);
            yield return Pause(.07f * scale);
            // The last short correction follows the moving target; the mesh
            // head lands on the bug, while the pointer remains on the handle.
            float strikeStart = clock, strikeAngle = swatterAngle;
            while (clock - strikeStart < .1f * scale)
            {
                float u = Mathf.Clamp01((clock - strikeStart) / (.1f * scale));
                swatterAngle = Mathf.Lerp(strikeAngle, 0f, u * u);
                SetPointer(LocalToScreen(FlightPosition(bug, clock) - Vector2.up * SwatterHeadOffset));
                yield return null;
            }
            swatterAngle = 0f;
            SetPointer(LocalToScreen(FlightPosition(bug, clock) - Vector2.up * SwatterHeadOffset));
            cursor.SetScriptPressed(owner, true);
            Squash(bug);
            PlaySound(swatSound, .42f + i * .025f, "Chapter02_BugSwat", .62f + i * .035f,
                i % 2 == 0 ? .96f : 1.04f);
            float recoilStart = clock;
            while (clock - recoilStart < .12f * scale)
            {
                swatterAngle = -9f * Mathf.Sin(Mathf.Clamp01((clock - recoilStart) / (.12f * scale)) * Mathf.PI);
                yield return null;
            }
            cursor.SetScriptPressed(owner, false);swatterAngle = 0f;
        }
        yield return Pause(.45f * scale);
        // Put the swatter down offscreen before giving back a regular pointer.
        yield return MoveHuman(outside, .4f * scale, -.6f);
        carryingSwatter = false;swatter.gameObject.SetActive(false);
        yield return MoveHuman(LocalToScreen(new Vector2(root.rect.width * .18f, -root.rect.height * .08f)), .42f * scale, .5f);
        while (clock - huntStart < 6f * scale) yield return null;
        cursor.SetScriptPressed(owner, false);
        ReleaseWork();
        // The director releases its lock and synchronizes the physical cursor
        // only once the chapter/save handoff has succeeded.
        Completed = true;Stage = "complete";running = false;
        DestroyVisuals();
    }

    void Update()
    {
        if (!running || root == null) return;
        TickBugAudio();
        clock += Time.unscaledDeltaTime;
        if (heldGlitch != null) heldGlitch.SetFloat("_Phase", phaseStart + clock);
        foreach (var bug in bugs)
        {
            if (bug.image == null) continue;
            if (bug.hitTime < 0f)
            {
                bug.image.sprite = flyFrames[(Mathf.FloorToInt(clock * 10f + bug.seed)) % 2];
                bug.image.rectTransform.anchoredPosition = Pixel(FlightPosition(bug, clock));
            }
            else
            {
                float fall = Mathf.Max(0f, clock - bug.hitTime - .09f);
                bug.image.rectTransform.anchoredPosition = Pixel(bug.hitPosition - Vector2.up * (900f * fall * fall));
            }
        }
        if (blockStart >= 0f)
        {
            foreach (var block in blocks)
            {
                float t = Mathf.Max(0f, clock - blockStart - block.delay);
                block.rect.anchoredPosition = Pixel(block.origin + new Vector2(block.drift * t, -block.speed * t * t));
            }
        }
    }

    void LateUpdate()
    {
        if (!running || !carryingSwatter || swatter == null || cursor == null) return;
        RectTransformUtility.ScreenPointToLocalPointInRectangle(root, cursor.DisplayedScreenPosition, Camera, out swatterGrip);
        swatter.rectTransform.anchoredPosition = Pixel(swatterGrip);
        swatter.rectTransform.localEulerAngles = new Vector3(0f, 0f, swatterAngle);
    }

    Vector2 FlightPosition(Bug bug, float time)
    {
        return bug.home + new Vector2(Mathf.Sin(time * 2.2f + bug.seed) * 22f + Mathf.Sin(time * 4.1f + bug.seed) * 5f,
            Mathf.Cos(time * 2.7f + bug.seed) * 13f + Mathf.Sin(time * 5.3f + bug.seed) * 3f);
    }
    void SpawnBugs()
    {
        Vector2[] homes = { new Vector2(.22f, .65f), new Vector2(.64f, .72f), new Vector2(.39f, .35f), new Vector2(.77f, .38f) };
        for (int i = 0; i < homes.Length; i++)
        {
            var image = MakeImage("Bug_" + i, bugLayer, flyFrames[0]);
            image.rectTransform.sizeDelta = new Vector2(16f, 16f);
            bugs.Add(new Bug { image = image, home = Vector2.Scale(homes[i] - Vector2.one * .5f, root.rect.size), seed = i * 2.37f });
        }
    }
    void Squash(Bug bug)
    {
        bug.hitPosition = FlightPosition(bug, clock);bug.hitTime = clock;
        bug.image.sprite = squashedSprite;bug.image.rectTransform.localRotation = Quaternion.identity;
        bug.image.rectTransform.anchoredPosition = Pixel(bug.hitPosition);
        TickBugAudio();
    }

    void TickBugAudio()
    {
        int flying = 0;
        foreach (var bug in bugs)
            if (bug.image != null && bug.hitTime < 0f) flying++;
        if (!running || flying == 0 || !Application.isFocused || GameTestConsole.BlocksGameplayInput
            || OSSessionPersistence.IsRestoring)
        {
            bugAudio?.Stop();
            return;
        }
        if (bugAudio == null) bugAudio = new Chapter02AudioLoop(this);
        // One soft swarm, not four stacked insect voices. It thins as each bug
        // is hit and stops on the final impact, leaving the recovery quiet.
        bugAudio.Set("Chapter02_BugBuzz", .1f + flying * .025f, .95f + flying * .025f);
    }

    Button CreateRepairWindow(TMP_Text fontTemplate)
    {
        panel = Rect("DisplayRepairWindow", root);panel.sizeDelta = new Vector2(236f, 104f);
        var background = panel.gameObject.AddComponent<Image>();background.color = Color.black;
        Outline(panel, Paper);
        var title = Label("Title", panel, fontTemplate, new Vector2(0f, 32f), new Vector2(214f, 18f), 9f);
        title.alignment = TextAlignmentOptions.MidlineLeft;
        OSLocalizedText.SetRuntime(title, "ui.os.chapter01_ending.display_repair", "显示修复");
        var line = Rect("TitleRule", panel);line.sizeDelta = new Vector2(234f, 1f);line.anchoredPosition = new Vector2(0f, 18f);
        line.gameObject.AddComponent<Image>().color = Paper;
        var buttonRect = Rect("RepairPixelsButton", panel);buttonRect.sizeDelta = new Vector2(192f, 32f);buttonRect.anchoredPosition = new Vector2(0f, -12f);
        var graphic = buttonRect.gameObject.AddComponent<Image>();graphic.color = Paper;
        var button = buttonRect.gameObject.AddComponent<Button>();button.targetGraphic = graphic;
        // This is an authored automatic click: physical input cannot advance it.
        button.interactable = false;button.transition = Selectable.Transition.None;
        var text = Label("Label", buttonRect, fontTemplate, Vector2.zero, new Vector2(180f, 28f), 11f);
        text.color = Color.black;text.enableAutoSizing = true;text.fontSizeMin = 8f;text.fontSizeMax = 11f;
        OSLocalizedText.SetRuntime(text, "ui.os.chapter01_ending.repair_pixels", "修理坏点");
        return button;
    }
    IEnumerator ExpandWindow(float seconds)
    {
        float start = clock;
        while (clock - start < seconds)
        {
            float u = Mathf.Clamp01((clock - start) / seconds);
            float ease = 1f - Mathf.Pow(1f - u, 3f);
            panel.localScale = new Vector3(Mathf.Lerp(.08f, 1f, ease), Mathf.Lerp(.02f, 1f, ease), 1f);
            yield return null;
        }
        panel.localScale = Vector3.one;
    }
    void CreateScreenBlocks(RawImage frozenFrame)
    {
        int columns = Mathf.Clamp(Mathf.RoundToInt(root.rect.width / 32f), 12, 28);
        int rows = Mathf.Clamp(Mathf.RoundToInt(root.rect.height / (root.rect.width / columns)), 8, 18);
        Vector2 size = new Vector2(root.rect.width / columns, root.rect.height / rows);
        for (int y = 0; y < rows; y++)
        for (int x = 0; x < columns; x++)
        {
            var rect = Rect("ScreenBlock", blockLayer);rect.sizeDelta = size + Vector2.one * .08f;
            Vector2 origin = root.rect.min + Vector2.Scale(new Vector2(x + .5f, y + .5f), size);
            rect.anchoredPosition = origin;
            var raw = rect.gameObject.AddComponent<RawImage>();raw.texture = frozenFrame.texture;raw.material = frozenFrame.material;
            raw.uvRect = new Rect(x / (float)columns, y / (float)rows, 1f / columns, 1f / rows);raw.raycastTarget = false;
            float seed = Mathf.Repeat(Mathf.Sin(x * 17.7f + y * 41.3f) * 12345.67f, 1f);
            blocks.Add(new Block { rect = rect, origin = origin, delay = y / (float)rows * .24f + seed * .2f,
                speed = root.rect.height * (1.05f + seed * .5f), drift = (seed - .5f) * 9f });
        }
    }

    IEnumerator MoveHuman(Vector2 destination, float duration, float curve)
    {
        Vector2 start = cursor.DisplayedScreenPosition, difference = destination - start;
        Vector2 side = new Vector2(-difference.y, difference.x).normalized;
        Vector2 c1 = start + difference * .3f + side * Mathf.Min(45f * ScreenScale, difference.magnitude * .12f) * curve;
        Vector2 c2 = start + difference * .8f - side * Mathf.Min(18f * ScreenScale, difference.magnitude * .05f) * curve;
        float began = clock;
        while (clock - began < duration)
        {
            float t = Mathf.Clamp01((clock - began) / Mathf.Max(.01f, duration));
            float u = t * t * (3f - 2f * t), v = 1f - u;
            SetPointer(v * v * v * start + 3f * v * v * u * c1 + 3f * v * u * u * c2 + u * u * u * destination);
            yield return null;
        }
        SetPointer(destination);
    }
    IEnumerator Pause(float duration)
    {
        float start = clock;while (clock - start < duration) yield return null;
    }
    void SetPointer(Vector2 position) => cursor.SetScriptScreenPosition(cursorOwner, position);
    Camera Camera => canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : null;
    float ScreenScale => canvas != null ? canvas.scaleFactor : 1f;
    Vector2 LocalToScreen(Vector2 position) => RectTransformUtility.WorldToScreenPoint(Camera, root.TransformPoint(position));
    Vector2 ScreenPoint(RectTransform rect) => RectTransformUtility.WorldToScreenPoint(Camera, rect.TransformPoint(rect.rect.center));
    static Vector2 Pixel(Vector2 value) => new Vector2(Mathf.Round(value.x), Mathf.Round(value.y));
    static void PlaySound(AudioClip fallbackClip, float fallbackVolume, string cue, float gain, float pitch = 1f)
    {
        if (!Application.isPlaying || !Application.isFocused || OSSessionPersistence.IsRestoring) return;
        if (Chapter02Audio.FindClip(cue) != null) Chapter02Audio.Play(cue, gain, pitch);
        else if (fallbackClip != null) AudioManager.Instance?.PlaySfx(fallbackClip, fallbackVolume, pitch);
    }

    static RectTransform Rect(string name, Transform parent)
    {
        var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();rect.SetParent(parent, false);
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(.5f, .5f);return rect;
    }
    static void Stretch(RectTransform rect) { rect.anchorMin = Vector2.zero;rect.anchorMax = Vector2.one;rect.offsetMin = rect.offsetMax = Vector2.zero; }
    static Image MakeImage(string name, Transform parent, Sprite sprite)
    {
        var rect = Rect(name, parent);var image = rect.gameObject.AddComponent<Image>();image.sprite = sprite;image.raycastTarget = false;return image;
    }
    static TMP_Text Label(string name, Transform parent, TMP_Text template, Vector2 position, Vector2 size, float fontSize)
    {
        var rect = Rect(name, parent);rect.anchoredPosition = position;rect.sizeDelta = size;
        var label = rect.gameObject.AddComponent<TextMeshProUGUI>();label.font = template.font;label.fontSharedMaterial = template.fontSharedMaterial;
        label.fontSize = fontSize;label.color = Paper;label.alignment = TextAlignmentOptions.Midline;label.raycastTarget = false;
        label.textWrappingMode = TextWrappingModes.NoWrap;
        rect.gameObject.AddComponent<OSLocalizedText>().Initialize();
        return label;
    }
    static void Outline(RectTransform panel, Color color)
    {
        for (int i = 0; i < 4; i++)
        {
            var edge = Rect("Edge", panel);bool vertical = i < 2;
            edge.sizeDelta = vertical ? new Vector2(1f, panel.rect.height) : new Vector2(panel.rect.width, 1f);
            edge.anchoredPosition = vertical ? new Vector2((i == 0 ? -1 : 1) * (panel.rect.width - 1f) * .5f, 0f)
                : new Vector2(0f, (i == 2 ? -1 : 1) * (panel.rect.height - 1f) * .5f);
            edge.gameObject.AddComponent<Image>().color = color;
        }
    }
    void ReleaseWork()
    {
        if (ownsWork && character != null && character.CurrentExtendedAnimation == OSCharacterExtendedAnimation.Expend01)
            character.StopExtendedAnimation();
        ownsWork = false;character = null;
    }
    void DestroyVisuals()
    {
        bugAudio?.Stop();
        if (root != null) { root.gameObject.SetActive(false);Destroy(root.gameObject); }
        root = null;heldGlitch = null;bugs.Clear();blocks.Clear();carryingSwatter = false;
    }
    public void Cancel()
    {
        running = false;ReleaseWork();DestroyVisuals();
        if (cursor != null && cursorOwner != null && cursor.IsScriptControlledBy(cursorOwner))
            cursor.SetScriptPressed(cursorOwner, false);
        cursor = null;cursorOwner = null;
        if (!Completed) Stage = "idle";
    }
    void RefreshLanguage(LanguageState language)
    {
        if (root == null) return;
        foreach (var text in root.GetComponentsInChildren<OSLocalizedText>(true))
            text.Refresh(GameDataManager.Instance?.LocalizationData, language);
    }
    void OnEnable() => GameEvents.LanguageChanged += RefreshLanguage;
    void OnDisable()
    {
        GameEvents.LanguageChanged -= RefreshLanguage;
        Cancel();
    }

    void OnApplicationFocus(bool focused)
    {
        if (!focused) bugAudio?.Stop();
    }

    void OnDestroy()
    {
        bugAudio?.Dispose();
        bugAudio = null;
    }
}
