using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using MrTGame.Core;
using MrTGame.Systems;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;

// Temporary audit (round 11 localization pass, step 1: look only). For each of
// the ten languages it drives the final treatment screen to seven moments with
// the real localized words, takes a screenshot of each into
// Docs/Chapter04Theater/l10n/, and records missing glyphs, string lengths,
// falling-letter counts and typing times. It changes no game data.
public static class OSChapter04FinalHealL10nAudit
{
    const string LegacyShotFolder = "Docs/Chapter04Theater/l10n";
    static string ShotFolder => layout ? OutputDirectory : LegacyShotFolder;
    const BindingFlags Members = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
    static readonly LanguageState[] Languages =
    {
        LanguageState.zh_CN, LanguageState.zh_TW, LanguageState.en, LanguageState.ja, LanguageState.ko,
        LanguageState.fr, LanguageState.de, LanguageState.es, LanguageState.pt_BR, LanguageState.ru
    };
    static readonly List<object> report = new List<object>();
    static readonly List<string> errors = new List<string>();
    static GameDataManager data;
    static OSChapter04FinalHealPresentation heal;
    static double music;
    static float clock;
    public static string Status { get; private set; } = "idle";
    static bool after;
    static bool layout;
    static bool previousHealEnabled = true, previousLaneSounds = true;
    static readonly List<object> layoutFindings = new List<object>();
    static readonly List<string> coverageFailures = new List<string>();
    public static string OutputDirectory { get; private set; }
    public static string ReportPath => string.IsNullOrEmpty(OutputDirectory) ? null : Path.Combine(OutputDirectory, "audit.json");
    /// <summary>Round 11k re-check: f and g in ko/en/de/ru (after_ prefix) and Latin/Cyrillic test strings.</summary>
    public static string StartAfter() { after = true; layout = false; return Begin(); }

    [UnityEditor.MenuItem("Tools/MrTGame/Chapter 4 Final Heal/Localization Audit (Play Mode)")]
    static void Menu() => Debug.Log("OSChapter04FinalHealL10nAudit: " + Start());

    public static string Start() { after = false; layout = false; return Begin(); }

    /// <summary>Current authored chart, typography, complete epilogue and staff; no placeholder fixtures.</summary>
    public static string StartLayout()
    {
        if (Status == "running") return Status;
        after = false; layout = true;
        OutputDirectory = Path.GetFullPath(Path.Combine("Tools", "Chapter04FinalHeal", "Validation",
            "Localization_1103", "runtime-" + DateTime.Now.ToString("yyyyMMdd-HHmmss-fff")));
        return Begin();
    }

    static string Begin()
    {
        var saves = GameAutosaveService.Instance;
        if (Status == "running") return Status;
        if (!Application.isPlaying || saves == null || GameDataManager.Instance == null || GameTest.Instance == null) return "Play Mode initialization required";
        if (saves.IsTestSession) return "Fresh Play Mode required to protect the original Editor save path";
        if (saves.IsBusy || saves.IsWriting || GameManager.Instance == null || GameManager.Instance.IsTransitioning
            || GameTest.Instance.IsCheckpointJumpRunning) return "Wait for active save/scene operation";
        report.Clear(); errors.Clear(); layoutFindings.Clear(); coverageFailures.Clear();
        heal = null;
        data = GameDataManager.Instance; Status = "running";
        data.StartCoroutine(Guarded());
        return Status;
    }

