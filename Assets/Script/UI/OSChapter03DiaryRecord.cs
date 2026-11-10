using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

/// <summary>
/// The diary is one record whose contents are derived from collected papers.
/// No second copy of the text or collection state is stored in the save.
/// </summary>
public static class OSChapter03DiaryRecord
{
    public const int RecordIndex = 5;

    static readonly Regex SourceDate = new Regex(
        @"(?<year>[0-9]{4})年(?<month>[0-9]{1,2})月(?<day>[0-9]{1,2})日",
        RegexOptions.CultureInvariant);

    public static OSChapter03StoryState GetStory(GameDataManager data)
        => data?.SaveData?.osProcess?.memory?.chapter03Story;

    public static bool HasCollectedDiary(OSChapter03StoryState story)
    {
        if (story?.papers == null || story.papers.Count == 0) return false;
        var content = OSChapter03StoryContent.Instance;
        foreach (string id in story.papers)
            if (content.FindPaper(id)?.type == "diary") return true;
        return false;
    }

    public static bool EnsureUnlocked(GameDataManager data)
    {
        if (data == null || !HasCollectedDiary(GetStory(data))
            || data.IsRecordDataUnlocked(RecordIndex)) return false;
        return data.UnlockRecordData(RecordIndex);
    }

    public static IReadOnlyList<string> GetCollectedPaperIds(OSChapter03StoryState story)
    {
        var result = new List<string>();
        if (story?.papers == null || story.papers.Count == 0) return result;
        var content = OSChapter03StoryContent.Instance;
        var included = new HashSet<string>(StringComparer.Ordinal);
        foreach (string id in story.papers)
            if (content.FindPaper(id)?.type == "diary" && included.Add(id)) result.Add(id);
        result.Sort((left, right) => ComparePages(content, left, right));
        return result;
    }

    public static string BuildBody(OSChapter03StoryState story)
    {
        var pages = GetCollectedPaperIds(story);
        var content = OSChapter03StoryContent.Instance;
        string title = content.Text("ui.paper.diary");
        if (string.IsNullOrEmpty(title)) title = "日记";
        string entryTitle = content.Text("ui.diary_record.entry_title");
        if (string.IsNullOrEmpty(entryTitle)) entryTitle = title + "#{0}";
        var body = new StringBuilder();
        foreach (string id in pages)
        {
            if (body.Length > 0) body.Append("\n\n\n");
            int number = PageNumber(id);
            body.Append(number > 0 ? string.Format(CultureInfo.InvariantCulture, entryTitle, number) : title);
            body.Append("\n\n").Append(content.PaperBody(id));
        }
        return body.ToString();
    }

    static int ComparePages(OSChapter03StoryContent content, string left, string right)
    {
        int order = SourceDateKey(content, left).CompareTo(SourceDateKey(content, right));
        if (order != 0) return order;
        order = PageNumber(left).CompareTo(PageNumber(right));
        return order != 0 ? order : string.CompareOrdinal(left, right);
    }

    static int SourceDateKey(OSChapter03StoryContent content, string id)
    {
        var paper = content.FindPaper(id);
        if (paper?.paragraphKeys == null) return int.MaxValue;
        foreach (string key in paper.paragraphKeys)
        {
            if (key == null || !key.EndsWith(".date", StringComparison.Ordinal)) continue;
            // Sorting reads the canonical source date, never a translated date
            // or the order in which the player happened to find these pages.
            var match = SourceDate.Match(content.Find(key)?.text ?? string.Empty);
            if (!match.Success) continue;
            int year = int.Parse(match.Groups["year"].Value, CultureInfo.InvariantCulture);
            int month = int.Parse(match.Groups["month"].Value, CultureInfo.InvariantCulture);
            int day = int.Parse(match.Groups["day"].Value, CultureInfo.InvariantCulture);
            if (year > 0 && month >= 1 && month <= 12 && day >= 1 && day <= DateTime.DaysInMonth(year, month))
                return year * 10000 + month * 100 + day;
        }
        return int.MaxValue;
    }

    static int PageNumber(string id)
    {
        int split = id?.LastIndexOf('_') ?? -1;
        return split >= 0 && int.TryParse(id.Substring(split + 1), NumberStyles.None,
            CultureInfo.InvariantCulture, out int number) ? number : 0;
    }
}
