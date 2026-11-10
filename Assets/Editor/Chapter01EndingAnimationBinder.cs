using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

// Keeps Chapter01Ending.asset's animation frames and per-frame timing in step
// with the authored Aseprite file. Re-saving the .aseprite rebinds automatically.
static class Chapter01EndingAnimationBinder
{
    const string DataPath = "Assets/Data/Chapter01Ending.asset";
    internal const string SourcePath = "Assets/Art/Chapter01_End/Chapter01_End_Animation.aseprite";

    [InitializeOnLoadMethod]
    static void ScheduleBind() => EditorApplication.delayCall += () => Bind(false);

    [MenuItem("Tools/MrTGame/第一章结尾/绑定结尾动画帧")]
    static void BindFromMenu() => Bind(true);

    internal static void Bind(bool verbose)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        var data = AssetDatabase.LoadAssetAtPath<OSChapter01EndingData>(DataPath);
        var assets = AssetDatabase.LoadAllAssetsAtPath(SourcePath);
        if (data == null || assets == null || assets.Length == 0)
        {
            if (verbose) Debug.LogWarning("[Chapter 01 Ending] Missing " + DataPath + " or " + SourcePath);
            return;
        }

        var frames = new List<Sprite>();
        var seconds = new List<float>();
        ReadTimeline(assets.OfType<AnimationClip>().FirstOrDefault(), frames, seconds);
        if (frames.Count == 0)
        {
            frames.AddRange(assets.OfType<Sprite>().OrderBy(FrameNumber));
            seconds.Clear();
        }
        if (frames.Count == 0)
        {
            if (verbose) Debug.LogWarning("[Chapter 01 Ending] No sprites were imported from " + SourcePath);
            return;
        }

        var currentSeconds = data.animationFrameSeconds ?? new float[0];
        if (data.animationFrames != null && data.animationFrames.SequenceEqual(frames)
            && currentSeconds.Length == seconds.Count
            && currentSeconds.Zip(seconds, (a, b) => Mathf.Abs(a - b) < .0005f).All(same => same))
        {
            if (verbose) Debug.Log("[Chapter 01 Ending] Animation frames are already up to date (" + frames.Count + ").");
            return;
        }
        Undo.RecordObject(data, "Bind Chapter 01 ending animation");
        data.animationFrames = frames.ToArray();
        data.animationFrameSeconds = seconds.ToArray();
        EditorUtility.SetDirty(data);
        AssetDatabase.SaveAssetIfDirty(data);
        Debug.Log("[Chapter 01 Ending] Bound " + frames.Count + " animation frames from " + SourcePath + ".");
    }

    // The importer's clip carries the Aseprite frame order and durations.
    static void ReadTimeline(AnimationClip clip, List<Sprite> frames, List<float> seconds)
    {
        if (clip == null) return;
        foreach (var binding in AnimationUtility.GetObjectReferenceCurveBindings(clip))
        {
            if (binding.propertyName != "m_Sprite") continue;
            var keys = AnimationUtility.GetObjectReferenceCurve(clip, binding);
            for (int i = 0; i < keys.Length; i++)
            {
                var sprite = keys[i].value as Sprite;
                float next = i + 1 < keys.Length ? keys[i + 1].time : clip.length;
                float duration = next - keys[i].time;
                if (sprite == null || duration <= .0001f) continue;
                // A trailing hold key repeats the last sprite; fold it into that frame.
                if (frames.Count > 0 && frames[frames.Count - 1] == sprite) seconds[seconds.Count - 1] += duration;
                else { frames.Add(sprite); seconds.Add(duration); }
            }
            return;
        }
    }

    static int FrameNumber(Sprite sprite)
    {
        var match = Regex.Match(sprite.name, @"(\d+)$");
        return match.Success ? int.Parse(match.Groups[1].Value) : int.MaxValue;
    }
}

sealed class Chapter01EndingAnimationWatcher : AssetPostprocessor
{
    static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom)
    {
        if (imported.Contains(Chapter01EndingAnimationBinder.SourcePath))
            EditorApplication.delayCall += () => Chapter01EndingAnimationBinder.Bind(false);
    }
}
