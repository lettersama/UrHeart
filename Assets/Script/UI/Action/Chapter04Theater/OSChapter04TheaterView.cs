using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Chapter 4's TV picture, on OS_Handle.Instance.TVController.Screen.
///   Takeover - in chapter 4 the old curtains and valance are hidden and a
///              black backdrop with the 320 x 180 stage covers the screen; in
///              any other chapter they are left exactly as before.
///   Scale    - after a black border of borderUnits OS units on every side,
///              the stage takes the largest whole multiple of screen pixels
///              that fits what is left, centred and pixel-snapped.
///   Bridge   - OSChapter04TvAdapter feeds requests and pause; the stage reports
///              DoorLocked / FoldToLab / LabRummage with the revision captured
///              when the chain began, and halts if a report is refused.
/// One RGBA texture, one stage pixel per texel, redrawn only on frame steps.
/// It never writes story or save data.
/// </summary>
[DisallowMultipleComponent]
public sealed class OSChapter04TheaterView : OSChapter04TvAdapter
{
    const int TakeoverChapter = 4;
    static readonly string[] LegacyCurtainNames = { "LeftCurtain", "RightCurtain", "Valance" };

    [Tooltip("Minimum black border on every side of TV.Screen, in OS (canvas) units; rounded up to screen pixels.")]
    [SerializeField, Min(0f)] float borderUnits = 8f;
    [Tooltip("Locked: CityArrive runs on into the rattling door (current bridge chain). Enter: CityArrive waits at the door until GotoEnterDoor() (script v1, line 9).")]
    [SerializeField] OSChapter04TheaterStage.DoorMode doorMode = OSChapter04TheaterStage.DoorMode.Enter;
    [Tooltip("Optional replacements by piece id (e.g. puppet_carry). Empty: Resources/Chapter04/Theater/<id>.png.")]
    [SerializeField] List<OSChapter04TheaterLibrary.Override> pieceOverrides = new List<OSChapter04TheaterLibrary.Override>();

    RectTransform screen, root, stageRect;
    RawImage stageImage;
    Texture2D texture;
    Color32[] pixels;
    OSChapter04TheaterStage stage;
    readonly List<GameObject> hiddenCurtains = new List<GameObject>();
    bool animationPaused = true, takenOver, built, warnedNoWholeScale;
    int chainRevision;
    bool previewing, previewPaused;
    string pendingState;
    public string RequestedStateId { get; private set; }
    /// <summary>The requested state still shows the LabTidy stand-in (NorthSettlement and later).</summary>
    public bool UsesPendingSceneArtwork => OSChapter04PresentationBridge.IsLaterScene(RequestedStateId)
        && !OSChapter04TheaterStage.HasSceneArtwork(RequestedStateId);

    public OSChapter04TheaterStage Stage { get { Build(); return stage; } }
    public bool IsTakenOver => takenOver;
    public bool IsAnimationPaused => animationPaused;
    /// <summary>TV body size in real screen pixels, measured on the last layout.</summary>
    public Vector2 ScreenPixelSize { get; private set; }
    /// <summary>Screen pixels per stage pixel; 0 before the first layout. Fractional only if even 1x does not fit.</summary>
    public float PixelScale { get; private set; }
    /// <summary>The guaranteed border, in screen pixels, from the last layout.</summary>
    public float BorderPixels { get; private set; }

    /// <summary>Called by OSTVWindowController.Create; safe to call again.</summary>
    public static OSChapter04TheaterView Attach(OSTVWindowController tv)
    {
        if (tv == null || tv.Screen == null) return null;
        var view = tv.Screen.GetComponent<OSChapter04TheaterView>();
        return view != null ? view : tv.Screen.gameObject.AddComponent<OSChapter04TheaterView>();
    }

    void Build()
    {
        if (built) return;
        built = true;
        screen = (RectTransform)transform;
        stage = new OSChapter04TheaterStage(new OSChapter04TheaterLibrary(pieceOverrides));
        stage.Boundary = ReportBoundary;
        stage.Mode = doorMode;

        var rootObject = new GameObject("Chapter04Theater", typeof(RectTransform), typeof(Image));
        rootObject.layer = gameObject.layer;
        root = (RectTransform)rootObject.transform;
        root.SetParent(screen, false);
        root.anchorMin = Vector2.zero; root.anchorMax = Vector2.one;
        root.offsetMin = root.offsetMax = Vector2.zero;
        var backdrop = rootObject.GetComponent<Image>();
        backdrop.color = Color.black;
        backdrop.raycastTarget = false;

        var stageObject = new GameObject("Stage", typeof(RectTransform), typeof(RawImage));
        stageObject.layer = gameObject.layer;
        stageRect = (RectTransform)stageObject.transform;
        stageRect.SetParent(root, false);
        stageRect.anchorMin = stageRect.anchorMax = stageRect.pivot = new Vector2(.5f, .5f);
        stageImage = stageObject.GetComponent<RawImage>();
        stageImage.raycastTarget = false;
        texture = new Texture2D(OSChapter04TheaterStage.Width, OSChapter04TheaterStage.Height, TextureFormat.RGBA32, false)
        { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp, name = "Chapter04Theater" };
        pixels = new Color32[OSChapter04TheaterStage.Width * OSChapter04TheaterStage.Height];
        stageImage.texture = texture;
        stageImage.color = Color.white;

        foreach (string name in LegacyCurtainNames)
        {
            Transform curtain = screen.Find(name);
            if (curtain != null) hiddenCurtains.Add(curtain.gameObject);
        }
        rootObject.SetActive(false);
        Redraw();
    }

