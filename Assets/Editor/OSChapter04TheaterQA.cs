using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.UI;

// Play Mode check of the TV theatre against a standalone presentation bridge:
// the real story director, save and TV window are never touched. A throwaway
// canvas mimics TV.Screen (314 x 203 units, old curtain children included).
public static class OSChapter04TheaterQA
{
    const string OutputFolder = "Docs/Chapter04Theater/qa";
    static readonly List<string> checks = new List<string>();
    static readonly List<string> failures = new List<string>();
    static readonly List<string> runtimeErrors = new List<string>();
    static readonly List<object> observations = new List<object>();
    static readonly List<KeyValuePair<string, float>> reports = new List<KeyValuePair<string, float>>();
    static GameObject rig;
    static OSChapter04PresentationBridge bridge;
    static OSChapter04TheaterView view;
    static GameObject screenObject;
    static GameObject[] curtains;

    public static string Status { get; private set; } = "idle";

    public static string Start()
    {
        if (!Application.isPlaying) return "Play Mode required";
        if (Status == "running") return Status;
        MonoBehaviour host = GameDataManager.Instance;
        if (host == null) return "GameDataManager required";
        checks.Clear(); failures.Clear(); runtimeErrors.Clear(); observations.Clear(); reports.Clear();
        Status = "running";
        host.StartCoroutine(Guarded());
        return Status;
    }

    static IEnumerator Guarded()
    {
        Application.logMessageReceived += Capture;
        Exception failure = null;
        IEnumerator work = Run();
        var stack = new Stack<IEnumerator>();
        stack.Push(work);
        while (stack.Count > 0)
        {
            object next = null;
            try
            {
                if (!stack.Peek().MoveNext()) { stack.Pop(); continue; }
                next = stack.Peek().Current;
            }
            catch (Exception error) { failure = error; }
            if (failure != null) break;
            if (next is IEnumerator nested) stack.Push(nested);
            else yield return next;
        }
        Application.logMessageReceived -= Capture;
        if (rig != null) UnityEngine.Object.Destroy(rig);
        if (failure != null) failures.Add("exception: " + failure);
        bool pass = failures.Count == 0 && runtimeErrors.Count == 0;
        Status = pass ? "PASS: " + checks.Count + " checks" : "FAIL: " + failures.Count + " failed, " + runtimeErrors.Count + " runtime errors";
        Directory.CreateDirectory(OutputFolder);
        File.WriteAllText(Path.Combine(OutputFolder, "results.json"), JsonConvert.SerializeObject(new
        {
            status = Status, time = DateTime.Now.ToString("s"), checks, failures, runtimeErrors, observations,
            note = "Standalone bridge + throwaway canvas; no story, save or TV window touched."
        }, Formatting.Indented));
    }

