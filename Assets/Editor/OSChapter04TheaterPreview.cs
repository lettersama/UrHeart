using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Editor-only preview of the chapter-4 TV theatre. Edit Mode: plays its own
/// stage in this window (no bridge, no save, no story). Play Mode: can also
/// drive the real TV view (OSChapter04TheaterView.PreviewBegin). Capture writes
/// key frames to Docs/Chapter04Theater/.
/// </summary>
public sealed class OSChapter04TheaterPreview : EditorWindow
{
    const string DocsFolder = "Docs/Chapter04Theater";

    OSChapter04TheaterStage stage;
    Texture2D texture;
    Color32[] pixels;
    double lastTime;
    bool paused;
    int zoom = 3;
    readonly List<string> boundaries = new List<string>();

    [MenuItem("Tools/MrTGame/第四章剧场/预览窗口")]
    static void Open() => GetWindow<OSChapter04TheaterPreview>("第四章剧场");

    [MenuItem("Tools/MrTGame/第四章剧场/导出关键帧截图")]
    static void CaptureMenu() => Debug.Log(CaptureForDocs());

    void OnEnable()
    {
        stage = new OSChapter04TheaterStage(new OSChapter04TheaterLibrary());
        stage.Boundary = id => { boundaries.Add(id + " @" + EditorApplication.timeSinceStartup.ToString("0.00")); return true; };
        texture = new Texture2D(OSChapter04TheaterStage.Width, OSChapter04TheaterStage.Height, TextureFormat.RGBA32, false)
        { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.HideAndDontSave };
        pixels = new Color32[OSChapter04TheaterStage.Width * OSChapter04TheaterStage.Height];
        lastTime = EditorApplication.timeSinceStartup;
        EditorApplication.update += Tick;
        Redraw();
    }

    void OnDisable()
    {
        EditorApplication.update -= Tick;
        if (texture != null) DestroyImmediate(texture);
    }

    void Tick()
    {
        double now = EditorApplication.timeSinceStartup;
        float dt = (float)(now - lastTime);
        lastTime = now;
        if (!paused && stage.Advance(dt)) { Redraw(); Repaint(); }
    }

    void Redraw()
    {
        stage.Render(pixels);
        texture.SetPixels32(pixels);
        texture.Apply(false);
    }

    void Begin(string id, bool restoring)
    {
        boundaries.Clear();
        paused = false;
        if (!restoring && id == OSChapter04PresentationBridge.CityArrive
            && stage.CurrentPhase != OSChapter04TheaterStage.Phase.Walk)
            stage.Begin(OSChapter04PresentationBridge.CityWalk, true);
        stage.Begin(id, restoring);
        Redraw();
    }

    static readonly string[] SceneStates =
    {
        OSChapter04PresentationBridge.MedicineReady, OSChapter04PresentationBridge.WastelandWalk,
        OSChapter04PresentationBridge.SettlementWait, OSChapter04PresentationBridge.TentCare,
    };

    /// <summary>Round 8: the rain on the walk, the grave, laying the flowers down, the breakdown.</summary>
    static readonly string[] GraveStates =
    {
        OSChapter04TheaterStage.FlowerWalkRain, OSChapter04PresentationBridge.Grave,
        OSChapter04TheaterStage.GravePlaceFlowers, OSChapter04PresentationBridge.GraveBreakdown,
    };

    /// <summary>Round 7: lines 27, 36 and 43.</summary>
    static readonly string[] LaterStates =
    {
        OSChapter04PresentationBridge.NorthSettlement, OSChapter04PresentationBridge.LabNight,
        OSChapter04PresentationBridge.FlowerWalk,
    };

    /// <summary>Sections 2-3 in the window: a fresh request from an empty stage starts in the tidied lab.</summary>
    void BeginScene(string id, bool restoring)
    {
        boundaries.Clear();
        paused = false;
        if (!restoring && (stage.CurrentPhase == OSChapter04TheaterStage.Phase.Idle
            || id == OSChapter04PresentationBridge.MedicineReady)) stage.Begin(OSChapter04PresentationBridge.LabTidy, true);
        stage.Begin(id, restoring);
        Redraw();
    }

