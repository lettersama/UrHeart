using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;
using UnityEngine.UI;

// Synchronous Edit Mode checks using disposable objects and a private font atlas.
// No active scene objects, player saves, language settings or source assets are edited.
public static class OSChapter03YardZoomQA
{
    const BindingFlags Instance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    const BindingFlags Static = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
    static readonly List<string> checks = new List<string>(), errors = new List<string>();
    static readonly List<object> metrics = new List<object>();

    public static string Run()
    {
        if (Application.isPlaying) return "Run OSChapter03YardZoomQA.Run() in Edit Mode only.";
        checks.Clear(); errors.Clear(); metrics.Clear();
        Case("zoom crop, input and restoration", Zoom);
        Case("white strip timing and layout", Thoughts);
        Case("scramble character classes", Scramble);
        string json = Newtonsoft.Json.JsonConvert.SerializeObject(new {
            status = errors.Count == 0 ? "PASS" : "FAIL", passed = checks.Count, failed = errors.Count,
            checks, errors, metrics,
            note = "EditMode disposable objects only; actual English/German text with a private copy of the authored font metrics."
        }, Newtonsoft.Json.Formatting.Indented);
        string path = Path.GetFullPath(Path.Combine("Temp", "OSChapter03YardZoomQA"));
        Directory.CreateDirectory(path); File.WriteAllText(Path.Combine(path, "results.json"), json);
        return json;
    }

    static void Case(string name, Action work)
    { try { work(); } catch (Exception error) { errors.Add(name + ": " + error); } }
    static void Check(bool value, string description)
    { if (value) checks.Add(description); else errors.Add(description); }
    static object Get(object target, string field) => target.GetType().GetField(field, Instance).GetValue(target);
    static void Set(object target, string field, object value) => target.GetType().GetField(field, Instance).SetValue(target, value);
    static object Call(object target, string method, params object[] args)
        => target.GetType().GetMethod(method, Instance).Invoke(target, args);
    static bool Near(float a, float b) => Mathf.Abs(a - b) < .0001f;

