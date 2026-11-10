using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>Built from the checked-in chapter script; localized values stay in the game's normal table.</summary>
public sealed class OSChapter03StoryContent
{
    [Serializable] public sealed class Entry
    {
        public string key;
        public string kind;
        public string text;
        public int node;
    }
    [Serializable] public sealed class Paper
    {
        public string id;
        public string authorHeading;
        public string type;
        public string[] paragraphKeys;
        public string collectNarrationKey;
        public bool collectInLetterSlot;
        public bool robotReads;
    }
    [Serializable] sealed class Source
    {
        public Entry[] entries;
        public Paper[] papers;
    }

    static OSChapter03StoryContent instance;
    readonly Dictionary<string, Entry> entries = new Dictionary<string, Entry>(StringComparer.Ordinal);
    readonly Dictionary<string, Paper> papers = new Dictionary<string, Paper>(StringComparer.Ordinal);
    public static OSChapter03StoryContent Instance => instance ?? (instance = Load());
    public bool Available => entries.Count > 0;
    public IEnumerable<Entry> Entries => entries.Values;

    static OSChapter03StoryContent Load()
    {
        var result = new OSChapter03StoryContent();
        var asset = Resources.Load<TextAsset>("Chapter03Story/content");
        if (asset == null) return result;
        var source = JsonUtility.FromJson<Source>(asset.text);
        if (source?.entries != null)
            foreach (var entry in source.entries)
                if (entry != null && !string.IsNullOrEmpty(entry.key)) result.entries[entry.key] = entry;
        if (source?.papers != null)
            foreach (var paper in source.papers)
                if (paper != null && !string.IsNullOrEmpty(paper.id)) result.papers[paper.id] = paper;
        return result;
    }

    public static string FullKey(string key) => string.IsNullOrEmpty(key) || key.StartsWith("os.chapter03.", StringComparison.Ordinal)
        ? key : "os.chapter03." + key;
    public Entry Find(string key) => key != null && entries.TryGetValue(FullKey(key), out var value) ? value : null;
    public int Node(string key) => Find(key)?.node ?? -1;
    public Paper FindPaper(string id) => id != null && papers.TryGetValue(id, out var value) ? value : null;
    public string Text(string key)
    {
        string full = FullKey(key);
        if (GameDataManager.Instance != null && GameDataManager.Instance.TryGetLocalizedText(full, out string text)
            && !string.IsNullOrEmpty(text)) return text;
        return Find(full)?.text ?? string.Empty;
    }
    public string PaperBody(string id)
    {
        var paper = FindPaper(id);
        if (paper?.paragraphKeys == null) return string.Empty;
        var text = new string[paper.paragraphKeys.Length];
        for (int i = 0; i < text.Length; i++) text[i] = Text(paper.paragraphKeys[i]);
        return string.Join("\n\n", text);
    }
    public static void ResetCache() => instance = null;
}