    protected override void OnEnable()
    {
        Build();
        RefreshTakeover();
        base.OnEnable();
    }

    void OnDestroy()
    {
        if (texture != null) Destroy(texture);
    }

    protected override void Update()
    {
        base.Update();
        RefreshTakeover();
        if (!takenOver) return;
        stage.Mode = doorMode;
        bool running = previewing ? !previewPaused : !animationPaused;
        // Unscaled: the bridge, not timeScale, says when the act may move.
        if (running)
        {
            bool changed = stage.Advance(Time.unscaledDeltaTime);
            if (pendingState == OSChapter04PresentationBridge.EnterDoor && stage.CurrentPhase == OSChapter04TheaterStage.Phase.AtDoor)
            { pendingState = null; stage.GotoEnterDoor(); changed = true; }
            else if (pendingState == OSChapter04PresentationBridge.LabTidy && stage.CurrentPhase == OSChapter04TheaterStage.Phase.AtDoor)
            { stage.GotoEnterDoor(); changed = true; }
            else if (pendingState == OSChapter04PresentationBridge.LabTidy && stage.CurrentPhase == OSChapter04TheaterStage.Phase.Rummage)
            { pendingState = null; stage.BeginTidyPreview(false); changed = true; }
            if (changed) Redraw();
        }
    }

    void LateUpdate()
    {
        if (takenOver) Layout();
    }

    // ================================================================ bridge

    protected override void StartPresentation(string stateId, bool restoring, int revision)
    {
        Build();
        previewing = false;
        chainRevision = revision;
        RequestedStateId = stateId;
        pendingState = null;
        if (OSChapter04TheaterStage.HasSceneArtwork(stateId))
        {
            // Sections 2-3 have their own pictures; the stage queues a request that
            // arrives mid-transition and starts it as soon as it can.
            stage.Begin(stateId, restoring);
        }
        else if (OSChapter04PresentationBridge.IsLaterScene(stateId))
        {
            // Later scenes are intentionally supplied by the next frontend pass.
            // Keep a deterministic existing picture; do not clear TV or stall dialogue.
            if (restoring || stage.CurrentPhase != OSChapter04TheaterStage.Phase.Tidy || stage.HasQueuedRequest)
                // Begin also retires an older queued scene before showing the
                // stand-in for this newer story request.
                stage.Begin(stateId, true);
        }
        else if (!restoring && stateId == OSChapter04PresentationBridge.EnterDoor
            && stage.CurrentPhase == OSChapter04TheaterStage.Phase.Arrive)
            pendingState = stateId;
        else if (!restoring && stateId == OSChapter04PresentationBridge.LabTidy
            && (stage.CurrentPhase == OSChapter04TheaterStage.Phase.EnterDoor
                || stage.CurrentPhase == OSChapter04TheaterStage.Phase.Arrive
                || stage.CurrentPhase == OSChapter04TheaterStage.Phase.AtDoor
                || stage.CurrentPhase == OSChapter04TheaterStage.Phase.Fold
                || stage.CurrentPhase == OSChapter04TheaterStage.Phase.LabRise))
            pendingState = stateId;
        else stage.Begin(stateId, restoring);
        Redraw();
    }

    protected override void SetAnimationPaused(bool paused) => animationPaused = paused;

    protected override void ClearPresentation()
    {
        Build();
        if (previewing) return;
        pendingState = null;
        RequestedStateId = null;
        stage.Clear();
        Redraw();
    }

    // A newer request is waiting (here or in the stage): the running chain is stale, so do not report it.
    bool ReportBoundary(string stateId) => previewing || pendingState != null || stage.HasQueuedRequest
        || ReportPhase(stateId, chainRevision);

    /// <summary>
    /// Grief tier on the TV (round 8): she folds back and the grave scene lies
    /// down in GraveBreakdown. OSChapter04GriefPresentation forwards the tier.
    /// </summary>
    public void SetGriefTier(int tier, bool restoring)
    {
        Build();
        stage.SetGriefTier(tier, restoring);
        Redraw();
    }