    static void Zoom()
    {
        var go = new GameObject("Chapter03YardZoomQA_View", typeof(RectTransform));
        go.hideFlags = HideFlags.HideAndDontSave; go.SetActive(false);
        var rect = (RectTransform)go.transform;
        rect.sizeDelta = new Vector2(240f, 160f);
        var raw = go.AddComponent<RawImage>(); var view = go.AddComponent<OSHomeView>();
        Set(view, "image", raw);
        Set(view, "room", new OSHomeRoom { id = "qa", floor = new RectInt(0, 0, 15, 10) });
        Set(view, "ink", new byte[240 * 160]);
        try
        {
            SetPlayer(view, 7, 4);
            view.SetZoom(false, true);
            Check(raw.uvRect == new Rect(0, 0, 1, 1) && !view.Zoomed, "Instant normal view restores full UV rectangle");
            Check((bool)Call(view, "AcceptsInput"), "Normal view accepts map input in an isolated fixture");
            CheckTile(view, rect, new Vector2(-112f, 72f), new Vector2Int(0, 0), "Normal top-left tile mapping");
            CheckTile(view, rect, new Vector2(112f, -72f), new Vector2Int(14, 9), "Normal bottom-right tile mapping");

            view.SetZoom(true);
            Check(view.Zoomed && !(bool)Call(view, "AcceptsInput"), "Zoom request blocks interaction before crop changes");
            Check(Near(raw.uvRect.width, 1f), "Zoom request starts with the full map until its blink midpoint");
            int swaps = 0;
            for (int frame = 0; frame < 8; frame++)
            {
                var ink = (byte[])Get(view, "ink"); Array.Fill(ink, (byte)1);
                float before = raw.uvRect.width;
                Set(view, "dt", .05f); Call(view, "TickZoom");
                if (!Near(before, raw.uvRect.width))
                {
                    swaps++;
                    Check(ink.All(x => x == 0), "Zoom midpoint crop swap is completely black even when a frame crosses .16 seconds");
                }
                Check(Near(raw.uvRect.width, 1f) || Near(raw.uvRect.width, .5f), "Zoom never uses fractional intermediate scale at frame " + frame);
            }
            Check(swaps == 1 && view.Zoomed && Near(raw.uvRect.width, .5f) && Near(raw.uvRect.height, .5f),
                "Blink completes with exactly one transition to a 2x crop");
            Check((float)Get(view, "zoomBlink") < 0f, "Completed zoom has no outstanding blink timer");
            Check(!(bool)Call(view, "AcceptsInput"), "Zoomed scene blocks map clicks and hover routing");
            CheckTile(view, rect, Vector2.zero, new Vector2Int(7, 4), "Zoomed center maps to the player tile");

            foreach (var tile in new[] { new Vector2Int(0, 0), new Vector2Int(14, 0), new Vector2Int(0, 9),
                new Vector2Int(14, 9), new Vector2Int(7, 4) })
            {
                SetPlayer(view, tile.x, tile.y); view.SetZoom(true, true);
                Rect uv = raw.uvRect;
                float left = uv.x * 240, top = (1 - uv.y - uv.height) * 160;
                Vector2 player = new Vector2(tile.x * 16 + 8, tile.y * 16 + 8);
                Check(Near(uv.width, .5f) && Near(uv.height, .5f) && uv.xMin >= 0f && uv.xMax <= 1f
                    && uv.yMin >= 0f && uv.yMax <= 1f, "2x crop remains inside texture at " + tile);
                Check(player.x >= left && player.x < left + 120 && player.y >= top && player.y < top + 80,
                    "Crop retains the player at " + tile);
                Check(Near(left, Mathf.Round(left)) && Near(top, Mathf.Round(top)), "Crop tracks only whole source pixels at " + tile);
            }
            SetPlayer(view, 0, 0); view.SetZoom(true, true);
            Check(raw.uvRect == new Rect(0f, .5f, .5f, .5f), "Top-left crop stops at both map edges");
            CheckTile(view, rect, new Vector2(-104f, 64f), new Vector2Int(0, 0), "Zoomed top-left edge maps through cropped UVs");
            SetPlayer(view, 14, 9); view.SetZoom(true, true);
            Check(raw.uvRect == new Rect(.5f, 0f, .5f, .5f), "Bottom-right crop stops at both map edges");

            view.SetZoom(false); Set(view, "dt", .05f); Call(view, "TickZoom");
            Check(view.Zoomed && !(bool)Call(view, "AcceptsInput"), "Zoom-out keeps input locked while close crop remains shown");
            view.SetZoom(false, true);
            Check(!view.Zoomed && raw.uvRect == new Rect(0, 0, 1, 1) && (float)Get(view, "zoomBlink") < 0f,
                "Instant normal restore cancels a running zoom-out blink");
            view.SetZoom(true); Set(view, "dt", .05f); Call(view, "TickZoom");
            // The production reopen path calls Load before reapplying the saved yard flags.
            view.Load(null);
            Check(!view.Zoomed && raw.uvRect == new Rect(0, 0, 1, 1) && (float)Get(view, "zoomBlink") < 0f,
                "Rebuilding the view clears a partially started zoom and its blink");
            view.SetZoom(true, true);
            Check(view.Zoomed && Near(raw.uvRect.width, .5f) && (float)Get(view, "zoomBlink") < 0f,
                "Reapplying saved close-up state is immediate without an old blink");
        }
        finally
        {
            var layout = Get(view, "runtime") as UnityEngine.Object;
            Set(view, "runtime", null); Set(view, "texture", null);
            if (layout != null) UnityEngine.Object.DestroyImmediate(layout);
            UnityEngine.Object.DestroyImmediate(go);
        }
    }