    void OnGUI()
    {
        EditorGUILayout.LabelField("窗口内预览（不经过桥接、不写存档）", EditorStyles.boldLabel);
        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("CityWalk 首次立起")) Begin(OSChapter04PresentationBridge.CityWalk, false);
            if (GUILayout.Button("CityArrive 全流程（门锁）"))
            {
                stage.Mode = OSChapter04TheaterStage.DoorMode.Locked;
                Begin(OSChapter04PresentationBridge.CityArrive, false);
            }
            if (GUILayout.Button("读档 CityWalk")) Begin(OSChapter04PresentationBridge.CityWalk, true);
            if (GUILayout.Button("读档 LabRummage")) Begin(OSChapter04PresentationBridge.LabRummage, true);
        }
        using (new EditorGUILayout.HorizontalScope())
        {
            EditorGUILayout.LabelField("台本 v1", GUILayout.Width(50));
            if (GUILayout.Button("第7句 CityArrive→等门口"))
            {
                stage.Mode = OSChapter04TheaterStage.DoorMode.Enter;
                Begin(OSChapter04PresentationBridge.CityArrive, false);
            }
            using (new EditorGUI.DisabledScope(stage.CurrentPhase != OSChapter04TheaterStage.Phase.AtDoor))
                if (GUILayout.Button("第9句 进门")) { stage.GotoEnterDoor(); Redraw(); }
            if (GUILayout.Button("读档 门口等待"))
            {
                boundaries.Clear(); paused = false;
                stage.Mode = OSChapter04TheaterStage.DoorMode.Enter;
                stage.BeginAtDoor(); Redraw();
            }
            if (GUILayout.Button("第10句 LabTidy")) { boundaries.Clear(); paused = false; stage.BeginTidyPreview(); Redraw(); }
            if (GUILayout.Button("读档 LabTidy")) { boundaries.Clear(); paused = false; stage.BeginTidyPreview(true); Redraw(); }
        }
        using (new EditorGUILayout.HorizontalScope())
        {
            EditorGUILayout.LabelField("第 2-3 节", GUILayout.Width(50));
            foreach (string id in SceneStates)
                if (GUILayout.Button(id)) BeginScene(id, false);
            if (GUILayout.Button("TentCare→WastelandWalk"))
            {
                BeginScene(OSChapter04PresentationBridge.TentCare, true);
                stage.Begin(OSChapter04PresentationBridge.WastelandWalk, false);
                Redraw();
            }
        }
        using (new EditorGUILayout.HorizontalScope())
        {
            EditorGUILayout.LabelField("", GUILayout.Width(50));
            foreach (string id in SceneStates)
                if (GUILayout.Button("读档 " + id)) BeginScene(id, true);
        }
        using (new EditorGUILayout.HorizontalScope())
        {
            EditorGUILayout.LabelField("第 5-6 节", GUILayout.Width(50));
            foreach (string id in LaterStates)
                if (GUILayout.Button(id)) BeginScene(id, false);
            foreach (string id in LaterStates)
                if (GUILayout.Button("读档 " + id)) BeginScene(id, true);
        }
        using (new EditorGUILayout.HorizontalScope())
        {
            EditorGUILayout.LabelField("第 7 节", GUILayout.Width(50));
            foreach (string id in GraveStates)
                if (GUILayout.Button(id)) BeginScene(id, false);
            foreach (string id in GraveStates)
                if (GUILayout.Button("读档 " + id)) BeginScene(id, true);
        }
        using (new EditorGUILayout.HorizontalScope())
        {
            EditorGUILayout.LabelField("情绪档", GUILayout.Width(50));
            for (int tier = 0; tier <= OSChapter04TheaterStage.GriefMaxTier; tier++)
            {
                if (GUILayout.Button("档 " + tier)) { stage.SetGriefTier(tier, false); Redraw(); }
                if (GUILayout.Button("读档 档 " + tier)) { stage.SetGriefTier(tier, true); Redraw(); }
            }
            EditorGUILayout.LabelField("当前 " + stage.GriefTier + " / " + stage.GriefAngle + "°", GUILayout.Width(90));
        }
        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button(paused ? "继续" : "暂停")) paused = !paused;
            if (GUILayout.Button("单步 1 帧")) { paused = true; stage.Step(); Redraw(); }
            zoom = EditorGUILayout.IntSlider("放大", zoom, 1, 4);
        }
        EditorGUILayout.LabelField(string.Format("阶段 {0}  帧 {1}  桥接状态 {2}{3}  纸偶 {4} x {5:0}  门中线 {6}",
            stage.CurrentPhase, stage.PhaseFrame, stage.StateId, stage.IsHalted ? "（已停）" : "", stage.PuppetArt,
            stage.PuppetX, stage.DoorCenter) + (stage.TidyActivity.Length > 0 ? "  循环 " + stage.TidyActivity : "")
            + (stage.HasQueuedRequest ? "  排队 " + stage.QueuedState : ""));
        if (boundaries.Count > 0) EditorGUILayout.LabelField("边界：" + string.Join("  →  ", boundaries));

        Rect area = GUILayoutUtility.GetRect(OSChapter04TheaterStage.Width * zoom, OSChapter04TheaterStage.Height * zoom,
            GUILayout.ExpandWidth(false), GUILayout.ExpandHeight(false));
        EditorGUI.DrawRect(area, Color.black);
        GUI.DrawTexture(area, texture, ScaleMode.StretchToFill, false);

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Play Mode：驱动 TV 里的剧场视图", EditorStyles.boldLabel);
        OSChapter04TheaterView view = FindView();
        using (new EditorGUI.DisabledScope(view == null))
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("CityWalk 首次立起")) view.PreviewBegin(OSChapter04PresentationBridge.CityWalk, false);
                if (GUILayout.Button("CityArrive 全流程（门锁）"))
                {
                    view.DoorMode = OSChapter04TheaterStage.DoorMode.Locked;
                    view.PreviewBegin(OSChapter04PresentationBridge.CityArrive, false);
                }
                if (GUILayout.Button("读档 LabRummage")) view.PreviewBegin(OSChapter04PresentationBridge.LabRummage, true);
                if (GUILayout.Button("结束预览")) view.PreviewStop();
            }
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("第7句 CityArrive→等门口")) view.PreviewArriveToDoor();
                if (GUILayout.Button("第9句 进门")) view.GotoEnterDoor();
                if (GUILayout.Button("读档 门口等待")) view.PreviewRestoreAtDoor();
                if (GUILayout.Button("第10句 LabTidy")) view.PreviewTidy();
                if (GUILayout.Button("读档 LabTidy")) view.PreviewTidy(true);
            }
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField("第 2-3 节", GUILayout.Width(50));
                foreach (string id in SceneStates)
                    if (GUILayout.Button(id)) view.PreviewScene(id, false);
                if (GUILayout.Button("TentCare→WastelandWalk")) view.PreviewTentToWasteland();
            }
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField("", GUILayout.Width(50));
                foreach (string id in SceneStates)
                    if (GUILayout.Button("读档 " + id)) view.PreviewScene(id, true);
            }
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField("第 5-6 节", GUILayout.Width(50));
                foreach (string id in LaterStates)
                    if (GUILayout.Button(id)) view.PreviewScene(id, false);
                foreach (string id in LaterStates)
                    if (GUILayout.Button("读档 " + id)) view.PreviewScene(id, true);
            }
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField("第 7 节", GUILayout.Width(50));
                foreach (string id in GraveStates)
                    if (GUILayout.Button(id)) view.PreviewScene(id, false);
                for (int tier = 0; tier <= OSChapter04TheaterStage.GriefMaxTier; tier++)
                    if (GUILayout.Button("档 " + tier))
                    {
                        // The whole presentation (chat, screen, pointer, TV) when chapter 4 is running.
                        var grief = OSChapter04GriefPresentation.Ensure();
                        if (grief != null) grief.SetGriefTier(tier, false); else view.SetGriefTier(tier, false);
                    }
                if (GUILayout.Button("警告窗")) OSChapter04GriefPresentation.Ensure()?.ShowFailureWarning(false);
            }
            if (view != null)
                EditorGUILayout.LabelField(string.Format("TV.Screen {0} px，倍数 {1}，{2}，阶段 {3} 帧 {4}",
                    view.ScreenPixelSize, view.PixelScale, view.IsPreviewing ? "预览中" : "跟随桥接",
                    view.Stage.CurrentPhase, view.Stage.PhaseFrame));
            else EditorGUILayout.HelpBox("需要 Play Mode 且 TV 窗口已创建。", MessageType.None);
        }
        if (GUILayout.Button("导出关键帧到 " + DocsFolder)) Debug.Log(CaptureForDocs());
    }

    static OSChapter04TheaterView FindView()
    {
        if (!Application.isPlaying || OS_Handle.Instance == null) return null;
        OSTVWindowController tv = OS_Handle.Instance.TVController;
        return tv != null && tv.Screen != null ? tv.Screen.GetComponent<OSChapter04TheaterView>() : null;
    }

    // ================================================================ capture

    /// <summary>
    /// Plays the three previews headlessly and saves key frames (3x, PNG) plus a
    /// contact sheet. Also checks a restored LabRummage frame against the first
    /// LabRummage frame of the full chain. Returns a short report.
    /// </summary>
    public static string CaptureForDocs(int scale = 3)
    {
        Directory.CreateDirectory(DocsFolder);
        var report = new StringBuilder();
        var shots = new List<KeyValuePair<string, byte[]>>();
        var library = new OSChapter04TheaterLibrary();
        var events = new List<string>();
        var stage = new OSChapter04TheaterStage(library);
        int frame = 0;
        stage.Boundary = id => { events.Add(id + "@" + frame); return true; };

        void Shot(string name)
        {
            shots.Add(new KeyValuePair<string, byte[]>(name, (byte[])stage.DrawInk().Clone()));
        }
        void Run(int frames) { for (int i = 0; i < frames; i++) { stage.Step(); frame++; } }

        // 1. Fresh CityWalk: empty stage, back-to-front stand-up, walk.
        stage.Begin(OSChapter04PresentationBridge.CityWalk, false);
        Shot("01_citywalk_empty");
        Run(OSChapter04TheaterStage.EmptyHoldFrames + 8); Shot("02_citywalk_rise_far");
        Run(10); Shot("03_citywalk_rise_mid");
        while (stage.CurrentPhase == OSChapter04TheaterStage.Phase.Rise) Run(1);
        report.AppendLine("CityWalk 立起共 " + frame + " 帧 (" + (frame / 30f).ToString("0.00") + " s)");
        Shot("04_citywalk_standing");
        Run(9); Shot("05_citywalk_hop_air");
        Run(120); Shot("06_citywalk_walking");

        // 2. CityArrive chain from the walking city.
        frame = 0; events.Clear();
        stage.Begin(OSChapter04PresentationBridge.CityArrive, false);
        Run(150); Shot("07_arrive_house_in");
        while (stage.CurrentPhase == OSChapter04TheaterStage.Phase.Arrive && !stage.IsArrivalStopped) Run(1);
        int stopFrame = frame;
        Shot("08_arrive_stopped");
        while (stage.CurrentPhase == OSChapter04TheaterStage.Phase.Arrive) Run(1);
        Shot("09_doorlocked_start");
        Run(2); Shot("10_doorlocked_rattle");
        while (stage.CurrentPhase == OSChapter04TheaterStage.Phase.DoorLocked) Run(1);
        Run(5); Shot("11_fold_front");
        Run(9); Shot("12_fold_middle");
        Run(16); Shot("13_fold_flat_rummage_swap");
        while (stage.CurrentPhase == OSChapter04TheaterStage.Phase.Fold) Run(1);
        Run(6); Shot("14_lab_rise");
        Run(10); Shot("15_lab_rise_near");
        while (stage.CurrentPhase == OSChapter04TheaterStage.Phase.LabRise) Run(1);
        byte[] chainFirst = (byte[])stage.DrawInk().Clone();
        Shot("16_labrummage_first");
        Run(12); Shot("17_labrummage_sink");
        Run(72); Shot("18_labrummage_lean");
        int rummageFrame = stage.PhaseFrame;
        report.AppendLine("CityArrive 链：停住 @" + stopFrame + "，边界 " + string.Join(", ", events)
            + "，house x " + stage.HouseX + "，门中线 " + stage.DoorCenter);

        // 3. Restore LabRummage must match the chain's first loop frame.
        var restore = new OSChapter04TheaterStage(library);
        restore.Begin(OSChapter04PresentationBridge.LabRummage, true);
        byte[] restored = restore.DrawInk();
        int diff = 0;
        for (int i = 0; i < restored.Length; i++) if (restored[i] != chainFirst[i]) diff++;
        report.AppendLine("读档 LabRummage 与首次播放首帧差异像素：" + diff);

        // The walk can be at any phase when CityArrive lands: the lab must not care.
        var phaseReport = new List<string>();
        for (int walked = 0; walked < 10; walked++)
        {
            var run = new OSChapter04TheaterStage(library);
            run.Begin(OSChapter04PresentationBridge.CityWalk, true);
            for (int i = 0; i < 37 + walked * 7; i++) run.Step();
            run.Begin(OSChapter04PresentationBridge.CityArrive, false);
            float doorX = -1f; bool moved = false; int door = 0;
            while (run.CurrentPhase != OSChapter04TheaterStage.Phase.Rummage)
            {
                run.Step();
                if (run.CurrentPhase == OSChapter04TheaterStage.Phase.DoorLocked && doorX < 0f) { doorX = run.PuppetX; door = run.DoorCenter; }
                if (doorX >= 0f && !Mathf.Approximately(run.PuppetX, doorX)) moved = true;
            }
            int runDiff = 0;
            byte[] ink = run.DrawInk();
            for (int i = 0; i < ink.Length; i++) if (ink[i] != restored[i]) runDiff++;
            phaseReport.Add("门" + door + "/纸偶" + doorX + (moved ? "/移动!" : "") + "/差" + runDiff);
        }
        report.AppendLine("不同走路相位进场：" + string.Join("  ", phaseReport));

        // 3b. Script v1 (Enter mode): line 7 CityArrive waits at the door, line 9 walks in.
        var entry = new OSChapter04TheaterStage(library) { Mode = OSChapter04TheaterStage.DoorMode.Enter };
        var entryEvents = new List<string>();
        int entryFrame = 0;
        entry.Boundary = id => { entryEvents.Add(id + "@" + entryFrame); return true; };
        void RunEntry(int frames) { for (int i = 0; i < frames; i++) { entry.Step(); entryFrame++; } }
        void ShotEntry(string name) => shots.Add(new KeyValuePair<string, byte[]>(name, (byte[])entry.DrawInk().Clone()));
        entry.Begin(OSChapter04PresentationBridge.CityWalk, true);
        RunEntry(40);
        entry.Begin(OSChapter04PresentationBridge.CityArrive, false);
        while (entry.CurrentPhase == OSChapter04TheaterStage.Phase.Arrive) RunEntry(1);
        int atDoorFrame = entryFrame;
        RunEntry(58); ShotEntry("32_v1_atdoor_wait");
        RunEntry(600);
        report.AppendLine("台本 v1：门口等待从第 " + atDoorFrame + " 帧开始，再等 600 帧仍在 " + entry.CurrentPhase
            + "，期间边界 " + (entryEvents.Count == 0 ? "无" : string.Join(",", entryEvents)));
        entry.GotoEnterDoor();
        RunEntry(3); ShotEntry("33_v1_door_opening");
        RunEntry(12); ShotEntry("34_v1_step_in_behind_frame");
        RunEntry(16); ShotEntry("35_v1_second_step");
        while (entry.CurrentPhase == OSChapter04TheaterStage.Phase.EnterDoor && entry.DoorPanelWidth < 30) RunEntry(1);
        ShotEntry("36_v1_door_closed");
        while (entry.CurrentPhase == OSChapter04TheaterStage.Phase.EnterDoor) RunEntry(1);
        RunEntry(14); ShotEntry("37_v1_fold");
        while (entry.CurrentPhase == OSChapter04TheaterStage.Phase.Fold) RunEntry(1);
        RunEntry(16); ShotEntry("38_v1_lab_rise_with_puppet");
        while (entry.CurrentPhase == OSChapter04TheaterStage.Phase.LabRise) RunEntry(1);
        byte[] entryFirst = entry.DrawInk();
        int entryDiff = 0;
        for (int i = 0; i < entryFirst.Length; i++) if (entryFirst[i] != restored[i]) entryDiff++;
        report.AppendLine("台本 v1：进门后边界 " + string.Join(", ", entryEvents) + "；LabRummage 首帧与读档差异像素：" + entryDiff);
        var atDoorRestore = new OSChapter04TheaterStage(library) { Mode = OSChapter04TheaterStage.DoorMode.Enter };
        atDoorRestore.BeginAtDoor();
        shots.Add(new KeyValuePair<string, byte[]>("39_v1_restore_atdoor", (byte[])atDoorRestore.DrawInk().Clone()));

        // 4. Line-9 tidied lab (layout preview only, no bridge state).
        stage.BeginTidyPreview();
        Run(10); Shot("19_labtidy_fold");
        while (stage.CurrentPhase == OSChapter04TheaterStage.Phase.TidyFold) Run(1);
        Run(8); Shot("20_labtidy_rise");
        while (stage.CurrentPhase == OSChapter04TheaterStage.Phase.TidyRise) Run(1);
        Run(30); Shot("21_labtidy_hop_to_bench");
        string Until(OSChapter04TheaterStage.TidyMode mode, int frame)
        {
            int guard = 0;
            while ((stage.CurrentTidy.mode != mode || stage.CurrentTidy.frame != frame) && guard++ < 4000) Run(1);
            return mode + "@x" + stage.CurrentTidy.x;
        }
        var loop = new List<string>();
        loop.Add(Until(OSChapter04TheaterStage.TidyMode.Work, 2)); Shot("22_labtidy_work_rock");
        loop.Add(Until(OSChapter04TheaterStage.TidyMode.Walk, 50)); Shot("23_labtidy_walk_left");
        loop.Add(Until(OSChapter04TheaterStage.TidyMode.SitUp, 9)); Shot("24_labtidy_hop_up_stool");
        loop.Add(Until(OSChapter04TheaterStage.TidyMode.Sit, 20)); Shot("25_labtidy_sit_cup");
        loop.Add(Until(OSChapter04TheaterStage.TidyMode.Sit, 57)); Shot("26_labtidy_sit_sip");
        loop.Add(Until(OSChapter04TheaterStage.TidyMode.SitDown, 9)); Shot("27_labtidy_hop_down");
        loop.Add(Until(OSChapter04TheaterStage.TidyMode.Walk, 50)); Shot("28_labtidy_walk_right");
        loop.Add(Until(OSChapter04TheaterStage.TidyMode.Work, 0));
        report.AppendLine("LabTidy 从 LabRummage 第 " + rummageFrame + " 帧开始；循环：" + string.Join(" → ", loop));
        var tidyRestore = new OSChapter04TheaterStage(library);
        tidyRestore.BeginTidyPreview(true);
        report.AppendLine("读档 LabTidy 首帧：" + tidyRestore.CurrentTidy.mode + " " + tidyRestore.CurrentTidy.frame
            + " @x" + tidyRestore.CurrentTidy.x);
        shots.Add(new KeyValuePair<string, byte[]>("29_restore_labtidy", (byte[])tidyRestore.DrawInk().Clone()));
        shots.Add(new KeyValuePair<string, byte[]>("30_restore_labrummage", (byte[])restored.Clone()));
        restore.Begin(OSChapter04PresentationBridge.CityWalk, true);
        shots.Add(new KeyValuePair<string, byte[]>("31_restore_citywalk", (byte[])restore.DrawInk().Clone()));

        // 5. Sections 2-3 (round 6): medicine, then the three scene changes.
        var scenes = new OSChapter04TheaterStage(library);
        void RunScene(int frames) { for (int i = 0; i < frames; i++) scenes.Step(); }
        void ShotScene(string name) => shots.Add(new KeyValuePair<string, byte[]>(name, (byte[])scenes.DrawInk().Clone()));
        int SceneUntil(OSChapter04TheaterStage.Phase phase)
        {
            int n = 0;
            while (scenes.CurrentPhase != phase && n < 3000) { scenes.Step(); n++; }
            return n;
        }
        scenes.BeginTidyPreview(true);
        RunScene(200);
        scenes.Begin(OSChapter04PresentationBridge.MedicineReady, false);
        int held = 0;
        while (!scenes.IsHoldingVial && held < 600) { scenes.Step(); held++; }
        RunScene(20); ShotScene("40_medicine_hold_vial");
        scenes.Begin(OSChapter04PresentationBridge.WastelandWalk, false);
        RunScene(14); ShotScene("41_lab_fold_out_mid");
        int toWaste = SceneUntil(OSChapter04TheaterStage.Phase.Scene);
        RunScene(60); ShotScene("42_wasteland_walk");
        scenes.Begin(OSChapter04PresentationBridge.SettlementWait, false);
        int toSettle = SceneUntil(OSChapter04TheaterStage.Phase.Scene);
        RunScene(30); ShotScene("43_settlement_wait");
        scenes.Begin(OSChapter04PresentationBridge.TentCare, false);
        int toTent = SceneUntil(OSChapter04TheaterStage.Phase.Scene);
        RunScene(20); ShotScene("44_tent_care");
        scenes.Begin(OSChapter04PresentationBridge.WastelandWalk, false);
        SceneUntil(OSChapter04TheaterStage.Phase.SceneIn);
        RunScene(10); ShotScene("45_tent_to_wasteland_rise_mid");
        report.AppendLine("第 2-3 节：药瓶 " + held + " 帧举起；换景 Lab→Wasteland " + toWaste + "、→Settlement " + toSettle
            + "、→Tent " + toTent + " 帧");

        // 6. Round 7: lines 27, 36, 43, played on from the walk above.
        SceneUntil(OSChapter04TheaterStage.Phase.Scene);
        scenes.Begin(OSChapter04PresentationBridge.NorthSettlement, false);
        int toNorth = SceneUntil(OSChapter04TheaterStage.Phase.Scene);
        RunScene(40); ShotScene("46_north_settlement");
        scenes.Begin(OSChapter04PresentationBridge.LabNight, false);
        int toNight = SceneUntil(OSChapter04TheaterStage.Phase.Scene);
        RunScene(12); ShotScene("47_labnight_darkening_mid");
        report.AppendLine("第 5-6 节：LabNight 遮罩中途档位 " + scenes.OverlayLevel);
        RunScene(60); ShotScene("48_labnight");
        scenes.Begin(OSChapter04PresentationBridge.FlowerWalk, false);
        int toFlower = SceneUntil(OSChapter04TheaterStage.Phase.Scene);
        RunScene(60); ShotScene("49_flowerwalk_first_play");
        var flowerRestore = new OSChapter04TheaterStage(library);
        flowerRestore.Begin(OSChapter04PresentationBridge.FlowerWalk, true);
        for (int i = 0; i < 60; i++) flowerRestore.Step();
        shots.Add(new KeyValuePair<string, byte[]>("50_flowerwalk_restore_spire", (byte[])flowerRestore.DrawInk().Clone()));
        report.AppendLine("第 5-6 节：换景 →North " + toNorth + "、→LabNight " + toNight + "、→FlowerWalk " + toFlower + " 帧");

        // 7. Round 8: the rain, the grave, laying the flowers down, the grief tiers on the TV.
        scenes.Begin(OSChapter04TheaterStage.FlowerWalkRain, false);
        RunScene(45); ShotScene("51_flowerwalk_rain");
        scenes.Begin(OSChapter04PresentationBridge.Grave, false);
        int toGrave = SceneUntil(OSChapter04TheaterStage.Phase.Scene);
        RunScene(40); ShotScene("52_grave");
        scenes.Begin(OSChapter04TheaterStage.GravePlaceFlowers, false);
        RunScene(28); ShotScene("53_grave_place_flowers_mid");
        int placing = 28;
        while (!scenes.FlowersPlaced && placing < 400) { scenes.Step(); placing++; }
        RunScene(20); ShotScene("54_grave_flowers_placed");
        scenes.Begin(OSChapter04PresentationBridge.GraveBreakdown, false);
        for (int tier = 1; tier <= 3; tier++)
        {
            scenes.SetGriefTier(tier, false);
            RunScene(tier == 3 ? 150 : 60);
            ShotScene("5" + (4 + tier) + "_grief" + tier + "_tv");
        }
        report.AppendLine("第 8 轮：→Grave " + toGrave + " 帧，放花 " + placing + " 帧，档 3 折角 " + scenes.GriefAngle + "°");

        shots.Sort((a, b) => string.CompareOrdinal(a.Key, b.Key));
        foreach (KeyValuePair<string, byte[]> shot in shots)
            File.WriteAllBytes(Path.Combine(DocsFolder, shot.Key + ".png"), Encode(shot.Value, scale));
        File.WriteAllBytes(Path.Combine(DocsFolder, "00_contact_sheet.png"), Sheet(shots, 4));
        if (library.Missing.Count > 0) report.AppendLine("缺少纸片（灰块占位）：" + string.Join(", ", library.Missing));
        report.AppendLine("已写入 " + shots.Count + " 张到 " + DocsFolder);
        return report.ToString();
    }

    static readonly Color32 Gray = new Color32(0x87, 0x82, 0x7E, 255), Black = new Color32(0, 0, 0, 255);

    static byte[] Encode(byte[] ink, int scale)
    {
        int w = OSChapter04TheaterStage.Width, h = OSChapter04TheaterStage.Height;
        var tex = new Texture2D(w * scale, h * scale, TextureFormat.RGBA32, false);
        var data = new Color32[w * scale * h * scale];
        for (int y = 0; y < h * scale; y++)
            for (int x = 0; x < w * scale; x++)
                data[(h * scale - 1 - y) * w * scale + x] = ink[(y / scale) * w + x / scale] == OSChapter04TheaterPiece.Gray ? Gray : Black;
        tex.SetPixels32(data);
        byte[] png = tex.EncodeToPNG();
        DestroyImmediate(tex);
        return png;
    }

    /// <summary>1x frames in a grid, 4 px dark-blue gutters, so the sheet reads as separate shots.</summary>
    static byte[] Sheet(List<KeyValuePair<string, byte[]>> shots, int columns)
    {
        int w = OSChapter04TheaterStage.Width, h = OSChapter04TheaterStage.Height, gap = 4;
        int rows = (shots.Count + columns - 1) / columns;
        int sw = columns * (w + gap) + gap, sh = rows * (h + gap) + gap;
        var data = new Color32[sw * sh];
        var gutter = new Color32(30, 40, 60, 255);
        for (int i = 0; i < data.Length; i++) data[i] = gutter;
        for (int s = 0; s < shots.Count; s++)
        {
            int ox = gap + (s % columns) * (w + gap), oy = gap + (s / columns) * (h + gap);
            byte[] ink = shots[s].Value;
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                    data[(sh - 1 - (oy + y)) * sw + ox + x] = ink[y * w + x] == OSChapter04TheaterPiece.Gray ? Gray : Black;
        }
        var tex = new Texture2D(sw, sh, TextureFormat.RGBA32, false);
        tex.SetPixels32(data);
        byte[] png = tex.EncodeToPNG();
        DestroyImmediate(tex);
        return png;
    }
}
