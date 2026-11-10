using System;
using System.Collections.Generic;
using UnityEngine;

// Kept separate from the entry/upgrade protocol and from the Home renderer.
// Queue heads are committed only when their text, choice or presentation ends.
[Serializable]
public sealed class OSChapter03StoryState
{
    public int version;
    public int phase;
    public int clock;
    public int knowAnswer;
    public int coffeeStep;
    public bool homeOpened;
    public bool liveOffline;
    // Last presented Live entry (or begun portrait effect), never the next queued line.
    public string livePortraitCue;
    public List<string> flags = new List<string>();
    public List<string> papers = new List<string>();
    public List<string> items = new List<string>();
    public List<string> yardVisited = new List<string>();
    // Object + response stage, separate from the six-object yard objective.
    public List<string> inspectedThings = new List<string>();
    public int interactionVisualVersion;
    public List<Vector2Int> yardFootprints = new List<Vector2Int>();
    public List<OSChapter03StoryBeat> memoryQueue = new List<OSChapter03StoryBeat>();
    public List<OSChapter03StoryBeat> liveQueue = new List<OSChapter03StoryBeat>();
    public Vector2 liveHomePosition;
    public bool liveHomePositionSaved;
    public int letterPage;

    public float Progress
    {
        get
        {
            if (Has("letter_collected")) return 1f;
            if (Has("letter_started")) return .95f;
            if (Has("box_open")) return .9f;
            if (Has("bed_second_started")) return .84f;
            if (Has("lab_open")) return .74f + (papers.Contains("diary_13") ? .06f : 0f);
            if (Has("mirror_done")) return .71f;
            if (Has("mirror_visible")) return .64f;
            if (Has("bed_open")) return .6f;
            if (Has("yard_returned")) return .55f;
            if (Has("final_knock")) return .42f + Mathf.Min(6, yardVisited.Count) * .015f;
            if (Has("deduction_done")) return .4f;
            int clues = (Has("shoes_seen") ? 1 : 0) + (Has("morning_seen") ? 1 : 0)
                + (Has("medicine_seen") ? 1 : 0);
            return homeOpened ? .12f + clues * .085f : .02f;
        }
    }

    public bool Has(string flag) => flags != null && flags.Contains(flag);
    public bool Add(string flag)
    {
        EnsureData();
        if (string.IsNullOrEmpty(flag) || flags.Contains(flag)) return false;
        flags.Add(flag);
        return true;
    }

    public void EnsureData()
    {
        if (flags == null) flags = new List<string>();
        if (papers == null) papers = new List<string>();
        if (items == null) items = new List<string>();
        if (yardVisited == null) yardVisited = new List<string>();
        if (inspectedThings == null) inspectedThings = new List<string>();
        if (yardFootprints == null) yardFootprints = new List<Vector2Int>();
        if (memoryQueue == null) memoryQueue = new List<OSChapter03StoryBeat>();
        if (liveQueue == null) liveQueue = new List<OSChapter03StoryBeat>();
        memoryQueue.RemoveAll(b => b == null);
        liveQueue.RemoveAll(b => b == null);
        knowAnswer = Mathf.Clamp(knowAnswer, 0, 2);
        clock = Mathf.Clamp(clock, 0, 3);
        coffeeStep = Mathf.Clamp(coffeeStep, 0, 5);
        letterPage = Mathf.Max(0, letterPage);
    }
}

[Serializable]
public sealed class OSChapter03StoryBeat
{
    public string type;
    public string key;
    public string value;
    // Shared by every response to one object click; persists across save/restore.
    public string interactionSource;
    public int number;
    public float remaining;
    public bool begun;
    public bool complete;

    public OSChapter03StoryBeat(string kind, string content = null, string argument = null,
        float delay = 0f, int index = 0)
    { type = kind; key = content; value = argument; remaining = delay; number = index; }
}