    static void Capture(string message, string stack, LogType type)
    {
        if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert)
            runtimeErrors.Add(message);
    }

    static void Check(bool ok, string label)
    {
        checks.Add((ok ? "ok   " : "FAIL ") + label);
        if (!ok) failures.Add(label);
    }

    static OSChapter04TheaterStage Stage => view.Stage;

    static IEnumerator WaitUntil(Func<bool> done, float timeout)
    {
        float start = Time.unscaledTime;
        while (!done() && Time.unscaledTime - start < timeout) yield return null;
    }

    static IEnumerator WaitWatching(Func<bool> done, float timeout, Action each)
    {
        float start = Time.unscaledTime;
        each();
        while (!done() && Time.unscaledTime - start < timeout) { yield return null; each(); }
    }

    static IEnumerator Wait(float seconds)
    {
        float start = Time.unscaledTime;
        while (Time.unscaledTime - start < seconds) yield return null;
    }

    static void BuildRig()
    {
        rig = new GameObject("Chapter04TheaterQA Rig");
        var canvas = rig.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 32000;
        var scaler = rig.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(640, 360);
        scaler.matchWidthOrHeight = 1f;

        screenObject = new GameObject("DetailsArea", typeof(RectTransform), typeof(Image), typeof(RectMask2D));
        screenObject.SetActive(false);
        var rect = (RectTransform)screenObject.transform;
        rect.SetParent(rig.transform, false);
        rect.sizeDelta = new Vector2(314, 203);
        screenObject.GetComponent<Image>().color = new Color32(15, 14, 16, 255);
        curtains = new[] { "LeftCurtain", "RightCurtain", "Valance" }.Select(name =>
        {
            var curtain = new GameObject(name, typeof(RectTransform), typeof(Image));
            curtain.transform.SetParent(rect, false);
            curtain.GetComponent<Image>().color = new Color32(96, 45, 54, 255);
            return curtain;
        }).ToArray();

        var bridgeObject = new GameObject("Bridge");
        bridgeObject.transform.SetParent(rig.transform, false);
        bridge = bridgeObject.AddComponent<OSChapter04PresentationBridge>();
        bridge.StateChanged += state => reports.Add(new KeyValuePair<string, float>(state, Time.unscaledTime));

        view = screenObject.AddComponent<OSChapter04TheaterView>();
        view.DebugUseBridge(bridge);
        screenObject.SetActive(true);
    }

    static IEnumerator Shot(string name)
    {
        yield return new WaitForEndOfFrame();
        Texture2D full = ScreenCapture.CaptureScreenshotAsTexture();
        var rect = (RectTransform)screenObject.transform;
        var corners = new Vector3[4];
        rect.GetWorldCorners(corners);
        int x = Mathf.Clamp(Mathf.FloorToInt(corners[0].x), 0, full.width - 1);
        int y = Mathf.Clamp(Mathf.FloorToInt(corners[0].y), 0, full.height - 1);
        int w = Mathf.Clamp(Mathf.CeilToInt(corners[2].x) - x, 1, full.width - x);
        int h = Mathf.Clamp(Mathf.CeilToInt(corners[2].y) - y, 1, full.height - y);
        var crop = new Texture2D(w, h, TextureFormat.RGBA32, false);
        crop.SetPixels(full.GetPixels(x, y, w, h));
        crop.Apply();
        Directory.CreateDirectory(OutputFolder);
        File.WriteAllBytes(Path.Combine(OutputFolder, name + ".png"), crop.EncodeToPNG());
        UnityEngine.Object.Destroy(full);
        UnityEngine.Object.Destroy(crop);
    }

    static IEnumerator Run()
    {
        BuildRig();
        // Let the CanvasScaler apply, then one more layout pass.
        Canvas rigCanvas = rig.GetComponent<Canvas>();
        yield return WaitUntil(() => !Mathf.Approximately(rigCanvas.scaleFactor, 1f), 3f);
        yield return null;
        yield return null;

        // Takeover and scale.
        Check(view.IsTakenOver && curtains.All(c => !c.activeSelf), "takeover hides the old curtains and valance");
        Check(Stage.CurrentPhase == OSChapter04TheaterStage.Phase.Idle, "unbound bridge shows the empty stage");
        Check(view.PixelScale >= 1f && Mathf.Approximately(view.PixelScale, Mathf.Round(view.PixelScale)),
            "stage uses a whole-number pixel scale (" + view.PixelScale + "x for " + view.ScreenPixelSize + " px)");
        var stageRect = (RectTransform)screenObject.transform.Find("Chapter04Theater/Stage");
        var stageCorners = new Vector3[4];
        stageRect.GetWorldCorners(stageCorners);
        float pxW = stageCorners[2].x - stageCorners[0].x, pxH = stageCorners[2].y - stageCorners[0].y;
        Check(Mathf.Abs(pxW - 320 * view.PixelScale) < .05f && Mathf.Abs(pxH - 180 * view.PixelScale) < .05f
            && Mathf.Abs(stageCorners[0].x - Mathf.Round(stageCorners[0].x)) < .05f
            && Mathf.Abs(stageCorners[0].y - Mathf.Round(stageCorners[0].y)) < .05f,
            "stage rect is 320x180 x scale and starts on a whole screen pixel");
        var screenCorners = new Vector3[4];
        ((RectTransform)screenObject.transform).GetWorldCorners(screenCorners);
        float left = stageCorners[0].x - screenCorners[0].x, right = screenCorners[2].x - stageCorners[2].x;
        float bottom = stageCorners[0].y - screenCorners[0].y, top = screenCorners[2].y - stageCorners[2].y;
        Check(view.BorderPixels > 0f && Mathf.Min(Mathf.Min(left, right), Mathf.Min(top, bottom)) >= view.BorderPixels - .5f,
            "black border of at least " + view.BorderPixels + " px on every side (l " + left + " r " + right + " t " + top + " b " + bottom + ")");
        observations.Add(new { screen = new[] { Screen.width, Screen.height },
            tvScreenPixels = new[] { view.ScreenPixelSize.x, view.ScreenPixelSize.y }, pixelScale = view.PixelScale,
            borderPixels = view.BorderPixels, margins = new[] { left, right, top, bottom },
            stageCorner = new[] { stageCorners[0].x, stageCorners[0].y }, stagePixels = new[] { pxW, pxH } });

        // Preserve coverage of old saves/renderers, then test the production Enter route below.
        view.DoorMode = OSChapter04TheaterStage.DoorMode.Locked;
        // Fresh CityWalk: rise, then walk.
        bridge.SetPaused(false);
        bridge.Bind(OSChapter04PresentationBridge.CityWalk, false);
        yield return null;
        Check(Stage.CurrentPhase == OSChapter04TheaterStage.Phase.Rise, "fresh CityWalk starts from the empty stage");
        float riseStart = Time.unscaledTime;
        yield return Wait(.6f);
        yield return Shot("qa_01_rise");
        yield return WaitUntil(() => Stage.CurrentPhase == OSChapter04TheaterStage.Phase.Walk, 6f);
        float riseSeconds = Time.unscaledTime - riseStart;
        Check(Stage.CurrentPhase == OSChapter04TheaterStage.Phase.Walk && riseSeconds > 1.5f && riseSeconds < 2.3f,
            "city stands up back to front and starts walking (" + riseSeconds.ToString("0.00") + " s, expected 1.70)");

        // Pause freezes the clock; resume continues.
        yield return Wait(.4f);
        bridge.SetPaused(true);
        yield return null;
        int frozen = Stage.PhaseFrame;
        yield return Wait(.6f);
        Check(Stage.PhaseFrame == frozen, "pause stops every animation clock");
        bridge.SetPaused(false);
        yield return Wait(.3f);
        Check(Stage.PhaseFrame > frozen && Stage.PhaseFrame <= frozen + 12, "resume continues from the same frame");

        // Window close / reopen does not replay the opening.
        int beforeClose = Stage.PhaseFrame;
        screenObject.SetActive(false);
        yield return Wait(.4f);
        Check(Stage.PhaseFrame == beforeClose, "closed window keeps the stage still");
        screenObject.SetActive(true);
        yield return Wait(.2f);
        Check(Stage.CurrentPhase == OSChapter04TheaterStage.Phase.Walk && Stage.PhaseFrame > beforeClose,
            "reopening keeps walking instead of replaying the stand-up");
        yield return Shot("qa_02_walk");

        // Full arrival chain with real reports.
        reports.Clear();
        float chainStart = Time.unscaledTime;
        Check(bridge.Goto(OSChapter04PresentationBridge.CityArrive), "bridge accepts CityArrive");
        yield return null;
        Check(Stage.CurrentPhase == OSChapter04TheaterStage.Phase.Arrive, "CityArrive starts the house arrival");
        yield return Wait(5f);
        yield return Shot("qa_03_arrive");
        yield return WaitUntil(() => bridge.CurrentState == OSChapter04PresentationBridge.DoorLocked, 12f);
        yield return Shot("qa_04_doorlocked");
        // From the door into the lab her x must never change, not even for one frame.
        var seenX = new HashSet<float>();
        yield return WaitWatching(() => bridge.CurrentState == OSChapter04PresentationBridge.FoldToLab, 6f, () => seenX.Add(Stage.PuppetX));
        yield return WaitWatching(() => Stage.CurrentPhase == OSChapter04TheaterStage.Phase.LabRise, 3f, () => seenX.Add(Stage.PuppetX));
        yield return Shot("qa_05_fold");
        yield return WaitWatching(() => bridge.CurrentState == OSChapter04PresentationBridge.LabRummage, 6f, () => seenX.Add(Stage.PuppetX));
        yield return Wait(.3f);
        seenX.Add(Stage.PuppetX);
        Check(seenX.Count == 1 && seenX.Contains(OSChapter04TheaterStage.PuppetLeft),
            "puppet x stays " + OSChapter04TheaterStage.PuppetLeft + " from the door through LabRummage (seen " + string.Join(",", seenX) + ")");
        string[] order = reports.Select(r => r.Key).ToArray();
        var times = reports.Select(r => r.Value - chainStart).ToArray();
        observations.Add(new { chain = order, seconds = times.Select(t => Math.Round(t, 3)).ToArray(), houseX = Stage.HouseX, doorCenter = Stage.DoorCenter });
        Check(order.SequenceEqual(new[] { "CityArrive", "DoorLocked", "FoldToLab", "LabRummage" }),
            "chain reports DoorLocked -> FoldToLab -> LabRummage in order (" + string.Join(",", order) + ")");
        Check(times.Length == 4 && times[2] - times[1] > 1f && times[3] - times[2] > 1.5f,
            "reports land on real animation boundaries, not in one frame");
        Check(Stage.CurrentPhase == OSChapter04TheaterStage.Phase.Rummage && !Stage.IsHalted, "chain ends in the LabRummage loop");
        Check(Mathf.Abs(Stage.DoorCenter - OSChapter04TheaterStage.DoorStopCenter) <= 1,
            "the door came to rest in front of her at x 130 (+-1: " + Stage.DoorCenter + ")");
        int frame = Stage.PhaseFrame;
        var reference = new OSChapter04TheaterStage(new OSChapter04TheaterLibrary());
        reference.Begin(OSChapter04PresentationBridge.LabRummage, true);
        for (int i = 0; i < frame; i++) reference.Step();
        Check(view.Stage.DrawInk().SequenceEqual(reference.DrawInk()), "first-play LabRummage matches a restored LabRummage frame for frame");
        yield return Shot("qa_06_labrummage");

        // Pause inside the chain: no report while paused.
        bridge.Bind(OSChapter04PresentationBridge.CityWalk, true);
        yield return null;
        Check(Stage.CurrentPhase == OSChapter04TheaterStage.Phase.Walk && Stage.PhaseFrame <= 3, "restored CityWalk goes straight to the walk loop");
        reports.Clear();
        bridge.Goto(OSChapter04PresentationBridge.CityArrive);
        yield return WaitUntil(() => bridge.CurrentState == OSChapter04PresentationBridge.DoorLocked, 14f);
        bridge.SetPaused(true);
        int pausedFrame = Stage.PhaseFrame;
        yield return Wait(2f);
        Check(bridge.CurrentState == OSChapter04PresentationBridge.DoorLocked && Stage.PhaseFrame == pausedFrame,
            "paused chain neither animates nor reports FoldToLab");
        bridge.SetPaused(false);
        yield return WaitUntil(() => bridge.CurrentState == OSChapter04PresentationBridge.LabRummage, 8f);
        Check(bridge.CurrentState == OSChapter04PresentationBridge.LabRummage, "resumed chain finishes");

        // A newer request makes the running chain stale.
        bridge.Bind(OSChapter04PresentationBridge.CityWalk, true);
        bridge.Goto(OSChapter04PresentationBridge.CityArrive);
        yield return WaitUntil(() => bridge.CurrentState == OSChapter04PresentationBridge.DoorLocked, 14f);
        reports.Clear();
        bridge.Bind(OSChapter04PresentationBridge.CityWalk, true);
        yield return Wait(3f);
        Check(bridge.CurrentState == OSChapter04PresentationBridge.CityWalk
            && reports.All(r => r.Key == OSChapter04PresentationBridge.CityWalk)
            && Stage.CurrentPhase == OSChapter04TheaterStage.Phase.Walk,
            "a re-bind mid-chain drops the old chain without late reports");

        // Script v1 (Enter mode) through the real view and bridge: line 7 waits, line 9 walks in.
        Check(!view.GotoEnterDoor(), "GotoEnterDoor is refused while she is not waiting at the door");
        view.DoorMode = OSChapter04TheaterStage.DoorMode.Enter;
        bridge.Bind(OSChapter04PresentationBridge.CityWalk, true);
        yield return null;
        reports.Clear();
        bridge.Goto(OSChapter04PresentationBridge.CityArrive);
        yield return WaitUntil(() => Stage.CurrentPhase == OSChapter04TheaterStage.Phase.AtDoor, 14f);
        Check(Stage.CurrentPhase == OSChapter04TheaterStage.Phase.AtDoor && bridge.CurrentState == OSChapter04PresentationBridge.AtDoor
            && Mathf.Abs(Stage.DoorCenter - OSChapter04TheaterStage.DoorStopCenter) <= 1,
            "Enter mode: CityArrive stops with the door in front of her and waits (AtDoor)");
        yield return Wait(2.5f);
        Check(Stage.CurrentPhase == OSChapter04TheaterStage.Phase.AtDoor && bridge.CurrentState == OSChapter04PresentationBridge.AtDoor
            && reports.Select(r => r.Key).SequenceEqual(new[] { "CityArrive", "AtDoor" }), "arrival reports the stable door wait once and does not advance by itself");
        yield return Shot("qa_07_v1_atdoor");
        Check(bridge.Goto(OSChapter04PresentationBridge.EnterDoor), "line 9: the story bridge requests EnterDoor at the door");
        Check(!view.GotoEnterDoor(), "a second GotoEnterDoor while walking in is refused");
        yield return Wait(.7f);
        yield return Shot("qa_08_v1_enterdoor");
        yield return WaitUntil(() => bridge.CurrentState == OSChapter04PresentationBridge.LabRummage, 10f);
        string[] entryOrder = reports.Select(r => r.Key).ToArray();
        Check(entryOrder.SequenceEqual(new[] { "CityArrive", "AtDoor", "EnterDoor", "FoldToLab", "LabRummage" })
            && Stage.CurrentPhase == OSChapter04TheaterStage.Phase.Rummage && Stage.IsPuppetVisible,
            "Enter chain reports AtDoor -> EnterDoor -> FoldToLab -> LabRummage (" + string.Join(",", entryOrder) + ")");
        yield return Shot("qa_09_v1_labrummage");
        Check(bridge.Goto(OSChapter04PresentationBridge.LabTidy), "line 10: the story bridge requests LabTidy");
        yield return WaitUntil(() => Stage.CurrentPhase == OSChapter04TheaterStage.Phase.Tidy, 6f);
        Check(Stage.CurrentPhase == OSChapter04TheaterStage.Phase.Tidy && !Stage.IsHalted, "LabTidy reaches its stable loop through the real bridge");
        bridge.Bind(OSChapter04PresentationBridge.LabTidy, true);
        yield return null;
        Check(Stage.CurrentPhase == OSChapter04TheaterStage.Phase.Tidy && Stage.PhaseFrame < 10, "LabTidy restore starts the experiment cycle directly");
        bridge.Bind(OSChapter04PresentationBridge.AtDoor, true);
        yield return null;
        Check(Stage.CurrentPhase == OSChapter04TheaterStage.Phase.AtDoor && Stage.PuppetX == OSChapter04TheaterStage.PuppetLeft, "AtDoor restore preserves the waiting pose at x96");
        view.DoorMode = OSChapter04TheaterStage.DoorMode.Locked;

        // Restore LabRummage directly.
        bridge.Bind(OSChapter04PresentationBridge.EnterDoor, true);
        yield return null;
        Check(bridge.CurrentState == OSChapter04PresentationBridge.LabRummage
            && Stage.CurrentPhase == OSChapter04TheaterStage.Phase.Rummage && Stage.PhaseFrame <= 30
            && Mathf.Approximately(Stage.PuppetX, OSChapter04TheaterStage.PuppetLeft),
            "restoring a saved transition lands directly in LabRummage at x 96");

        yield return RunScenes();
        yield return RunLaterScenes();

        // Unbind clears.
        bridge.Unbind();
        yield return null;
        Check(Stage.CurrentPhase == OSChapter04TheaterStage.Phase.Idle, "PresentationCleared returns to the empty stage");

        // Outside chapter 4 (no debug bridge) the old curtains come back untouched.
        view.DebugUseBridge(null);
        yield return null;
        if (GameDataManager.Instance.CurrentChapterIndex != 4)
            Check(!view.IsTakenOver && curtains.All(c => c.activeSelf)
                && !screenObject.transform.Find("Chapter04Theater").gameObject.activeSelf,
                "outside chapter 4 the curtains are shown again and the stage is hidden");

        CheckLayoutAndTidy();
        CheckDoorModes();
        CheckScenes();
        CheckLaterScenes();
        CheckGraveSection();
    }

    /// <summary>Round 7 through the real view and bridge: lines 27, 36, 43 in order, then restores.</summary>
    static IEnumerator RunLaterScenes()
    {
        bridge.Bind(OSChapter04PresentationBridge.WastelandWalk, true);
        bridge.SetPaused(false);
        yield return Wait(.5f);

        Check(bridge.Goto(OSChapter04PresentationBridge.NorthSettlement), "line 27: the story bridge requests NorthSettlement");
        yield return null;
        Check(!view.UsesPendingSceneArtwork && Stage.CurrentPhase == OSChapter04TheaterStage.Phase.SceneOut,
            "NorthSettlement draws its own picture: the walk folds away at once");
        yield return WaitUntil(() => Stage.CurrentPhase == OSChapter04TheaterStage.Phase.Scene, 6f);
        yield return Wait(1f);
        Stage.DrawInk();
        Check(Stage.StateId == OSChapter04PresentationBridge.NorthSettlement && Stage.CurrentPhase == OSChapter04TheaterStage.Phase.Scene
            && !Stage.IsPuppetDrawn, "NorthSettlement loops with the boy and his mother only (Live is not on stage)");
        yield return Shot("qa_16_north_settlement");

        Check(bridge.Goto(OSChapter04PresentationBridge.LabNight), "line 36: the story bridge requests LabNight");
        yield return WaitUntil(() => Stage.CurrentPhase == OSChapter04TheaterStage.Phase.Scene, 6f);
        float roomUp = Time.unscaledTime;
        Check(Stage.OverlayLevel <= 2, "LabNight: the room stands up before the night darkens it (step " + Stage.OverlayLevel + ")");
        yield return WaitUntil(() => Stage.OverlayLevel == 5, 3f);
        float dark = Time.unscaledTime - roomUp;
        Stage.DrawInk();
        Check(Stage.OverlayLevel == 5 && dark > .5f && dark < 1.6f,
            "LabNight: the overlay darkens in steps over about a second (" + dark.ToString("0.00") + " s)");
        Check(Mathf.Approximately(Stage.PuppetX, OSChapter04TheaterStage.TidyRestX) && Stage.LastPuppetArt == OSChapter04TheaterStage.PuppetSitCup,
            "LabNight: she sits on the stool at x 32 with her cup");
        yield return Wait(1f);
        yield return Shot("qa_17_labnight");

        Check(bridge.Goto(OSChapter04PresentationBridge.FlowerWalk), "line 43: the story bridge requests FlowerWalk");
        yield return null;
        Check(Stage.CurrentPhase == OSChapter04TheaterStage.Phase.SceneOut && Stage.OverlayLevel > 0,
            "leaving LabNight lifts the overlay first");
        yield return WaitUntil(() => Stage.CurrentPhase == OSChapter04TheaterStage.Phase.Scene, 8f);
        yield return Wait(2f);
        Stage.DrawInk();
        Check(Stage.StateId == OSChapter04PresentationBridge.FlowerWalk && Mathf.Approximately(Stage.PuppetX, OSChapter04TheaterStage.PuppetLeft)
            && Stage.LastPuppetArt == "puppet_flowers" && Stage.SpireX > OSChapter04TheaterStage.SpireStopX,
            "FlowerWalk walks with the flowers at x 96 while the spire is still sliding in (x " + Stage.SpireX + ")");
        yield return Shot("qa_18_flowerwalk");

        foreach (string state in new[] { OSChapter04PresentationBridge.NorthSettlement, OSChapter04PresentationBridge.LabNight,
            OSChapter04PresentationBridge.FlowerWalk })
        {
            bridge.Bind(state, true);
            yield return null;
            Stage.DrawInk();
            bool settled = state == OSChapter04PresentationBridge.LabNight ? Stage.OverlayLevel == 5
                : state == OSChapter04PresentationBridge.FlowerWalk ? Stage.SpireX == OSChapter04TheaterStage.SpireStopX
                : !Stage.IsPuppetDrawn;
            Check(Stage.StateId == state && Stage.CurrentPhase == OSChapter04TheaterStage.Phase.Scene && Stage.PhaseFrame <= 3 && settled,
                "restored " + state + " lands directly in its settled loop");
        }
    }

    /// <summary>Round 7 on headless stages: art, the overlay steps, figures, the spire, she folds only where she is.</summary>
    static void CheckLaterScenes()
    {
        var library = new OSChapter04TheaterLibrary();
        string[] art = { "water_jar", "desk_lamp", "spire_far", "skyline_empty", "night_overlay", "puppet_mother_bed",
            "puppet_boy_scales", "researcher_a", "researcher_b", "puppet_flowers" };
        string[] absent = art.Where(id => !library.Exists(id)).ToArray();
        Check(absent.Length == 0, "round 7 art present (" + art.Length + " pieces" + (absent.Length > 0 ? ", missing " + string.Join(",", absent) : "") + ")");
        foreach (string state in new[] { OSChapter04PresentationBridge.NorthSettlement, OSChapter04PresentationBridge.LabNight,
            OSChapter04PresentationBridge.FlowerWalk })
            Check(OSChapter04TheaterStage.HasSceneArtwork(state), state + " has its own picture");
        Check(!OSChapter04TheaterStage.HasSceneArtwork(OSChapter04PresentationBridge.GirlHome), "GirlHome (old saves only) still uses the LabTidy fallback");

        int Until(OSChapter04TheaterStage s, OSChapter04TheaterStage.Phase phase)
        {
            int n = 0;
            while (s.CurrentPhase != phase && n < 2000) { s.Step(); n++; }
            return n;
        }
        int Distinct(OSChapter04TheaterStage s, int frames)
        {
            var seen = new HashSet<string>();
            for (int i = 0; i < frames; i++) { seen.Add(Convert.ToBase64String(s.DrawInk())); s.Step(); }
            return seen.Count;
        }

        // TentCare -> NorthSettlement: she folds away with the tent and never comes back up.
        var stage = new OSChapter04TheaterStage(library);
        stage.Begin(OSChapter04PresentationBridge.TentCare, true);
        stage.Begin(OSChapter04PresentationBridge.NorthSettlement, false);
        bool drawnAtStart = (stage.DrawInk() != null) && stage.IsPuppetDrawn, everAfterIn = false;
        while (stage.CurrentPhase == OSChapter04TheaterStage.Phase.SceneOut) stage.Step();
        for (int i = 0; i < 400; i++) { stage.DrawInk(); everAfterIn |= stage.IsPuppetDrawn; stage.Step(); }
        Check(drawnAtStart && !everAfterIn && stage.StateId == OSChapter04PresentationBridge.NorthSettlement,
            "NorthSettlement: she lies down with TentCare and is not on the new stage");
        Check(Distinct(stage, 300) > 4, "NorthSettlement: the mother breathes, the boy shuffles and dips, the fire flickers");

        // NorthSettlement -> LabNight: no puppet to fold; the room stands, then darkens 1..5 over 30 frames.
        stage.Begin(OSChapter04PresentationBridge.LabNight, false);
        stage.DrawInk();
        bool noneFolding = !stage.IsPuppetDrawn;
        bool darkDuringRise = false;
        while (stage.CurrentPhase != OSChapter04TheaterStage.Phase.Scene) { darkDuringRise |= stage.OverlayLevel > 0; stage.Step(); }
        var levels = new List<int>();
        int grayStart = stage.DrawInk().Count(v => v == OSChapter04TheaterPiece.Gray);
        // The stool she sits on stays above the dark: its own opaque pixels never change.
        var stoolRow = OSChapter04TheaterStage.LabLayout.First(r => r.id == "stool");
        var stoolArt = library.Get("stool");
        byte[] stoolPixels = stoolArt.Dimmed(OSChapter04TheaterPiece.Undimmed);
        int stoolTop = stoolRow.bottom - stoolArt.Height + 1;
        Func<byte[], string> stoolInk = ink =>
        {
            var sb = new System.Text.StringBuilder();
            for (int y = 0; y < stoolArt.Height; y++)
                for (int x = 0; x < stoolArt.Width; x++)
                    if (stoolPixels[y * stoolArt.Width + x] != OSChapter04TheaterPiece.Clear)
                        sb.Append(ink[(stoolTop + y) * OSChapter04TheaterStage.Width + stoolRow.x + x]);
            return sb.ToString();
        };
        string stoolLit = stoolInk(stage.DrawInk());
        for (int i = 0; i < 40; i++) { levels.Add(stage.OverlayLevel); stage.Step(); }
        int grayDark = stage.DrawInk().Count(v => v == OSChapter04TheaterPiece.Gray);
        string stoolDark = stoolInk(stage.DrawInk());
        bool rising = levels[0] == 1 && levels[levels.Count - 1] == 5;
        for (int i = 1; i < levels.Count; i++) rising &= levels[i] >= levels[i - 1] && levels[i] - levels[i - 1] <= 1;
        observations.Add(new { labNightOverlay = levels.Distinct().ToArray(), grayStart, grayDark });
        Check(noneFolding && !darkDuringRise && rising && grayDark < grayStart * .8f,
            "LabNight: nothing of her folds from NorthSettlement; the room stands up lit and darkens one step at a time (" + string.Join(",", levels.Distinct()) + ")");
        Check(stage.IsPuppetDrawn && Mathf.Approximately(stage.PuppetX, OSChapter04TheaterStage.TidyRestX)
            && stage.LastPuppetArt == OSChapter04TheaterStage.PuppetSitCup, "LabNight: she sits at x 32 with puppet_sit_cup, above the dark");
        Check(stoolLit == stoolDark, "LabNight: her stool is drawn above the night overlay with the figures, unchanged by the dark");
        Check(Distinct(stage, 300) > 3, "LabNight: one researcher leans toward the desk, the other scribbles, her cup lifts");

        // LabNight -> FlowerWalk: the overlay lifts (5 steps) before anything folds.
        stage.Begin(OSChapter04PresentationBridge.FlowerWalk, false);
        var outLevels = new List<int>();
        bool upWhileLifting = true;
        for (int i = 0; i < 30; i++) { outLevels.Add(stage.OverlayLevel); stage.DrawInk(); upWhileLifting &= stage.IsPuppetDrawn; stage.Step(); }
        Check(outLevels[0] == 4 && outLevels[29] == 0 && upWhileLifting && stage.CurrentPhase == OSChapter04TheaterStage.Phase.SceneOut,
            "leaving LabNight: the overlay steps back out over 30 frames while the room still stands (" + string.Join(",", outLevels.Distinct()) + ")");
        Until(stage, OSChapter04TheaterStage.Phase.Scene);
        var spire = new List<int?>();
        for (int i = 0; i <= 1200; i++) { if (i % 300 == 0) spire.Add(stage.SpireX); stage.Step(); }
        observations.Add(new { spireX = spire.ToArray() });
        Check(spire[0] == OSChapter04TheaterStage.Width && spire[1] < spire[0] && spire[1] > OSChapter04TheaterStage.SpireStopX
            && spire[4] == OSChapter04TheaterStage.SpireStopX, "FlowerWalk: the spire slides in from off stage and rests at x 230 (" + string.Join(",", spire) + ")");
        stage.DrawInk();
        Check(Mathf.Approximately(stage.PuppetX, OSChapter04TheaterStage.PuppetLeft) && stage.LastPuppetArt == "puppet_flowers",
            "FlowerWalk: she hops on the spot at x 96 with the flowers");

        foreach (string state in new[] { OSChapter04PresentationBridge.NorthSettlement, OSChapter04PresentationBridge.LabNight,
            OSChapter04PresentationBridge.FlowerWalk })
        {
            var restored = new OSChapter04TheaterStage(library);
            restored.Begin(state, true);
            restored.DrawInk();
            bool settled = state == OSChapter04PresentationBridge.LabNight ? restored.OverlayLevel == 5
                : state == OSChapter04PresentationBridge.FlowerWalk ? restored.SpireX == OSChapter04TheaterStage.SpireStopX
                : !restored.IsPuppetDrawn;
            Check(restored.CurrentPhase == OSChapter04TheaterStage.Phase.Scene && restored.PhaseFrame == 0 && settled,
                "restore " + state + " builds the settled loop directly");
        }

        // A request during the overlay lift waits, then plays.
        var early = new OSChapter04TheaterStage(library);
        early.Begin(OSChapter04PresentationBridge.LabNight, true);
        early.Begin(OSChapter04PresentationBridge.FlowerWalk, false);
        for (int i = 0; i < 5; i++) early.Step();
        early.Begin(OSChapter04PresentationBridge.WastelandWalk, false);
        bool queued = early.HasQueuedRequest;
        int guard = 0;
        while (!(early.CurrentPhase == OSChapter04TheaterStage.Phase.Scene && early.StateId == OSChapter04PresentationBridge.WastelandWalk)
            && guard++ < 800) early.Step();
        Check(queued && early.StateId == OSChapter04PresentationBridge.WastelandWalk && !early.HasQueuedRequest,
            "a request arriving while the night lifts waits and then plays (" + guard + " frames)");
        Check(library.Missing.Count == 0, "round 7: no gray placeholder blocks were drawn (" + string.Join(",", library.Missing) + ")");
    }

    /// <summary>Sections 2-3 (round 6) through the real view and bridge: lines 19-26 in order, plus an early request.</summary>
    static IEnumerator RunScenes()
    {
        bridge.Bind(OSChapter04PresentationBridge.LabTidy, true);
        yield return Wait(1f);
        Check(bridge.Goto(OSChapter04PresentationBridge.MedicineReady), "line 19: the story bridge requests MedicineReady from LabTidy");
        yield return null;
        Check(!view.UsesPendingSceneArtwork && Stage.CurrentPhase == OSChapter04TheaterStage.Phase.Medicine
            && Stage.StateId == OSChapter04PresentationBridge.MedicineReady, "MedicineReady draws its own picture, not the LabTidy fallback");
        yield return WaitUntil(() => Stage.IsHoldingVial, 8f);
        Stage.DrawInk();
        Check(Stage.IsHoldingVial && Mathf.Approximately(Stage.PuppetX, OSChapter04TheaterStage.MedicineX)
            && Stage.LastPuppetArt == OSChapter04TheaterStage.PuppetHoldVial, "MedicineReady settles at x 118 holding the vial");
        yield return Wait(.5f);
        yield return Shot("qa_10_medicine");

        var targets = new[]
        {
            new KeyValuePair<string, float>(OSChapter04PresentationBridge.WastelandWalk, OSChapter04TheaterStage.PuppetLeft),
            new KeyValuePair<string, float>(OSChapter04PresentationBridge.SettlementWait, 24),
            new KeyValuePair<string, float>(OSChapter04PresentationBridge.TentCare, 176),
            new KeyValuePair<string, float>(OSChapter04PresentationBridge.WastelandWalk, OSChapter04TheaterStage.PuppetLeft),
        };
        for (int t = 0; t < targets.Length; t++)
        {
            string state = targets[t].Key;
            Check(bridge.Goto(state), "the story bridge requests " + state);
            yield return null;
            Check(!view.UsesPendingSceneArtwork && Stage.CurrentPhase == OSChapter04TheaterStage.Phase.SceneOut,
                state + ": the scene change starts at once (fold out)");
            if (t == 0) { yield return Wait(.45f); yield return Shot("qa_11_fold_out_mid"); }
            if (t == 3) { yield return WaitUntil(() => Stage.CurrentPhase == OSChapter04TheaterStage.Phase.SceneIn, 4f); yield return Wait(.3f); yield return Shot("qa_15_rise_mid"); }
            yield return WaitUntil(() => Stage.CurrentPhase == OSChapter04TheaterStage.Phase.Scene, 6f);
            Check(Stage.CurrentPhase == OSChapter04TheaterStage.Phase.Scene && Stage.StateId == state
                && bridge.CurrentState == state && Mathf.Approximately(Stage.PuppetX, targets[t].Value),
                state + ": stands up and loops with her at x " + targets[t].Value + " (x " + Stage.PuppetX + ")");
            yield return Wait(1f);
            if (t < 3) yield return Shot("qa_1" + (2 + t) + "_" + state.ToLowerInvariant());
        }

        // Early request: the next line lands while the change is still folding.
        Check(bridge.Goto(OSChapter04PresentationBridge.SettlementWait), "early: SettlementWait requested");
        yield return Wait(.15f);
        Check(bridge.Goto(OSChapter04PresentationBridge.TentCare), "early: TentCare requested mid fold");
        yield return null;
        Check(Stage.HasQueuedRequest && Stage.QueuedState == OSChapter04PresentationBridge.TentCare, "early: TentCare waits in the stage queue");
        yield return WaitUntil(() => Stage.CurrentPhase == OSChapter04TheaterStage.Phase.Scene
            && Stage.StateId == OSChapter04PresentationBridge.TentCare, 10f);
        Check(Stage.StateId == OSChapter04PresentationBridge.TentCare && Stage.CurrentPhase == OSChapter04TheaterStage.Phase.Scene
            && !Stage.HasQueuedRequest && bridge.CurrentState == OSChapter04PresentationBridge.TentCare,
            "early: the queued request plays right after the running change; nothing stalls");

        bridge.Goto(OSChapter04PresentationBridge.SettlementWait);
        bridge.Goto(OSChapter04PresentationBridge.WastelandWalk);
        Check(Stage.HasQueuedRequest, "later fallback regression starts with an older queued scene");
        Check(bridge.Goto(OSChapter04PresentationBridge.GirlHome), "a newer unbuilt scene replaces the queued request");
        yield return Wait(3f);
        Check(view.RequestedStateId == OSChapter04PresentationBridge.GirlHome && view.UsesPendingSceneArtwork
            && Stage.CurrentPhase == OSChapter04TheaterStage.Phase.Tidy && !Stage.HasQueuedRequest
            && bridge.CurrentState == OSChapter04PresentationBridge.GirlHome,
            "the newer fallback remains visible and an old queued scene never takes it over");

        bridge.Unbind();
        Check(Stage.CurrentPhase == OSChapter04TheaterStage.Phase.Idle, "late binding regression begins with an empty stage");
        bridge.Bind(OSChapter04PresentationBridge.TentCare, false);
        bridge.SetPaused(false);
        yield return WaitUntil(() => Stage.CurrentPhase == OSChapter04TheaterStage.Phase.Scene, 6f);
        Check(Stage.StateId == OSChapter04PresentationBridge.TentCare
            && Stage.CurrentPhase == OSChapter04TheaterStage.Phase.Scene && !Stage.HasQueuedRequest,
            "a fresh later-scene request advances from an empty real view using its ordinary Update clock");

        foreach (string state in new[] { OSChapter04PresentationBridge.MedicineReady, OSChapter04PresentationBridge.WastelandWalk,
            OSChapter04PresentationBridge.SettlementWait, OSChapter04PresentationBridge.TentCare })
        {
            bridge.Bind(state, true);
            yield return null;
            Check(Stage.StateId == state && (state == OSChapter04PresentationBridge.MedicineReady ? Stage.IsHoldingVial
                : Stage.CurrentPhase == OSChapter04TheaterStage.Phase.Scene && Stage.PhaseFrame <= 3),
                "restored " + state + " lands directly in its loop");
        }
        view.PreviewScene(OSChapter04PresentationBridge.MedicineReady, false);
        yield return WaitUntil(() => Stage.IsHoldingVial, 8f);
        Check(view.IsPreviewing && Stage.IsHoldingVial && !Stage.HasQueuedRequest,
            "the MedicineReady preview button also works after viewing a later scene");
        view.PreviewStop();
    }

    /// <summary>Sections 2-3 on headless stages: art, positions, the puppet folding with the road layer, restores, queueing.</summary>
    static void CheckScenes()
    {
        var library = new OSChapter04TheaterLibrary();
        string[] art = { "bucket", "campfire", "drying_rack", "fg_grass", "fg_sign", "fg_stones", "hill_far_a", "hill_far_b",
            "lantern", "mat", "road_strip", "tent_big", "tent_interior", "tent_small", "puppet_backpack",
            OSChapter04TheaterStage.PuppetHoldVial, "puppet_sit_ground", "puppet_mother_daughter" };
        string[] absent = art.Where(id => !library.Exists(id)).ToArray();
        Check(absent.Length == 0, "round 6 art present (" + art.Length + " pieces" + (absent.Length > 0 ? ", missing " + string.Join(",", absent) : "") + ")");
        foreach (string state in new[] { OSChapter04PresentationBridge.MedicineReady, OSChapter04PresentationBridge.WastelandWalk,
            OSChapter04PresentationBridge.SettlementWait, OSChapter04PresentationBridge.TentCare })
            Check(OSChapter04TheaterStage.HasSceneArtwork(state), state + " has its own picture");
        Check(!OSChapter04TheaterStage.HasSceneArtwork(OSChapter04PresentationBridge.GirlHome), "GirlHome still uses the LabTidy fallback");

        // MedicineReady from every moment of the tidy loop: no jump, ends at x 118 holding the vial.
        var medicine = new List<string>();
        bool medicineSmooth = true, medicineLands = true;
        for (int offset = 0; offset < 900; offset += 23)
        {
            var stage = new OSChapter04TheaterStage(library);
            stage.BeginTidyPreview(true);
            for (int i = 0; i < offset; i++) stage.Step();
            float x = stage.PuppetX;
            string from = stage.CurrentTidy.mode.ToString();
            stage.Begin(OSChapter04PresentationBridge.MedicineReady, false);
            int frames = 0;
            while (!stage.IsHoldingVial && frames < 400)
            {
                if (Math.Abs(stage.PuppetX - x) > 2f) medicineSmooth = false;
                x = stage.PuppetX;
                stage.Step(); frames++;
            }
            stage.DrawInk();
            medicineLands &= stage.IsHoldingVial && Mathf.Approximately(stage.PuppetX, OSChapter04TheaterStage.MedicineX)
                && stage.LastPuppetArt == OSChapter04TheaterStage.PuppetHoldVial;
            if (offset % 115 == 0) medicine.Add(from + ":" + frames);
        }
        observations.Add(new { medicineFromTidy = medicine.ToArray() });
        Check(medicineLands, "MedicineReady from any tidy moment ends at x 118 holding the vial");
        Check(medicineSmooth, "MedicineReady finishes the current step and hops over without jumping (<= 2 px a frame)");

        // Scene changes: she is only drawn while the road layer stands, at one x, and never reappears mid-fold.
        var chain = new OSChapter04TheaterStage(library);
        chain.Begin(OSChapter04PresentationBridge.MedicineReady, true);
        var expectX = new Dictionary<string, int>
        {
            { OSChapter04PresentationBridge.WastelandWalk, OSChapter04TheaterStage.PuppetLeft },
            { OSChapter04PresentationBridge.SettlementWait, 24 },
            { OSChapter04PresentationBridge.TentCare, 176 },
        };
        var timing = new List<string>();
        foreach (string state in new[] { OSChapter04PresentationBridge.WastelandWalk, OSChapter04PresentationBridge.SettlementWait,
            OSChapter04PresentationBridge.TentCare, OSChapter04PresentationBridge.WastelandWalk })
        {
            float fromX = chain.PuppetX;
            chain.Begin(state, false);
            int outFrames = 0, inFrames = 0, outToggles = 0, inToggles = 0;
            bool outX = true, inX = true, wasDrawn = true, startsDrawn, endsHidden = true;
            chain.DrawInk();
            startsDrawn = chain.IsPuppetDrawn;
            while (chain.CurrentPhase == OSChapter04TheaterStage.Phase.SceneOut && outFrames < 600)
            {
                chain.DrawInk();
                if (chain.IsPuppetDrawn != wasDrawn) { outToggles++; wasDrawn = chain.IsPuppetDrawn; }
                if (chain.IsPuppetDrawn && !Mathf.Approximately(chain.PuppetX, fromX)) outX = false;
                endsHidden = !chain.IsPuppetDrawn;
                chain.Step(); outFrames++;
            }
            wasDrawn = false;
            while (chain.CurrentPhase == OSChapter04TheaterStage.Phase.SceneIn && inFrames < 600)
            {
                chain.DrawInk();
                if (chain.IsPuppetDrawn != wasDrawn) { inToggles++; wasDrawn = chain.IsPuppetDrawn; }
                if (chain.IsPuppetDrawn && !Mathf.Approximately(chain.PuppetX, expectX[state])) inX = false;
                chain.Step(); inFrames++;
            }
            chain.DrawInk();
            timing.Add(state + " out " + outFrames + " in " + inFrames);
            Check(startsDrawn && outToggles == 1 && endsHidden && outX,
                state + ": she lies down once with the road layer at x " + fromX + " and stays down");
            Check(inToggles == 1 && inX && chain.IsPuppetDrawn && chain.CurrentPhase == OSChapter04TheaterStage.Phase.Scene
                && chain.StateId == state, state + ": she stands up once with the road layer at x " + expectX[state]);
            Check(outFrames < 120 && inFrames < 60, state + ": change takes " + outFrames + " + " + inFrames + " frames");
        }
        observations.Add(new { sceneChanges = timing.ToArray() });

        // Restores build the loop directly.
        foreach (KeyValuePair<string, int> pair in expectX)
        {
            var restored = new OSChapter04TheaterStage(library);
            restored.Begin(pair.Key, true);
            restored.DrawInk();
            Check(restored.CurrentPhase == OSChapter04TheaterStage.Phase.Scene && restored.PhaseFrame == 0
                && restored.StateId == pair.Key && restored.IsPuppetDrawn && Mathf.Approximately(restored.PuppetX, pair.Value),
                "restore " + pair.Key + " builds the loop with her at x " + pair.Value);
        }
        var medRestore = new OSChapter04TheaterStage(library);
        medRestore.Begin(OSChapter04PresentationBridge.MedicineReady, true);
        medRestore.DrawInk();
        Check(medRestore.IsHoldingVial && medRestore.LastPuppetArt == OSChapter04TheaterStage.PuppetHoldVial
            && Mathf.Approximately(medRestore.PuppetX, OSChapter04TheaterStage.MedicineX), "restore MedicineReady: x 118 holding the vial");

        // Requests that arrive early: before the lab is tidied, mid medicine hop, mid scene change.
        var early = new OSChapter04TheaterStage(library) { Mode = OSChapter04TheaterStage.DoorMode.Enter };
        early.BeginAtDoor();
        early.Begin(OSChapter04PresentationBridge.MedicineReady, false);
        int guard = 0;
        while (!early.IsHoldingVial && guard++ < 1500) early.Step();
        Check(early.IsHoldingVial, "MedicineReady requested at the door: walks in, tidies, then holds the vial (" + guard + " frames)");
        early.Begin(OSChapter04PresentationBridge.WastelandWalk, false);
        for (int i = 0; i < 3; i++) early.Step();
        early.Begin(OSChapter04PresentationBridge.SettlementWait, false);
        bool queued = early.HasQueuedRequest;
        guard = 0;
        while (!(early.CurrentPhase == OSChapter04TheaterStage.Phase.Scene && early.StateId == OSChapter04PresentationBridge.SettlementWait)
            && guard++ < 600) early.Step();
        Check(queued && !early.HasQueuedRequest && early.StateId == OSChapter04PresentationBridge.SettlementWait
            && early.CurrentPhase == OSChapter04TheaterStage.Phase.Scene, "a request during a change waits, then plays (" + guard + " frames)");
        var fromIdle = new OSChapter04TheaterStage(library);
        fromIdle.Begin(OSChapter04PresentationBridge.TentCare, false);
        guard = 0;
        while (fromIdle.CurrentPhase != OSChapter04TheaterStage.Phase.Scene && guard++ < 600)
            fromIdle.Advance(OSChapter04TheaterStage.FrameSeconds);
        Check(fromIdle.StateId == OSChapter04PresentationBridge.TentCare, "a fresh scene request on an empty stage still arrives (" + guard + " frames)");
        var duringDoorFold = new OSChapter04TheaterStage(library) { Mode = OSChapter04TheaterStage.DoorMode.Enter };
        duringDoorFold.BeginAtDoor();
        duringDoorFold.GotoEnterDoor();
        guard = 0;
        while (duringDoorFold.CurrentPhase != OSChapter04TheaterStage.Phase.Fold && guard++ < 300) duringDoorFold.Step();
        duringDoorFold.DrawInk();
        Check(!duringDoorFold.IsPuppetDrawn, "the entered puppet stays hidden while the city folds");
        duringDoorFold.Begin(OSChapter04PresentationBridge.MedicineReady, false);
        duringDoorFold.DrawInk();
        Check(duringDoorFold.HasQueuedRequest && !duringDoorFold.IsPuppetDrawn,
            "an early medicine request preserves the current fold pose instead of flashing the entered puppet");
        guard = 0;
        while (!duringDoorFold.IsHoldingVial && guard++ < 900)
            duringDoorFold.Advance(OSChapter04TheaterStage.FrameSeconds);
        Check(duringDoorFold.IsHoldingVial && !duringDoorFold.HasQueuedRequest,
            "the request queued during the door fold still reaches the medicine loop");
        Check(library.Missing.Count == 0, "no gray placeholder blocks were drawn (" + string.Join(",", library.Missing) + ")");
    }

    /// <summary>Layout table and the preview-only tidy loop, on a headless stage (no bridge).</summary>
    static void CheckLayoutAndTidy()
    {
        var rummageBox = OSChapter04TheaterStage.LabLayout.Where(r => r.id == "box" && r.set == OSChapter04TheaterStage.LabSet.Mess).ToArray();
        Check(rummageBox.Length == 1 && rummageBox[0].x == 138 && rummageBox[0].bottom == 176
            && rummageBox[0].layer == OSChapter04TheaterStage.LabNear, "LabRummage box sits under her hands at x 138, bottom 176, near layer");

        var library = new OSChapter04TheaterLibrary();
        foreach (string id in new[] { "sofa_clean", "counter_coffee_clean", "fg_plant_alive", "puppet_rest", "puppet_idle", "puppet_sit_cup" })
            Check(library.Exists(id), "tidy art present: " + id);

        var fresh = new OSChapter04TheaterStage(library);
        fresh.Begin(OSChapter04PresentationBridge.LabRummage, true);
        fresh.BeginTidyPreview();
        int guard = 0;
        while (fresh.CurrentPhase != OSChapter04TheaterStage.Phase.Tidy && guard++ < 600) fresh.Step();
        var first = fresh.CurrentTidy;
        Check(first.mode == OSChapter04TheaterStage.TidyMode.Walk && first.x > OSChapter04TheaterStage.PuppetLeft
            && first.x <= OSChapter04TheaterStage.PuppetLeft + 2, "fresh LabTidy: she starts hopping from x 96 toward the bench");
        var seen = new List<string>();
        var spots = new Dictionary<OSChapter04TheaterStage.TidyMode, HashSet<int>>
        {
            { OSChapter04TheaterStage.TidyMode.Work, new HashSet<int>() },
            { OSChapter04TheaterStage.TidyMode.Sit, new HashSet<int>() },
            { OSChapter04TheaterStage.TidyMode.SitDown, new HashSet<int>() },
        };
        var sitArt = new HashSet<string>();
        var modeFrames = new Dictionary<OSChapter04TheaterStage.TidyMode, int>();
        OSChapter04TheaterStage.TidyMode last = first.mode;
        int previousX = first.x;
        bool smooth = true;
        for (int i = 0; i < 2400; i++)
        {
            fresh.Step();
            var now = fresh.CurrentTidy;
            if (now.mode != last) { seen.Add(now.mode + "@" + now.x); last = now.mode; }
            if (spots.TryGetValue(now.mode, out HashSet<int> set)) set.Add(now.x);
            if (Math.Abs(now.x - previousX) > 2) smooth = false;
            previousX = now.x;
            if (now.mode == OSChapter04TheaterStage.TidyMode.Sit) { fresh.DrawInk(); sitArt.Add(fresh.LastPuppetArt); }
            if (seen.Count >= 1 && seen.Count <= 6) modeFrames[now.mode] = (modeFrames.TryGetValue(now.mode, out int n) ? n : 0) + 1;
        }
        observations.Add(new { tidyLoop = seen.Take(12).ToArray(), sitArt = sitArt.ToArray() });
        Check(spots[OSChapter04TheaterStage.TidyMode.Work].SetEquals(new[] { OSChapter04TheaterStage.TidyWorkX })
            && spots[OSChapter04TheaterStage.TidyMode.Sit].SetEquals(new[] { OSChapter04TheaterStage.TidyRestX })
            && spots[OSChapter04TheaterStage.TidyMode.SitDown].SetEquals(new[] { OSChapter04TheaterStage.TidyRestX }),
            "tidy loop works at x " + OSChapter04TheaterStage.TidyWorkX + " and sits on the stool at x " + OSChapter04TheaterStage.TidyRestX);
        string[] cycle = { "Work@166", "Walk@", "SitUp@", "Sit@32", "SitDown@32", "Walk@" };
        bool ordered = seen.Count >= 12;
        for (int i = 0; ordered && i < 12; i++) ordered = seen[i].StartsWith(cycle[i % cycle.Length]);
        Check(ordered, "tidy loop order, twice: work -> hop left -> hop up the stool -> sit -> hop down -> hop right ("
            + string.Join(", ", seen.Take(6)) + ")");
        Check(modeFrames.TryGetValue(OSChapter04TheaterStage.TidyMode.SitUp, out int up) && up == 16
            && modeFrames.TryGetValue(OSChapter04TheaterStage.TidyMode.SitDown, out int down) && down == 18,
            "exactly one hop up (16 frames) and one hop down (16 + 2 landing) per cycle");
        Check(sitArt.Count == 1 && sitArt.Contains("puppet_sit_cup"),
            "sitting uses puppet_sit_cup (fallback puppet_rest only if it is missing): " + string.Join(",", sitArt));
        var sitPiece = library.Get("puppet_sit_cup");
        var standPiece = library.Get("puppet_idle");
        int sitTop = OSChapter04TheaterStage.PuppetSpriteTop(sitPiece.Height), standTop = OSChapter04TheaterStage.PuppetSpriteTop(standPiece.Height);
        var stoolRow = OSChapter04TheaterStage.LabLayout.First(r => r.id == "stool");
        int stoolTop = stoolRow.bottom - library.Get("stool").Height + 1;
        Check(sitTop + sitPiece.Height == standTop + standPiece.Height,
            "seated art (" + sitPiece.Width + "x" + sitPiece.Height + ") shares the standing art's bottom edge (y " + (standTop + standPiece.Height - 1) + ")");
        Check(sitTop + 48 == stoolTop && OSChapter04TheaterStage.TidyRestX + 34 == stoolRow.x + library.Get("stool").Width / 2,
            "seat row 48 lands on the stool top (y " + stoolTop + ") with her body column over the stool centre (x " + (OSChapter04TheaterStage.TidyRestX + 34) + ")");
        Check(smooth, "tidy hops never jump more than 2 px in a frame (every step lands on its spot)");

        var restored = new OSChapter04TheaterStage(library);
        restored.BeginTidyPreview(true);
        var start = restored.CurrentTidy;
        Check(restored.CurrentPhase == OSChapter04TheaterStage.Phase.Tidy && start.mode == OSChapter04TheaterStage.TidyMode.Work
            && start.frame == 0 && start.x == OSChapter04TheaterStage.TidyWorkX, "restored LabTidy starts at the first work frame");
    }

    /// <summary>Both door modes on headless stages: the default chain is unchanged, Enter waits and walks in.</summary>
    static void CheckDoorModes()
    {
        var library = new OSChapter04TheaterLibrary();
        var restored = new OSChapter04TheaterStage(library);
        restored.Begin(OSChapter04PresentationBridge.LabRummage, true);
        byte[] restoredInk = (byte[])restored.DrawInk().Clone();

        foreach (OSChapter04TheaterStage.DoorMode mode in new[] { OSChapter04TheaterStage.DoorMode.Locked, OSChapter04TheaterStage.DoorMode.Enter })
        {
            var events = new List<string>();
            var stage = new OSChapter04TheaterStage(library) { Mode = mode };
            stage.Boundary = id => { events.Add(id); return true; };
            stage.Begin(OSChapter04PresentationBridge.CityWalk, true);
            for (int i = 0; i < 23; i++) stage.Step();
            stage.Begin(OSChapter04PresentationBridge.CityArrive, false);
            int guard = 0;
            while (stage.CurrentPhase == OSChapter04TheaterStage.Phase.Arrive && guard++ < 1000) stage.Step();
            if (mode == OSChapter04TheaterStage.DoorMode.Locked)
            {
                Check(stage.CurrentPhase == OSChapter04TheaterStage.Phase.DoorLocked, "Locked (default): CityArrive runs on into the rattling door");
            }
            else
            {
                Check(stage.CurrentPhase == OSChapter04TheaterStage.Phase.AtDoor && events.SequenceEqual(new[] { "AtDoor" }), "Enter: CityArrive reports AtDoor once");
                bool stayed = true;
                for (int i = 0; i < 900; i++)
                {
                    stage.Step();
                    stayed &= stage.CurrentPhase == OSChapter04TheaterStage.Phase.AtDoor;
                }
                Check(stayed && events.Count == 1 && stage.IsPuppetVisible && Mathf.Approximately(stage.PuppetX, OSChapter04TheaterStage.PuppetLeft),
                    "Enter: AtDoor loops for 30 s with puppet_carry at x 96 without duplicate reports");
                Check(stage.GotoEnterDoor() && stage.CurrentPhase == OSChapter04TheaterStage.Phase.EnterDoor && events.SequenceEqual(new[] { "AtDoor", "EnterDoor" }),
                    "Enter: GotoEnterDoor starts and reports EnterDoor");
                var widths = new List<int>();
                bool hiddenAfterSteps = false, visibleWhileStepping = false;
                while (stage.CurrentPhase == OSChapter04TheaterStage.Phase.EnterDoor && guard++ < 2000)
                {
                    widths.Add(stage.DoorPanelWidth);
                    if (stage.PhaseFrame == 10) visibleWhileStepping = stage.IsPuppetVisible;
                    if (stage.DoorPanelWidth == 30 && stage.PhaseFrame > 10) hiddenAfterSteps = !stage.IsPuppetVisible;
                    stage.Step();
                }
                observations.Add(new { enterDoorWidths = widths.Take(12).Concat(widths.Skip(36).Take(8)).ToArray(), enterDoorFrames = widths.Count });
                Check(widths.Count > 12 && widths[0] < 30 && widths.Contains(0) && widths[widths.Count - 1] == 30,
                    "Enter: the door swings open to 0 and closes again (" + widths.Count + " frames)");
                Check(visibleWhileStepping && hiddenAfterSteps, "Enter: she hops in, then is not drawn once the door has closed behind her");
                bool hiddenInFold = true;
                while (stage.CurrentPhase == OSChapter04TheaterStage.Phase.Fold && guard++ < 3000) { hiddenInFold &= !stage.IsPuppetVisible; stage.Step(); }
                bool rose = false, startsHidden = !stage.IsPuppetVisible;
                while (stage.CurrentPhase == OSChapter04TheaterStage.Phase.LabRise && guard++ < 3000) { rose |= stage.IsPuppetVisible; stage.Step(); }
                Check(hiddenInFold && startsHidden && rose, "Enter: hidden while the city folds, stands up again with the room's near layer");
            }
            while (stage.CurrentPhase != OSChapter04TheaterStage.Phase.Rummage && guard++ < 4000) stage.Step();
            string[] expected = mode == OSChapter04TheaterStage.DoorMode.Locked
                ? new[] { "DoorLocked", "FoldToLab", "LabRummage" }
                : new[] { "AtDoor", "EnterDoor", "FoldToLab", "LabRummage" };
            Check(events.SequenceEqual(expected) && stage.DrawInk().SequenceEqual(restoredInk),
                mode + ": ordered boundaries and first LabRummage frame equal a restore");
        }

        var atDoor = new OSChapter04TheaterStage(library) { Mode = OSChapter04TheaterStage.DoorMode.Enter };
        atDoor.BeginAtDoor();
        Check(atDoor.CurrentPhase == OSChapter04TheaterStage.Phase.AtDoor && atDoor.DoorCenter == OSChapter04TheaterStage.DoorStopCenter
            && atDoor.StateId == OSChapter04PresentationBridge.AtDoor, "restoring at the door builds the AtDoor loop directly (door centre 130)");
    }

    /// <summary>Round 8 on headless stages: rain, the grave, laying the flowers down, the grief tiers, restores.</summary>
    static void CheckGraveSection()
    {
        var library = new OSChapter04TheaterLibrary();
        string[] art = { "chapel_ruin", "dead_tree", "fence_iron", "brick_wall_low", "lamp_post_grave", "mother_grave",
            "flowers_vial_ground", "fg_fence", "puppet_kneel_place" };
        string[] absent = art.Where(id => !library.Exists(id)).ToArray();
        Check(absent.Length == 0, "round 8 art present (" + art.Length + " pieces" + (absent.Length > 0 ? ", missing " + string.Join(",", absent) : "") + ")");
        foreach (string state in new[] { OSChapter04TheaterStage.FlowerWalkRain, OSChapter04PresentationBridge.Grave,
            OSChapter04TheaterStage.GravePlaceFlowers, OSChapter04PresentationBridge.GraveBreakdown })
            Check(OSChapter04TheaterStage.HasSceneArtwork(state), state + " has its own picture");

        // Line 3: the rain starts on the same walk, without a scene change; the spire keeps sliding.
        var stage = new OSChapter04TheaterStage(library);
        stage.Begin(OSChapter04PresentationBridge.FlowerWalk, true);
        for (int i = 0; i < 30; i++) stage.Step();
        int? spireBefore = stage.SpireX;
        stage.Begin(OSChapter04TheaterStage.FlowerWalkRain, false);
        stage.DrawInk();
        Check(stage.CurrentPhase == OSChapter04TheaterStage.Phase.Scene && stage.StateId == OSChapter04TheaterStage.FlowerWalkRain
            && stage.IsRaining && stage.SpireX == spireBefore && stage.LastPuppetArt == OSChapter04TheaterStage.PuppetFlowers,
            "FlowerWalkRain starts the rain on the same walk: no fold, the spire and her hop carry on");
        var fresh = new OSChapter04TheaterStage(library);
        fresh.Begin(OSChapter04TheaterStage.FlowerWalkRain, true);
        Check(fresh.IsRaining && fresh.SpireX == OSChapter04TheaterStage.SpireStopX, "restore FlowerWalkRain: raining, spire at rest");

        // Line 4: the walk folds into the grave; the rain never stops or folds.
        stage.Begin(OSChapter04PresentationBridge.Grave, false);
        bool rainedThrough = true, folded = false;
        int guard = 0;
        while (stage.CurrentPhase != OSChapter04TheaterStage.Phase.Scene && guard++ < 400)
        {
            stage.DrawInk(); rainedThrough &= stage.IsRaining;
            folded |= stage.CurrentPhase == OSChapter04TheaterStage.Phase.SceneOut;
            stage.Step();
        }
        stage.DrawInk();
        Check(folded && rainedThrough && stage.IsRaining && stage.StateId == OSChapter04PresentationBridge.Grave,
            "Grave: the walk folds away and the cemetery stands up while the rain keeps falling (" + guard + " frames)");
        Check(Mathf.Approximately(stage.PuppetX, OSChapter04TheaterStage.GravePuppetX) && stage.LastPuppetArt == OSChapter04TheaterStage.PuppetFlowers
            && !stage.FlowersPlaced, "Grave: she stands at x 130 holding the flowers");

        // Line 6: she kneels, sinks 2 px, the flowers stand up, she stands empty-handed.
        stage.Begin(OSChapter04TheaterStage.GravePlaceFlowers, false);
        var arts = new List<string>();
        int placing = 0;
        bool noFold = true;
        while (!stage.FlowersPlaced && placing < 400)
        {
            stage.DrawInk();
            if (arts.Count == 0 || arts[arts.Count - 1] != stage.LastPuppetArt) arts.Add(stage.LastPuppetArt);
            noFold &= stage.CurrentPhase == OSChapter04TheaterStage.Phase.Scene;
            stage.Step(); placing++;
        }
        stage.DrawInk();
        if (arts[arts.Count - 1] != stage.LastPuppetArt) arts.Add(stage.LastPuppetArt);
        observations.Add(new { gravePlaceArts = arts.ToArray(), gravePlaceFrames = placing });
        Check(noFold && arts.SequenceEqual(new[] { OSChapter04TheaterStage.PuppetKneelPlace, OSChapter04TheaterStage.PuppetIdle })
            && placing > 40 && placing < 70, "GravePlaceFlowers: kneel, the flowers stand up, then she stands empty-handed ("
            + string.Join(" -> ", arts) + ", " + placing + " frames)");
        Check(Mathf.Approximately(stage.PuppetX, OSChapter04TheaterStage.GravePuppetX), "she lays them down without moving from x 130");
        var placed = new OSChapter04TheaterStage(library);
        placed.Begin(OSChapter04TheaterStage.GravePlaceFlowers, true);
        placed.DrawInk();
        Check(placed.FlowersPlaced && placed.LastPuppetArt == OSChapter04TheaterStage.PuppetIdle && placed.IsRaining,
            "restore GravePlaceFlowers: standing, the flowers already at the stone");

        // Line 7 on: grief tiers fold her back and lay the scene down far to near.
        stage.Begin(OSChapter04PresentationBridge.GraveBreakdown, false);
        Check(stage.CurrentPhase == OSChapter04TheaterStage.Phase.Scene && stage.FlowersPlaced, "GraveBreakdown keeps the same picture");
        var angles = new List<string>();
        for (int tier = 1; tier <= 3; tier++)
        {
            stage.SetGriefTier(tier, false);
            for (int i = 0; i < (tier == 3 ? 140 : 40); i++) stage.Step();
            angles.Add(tier + ":" + stage.GriefAngle);
            bool farFlat = stage.IsGriefLayerFlat(0), backFlat = stage.IsGriefLayerFlat(2);
            Check(stage.GriefAngle == tier * 15 && farFlat == (tier >= 2) && backFlat == (tier >= 3),
                "grief tier " + tier + ": folded back " + stage.GriefAngle + " degrees, far layer " + (farFlat ? "flat" : "standing")
                + ", back row " + (backFlat ? "flat" : "standing"));
        }
        observations.Add(new { griefAngles = angles.ToArray() });
        byte[] ink = (byte[])stage.DrawInk().Clone();
        int stoneInk = 0, lampInk = 0;
        for (int y = 108; y < 160; y++)
        {
            for (int x = 196; x < 232; x++) if (ink[y * OSChapter04TheaterStage.Width + x] == OSChapter04TheaterPiece.Gray) stoneInk++;
            for (int x = 44; x < 54; x++) if (ink[y * OSChapter04TheaterStage.Width + x] == OSChapter04TheaterPiece.Gray) lampInk++;
        }
        Check(stoneInk > 200 && lampInk < 8 && stage.IsPuppetDrawn,
            "tier 3 leaves only her, the stone and the flowers (stone " + stoneInk + " px, lamp column " + lampInk + " px of rain)");
        stage.SetGriefTier(3, false);
        Check(stage.GriefAngle == 45 && stage.IsGriefLayerFlat(1), "the same tier again is ignored");
        stage.SetGriefTier(0, false);
        for (int i = 0; i < 20; i++) stage.Step();
        Check(stage.GriefAngle == 0 && !stage.IsGriefLayerFlat(0) && !stage.IsGriefLayerFlat(5), "tier 0 clears the grief pose");

        var restored = new OSChapter04TheaterStage(library);
        restored.Begin(OSChapter04PresentationBridge.GraveBreakdown, true);
        restored.SetGriefTier(3, true);
        Check(restored.GriefAngle == 45 && restored.IsGriefLayerFlat(0) && restored.IsGriefLayerFlat(5) && restored.FlowersPlaced,
            "restore GraveBreakdown at tier 3: folded back and laid flat at once");
        var early = new OSChapter04TheaterStage(library);
        early.Begin(OSChapter04PresentationBridge.Grave, true);
        early.SetGriefTier(2, false);
        for (int i = 0; i < 30; i++) early.Step();
        Check(!early.IsGriefLayerFlat(0), "a tier set before GraveBreakdown waits for it");
        early.Begin(OSChapter04PresentationBridge.GraveBreakdown, false);
        guard = 0;
        while (!early.IsGriefLayerFlat(0) && guard++ < 200) early.Step();
        Check(early.IsGriefLayerFlat(0) && early.GriefAngle == 30, "...and plays once GraveBreakdown begins (" + guard + " frames)");
        Check(library.Missing.Count == 0, "round 8: no gray placeholder blocks were drawn (" + string.Join(",", library.Missing) + ")");
    }
}