    /// <summary>How CityArrive ends: Locked (current bridge chain) or Enter (waits at the door for line 9).</summary>
    public OSChapter04TheaterStage.DoorMode DoorMode
    {
        get => doorMode;
        set { doorMode = value; if (stage != null) stage.Mode = value; }
    }

    /// <summary>
    /// Line 9 (script v1): she walks in through the door, then FoldToLab -> LabRummage.
    /// Only while waiting at the door in Enter mode; boundaries go to the bridge with
    /// the chain's revision like every other report. The backend calls this once it
    /// has a line-9 signal. Returns false otherwise.
    /// </summary>
    public bool GotoEnterDoor()
    {
        Build();
        bool ok = stage.GotoEnterDoor();
        if (ok) Redraw();
        return ok;
    }

    // ================================================================ display

    void RefreshTakeover()
    {
        bool take = previewing || GameDataManager.Instance?.CurrentChapterIndex == TakeoverChapter;
#if UNITY_EDITOR
        take |= HasDebugBridge;
#endif
        if (take == takenOver) return;
        takenOver = take;
        root.gameObject.SetActive(take);
        root.SetAsLastSibling();
        foreach (GameObject curtain in hiddenCurtains)
            if (curtain != null) curtain.SetActive(!take);
        if (take) Layout();
    }

    void Layout()
    {
        Canvas canvas = screen.GetComponentInParent<Canvas>();
        if (canvas == null) return;
        canvas = canvas.rootCanvas;
        float unit = canvas.scaleFactor > 0f ? canvas.scaleFactor : 1f;
        Vector2 size = screen.rect.size * unit;
        ScreenPixelSize = size;
        // Keep a black border of at least borderUnits OS units on every side.
        float border = Mathf.Ceil(borderUnits * unit - .001f); // 8 x 6.00001 is still 48
        BorderPixels = border;
        Vector2 room = Vector2.Max(Vector2.zero, size - Vector2.one * (2f * border));
        float fit = Mathf.Min(room.x / OSChapter04TheaterStage.Width, room.y / OSChapter04TheaterStage.Height);
        float scale = Mathf.Floor(fit);
        if (scale < 1f)
        {
            // Smaller than one pixel per stage pixel: shrink to fit, the only non-whole case.
            scale = Mathf.Max(fit, .01f);
            if (!warnedNoWholeScale)
            {
                warnedNoWholeScale = true;
                Debug.LogWarning("[Chapter04Theater] TV screen " + size + " px minus its border is smaller than 320x180; scaling down.");
            }
        }
        PixelScale = scale;
        stageRect.sizeDelta = new Vector2(OSChapter04TheaterStage.Width, OSChapter04TheaterStage.Height) * (scale / unit);

        // Snap the stage's lower-left corner to a whole screen pixel.
        Camera camera = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
        var corners = new Vector3[4];
        root.GetWorldCorners(corners);
        Vector2 a = RectTransformUtility.WorldToScreenPoint(camera, corners[0]);
        Vector2 b = RectTransformUtility.WorldToScreenPoint(camera, corners[2]);
        Vector2 stagePixels = new Vector2(OSChapter04TheaterStage.Width, OSChapter04TheaterStage.Height) * scale;
        Vector2 corner = (a + b - stagePixels) * .5f;
        Vector2 snapped = new Vector2(Mathf.Round(corner.x), Mathf.Round(corner.y));
        float onScreen = (b.x - a.x) / Mathf.Max(1f, size.x);
        stageRect.anchoredPosition = onScreen > .01f ? (snapped - corner) / (unit * onScreen) : Vector2.zero;
    }

    void Redraw()
    {
        if (stage == null) return;
        stage.Render(pixels);
        texture.SetPixels32(pixels);
        texture.Apply(false);
    }

#if UNITY_EDITOR
    // ================================================================ editor preview
    // Drives the view directly: no bridge, no save. A real bridge request ends it.

    public bool IsPreviewing => previewing;
    public bool PreviewPaused { get => previewPaused; set => previewPaused = value; }

    public void PreviewBegin(string stateId, bool restoring)
    {
        Build();
        previewing = true;
        previewPaused = false;
        RefreshTakeover();
        if (!restoring && stateId == OSChapter04PresentationBridge.CityArrive
            && stage.CurrentPhase != OSChapter04TheaterStage.Phase.Walk)
            stage.Begin(OSChapter04PresentationBridge.CityWalk, true);
        stage.Begin(stateId, restoring);
        Redraw();
    }

    /// <summary>The line-9 tidied lab from the LabRummage room (layout data only; no bridge state yet).</summary>
    public void PreviewTidy(bool restoring = false)
    {
        Build();
        previewing = true;
        previewPaused = false;
        RefreshTakeover();
        stage.BeginTidyPreview(restoring);
        Redraw();
    }

