#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Read-only inspection of the real player cursor and active paper.
/// Open a chapter 3 diary in Play Mode, then call Run(). No UI, save, cursor
/// position, scene, or presentation setting is changed by this check.</summary>
public static class OSPlayerCursorLayerQA
{
    public static string Run()
    {
        var checks = new List<object>();
        var failures = new List<string>();
        void Check(bool passed, string description)
        {
            checks.Add(new { passed, description });
            if (!passed) failures.Add(description);
        }

        var cursor = CursorHandle.Instance;
        var os = OS_Handle.Instance;
        var player = cursor != null ? cursor.GetComponent<Canvas>() : null;
        var icon = cursor != null ? cursor.transform.Find("CursorIcon")?.GetComponent<Image>() : null;
        var paper = os?.Chapter03Story?.Presentation?.PaperRoot;
        var modal = EffectiveCanvas(paper);
        var live = os?.CharacterWindowController != null ? os.CharacterWindowController.transform : null;
        var red = live != null && live.parent != null ? live.parent.Find("CharacterPermanentRedPointer") : null;
        var redCanvas = EffectiveCanvas(red);
        Canvas night = null;
        var fade = AdditionHandle.Instance?.OSFadeTransition?.GetComponent<Canvas>();
        if (os != null)
            foreach (var canvas in os.GetComponentsInChildren<Canvas>(true))
            {
                if (canvas.name == "Chapter02Nightfall" && canvas.isActiveAndEnabled) night = canvas;
            }

        Check(Application.isPlaying, "inspection runs in Play Mode");
        Check(cursor != null && cursor.isActiveAndEnabled, "real CursorHandle is enabled");
        Check(player != null && player.isActiveAndEnabled, "player cursor Canvas is enabled");
        Check(icon != null && icon.isActiveAndEnabled && icon.sprite != null
            && icon.color.a > 0f && icon.canvasRenderer.GetInheritedAlpha() > 0f,
            "player cursor image has visible content");
        Check(icon != null && !icon.raycastTarget, "player cursor cannot intercept paper input");
        Check(os != null && os.IsOSVisible, "OS is visible");
        Check(paper != null && paper.gameObject.activeInHierarchy && modal != null,
            "a real paper or unfolded letter is open");
        Check(player != null && player.sortingOrder >= 1500, "player cursor uses the foreground sorting tier");
        Check(player != null && modal != null && SharesDrawPass(player, modal),
            "player cursor and paper share the expected camera draw pass");
        Check(IsAbove(player, modal), "player cursor draws above the paper modal");
        if (night != null) Check(IsAbove(player, night), "player cursor draws above the night mask");
        if (red != null && red.gameObject.activeInHierarchy)
            Check(IsAbove(player, redCanvas), "player cursor draws above the red character pointer");
        Check(fade != null, "authored OS fade resolves from AdditionCanvas");
        Check(IsAbove(fade, player), "authored OS fade renders above the player cursor");

        bool pointerInsidePaper = paper != null && cursor != null
            && RectTransformUtility.RectangleContainsScreenPoint(paper, cursor.DisplayedScreenPosition,
                EventCamera(paper));
        return JsonConvert.SerializeObject(new
        {
            status = failures.Count == 0 ? "PASS" : "FAIL",
            checks, failures, readOnly = true, pointerInsidePaper,
            player = Describe(player), paper = Describe(modal), night = Describe(night),
            redPointer = Describe(redCanvas), osFade = Describe(fade),
            displayedPointer = cursor != null ? cursor.DisplayedScreenPosition.ToString() : null
        }, Formatting.Indented);
    }

    static Canvas EffectiveCanvas(Transform target)
    {
        if (target == null) return null;
        foreach (var canvas in target.GetComponentsInParent<Canvas>(true))
            if (canvas.isActiveAndEnabled && (canvas.isRootCanvas || canvas.overrideSorting)) return canvas;
        return null;
    }

    static bool SharesDrawPass(Canvas a, Canvas b)
    {
        if (a == null || b == null) return false;
        var ar = RenderRoot(a); var br = RenderRoot(b);
        return ar.targetDisplay == br.targetDisplay && ar.renderMode == br.renderMode && (ar.renderMode == RenderMode.ScreenSpaceOverlay
            || ar.worldCamera == br.worldCamera);
    }

    static bool IsAbove(Canvas a, Canvas b)
    {
        if (a == null || b == null) return false;
        var ar = RenderRoot(a); var br = RenderRoot(b);
        if (ar.targetDisplay != br.targetDisplay) return false;
        if (ar.renderMode == RenderMode.ScreenSpaceOverlay && br.renderMode != RenderMode.ScreenSpaceOverlay) return true;
        if (!SharesDrawPass(a, b)) return false;
        int layerA = SortingLayer.GetLayerValueFromID(a.sortingLayerID);
        int layerB = SortingLayer.GetLayerValueFromID(b.sortingLayerID);
        return layerA > layerB || layerA == layerB && a.sortingOrder > b.sortingOrder;
    }

    // Canvas.rootCanvas may report the disabled nested fade as its own root.
    // Walk the authored hierarchy so this also describes its next active frame.
    static Canvas RenderRoot(Canvas canvas)
    {
        var root = canvas;
        foreach (var parent in canvas.GetComponentsInParent<Canvas>(true)) root = parent;
        return root;
    }

    static Camera EventCamera(Transform target)
    {
        var canvas = target.GetComponentInParent<Canvas>();
        if (canvas != null) canvas = RenderRoot(canvas);
        return canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : null;
    }

    static object Describe(Canvas canvas) => canvas == null ? null : new
    {
        path = Path(canvas.transform), enabled = canvas.isActiveAndEnabled,
        order = canvas.sortingOrder, layer = canvas.sortingLayerName,
        overrideSorting = canvas.overrideSorting, mode = RenderRoot(canvas).renderMode.ToString(),
        camera = RenderRoot(canvas).worldCamera != null ? RenderRoot(canvas).worldCamera.name : null
    };

    static string Path(Transform target)
    {
        string path = target.name;
        while (target.parent != null) { target = target.parent; path = target.name + "/" + path; }
        return path;
    }
}
#endif