    static void SetPlayer(OSHomeView view, int x, int y)
    { var tile = new Vector2Int(x, y); Set(view, "me", tile); Set(view, "meFrom", tile); Set(view, "stepClock", -1f); }
    static void CheckTile(OSHomeView view, RectTransform rect, Vector2 local, Vector2Int expected, string title)
    {
        Vector2 screen = RectTransformUtility.WorldToScreenPoint(null, rect.TransformPoint(local));
        object[] args = { screen, null, new Vector2Int() };
        bool found = (bool)Call(view, "ToTile", args);
        Check(found && (Vector2Int)args[2] == expected, title + " => " + args[2]);
    }

    static void Thoughts()
    {
        var source = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(
            AssetDatabase.GUIDToAssetPath("d4da0986c38787a439e68a0d2d93415d"));
        if (source == null || source.sourceFontFile == null) throw new InvalidOperationException("Authored memory font source is missing.");
        // Fresh font/atlas: metrics come from the actual authored face, with no source-atlas writes.
        var font = TMP_FontAsset.CreateFontAsset(source.sourceFontFile, Mathf.RoundToInt(source.faceInfo.pointSize),
            source.atlasPadding, source.atlasRenderMode, 1024, 1024, AtlasPopulationMode.Dynamic, true);
        font.faceInfo = source.faceInfo; font.hideFlags = HideFlags.HideAndDontSave;
        font.fallbackFontAssetTable = new List<TMP_FontAsset>();
        var go = new GameObject("Chapter03YardZoomQA_Canvas", typeof(RectTransform), typeof(Canvas));
        go.hideFlags = HideFlags.HideAndDontSave;
        var host = (RectTransform)go.transform; host.sizeDelta = new Vector2(640f, 360f);
        var mapGo = new GameObject("Map", typeof(RectTransform)); mapGo.hideFlags = HideFlags.HideAndDontSave;
        var map = (RectTransform)mapGo.transform; map.SetParent(host, false); map.sizeDelta = new Vector2(240f, 160f);
        var presentation = go.AddComponent<OSChapter03StoryPresentation>();
        try
        {
            presentation.Initialize(null, host, font);
            presentation.PlayThoughts(new[] { "First line", "Second line" }, 2.2f, null, map);
            var root = (RectTransform)Get(presentation, "thoughtRoot");
            var strip = (Image)Get(presentation, "thoughtStrip");
            var label = (TMP_Text)Get(presentation, "thoughtText");
            Check(root.parent == map && root.anchoredPosition == Vector2.zero, "White strip is centered inside the home map");
            Check(Near(root.sizeDelta.x, 240f) && Near(root.sizeDelta.y, 26f), "White strip occupies the authored map width and 26-pixel band");
            Check(Near(strip.rectTransform.sizeDelta.x, 0f) && Near(label.alpha, 0f), "Strip opens before any thought becomes visible");
            Check(label.color.r == 0 && label.color.g == 0 && label.color.b == 0 && label.richText,
                "White strip uses black base text and permits authored red spans");
            Check(!strip.raycastTarget && !label.raycastTarget && !root.GetComponent<CanvasGroup>().blocksRaycasts,
                "Thought strip never intercepts mouse input");
            Call(presentation, "TickThoughts", .14f);
            Check(strip.rectTransform.sizeDelta.x > 0f && strip.rectTransform.sizeDelta.x < 240f
                && Near(label.alpha, 0f), "Opening half-beat expands the strip while keeping text hidden");
            Check(Near(strip.rectTransform.sizeDelta.x % 2f, 0f), "Strip opening width advances on two-pixel boundaries");
            Call(presentation, "TickThoughts", .15f);
            Check(Near(strip.rectTransform.sizeDelta.x, 240f) && (int)Get(presentation, "thoughtIndex") == 0,
                "First thought begins only after the strip has fully opened");
            Set(presentation, "thoughtClock", 4.4f); Call(presentation, "TickThoughts", .14f);
            Check(Near(label.alpha, 0f) && strip.rectTransform.sizeDelta.x > 0f && strip.rectTransform.sizeDelta.x < 240f,
                "Thought text disappears before the closing strip animation");

            var localization = AssetDatabase.LoadAssetAtPath<LocalizationData_SO>("Assets/Data/LocalizationData.asset");
            foreach (var language in new[] { LanguageState.en, LanguageState.de })
                for (int i = 1; i <= 5; i++)
                {
                    string key = "os.chapter03.ui.resolve." + i.ToString("00");
                    if (!localization.TryGetText(key, language, out string text)) throw new InvalidOperationException("Missing " + language + " " + key);
                    Set(presentation, "thoughtLines", new[] { text });
                    Set(presentation, "thoughtClock", .2f); Set(presentation, "thoughtIndex", -1);
                    Call(presentation, "TickThoughts", 0f);
                    label.maxVisibleCharacters = int.MaxValue;
                    Vector2 size = label.rectTransform.rect.size;
                    Vector2 preferred = label.GetPreferredValues(label.text, size.x, float.PositiveInfinity);
                    metrics.Add(new { language = language.ToString(), key, text, font = source.name,
                        fontSize = label.fontSize, availableWidth = size.x, availableHeight = size.y,
                        preferredWidth = preferred.x, preferredHeight = preferred.y });
                    Check(preferred.x <= size.x + .1f && preferred.y <= size.y + .1f,
                        language + " " + key + " fits white strip: " + preferred + " <= " + size);
                }
        }
        finally
        {
            // Production cleanup uses delayed Destroy; strip it before immediate Edit Mode cleanup.
            Set(presentation, "thoughtRoot", null); Set(presentation, "thoughtText", null);
            Set(presentation, "thoughtStrip", null); Set(presentation, "thoughtGroup", null);
            ((IList)Get(presentation, "ownedRoots")).Clear(); ((IList)Get(presentation, "registeredGlows")).Clear();
            UnityEngine.Object.DestroyImmediate(go);
            foreach (var texture in font.atlasTextures) if (texture != null) UnityEngine.Object.DestroyImmediate(texture);
            if (font.material != null) UnityEngine.Object.DestroyImmediate(font.material);
            UnityEngine.Object.DestroyImmediate(font);
        }
    }

