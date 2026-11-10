using System;
using System.Collections.Generic;
using System.Reflection;
using TMPro;
using UnityEngine;

// Opt-in geometry regression checks. Uses a disposable offscreen canvas and the
// project's loaded font; never changes Memory state or reads/writes player saves.
public static class OSMemoryTopFadeQA
{
    static readonly MethodInfo RefreshFade = typeof(OSMemoryTopFade).GetMethod("LateUpdate", BindingFlags.Instance | BindingFlags.NonPublic);
    static readonly MethodInfo AlignDither = typeof(OSDitherFill).GetMethod("LateUpdate", BindingFlags.Instance | BindingFlags.NonPublic);

    public static string RunGeometry()
    {
        var checks = new List<string>();
        var source = OS_Handle.Instance?.MemoryWindowController;
        TMP_FontAsset font = source != null && source.TextUnits.Count > 0 ? source.TextUnits[0].Label?.font : null;
        if (font == null) font = TMP_Settings.defaultFontAsset;
        if (font == null) throw new InvalidOperationException("Load the Memory font before running TopFade QA.");
        var root = new GameObject("MemoryTopFadeQA_Disposable", typeof(RectTransform), typeof(Canvas));
        root.hideFlags = HideFlags.DontSave;
        var canvas = root.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        var rootRect = (RectTransform)root.transform;
        rootRect.sizeDelta = new Vector2(640f, 360f);
        rootRect.position = new Vector3(100000f, 100000f, 0f);
        try
        {
            RectTransform mask = Rect("Mask", root.transform, new Vector2(200f, 100f));
            var clip = mask.gameObject.AddComponent<UnityEngine.UI.RectMask2D>();
            RectTransform pool = Rect("Pool", mask, new Vector2(200f, 400f));
            RectTransform textRect = Rect("Text", pool, new Vector2(200f, 400f));
            var label = textRect.gameObject.AddComponent<TextMeshProUGUI>();
            label.font = font;
            label.fontSize = 18f;
            label.lineSpacing = 15f;
            label.alignment = TextAlignmentOptions.TopLeft;
            label.textWrappingMode = TextWrappingModes.NoWrap;
            label.overflowMode = TextOverflowModes.Overflow;
            label.raycastTarget = false;
            label.text = "HHH\nHHH";
            label.ForceMeshUpdate();
            var fade = OSMemoryTopFade.Ensure(mask, clip, pool);
            Action refresh = () =>
            {
                RefreshFade.Invoke(fade, null);
                foreach (var fill in fade.GetComponentsInChildren<OSDitherFill>(true)) AlignDither.Invoke(fill, null);
            };
            Action<bool, string> check = (condition, message) =>
            {
                if (!condition) throw new InvalidOperationException("[Memory TopFade QA] " + message);
                checks.Add(message);
            };

            SetTop(mask, pool, label, 0, -4f);
            refresh();
            check(!fade.IsVisible, "a complete first line below the clip has no fade");
            SetTop(mask, pool, label, 0, 3f);
            Vector2 poolBefore = pool.anchoredPosition;
            refresh();
            check(fade.IsVisible && fade.FadeHeight > 0f, "a glyph line crossing the top clip enables the fade");
            check(pool.anchoredPosition == poolBefore, "fade does not move the manually scrolled text pool");
            CheckBands(fade, mask, clip, label, 0, check);
            float tallHeight = fade.FadeHeight;
            CheckGrid(fade, rootRect, check, "initial cut");

            Vector2 firstBounds = LineBounds(mask, label, 0);
            pool.anchoredPosition += Vector2.up * (mask.rect.yMax - 2.5f - firstBounds.x);
            refresh();
            check(fade.IsVisible && fade.FadeHeight < tallHeight && Mathf.Abs(fade.FadeHeight - 2.5f) < .08f,
                "a short cut fragment limits all three steps to that line");
            CheckBands(fade, mask, clip, label, 0, check);
            CheckGrid(fade, rootRect, check, "changed band height");

            firstBounds = LineBounds(mask, label, 0);
            Vector2 secondBounds = LineBounds(mask, label, 1);
            check(firstBounds.x > secondBounds.y, "fixture contains a real blank gap between lines");
            pool.anchoredPosition += Vector2.up * (mask.rect.yMax - (firstBounds.x + secondBounds.y) * .5f);
            refresh();
            check(!fade.IsVisible, "an offscreen old line above a fully visible next line has no fade");

            SetTop(mask, pool, label, 1, 3f);
            label.maxVisibleCharacters = label.textInfo.lineInfo[1].firstCharacterIndex;
            refresh();
            check(!fade.IsVisible, "an unrevealed typing line cannot enable the fade");
            label.maxVisibleCharacters = int.MaxValue;
            refresh();
            check(fade.IsVisible, "revealing the same cut line enables the fade");
            CheckBands(fade, mask, clip, label, 1, check);

            secondBounds = LineBounds(mask, label, 1);
            pool.anchoredPosition += Vector2.up * (mask.rect.yMax + 3f - secondBounds.x);
            refresh();
            check(!fade.IsVisible, "a text block entirely above the clip has no fade");
            label.text = string.Empty;
            refresh();
            check(!fade.IsVisible, "empty text has no fade");

            label.text = "HHH\nHHH";
            label.ForceMeshUpdate();
            clip.padding = new Vector4(7f, 8f, 11f, 13f);
            SetTop(mask, pool, label, 0, 3f - clip.padding.w);
            refresh();
            check(fade.IsVisible, "top mask padding is part of the actual clipping boundary");
            CheckBands(fade, mask, clip, label, 0, check);
            CheckGrid(fade, rootRect, check, "padded viewport");
            poolBefore = pool.anchoredPosition;
            mask.sizeDelta = new Vector2(175f, 120f);
            refresh();
            CheckBands(fade, mask, clip, label, 0, check);
            check(pool.anchoredPosition == poolBefore, "viewport resize leaves text positioning to Memory");
            clip.enabled = false;
            refresh();
            check(!fade.IsVisible, "a disabled clip cannot leave an overlay behind");
            return Newtonsoft.Json.JsonConvert.SerializeObject(new { status = "PASS", checks }, Newtonsoft.Json.Formatting.Indented);
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(root);
        }
    }