    static IEnumerator Guarded()
    {
        var saves = GameAutosaveService.Instance;
        var original = data.SaveData;
        var language = data.CurrentLanguageState;
        var setup = typeof(GameSetup).GetField("activeSetup", BindingFlags.Static | BindingFlags.NonPublic)?.GetValue(null) as GameSetup;
        bool demo = setup != null && (bool)setup.GetType().GetField("isDemo", Members).GetValue(setup);
        float volume = AudioListener.volume;
        var resources = GameResourceSystem.Instance;
        bool resourceEnabled = resources != null && resources.enabled;
        bool background = Application.runInBackground;
        var random = UnityEngine.Random.state;
        var previousMouse = Mouse.current;
        Mouse isolatedMouse = null;
        string[] protectedFiles = new[] { saves.SavePath, saves.SavePath + ".bak", saves.SavePath + ".tmp",
            "Assets/Data/LocalizationData.asset", "Assets/Resources/Chapter04/FinalHeal/chart.json" };
        var originalHashes = protectedFiles.Select(FileHash).ToArray();
        string isolated = layout ? Path.Combine(OutputDirectory, "isolated-saves")
            : Path.GetFullPath(Path.Combine("Temp", "OSChapter04FinalHealL10nAudit", Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(isolated);
        Application.logMessageReceived += Capture;
        saves.EnterTestSession(); saves.UseIsolatedStorageForTests(isolated); saves.EnterTestSession();
        data.ReplaceSaveData(JsonUtility.FromJson<GameSaveData>(JsonUtility.ToJson(original)));
        if (setup != null) setup.GetType().GetField("isDemo", Members).SetValue(setup, false);
        AudioListener.volume = 0f;
        if (layout)
        {
            Application.runInBackground = true;
            if (resources != null) resources.enabled = false;
            isolatedMouse = InputSystem.AddDevice<Mouse>("FinalHealLayoutAuditMouse");
            isolatedMouse.MakeCurrent();
        }
        Exception failure = null;
        var work = new Stack<IEnumerator>();
        work.Push(Run());
        while (work.Count > 0)
        {
            object next = null;
            try { if (!work.Peek().MoveNext()) { work.Pop(); continue; } next = work.Peek().Current; }
            catch (Exception error) { failure = error; }
            if (failure != null) break;
            if (next is IEnumerator nested) work.Push(nested); else yield return next;
        }
        try
        {
            if (heal != null) { heal.ClearAll(); heal.SetMusicClock(null); heal.SetPresentationClock(null); heal.enabled = previousHealEnabled; heal.LaneSoundsEnabled = previousLaneSounds; }
            if (GameTest.Instance?.IsCheckpointJumpRunning == true) GameTest.Instance.StopAllCoroutines();
            saves.EnterTestSession(); saves.EndSession(); GameManager.Instance?.OpenMainMenuWithoutSave();
            data.ReplaceSaveData(original); data.SetCurrentLanguage(language);
            if (setup != null) setup.GetType().GetField("isDemo", Members).SetValue(setup, demo);
            AudioListener.volume = volume;
            if (resources != null) resources.enabled = resourceEnabled;
            Application.runInBackground = background;
            UnityEngine.Random.state = random;
            if (isolatedMouse != null && isolatedMouse.added) InputSystem.RemoveDevice(isolatedMouse);
            if (previousMouse != null && previousMouse.added) previousMouse.MakeCurrent();
            if (!protectedFiles.Select(FileHash).SequenceEqual(originalHashes)) errors.Add("Protected save/content files changed");
        }
        catch (Exception error) { errors.Add("cleanup: " + error.Message); }
        Application.logMessageReceived -= Capture;
        if (failure != null) errors.Add("exception: " + failure);
        Status = errors.Count == 0 ? "DONE: " + Languages.Length + " languages" : "DONE with " + errors.Count + " errors";
        if (layout) Status += "; " + layoutFindings.Count + " layout findings; " + coverageFailures.Count + " coverage failures";
        Directory.CreateDirectory(ShotFolder);
        File.WriteAllText(Path.Combine(ShotFolder, "audit.json"), JsonConvert.SerializeObject(new {
            status = Status, time = DateTime.Now.ToString("s"), layout, errors, coverageFailures, layoutFindings, report,
            protectedFiles, originalHashes, finalHashes = protectedFiles.Select(FileHash).ToArray(),
            note = layout ? "Live production chart/localization/fonts. Virtual clocks, actual typography including heavy answers. Geometry outside the viewport during entry is not tested; frozen geometry findings may be authored composition and require screenshot review. All glyph boxes are measured fully revealed. Scrolling credits are tested against their own box and horizontal viewport only." : null
        }, Formatting.Indented));
    }

    static void Capture(string message, string stack, LogType type)
    {
        if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) errors.Add(message);
    }

    static IEnumerator Wait(Func<bool> done, float seconds)
    {
        float start = Time.unscaledTime;
        while (!done() && Time.unscaledTime - start < seconds) yield return null;
    }

    static IEnumerator Shot(string name)
    {
        heal.Render();
        yield return null;
        yield return new WaitForEndOfFrame();
        Directory.CreateDirectory(ShotFolder);
        string path = Path.Combine(ShotFolder, name + ".png");
        if (File.Exists(path)) File.Delete(path);
        ScreenCapture.CaptureScreenshot(path);
        yield return Wait(() => File.Exists(path) && new FileInfo(path).Length > 24, 10f);
    }

    /// <summary>Move both clocks by seconds, rendering every 1/30 s like the screen does.</summary>
    static IEnumerator Advance(double seconds)
    {
        for (double done = 0; done < seconds - 1e-6; done += 1d / 30d)
        {
            music += 1d / 30d; clock += 1f / 30f;
            heal.Render();
        }
        yield return null;
    }

    sealed class Note { public string id, phraseId, pairId, key, text; public int side, section, type; }

    static List<Note> ChartWords()
    {
        var asset = Resources.Load<TextAsset>("Chapter04/FinalHeal/chart");
        var json = JObject.Parse(asset.text);
        return json["notes"].Where(n => !string.IsNullOrEmpty((string)n["text"])).Select(n => new Note
        {
            id = (string)n["id"], phraseId = (string)n["phraseId"], pairId = (string)n["pairId"], key = (string)n["textKey"],
            text = (string)n["text"], side = (int)n["side"], section = (int)n["section"], type = (int)n["textType"]
        }).ToList();
    }

    static string Local(string key, string fallback) => data.GetLocalizedText(key, fallback) ?? fallback;

    static IEnumerator Run()
    {
        GameTest.Instance.JumpToCheckpoint(GameTestCheckpoint.Chapter04StoryStart);
        yield return Wait(() => !GameTest.Instance.IsCheckpointJumpRunning && !GameManager.Instance.IsTransitioning
            && OS_Handle.Instance != null && OS_Handle.Instance.LinkMessageController?.DisplayedTextLabel != null, 80f);
        yield return null;
        heal = OSChapter04FinalHealPresentation.Ensure();
        previousHealEnabled = heal.enabled; previousLaneSounds = heal.LaneSoundsEnabled;
        heal.SetMusicClock(() => music);
        heal.SetPresentationClock(() => clock);
        if (layout) { heal.enabled = false; yield return RunLayout(); yield break; }
        var words = ChartWords();
        var find = typeof(OSChapter04FinalHealPresentation).GetMethod("FindCharacter", Members);
        var measure = typeof(OSChapter04FinalHealPresentation).GetMethod("MeasureFlat", Members);
        clock = 1000f;
        var languages = after ? new[] { LanguageState.ko, LanguageState.en, LanguageState.de, LanguageState.ru } : Languages;
        foreach (var lang in languages)
        {
            data.SetCurrentLanguage(lang);
            yield return null; yield return null;
            string tag = (after ? "after_" : "") + lang.ToString();
            var template = OS_Handle.Instance.LinkMessageController.DisplayedTextLabel;
            var font = template.font;

            // ---- glyphs: the atlas path of the flying words (with TMP fallbacks) and TMP's own check
            var flyingMissing = new SortedSet<string>(); var fallbackUsed = new SortedSet<string>();
            foreach (var w in words.Where(w => w.section <= 2))
                foreach (char c in Local(w.key, w.text))
                {
                    if (char.IsWhiteSpace(c)) continue;
                    var args = new object[] { font, c, 0, null, null };
                    bool found = (bool)find.Invoke(null, args);
                    if (!found) flyingMissing.Add(c.ToString());
                    else if (args[4] is TMP_FontAsset owner && owner != font) fallbackUsed.Add(owner.name);
                }
            var tmpTexts = new List<string>();
            tmpTexts.AddRange(words.Where(w => w.section == 3).Select(w => Local(w.key, w.text)));
            for (int i = 1; i <= 6; i++) tmpTexts.Add(Local("chapter04.final_heal.epilogue.line_0" + i, ""));
            for (int i = 1; i <= 3; i++) tmpTexts.Add(Local("chapter04.final_heal.epilogue.choice_0" + i, ""));
            tmpTexts.Add(Local(OSChapter04FinalHealPresentation.PostcardHintKey, "点击结束游戏"));
            var tmpMissing = new SortedSet<string>();
            foreach (var text in tmpTexts)
                if (!string.IsNullOrEmpty(text) && !font.HasCharacters(text, out uint[] missing, true, true))
                    foreach (var c in missing) tmpMissing.Add(char.ConvertFromUtf32((int)c));
            // the labels already on screen keep the font they were made with: check that too
            var labelFont = heal.GetType().GetField("lineLabel", Members)?.GetValue(heal) as TMP_Text;

            // string lengths of the flying words, in font line units and in the far reach they take (14 px near end)
            var lengths = words.Where(w => w.section <= 2).GroupBy(w => w.phraseId).Select(g => g.First()).Select(w =>
            {
                string text = Local(w.key, w.text);
                float units = (float)measure.Invoke(heal, new object[] { text });
                float near = w.type == 2 ? 28f : 14f;
                return new { w.phraseId, text, chars = text.Length, units = Math.Round(units, 2), reachPx = Math.Round(near * units / .6931472f) };
            }).ToList();

            heal.ClearAll();
            music = 0d; clock += 10f;
            heal.Begin(true);
            heal.SetHeartStage(1, false);
            int fallingLetters = 0;
            var a5 = words.First(w => w.id == "s1_A5");
            if (!after) {

            // a) two strings in flight: A3 (left, hold) and A7 (right, hold)
            var a3 = words.First(w => w.id == "s1_A3"); var a7 = words.First(w => w.id == "s1_A7");
            heal.SpawnPhrase("a3", Local(a3.key, a3.text), a3.type, music + 3d, 3d, a3.side, 2d);
            heal.SpawnPhrase("a7", Local(a7.key, a7.text), a7.type, music + 3.4d, 3.4d, a7.side, 2d);
            yield return Advance(1.65d);
            yield return Shot(tag + "_a_flying");

            // b) A5 hit, its letters mid-fall
            int fallingBefore = heal.FallingLetters;
            heal.SpawnPhrase("a5", Local(a5.key, a5.text), a5.type, music + .6d, .6d, a5.side);
            yield return Advance(.6d);
            heal.ResolveHit("a5");
            fallingLetters = heal.FallingLetters - fallingBefore;
            yield return Advance(.25d);
            yield return Shot(tag + "_b_falling");

            // c) the second part frozen: its ten strings in the air
            heal.ClearAll(); music = 0d; clock += 10f; heal.Begin(true); heal.SetHeartStage(0, false);
            var frozen = words.Where(w => w.section == 2).ToList();
            float[] reached = { .45f, .7f, .55f, .8f, .4f, .65f, .5f, .75f, .6f, .58f };
            for (int i = 0; i < frozen.Count; i++)
            {
                double approach = 4d / reached[i % reached.Length];
                heal.SpawnPhrase(frozen[i].phraseId, Local(frozen[i].key, frozen[i].text), frozen[i].type, music + approach, approach, frozen[i].side);
            }
            yield return Advance(4d);
            heal.FreezeAll();
            yield return Advance(.2d);
            yield return Shot(tag + "_c_frozen");

            // d) one cut: C3 on A5, split beside the heart
            heal.ReleaseFreeze();
            var cuts = words.Where(w => w.section == 3).ToList();
            var c3 = cuts.First(w => w.id == "s3_C3");
            heal.BindAnswer(c3.phraseId, c3.pairId, Local(c3.key, c3.text), 0, music + .6d, .6d);
            yield return Advance(.6d);
            heal.ResolveHit(c3.phraseId);
            yield return Advance(.35d);
            yield return Shot(tag + "_d_cut");

            // e) the rest cut, then 「妈妈」 twice: the green lines all up (+3.0 s)
            foreach (var cut in cuts.Where(w => w.id != "s3_C3"))
            {
                heal.BindAnswer(cut.phraseId, cut.pairId, Local(cut.key, cut.text), 0, music + .6d, .6d);
                yield return Advance(.6d);
                heal.ResolveHit(cut.phraseId);
                yield return Advance(.2d);
            }
            var d2 = words.First(w => w.id == "s4_D2a");
            heal.SpawnPhrase("D2", Local(d2.key, d2.text), 0, music + 1.5d, 1.5d, d2.side);
            yield return Advance(1.5d);
            heal.ResolveMotherTap("D2", "D2", false);
            yield return Advance(.75d);
            heal.ResolveHit("D2");
            heal.SetHeartStage(3, true);
            yield return Advance(3.2d);
            yield return Shot(tag + "_e_green");
            }

            // f) the epilogue's longest line and longest reply; time the typing
            heal.ClearAll(); music = 0d; clock += 10f; heal.Begin(true);
            string line5 = Local("chapter04.final_heal.epilogue.line_05", "");
            string choice2 = Local("chapter04.final_heal.epilogue.choice_02", "");
            heal.PlayEpilogue(new[] { OSChapter04FinalHealPresentation.EpilogueStep.Line(line5, 0f),
                OSChapter04FinalHealPresentation.EpilogueStep.Choice(new[] { choice2 }) }, true);
            float typingFrom = clock;
            int guard = 0;
            while (!heal.IsEpilogueAwaitingChoice && guard++ < 30 * 60) { music += 1d / 30d; clock += 1f / 30f; heal.Render(); }
            float typingSeconds = clock - typingFrom;
            yield return null;
            yield return Shot(tag + "_f_dialogue");

            // g) the postcard and its hint
            heal.ChooseEpilogueOption(0);
            guard = 0;
            while (!heal.IsPostcardWaiting && guard++ < 30 * 10) { music += 1d / 30d; clock += 1f / 30f; heal.Render(); }
            yield return Advance(.2d);
            yield return Shot(tag + "_g_postcard");

            int dotRuns = System.Text.RegularExpressions.Regex.Matches(line5, @"(\.{2,}|…+|⋯+)").Count;
            report.Add(new
            {
                language = tag, font = font != null ? font.name : null, labelFont = labelFont != null ? labelFont.font.name : null,
                flyingMissing = flyingMissing.ToArray(), flyingFallbacks = fallbackUsed.ToArray(), tmpMissing = tmpMissing.ToArray(),
                lengths, fallingLettersForA5 = fallingLetters, a5 = Local(a5.key, a5.text),
                line05 = line5, line05Chars = line5.Length, line05TypingSeconds = Math.Round(typingSeconds, 2), line05DotRuns = dotRuns,
                choice02 = choice2, hint = Local(OSChapter04FinalHealPresentation.PostcardHintKey, "")
            });
        }
        if (after) yield return LatinPass();
        data.SetCurrentLanguage(LanguageState.zh_CN);
    }

    // Current-content layout audit. The older picture audit above is retained as a historical tool.
    static string FileHash(string path)
    {
        if (!File.Exists(path)) return "missing";
        using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(path))).Replace("-", "");
    }

    static object Read(object owner, string name)
    {
        var type = owner is Type t ? t : owner.GetType();
        return type.GetField(name, Members)?.GetValue(owner is Type ? null : owner)
            ?? type.GetProperty(name, Members)?.GetValue(owner is Type ? null : owner)
            ?? throw new MissingMemberException(type.Name, name);
    }

    static object Call(object owner, string name, params object[] arguments)
    {
        var type = owner is Type t ? t : owner.GetType();
        var method = type.GetMethods(Members).Single(m => m.Name == name && m.GetParameters().Length == arguments.Length);
        return method.Invoke(owner is Type ? null : owner, arguments);
    }

    sealed class AuditRect
    {
        public float left, top, right, bottom;
        public bool empty = true;
        public void Add(Vector2 point)
        {
            if (empty) { left = right = point.x; top = bottom = point.y; empty = false; }
            else { left = Mathf.Min(left, point.x); right = Mathf.Max(right, point.x); top = Mathf.Min(top, point.y); bottom = Mathf.Max(bottom, point.y); }
        }
        public bool OutsideScreen(bool scrolling = false) => !empty
            && (left < -1f || right > 641f || !scrolling && (top < -1f || bottom > 361f));
        public bool Contains(AuditRect inner) => inner.empty || !empty
            && inner.left >= left - 1f && inner.right <= right + 1f && inner.top >= top - 1f && inner.bottom <= bottom + 1f;
        public bool Overlaps(AuditRect other) => !empty && !other.empty
            && Mathf.Min(right, other.right) - Mathf.Max(left, other.left) > 1f
            && Mathf.Min(bottom, other.bottom) - Mathf.Max(top, other.top) > 1f;
    }

    sealed class LabelMeasure
    {
        public string name, key, text, font;
        public float fontSize, preferredWidth, preferredHeight;
        public int visibleCharacters, totalCharacters, lineCount;
        public bool active, boxOutsideScreen, inkOutsideScreen, inkOutsideBox;
        public AuditRect box, ink;
        public string[] missingGlyphs;
    }

    static Vector2 OsPoint(TMP_Text label, Vector3 local)
    {
        var root = (RectTransform)Read(heal, "textRoot");
        var point = root.InverseTransformPoint(label.rectTransform.TransformPoint(local));
        return new Vector2(point.x + 320f, 180f - point.y);
    }

    static LabelMeasure MeasureLabel(TMP_Text label, string key, string scope, bool scrolling = false)
    {
        if (label == null) throw new InvalidOperationException("Missing TMP label: " + key);
        int visible = label.maxVisibleCharacters;
        label.maxVisibleCharacters = int.MaxValue;
        label.ForceMeshUpdate(true, true);
        var box = new AuditRect(); var ink = new AuditRect();
        var rect = label.rectTransform.rect;
        box.Add(OsPoint(label, new Vector3(rect.xMin, rect.yMin, 0f)));
        box.Add(OsPoint(label, new Vector3(rect.xMax, rect.yMax, 0f)));
        for (int i = 0; i < label.textInfo.characterCount; i++)
        {
            var character = label.textInfo.characterInfo[i];
            if (!character.isVisible) continue;
            ink.Add(OsPoint(label, character.bottomLeft));
            ink.Add(OsPoint(label, character.topRight));
        }
        uint[] missing = Array.Empty<uint>();
        if (label.font != null) label.font.HasCharacters(label.text, out missing, true, true);
        var result = new LabelMeasure {
            name = label.name, key = key, text = label.text, font = label.font != null ? label.font.name : null,
            fontSize = label.fontSize, preferredWidth = label.preferredWidth, preferredHeight = label.preferredHeight,
            visibleCharacters = visible, totalCharacters = label.textInfo.characterCount, lineCount = label.textInfo.lineCount,
            active = label.gameObject.activeInHierarchy, box = box, ink = ink,
            boxOutsideScreen = box.OutsideScreen(scrolling), inkOutsideScreen = ink.OutsideScreen(scrolling),
            inkOutsideBox = !box.Contains(ink), missingGlyphs = (missing ?? Array.Empty<uint>()).Select(c => char.ConvertFromUtf32((int)c)).ToArray()
        };
        label.maxVisibleCharacters = visible; label.ForceMeshUpdate(true, true);
        if (result.boxOutsideScreen || result.inkOutsideScreen || result.inkOutsideBox || result.missingGlyphs.Length > 0)
            layoutFindings.Add(new { language = data.CurrentLanguageState.ToString(), scope, key, label = result });
        return result;
    }

    static object[] Overlaps(IList<LabelMeasure> labels, bool ink)
    {
        var result = new List<object>();
        for (int i = 0; i < labels.Count; i++) for (int j = i + 1; j < labels.Count; j++)
        {
            bool overlaps = ink ? labels[i].ink.Overlaps(labels[j].ink) : labels[i].box.Overlaps(labels[j].box);
            if (overlaps) result.Add(new { a = labels[i].key, b = labels[j].key });
        }
        return result.ToArray();
    }

    static object MeasurePhrase(OSChapter04FinalHealContent.Note note, string stage, float? distanceOverride = null)
    {
        var phrase = Call(heal, "FindPhrase", note.VisualId);
        if (phrase == null) throw new InvalidOperationException("Missing phrase " + note.VisualId);
        float distance = distanceOverride ?? (float)Read(phrase, "frozenDistance");
        var direction = (Vector2)Read(phrase, "dir");
        var near = new Vector2(OSChapter04FinalHealPresentation.HeartCenterX, OSChapter04FinalHealPresentation.HeartCenterY) + direction * distance;
        float height = (float)Read(phrase, "Near"), ratio = (float)Read(phrase, "ratio"), length = (float)Read(phrase, "length");
        bool crawl = (bool)Read(phrase, "Crawl"), words = (bool)Read(phrase, "wordMode");
        float reach = words && !crawl ? (float)Read(phrase, "wordReach") : height * (ratio - 1f) * length / Mathf.Log(ratio);
        float farHalf = height * ratio * .5f;
        var bounds = new AuditRect();
        if (!crawl)
        {
            var axis = note.side == OSChapter04FinalHealPresentation.SideRight ? direction : -direction;
            var up = new Vector2(axis.y, -axis.x);
            var far = near + direction * reach;
            float nearHalf = words ? farHalf : height * .5f;
            bounds.Add(near + up * nearHalf); bounds.Add(near - up * nearHalf);
            bounds.Add(far + up * farHalf); bounds.Add(far - up * farHalf);
        }
        else
        {
            float half = length * farHalf, rows = (1f + ratio) * .5f * height;
            bool down = note.side != OSChapter04FinalHealPresentation.SideTop;
            bounds.Add(new Vector2(near.x - half, down ? near.y : near.y - rows));
            bounds.Add(new Vector2(near.x + half, down ? near.y + rows : near.y));
        }
        var result = new {
            note.id, note.phraseId, note.textKey, text = Local(note.textKey, note.text), note.section, note.side,
            textType = (int)note.TextBeatType, stage, distance, directionX = direction.x, directionY = direction.y,
            nearHeight = height, ratio, units = length, reach, crawl, wordMode = words, rawBounds = bounds,
            rawGeometryOutsideScreen = bounds.OutsideScreen(), rasterPixels = heal.PhrasePixels(note.VisualId)
        };
        if (bounds.OutsideScreen()) layoutFindings.Add(new {
            language = data.CurrentLanguageState.ToString(), scope = "flying-" + stage, key = note.textKey,
            observation = result,
            note = stage == "frozen" ? "Frozen composition can intentionally extend beyond the viewport; review screenshot before changing it." : "Unclamped envelope at arrival; this is not an entry animation sample."
        });
        return result;
    }

    static void Tick(float seconds = 1f / 30f)
    {
        clock += seconds; music += seconds; heal.Render();
    }

    static void JumpMusic(double target)
    {
        clock += (float)Math.Max(0d, target - music); music = target; heal.Render();
    }

    static void ResetLayout()
    {
        heal.ClearAll(); clock += 20f; music = 0d;
        heal.SetPresentationClock(() => clock); heal.SetMusicClock(() => music);
        heal.Begin(true); heal.LaneSoundsEnabled = false;
    }

    static bool LayoutShots => data.CurrentLanguageState == LanguageState.en || data.CurrentLanguageState == LanguageState.ru;

    static IEnumerator RunLayout()
    {
        var resource = Resources.Load<TextAsset>(OSChapter04FinalHealContent.ResourcePath);
        if (resource == null) throw new InvalidOperationException("Missing production chart");
        var score = OSChapter04FinalHealContent.FromJson(resource.text);
        if (!score.Available) throw new InvalidOperationException(string.Join("; ", score.Validate()));
        var keys = data.LocalizationData.Entries.Where(e => e.Key.StartsWith("chapter04.final_heal.", StringComparison.Ordinal)).ToArray();
        foreach (var entry in keys) foreach (var language in Languages)
            if (entry.Values.Count(v => v.Language == language && !string.IsNullOrWhiteSpace(v.Text)) != 1)
                coverageFailures.Add(entry.Key + ": " + language);
        foreach (var language in Languages)
        {
            data.SetCurrentLanguage(language); yield return null; yield return null;
            string tag = language.ToString();
            ResetLayout(); heal.SetHeartStage(1, false);
            var arrival = new List<object>(); var frozen = new List<object>(); var fitting = new List<object>();
            var freezeDistances = new Dictionary<string, float>();
            var font = OS_Handle.Instance.LinkMessageController.DisplayedTextLabel.font;
            var flyingMissing = new SortedSet<string>();
            foreach (var note in score.Notes.Where(n => n.HasText && n.section != 3))
                foreach (char c in Local(note.textKey, note.text))
                {
                    if (char.IsWhiteSpace(c)) continue;
                    var args = new object[] { font, c, 0, null, null };
                    if (!(bool)Call(typeof(OSChapter04FinalHealPresentation), "FindCharacter", args)) flyingMissing.Add(c.ToString());
                }
            if (flyingMissing.Count > 0) layoutFindings.Add(new { language = tag, scope = "flying-glyphs", missing = flyingMissing.ToArray() });
            // Preserve the actual side-slot sequence from section one into section two.
            foreach (var note in score.Notes.Where(n => n.section == 1 && n.HasText).OrderBy(n => n.SpawnTime))
            {
                heal.SpawnPhrase(note.VisualId, Local(note.textKey, note.text), (int)note.TextBeatType,
                    note.HitTime, note.ApproachDuration, note.side, note.HoldDuration);
                JumpMusic(note.HitTime);
                var phrase = Call(heal, "FindPhrase", note.VisualId);
                arrival.Add(MeasurePhrase(note, "arrival", (float)Read(phrase, "arriveDistance")));
                if (language == LanguageState.pt_BR && note.id == "s1_A10") yield return Shot(tag + "_arrival_A10");
                Call(heal, "RemovePhrase", note.VisualId);
            }
            foreach (var note in score.Notes.Where(n => n.IsFrozenTarget).OrderBy(n => n.SpawnTime))
            {
                heal.SpawnPhrase(note.VisualId, Local(note.textKey, note.text), (int)note.TextBeatType,
                    note.VisualHitTime, note.VisualApproachDuration, note.side);
                var phrase = Call(heal, "FindPhrase", note.VisualId);
                var lane = Call(heal, "FindNote", note.VisualId);
                double spawn = (double)Read(phrase, "hitTime") - (double)Read(phrase, "approach");
                double laneHit = (double)Read(lane, "hitTime");
                float height = (float)Read(phrase, "Near"), ratio = (float)Read(phrase, "ratio");
                var direction = (Vector2)Read(phrase, "dir");
                float originalDistance = (float)Call(phrase, "Distance", (double)score.FreezeTime);
                bool adjusted = heal.FitPhraseAtTime(note.VisualId, score.FreezeTime);
                double fittedSpawn = (double)Read(phrase, "hitTime") - (double)Read(phrase, "approach");
                bool unchanged = Math.Abs(fittedSpawn - spawn) < .0001d && (double)Read(lane, "hitTime") == laneHit
                    && (float)Read(phrase, "Near") == height && (float)Read(phrase, "ratio") == ratio
                    && (Vector2)Read(phrase, "dir") == direction;
                if (!unchanged) coverageFailures.Add(tag + ": fitting changed spawn/beat/size/direction for " + note.VisualId);
                float fittedDistance = (float)Call(phrase, "Distance", (double)score.FreezeTime);
                freezeDistances.Add(note.VisualId, fittedDistance);
                fitting.Add(new { note.id, adjusted, spawn, fittedSpawn, laneHit, originalDistance, fittedDistance,
                    appearanceAndTimingPreserved = unchanged });
            }
            JumpMusic(score.FreezeTime); heal.SetSinkProgress(1f); heal.SetHeartStage(0, false); heal.FreezeAll(); heal.Render();
            foreach (var note in score.Notes.Where(n => n.IsFrozenTarget))
            {
                var phrase = Call(heal, "FindPhrase", note.VisualId);
                if (Mathf.Abs((float)Read(phrase, "frozenDistance") - freezeDistances[note.VisualId]) > .001f)
                    coverageFailures.Add(tag + ": freezing jumped the phrase position for " + note.VisualId);
                frozen.Add(MeasurePhrase(note, "frozen"));
            }
            if (LayoutShots || language == LanguageState.de) yield return Shot(tag + "_frozen");

            heal.ReleaseFreeze(); heal.SetSinkProgress(0f);
            foreach (var note in score.Notes.Where(n => n.IsAnswer).OrderBy(n => n.SpawnTime))
            {
                JumpMusic(note.SpawnTime);
                heal.BindAnswer(note.VisualId, note.pairId, Local(note.textKey, note.text), (int)note.TextBeatType,
                    note.HitTime, note.ApproachDuration, 0d);
                JumpMusic(note.HitTime); heal.ResolveHit(note.VisualId); Tick(.1f);
            }
            var mother = score.Notes.First(n => n.IsFirstMother);
            heal.SpawnPhrase(mother.VisualId, Local(mother.textKey, mother.text), (int)mother.TextBeatType,
                mother.HitTime, mother.ApproachDuration, mother.side);
            heal.SpawnBeat(mother.id, OSChapter04FinalHealPresentation.TypeTap, mother.HitTime);
            JumpMusic(score.FirstMotherTime);
            if (!heal.ResolveMotherTap(mother.VisualId, mother.id, false)) throw new InvalidOperationException("First mother tap did not resolve");
            var lastMother = score.Notes.First(n => n.IsLastMother);
            heal.SpawnBeat(lastMother.id, OSChapter04FinalHealPresentation.TypeTap, lastMother.HitTime);
            JumpMusic(score.FinalNoteTime);
            if (!heal.ResolveMotherTap(mother.VisualId, lastMother.id, true)) throw new InvalidOperationException("Final mother tap did not resolve");
            heal.SetHeartStage(3, true);
            for (int i = 0; i < 147; i++) Tick(); // 4.9 seconds: just before the authored 5.25-second fade.
            var green = new List<LabelMeasure>();
            var answerMap = (IDictionary)Read(heal, "answerLabels");
            foreach (DictionaryEntry entry in answerMap)
            {
                string id = (string)Read(entry.Key, "id");
                var note = score.Notes.Single(n => n.IsAnswer && n.VisualId == id);
                green.Add(MeasureLabel((TMP_Text)entry.Value, note.textKey, "green"));
            }
            if (green.Count != score.Notes.Count(n => n.IsAnswer)) coverageFailures.Add(tag + ": expected all ten actual green labels, got " + green.Count);
            var greenBoxOverlaps = Overlaps(green, false); var greenInkOverlaps = Overlaps(green, true);
            if (greenBoxOverlaps.Length > 0 || greenInkOverlaps.Length > 0)
                layoutFindings.Add(new { language = tag, scope = "green-overlap", boxPairs = greenBoxOverlaps, inkPairs = greenInkOverlaps });
            if (LayoutShots) yield return Shot(tag + "_green");

            var liveFontSwitches = new List<object>();
            if (language == LanguageState.en)
            {
                var retainedAnswers = answerMap.Keys.Cast<object>().ToArray();
                foreach (var fontLanguage in new[] { LanguageState.ko, LanguageState.en })
                {
                    data.SetCurrentLanguage(fontLanguage); yield return null; yield return null;
                    heal.Render();
                    var currentMap = (IDictionary)Read(heal, "answerLabels");
                    bool sameObjects = currentMap.Count == retainedAnswers.Length
                        && currentMap.Keys.Cast<object>().All(answer => retainedAnswers.Any(original => ReferenceEquals(original, answer)));
                    var template = OS_Handle.Instance.LinkMessageController.DisplayedTextLabel;
                    bool fontMatches = currentMap.Values.Cast<TMP_Text>().All(label => label.font == template.font
                        && (template.fontSharedMaterial == null || label.fontSharedMaterial == template.fontSharedMaterial));
                    if (!sameObjects || !fontMatches) coverageFailures.Add("en live font switch to " + fontLanguage + ": answer identity/font mismatch");
                    var switched = new List<LabelMeasure>();
                    foreach (DictionaryEntry entry in currentMap)
                    {
                        string id = (string)Read(entry.Key, "id");
                        var note = score.Notes.Single(n => n.IsAnswer && n.VisualId == id);
                        switched.Add(MeasureLabel((TMP_Text)entry.Value, note.textKey, "green-live-font-switch"));
                    }
                    var boxPairs = Overlaps(switched, false); var inkPairs = Overlaps(switched, true);
                    if (boxPairs.Length > 0 || inkPairs.Length > 0)
                        layoutFindings.Add(new { language = fontLanguage.ToString(), scope = "green-live-font-switch-overlap", boxPairs, inkPairs });
                    liveFontSwitches.Add(new { sourceTextLanguage = "en", targetFontLanguage = fontLanguage.ToString(),
                        sameAnswerObjects = sameObjects, allFontsMatchLiveChat = fontMatches, labels = switched, boxPairs, inkPairs });
                    yield return Shot("en_existing_answers_font_" + fontLanguage);
                }
            }

            ResetLayout();
            var credits = score.Credits;
            bool chinese = language == LanguageState.zh_CN || language == LanguageState.zh_TW;
            var logoPath = chinese && !string.IsNullOrEmpty(credits.chineseLogoResource) ? credits.chineseLogoResource : credits.logoResource;
            heal.SetEndingCredits(Resources.Load<Texture2D>(logoPath), credits.staffKeys.Select((key, i) => Local(key, credits.staffFallback[i])).ToArray(),
                credits.studioName, credits.logoPixelSize);
            var script = (OSChapter04FinalHealPresentation.EpilogueStep[])Call(typeof(OSChapter04FinalHealDirector), "BuildEpilogueScript");
            heal.PlayEpilogue(script, true); heal.Render();
            var epilogue = new List<object>(); var seen = new HashSet<int>();
            for (int guard = 0; guard < 1000 && !heal.IsPostcardWaiting; guard++)
            {
                Tick(.1f);
                int step = heal.EpilogueStepIndex;
                if (heal.EpiloguePhaseIndex != 3 || (bool)Read(heal, "epTyping") || step < 0 || step >= script.Length || !seen.Add(step)) continue;
                var line = MeasureLabel((TMP_Text)Read(heal, "lineLabel"), "epilogue.step_" + step + ".line", "epilogue");
                var options = new List<LabelMeasure>();
                foreach (var label in (TMP_Text[])Read(heal, "optionLabels"))
                    if (label.gameObject.activeInHierarchy) options.Add(MeasureLabel(label, "epilogue.step_" + step + "." + label.name, "epilogue"));
                var labels = new List<LabelMeasure> { line }; labels.AddRange(options);
                var overlaps = Overlaps(labels, true);
                if (overlaps.Length > 0) layoutFindings.Add(new { language = tag, scope = "epilogue-overlap", step, pairs = overlaps });
                epilogue.Add(new { step, isChoice = script[step].IsChoice, line, options, overlapPairs = overlaps });
                if (LayoutShots && (step == 3 || step == 4 || step == 6 || step == 7)) yield return Shot(tag + "_epilogue_" + step);
                if (heal.IsEpilogueAwaitingChoice && !heal.ChooseEpilogueOption(0)) throw new InvalidOperationException("Could not choose actual epilogue reply");
            }
            if (seen.Count != script.Length) coverageFailures.Add(tag + ": captured " + seen.Count + " of " + script.Length + " epilogue steps");
            if (!heal.IsPostcardWaiting) throw new InvalidOperationException("Postcard did not appear for " + tag);
            var hint = MeasureLabel((TMP_Text)Read(heal, "hintLabel"), OSChapter04FinalHealPresentation.PostcardHintKey, "postcard");
            if (LayoutShots) yield return Shot(tag + "_postcard");
            heal.PlayPress(); Tick(.9f);
            Call(heal, "EnsureCreditLayout");
            float endingAt = (float)Read(heal, "endingAt"), scrollStart = (float)Read(heal, "ScrollStart");
            int logoHeight = (int)Read(heal, "LogoHeight"), studioHeight = (int)Read(heal, "StudioHeight");
            clock = endingAt + scrollStart + (388f + logoHeight + studioHeight) / 24f; heal.Render();
            var staff = new List<LabelMeasure>(); int block = 0;
            foreach (var label in (IEnumerable<TMP_Text>)Read(heal, "creditLabels"))
                staff.Add(MeasureLabel(label, credits.staffKeys[block++], "staff", true));
            if (staff.Count != credits.staffKeys.Length) coverageFailures.Add(tag + ": staff block count mismatch");
            if (LayoutShots) yield return Shot(tag + "_staff");
            clock = endingAt + scrollStart + (int)Read(heal, "ScrollTarget") / 24f + .05f; heal.Render();
            if (LayoutShots) yield return Shot(tag + "_staff_thanks");
            report.Add(new { language = tag, font = font.name, formalPrefixKeys = keys.Length, flyingMissing = flyingMissing.ToArray(),
                arrival, frozen, fitting, green, greenBoxOverlaps, greenInkOverlaps, liveFontSwitches, epilogue, hint, staff,
                observedEpilogueSteps = seen.Count, realEpilogueSteps = script.Length });
            File.WriteAllText(Path.Combine(OutputDirectory, "progress.json"), JsonConvert.SerializeObject(new {
                language = tag, completedLanguages = report.Count, findingCount = layoutFindings.Count, coverageFailures
            }, Formatting.Indented));
            yield return null;
        }
    }

    // Spaced-script test strings for the flying words (QA only; no translations exist yet)
    static IEnumerator LatinPass()
    {
        data.SetCurrentLanguage(LanguageState.en);
        yield return null; yield return null;
        heal.ClearAll(); music = 0d; clock += 10f; heal.Begin(true); heal.SetHeartStage(1, false);
        var tests = new[]
        {
            ("lost", "Lost it.", 0, OSChapter04FinalHealPresentation.SideRight, 3.0d),
            ("repl", "Just a replacement.", 0, OSChapter04FinalHealPresentation.SideLeft, 3.2d),
            ("mom", "I couldn't even remember Mom.", 1, OSChapter04FinalHealPresentation.SideLeft, 3.6d),
            ("ru", "Просто машина.", 2, OSChapter04FinalHealPresentation.SideRight, 3.4d),
            ("top", "Not worth it.", 0, OSChapter04FinalHealPresentation.SideTop, 3.1d),
        };
        foreach (var t in tests) heal.SpawnPhrase(t.Item1, t.Item2, t.Item3, music + t.Item5, t.Item5, t.Item4, t.Item3 == 1 ? 2d : 0d);
        yield return Advance(1.8d);
        yield return Shot("after_latin_flying");
        var heights = tests.Select(t => new { t.Item2, ends = heal.PhraseEndHeights(t.Item1).ToString(), pixels = heal.PhrasePixels(t.Item1) }).ToList();
        yield return Advance(1.2d);
        int before = heal.FallingLetters;
        heal.ResolveHit("repl");
        int pieces = heal.FallingLetters - before;
        int beforeRu = heal.FallingLetters;
        yield return Advance(.2d);
        heal.ResolveHit("ru");
        int piecesRu = heal.FallingLetters - beforeRu;
        yield return Advance(.12d);
        yield return Shot("after_latin_falling");
        report.Add(new { latin = heights, fallingPiecesForJustAReplacement = pieces, fallingPiecesForProstoMashina = piecesRu });
    }
}