    static int Kind(char value) => (int)typeof(OSChapter03KeywordScramble).GetMethod("Kind", Static).Invoke(null, new object[] { value });
    static void Scramble()
    {
        foreach (char c in " ，。！？…\nぁァー々゠゛゜")
            Check(Kind(c) == 0, "Scramble preserves spacing/line-break-sensitive mark U+" + ((int)c).ToString("X4"));
        foreach (char c in "中漢あア한가") Check(Kind(c) == 1, "Wide glyph stays in wide scramble class: " + c);
        foreach (char c in "Ab3éЖ") Check(Kind(c) == 2, "Narrow letter/digit stays in narrow scramble class: " + c);
        var scramble = new OSChapter03KeywordScramble();
        string result = scramble.Prepare("abcXYZ 中漢あ한，ぁー。 <b>DEF</b><k>钥匙</k>", 123,
            c => Kind(c) == 1 ? 16f : Kind(c) == 2 ? 8f : 4f);
        Check(scramble.Active && result.Contains("钥匙</color>") && !result.Contains("<k>"),
            "Keywords stay readable and change only their markup to the OS red span");
        Check(result.Contains("，ぁー。") && result.Contains("<b>") && result.Contains("</b>"),
            "Scramble preserves punctuation, small kana and unrelated rich-text tags");
        string plain = System.Text.RegularExpressions.Regex.Replace(result, "<[^>]+>", "");
        Check(plain.Take(6).All(c => Kind(c) == 2) && plain.Skip(7).Take(4).All(c => Kind(c) == 1),
            "Prepared output never exchanges narrow letters and full-width glyphs");
        scramble.Clear(); Check(!scramble.Active, "Clearing scramble removes active churn state");
    }
}