    public static string InspectCurrentWindow()
    {
        var memory = OS_Handle.Instance?.MemoryWindowController;
        if (memory == null) throw new InvalidOperationException("An active Memory window is required.");
        var fade = memory.GetComponentInChildren<OSMemoryTopFade>(true);
        if (fade == null) throw new InvalidOperationException("Memory has no TopFade component.");
        RefreshFade.Invoke(fade, null);
        var rect = (RectTransform)fade.transform;
        var mask = (RectTransform)rect.parent;
        var corners = new Vector3[4];
        rect.GetWorldCorners(corners);
        return Newtonsoft.Json.JsonConvert.SerializeObject(new
        {
            node = memory.CurrentData?.DataIndex, windowActive = memory.isActiveAndEnabled,
            visible = fade.IsVisible, height = fade.FadeHeight,
            localTop = mask.InverseTransformPoint(corners[1]).y,
            maskTop = mask.rect.yMax - mask.GetComponent<UnityEngine.UI.RectMask2D>().padding.w,
            bands = Array.ConvertAll(fade.GetComponentsInChildren<OSDitherFill>(true), fill => new
            {
                level = fill.Level, active = fill.gameObject.activeInHierarchy,
                raycast = fill.Image.raycastTarget,
                height = ((RectTransform)fill.transform).rect.height
            })
        }, Newtonsoft.Json.Formatting.Indented);
    }

