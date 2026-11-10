using System.Collections.Generic;
using System.Globalization;

namespace MrTGame.Narrative
{
    public static class OSOptionLabel
    {
        public static string Format(string text, int displayIndex)
        {
            string value = (text ?? string.Empty).TrimStart();
            string prefix = (System.Math.Max(0, displayIndex) + 1).ToString(CultureInfo.InvariantCulture) + ".";
            return value.StartsWith(prefix, System.StringComparison.Ordinal) ? value : prefix + value;
        }
    }

    public static class OSMemoryLocalizationKeys
    {
        const string Prefix = "os.memory.";

        public static string GetTextKey(int dataIndex)
        {
            return string.Concat(
                Prefix,
                dataIndex.ToString(CultureInfo.InvariantCulture),
                ".text");
        }

        public static string GetOptionKey(int dataIndex, int optionId)
        {
            return string.Concat(
                Prefix,
                dataIndex.ToString(CultureInfo.InvariantCulture),
                ".option.",
                optionId.ToString(CultureInfo.InvariantCulture));
        }

        public static string GetChapterNameKey(string chapterId)
        {
            return GetChapterKey(chapterId, "name");
        }

        public static string GetChapterSummaryKey(string chapterId)
        {
            return GetChapterKey(chapterId, "summary");
        }

        static string GetChapterKey(string chapterId, string suffix)
        {
            if (string.IsNullOrWhiteSpace(chapterId))
                return string.Empty;

            string normalizedChapterId = chapterId
                .Trim()
                .ToLowerInvariant();
            return string.Concat(
                Prefix,
                "chapter.",
                normalizedChapterId,
                ".",
                suffix);
        }
    }

    public sealed class OSMemoryReadOptionData
    {
        readonly OSMemoryOptionData source;

        internal OSMemoryReadOptionData(
            OSMemoryOptionData source,
            string text,
            string localizationKey)
        {
            this.source = source;
            Text = text;
            LocalizationKey = localizationKey;
        }

        public int OptionId => source.OptionId;
        public string Text { get; }
        public string LocalizationKey { get; }
        public int TargetDataIndex => source.TargetDataIndex;
        public OSMemoryResourceChange ResourceChange => source.ResourceChange;
        public IReadOnlyList<OSMemoryActionData> Actions => source.Actions;
    }

    public sealed class OSMemoryReadData
    {
        readonly OSMemoryTextData source;

        internal OSMemoryReadData(
            OSMemoryTextData source,
            string text,
            string localizationKey,
            IReadOnlyList<OSMemoryReadOptionData> options)
        {
            this.source = source;
            Text = text;
            LocalizationKey = localizationKey;
            Options = options;
        }

        public int DataIndex => source.DataIndex;
        public string Text { get; }
        public string LocalizationKey { get; }
        public int NextDataIndex => source.NextDataIndex;
        public IReadOnlyList<OSMemoryLinkData> Links => source.Links;
        public IReadOnlyList<OSMemoryReadOptionData> Options { get; }
        public IReadOnlyList<OSMemoryActionData> OnEnterActions => source.OnEnterActions;
        public OSMemorySubwindowSequence SubwindowSequence => source.SubwindowSequence;
        public bool IsInteractiveSceneEntry => source.IsInteractiveSceneEntry;
        public bool IsSilentInteractiveEntry => IsInteractiveSceneEntry && string.IsNullOrEmpty(Text);
        // The approved 5410 authoring placeholder has no prose or blank history
        // row. Once the author fills it, it behaves as a normal memory node.
        public bool IsSilentEntry => IsSilentInteractiveEntry
            || (DataIndex == OSMemoryData.Chapter02PostHeartTalkDataIndex && string.IsNullOrEmpty(Text));
    }
}