    /// <summary>Leave preview; shows the bridge's current state as a restore (loops only).</summary>
    public void PreviewStop()
    {
        previewing = false;
        previewPaused = false;
        OSChapter04PresentationBridge bridge = Bridge;
        if (bridge != null && bridge.IsBound) stage.Begin(bridge.CurrentState, true);
        else stage.Clear();
        RefreshTakeover();
        Redraw();
    }

    [ContextMenu("剧场预览/CityWalk 首次立起")]
    void PreviewRise() => PreviewBegin(OSChapter04PresentationBridge.CityWalk, false);

    [ContextMenu("剧场预览/CityArrive 全流程")]
    void PreviewArrive()
    {
        DoorMode = OSChapter04TheaterStage.DoorMode.Locked;
        PreviewBegin(OSChapter04PresentationBridge.CityArrive, false);
    }

    /// <summary>Script v1, line 7: Enter mode, CityArrive stops at the door and waits.</summary>
    public void PreviewArriveToDoor()
    {
        DoorMode = OSChapter04TheaterStage.DoorMode.Enter;
        PreviewBegin(OSChapter04PresentationBridge.CityArrive, false);
    }

    /// <summary>Enter mode restored at the door (AtDoor loop).</summary>
    public void PreviewRestoreAtDoor()
    {
        Build();
        DoorMode = OSChapter04TheaterStage.DoorMode.Enter;
        previewing = true;
        previewPaused = false;
        RefreshTakeover();
        stage.BeginAtDoor();
        Redraw();
    }

    [ContextMenu("剧场预览/第7句 CityArrive→等门口")]
    void PreviewArriveToDoorMenu() => PreviewArriveToDoor();

    [ContextMenu("剧场预览/第9句 进门")]
    void PreviewEnterDoorMenu() => GotoEnterDoor();

    [ContextMenu("剧场预览/读档 门口等待")]
    void PreviewRestoreAtDoorMenu() => PreviewRestoreAtDoor();

    [ContextMenu("剧场预览/读档 CityWalk")]
    void PreviewRestoreWalk() => PreviewBegin(OSChapter04PresentationBridge.CityWalk, true);

    [ContextMenu("剧场预览/读档 LabRummage")]
    void PreviewRestoreLab() => PreviewBegin(OSChapter04PresentationBridge.LabRummage, true);

    [ContextMenu("剧场预览/第10句 LabTidy（仅布局预览）")]
    void PreviewTidyMenu() => PreviewTidy();

    [ContextMenu("剧场预览/读档 LabTidy（仅布局预览）")]
    void PreviewTidyRestoreMenu() => PreviewTidy(true);

    /// <summary>
    /// Sections 2-3: play (or restore) one scene state without the bridge. A fresh
    /// request from a stage that is not ready yet waits in the stage's queue.
    /// </summary>
    public void PreviewScene(string stateId, bool restoring)
    {
        Build();
        previewing = true;
        previewPaused = false;
        RefreshTakeover();
        if (!restoring && (stage.CurrentPhase == OSChapter04TheaterStage.Phase.Idle
            || stateId == OSChapter04PresentationBridge.MedicineReady)) stage.Begin(OSChapter04PresentationBridge.LabTidy, true);
        stage.Begin(stateId, restoring);
        Redraw();
    }

    /// <summary>TentCare restored, then the change back to WastelandWalk (line 26).</summary>
    public void PreviewTentToWasteland()
    {
        PreviewScene(OSChapter04PresentationBridge.TentCare, true);
        stage.Begin(OSChapter04PresentationBridge.WastelandWalk, false);
        Redraw();
    }

    [ContextMenu("剧场预览/第2-3节 MedicineReady")]
    void PreviewMedicineMenu() => PreviewScene(OSChapter04PresentationBridge.MedicineReady, false);

    [ContextMenu("剧场预览/第2-3节 WastelandWalk")]
    void PreviewWastelandMenu() => PreviewScene(OSChapter04PresentationBridge.WastelandWalk, false);

    [ContextMenu("剧场预览/第2-3节 SettlementWait")]
    void PreviewSettlementMenu() => PreviewScene(OSChapter04PresentationBridge.SettlementWait, false);

    [ContextMenu("剧场预览/第2-3节 TentCare")]
    void PreviewTentMenu() => PreviewScene(OSChapter04PresentationBridge.TentCare, false);

    [ContextMenu("剧场预览/第2-3节 TentCare→WastelandWalk")]
    void PreviewTentToWastelandMenu() => PreviewTentToWasteland();

    [ContextMenu("剧场预览/暂停或继续")]
    void PreviewTogglePause() => previewPaused = !previewPaused;

    [ContextMenu("剧场预览/结束预览")]
    void PreviewEnd() => PreviewStop();
#endif
}