    static RectTransform Rect(string name, Transform parent, Vector2 size)
    {
        var node = new GameObject(name, typeof(RectTransform));
        var rect = (RectTransform)node.transform;
        rect.SetParent(parent, false);
        rect.anchorMin = rect.anchorMax = new Vector2(.5f, 1f);
        rect.pivot = new Vector2(.5f, 1f);
        rect.sizeDelta = size;
        return rect;
    }

    static Vector2 LineBounds(RectTransform mask, TMP_Text label, int lineIndex)
    {
        float min = float.PositiveInfinity, max = float.NegativeInfinity;
        TMP_LineInfo line = label.textInfo.lineInfo[lineIndex];
        for (int c = line.firstCharacterIndex; c <= line.lastCharacterIndex; c++)
        {
            var glyph = label.textInfo.characterInfo[c];
            if (!glyph.isVisible) continue;
            min = Mathf.Min(min, mask.InverseTransformPoint(label.transform.TransformPoint(glyph.bottomLeft)).y);
            max = Mathf.Max(max, mask.InverseTransformPoint(label.transform.TransformPoint(glyph.topLeft)).y);
        }
        return new Vector2(min, max);
    }

    static void SetTop(RectTransform mask, RectTransform pool, TMP_Text label, int line, float aboveClip)
    {
        label.ForceMeshUpdate();
        float top = LineBounds(mask, label, line).y;
        pool.anchoredPosition += Vector2.up * (mask.rect.yMax + aboveClip - top);
    }

    static void CheckBands(OSMemoryTopFade fade, RectTransform mask, UnityEngine.UI.RectMask2D clip,
        TMP_Text label, int cutLine, Action<bool, string> check)
    {
        if (!fade.IsVisible) return;
        float clipTop = mask.rect.yMax - clip.padding.w;
        float lineBottom = LineBounds(mask, label, cutLine).x;
        var fills = fade.GetComponentsInChildren<OSDitherFill>();
        check(fills.Length == 3 && fills[0].Level == 12 && fills[1].Level == 8 && fills[2].Level == 4,
            "fade retains three black Bayer steps (12, 8, 4)");
        var corners = new Vector3[4];
        foreach (var fill in fills)
        {
            ((RectTransform)fill.transform).GetWorldCorners(corners);
            Vector3 bottom = mask.InverseTransformPoint(corners[0]);
            Vector3 top = mask.InverseTransformPoint(corners[2]);
            check(top.y <= clipTop + .08f && bottom.y >= lineBottom - .08f
                && bottom.x >= mask.rect.xMin + clip.padding.x - .08f
                && top.x <= mask.rect.xMax - clip.padding.z + .08f,
                fill.transform.parent.name + " stays below the title and inside only the cut line");
            check(!fill.Image.raycastTarget && fill.Image.maskable && fill.Image.color == Color.black,
                fill.transform.parent.name + " remains black, clipped, and input transparent");
        }
    }

    static void CheckGrid(OSMemoryTopFade fade, RectTransform root, Action<bool, string> check, string context)
    {
        var corners = new Vector3[4];
        foreach (var fill in fade.GetComponentsInChildren<OSDitherFill>())
        {
            ((RectTransform)fill.transform).GetWorldCorners(corners);
            Vector3 min = root.InverseTransformPoint(corners[0]);
            Vector3 max = root.InverseTransformPoint(corners[2]);
            UnityEngine.Rect uv = fill.Image.uvRect;
            check(Mathf.Abs(uv.xMin * 4f - (min.x - root.rect.xMin)) < .08f
                && Mathf.Abs(uv.yMin * 4f - (min.y - root.rect.yMin)) < .08f
                && Mathf.Abs(uv.width * 4f - (max.x - min.x)) < .08f
                && Mathf.Abs(uv.height * 4f - (max.y - min.y)) < .08f,
                context + ": " + fill.transform.parent.name + " keeps the shared four-pixel Bayer grid");
        }
    }
}
