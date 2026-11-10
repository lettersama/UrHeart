using System;
using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityEngine;

namespace MrTGame.Narrative
{
    public static class OSMemoryActionIds
    {
        // Option prerequisites are checked before resource changes or actions run.
        public const string RequireToolProgram = "tool.require_program";
        // Registers a presentation on entry; playback waits for this node's text.
        public const string PlaySubwindowsAfterText = "memory.play_subwindows_after_text";
        public const string EnterInteractiveScene = "memory.enter_interactive_scene";
    }

    [Serializable]
    public sealed class OSMemoryResourceChange
    {
        [SerializeField] float heart;
        [SerializeField] float battery;
        [SerializeField] float fault;

        public float Heart => heart;
        public float Battery => battery;
        public float Fault => fault;
    }

    [Serializable]
    public sealed class OSMemoryActionData
    {
        [SerializeField] string actionId;
        [SerializeField] string parameter;
        [SerializeField] UnityEngine.Object asset;

        public string ActionId => actionId;
        public string Parameter => parameter;
        public UnityEngine.Object Asset => asset;
    }

    [Serializable]
    public sealed class OSMemoryLinkData
    {
        [SerializeField] string linkId;
        [SerializeField] List<OSMemoryActionData> actions = new List<OSMemoryActionData>();

        public string LinkId => linkId;
        public IReadOnlyList<OSMemoryActionData> Actions => actions;
    }

    [Serializable]
    public sealed class OSMemoryOptionData
    {
        [SerializeField] int optionId;
        [SerializeField, TextArea(2, 5)] string sourceText;
        [SerializeField] int targetDataIndex = OSMemoryData.InvalidDataIndex;
        [SerializeField] OSMemoryResourceChange resourceChange = new OSMemoryResourceChange();
        [SerializeField] List<OSMemoryActionData> actions = new List<OSMemoryActionData>();

        string InspectorLabel => targetDataIndex.ToString();

        public int OptionId => optionId;
        public string SourceText => sourceText;
        public int TargetDataIndex => targetDataIndex;
        public OSMemoryResourceChange ResourceChange => resourceChange;
        public IReadOnlyList<OSMemoryActionData> Actions => actions;
    }

    [Serializable]
    public sealed class OSMemoryTextData
    {
        [SerializeField] int dataIndex;
        [SerializeField, TextArea(3, 10)] string sourceText;
        [SerializeField] int nextDataIndex = OSMemoryData.InvalidDataIndex;
        [SerializeField] List<OSMemoryLinkData> links = new List<OSMemoryLinkData>();
        [ListDrawerSettings(ListElementLabelName = "InspectorLabel")]
        [SerializeField] List<OSMemoryOptionData> options = new List<OSMemoryOptionData>();
        [SerializeField] List<OSMemoryActionData> onEnterActions = new List<OSMemoryActionData>();

        string InspectorLabel => nextDataIndex != OSMemoryData.InvalidDataIndex
            ? $"{dataIndex}->{nextDataIndex}"
            : dataIndex.ToString();

        public int DataIndex => dataIndex;
        public string SourceText => sourceText;
        public int NextDataIndex => nextDataIndex;
        public IReadOnlyList<OSMemoryLinkData> Links => links;
        public IReadOnlyList<OSMemoryOptionData> Options => options;
        public IReadOnlyList<OSMemoryActionData> OnEnterActions => onEnterActions;
        public bool IsInteractiveSceneEntry => onEnterActions.Exists(action =>
            action != null && action.ActionId == OSMemoryActionIds.EnterInteractiveScene);
        public OSMemorySubwindowSequence SubwindowSequence
        {
            get
            {
                foreach (OSMemoryActionData action in onEnterActions)
                    if (action != null && action.ActionId == OSMemoryActionIds.PlaySubwindowsAfterText)
                        return action.Asset as OSMemorySubwindowSequence;
                return null;
            }
        }
    }

    [Serializable]
    public sealed class OSMemoryChapterProgressRange
    {
        [SerializeField, Min(0)] int chapterSequence;
        [SerializeField] string chapterId;
        [SerializeField] string chapterNameText;
        [SerializeField, TextArea(2, 5)] string chapterSummaryText;
        [SerializeField] Sprite chapterIcon;
        [SerializeField] int firstDataIndex = OSMemoryData.InvalidDataIndex;
        [SerializeField] int lastDataIndex = OSMemoryData.InvalidDataIndex;
        [Tooltip("The chapter graph is only partially authored. Keep its progress at zero until the full graph is connected.")]
        [SerializeField] bool progressGraphIncomplete;

        string InspectorLabel =>
            string.IsNullOrWhiteSpace(chapterId)
                ? chapterSequence.ToString()
                : chapterSequence + " - " + chapterId;

        public int ChapterSequence => chapterSequence;
        public string ChapterId => chapterId;
        public string ChapterNameText => chapterNameText;
        public string ChapterSummaryText => chapterSummaryText;
        public Sprite ChapterIcon => chapterIcon;
        public int FirstDataIndex => firstDataIndex;
        public int LastDataIndex => lastDataIndex;
        public bool ProgressGraphIncomplete => progressGraphIncomplete;
        public bool HasMemoryDataRange =>
            firstDataIndex != OSMemoryData.InvalidDataIndex
            && lastDataIndex != OSMemoryData.InvalidDataIndex;
        public bool IsUIOnly =>
            firstDataIndex == OSMemoryData.InvalidDataIndex
            && lastDataIndex == OSMemoryData.InvalidDataIndex;
        public bool IsValid => HasMemoryDataRange || IsUIOnly;

        public bool Contains(int dataIndex)
        {
            if (!HasMemoryDataRange)
                return false;

            int minimum = Mathf.Min(firstDataIndex, lastDataIndex);
            int maximum = Mathf.Max(firstDataIndex, lastDataIndex);
            return dataIndex >= minimum && dataIndex <= maximum;
        }
    }

    [CreateAssetMenu(menuName = "MrTGame/Narrative/OS Memory Data", fileName = "OSMemoryData")]
    public sealed class OSMemoryData : ScriptableObject
    {
        public const int Chapter01Upgrade02DataIndex = 2670;
        public const int Chapter02Upgrade03DataIndex = 4310;
        public const int Chapter02CollectionWaitingDataIndex = 5400;
        public const int Chapter02PostHeartTalkDataIndex = 5410;
        public const int InvalidDataIndex = -1;

        [SerializeField] int startDataIndex = 1000;
        [ListDrawerSettings(ListElementLabelName = "InspectorLabel")]
        [SerializeField] List<OSMemoryTextData> dataList = new List<OSMemoryTextData>();
        [ListDrawerSettings(ListElementLabelName = "InspectorLabel")]
        [SerializeField]
        List<OSMemoryChapterProgressRange> chapterProgressRanges =
            new List<OSMemoryChapterProgressRange>();

        Dictionary<int, OSMemoryTextData> lookup;
        List<OSMemoryChapterProgressRange> orderedChapterProgressRanges;

        public int StartDataIndex => startDataIndex;
        public IReadOnlyList<OSMemoryTextData> DataList => dataList;
        public IReadOnlyList<OSMemoryChapterProgressRange> ChapterProgressRanges
        {
            get
            {
                EnsureLookup();
                if (orderedChapterProgressRanges != null)
                    return orderedChapterProgressRanges;

                return Array.Empty<OSMemoryChapterProgressRange>();
            }
        }

        public bool TryGetData(int dataIndex, out OSMemoryTextData data)
        {
            EnsureLookup();
            return lookup.TryGetValue(dataIndex, out data);
        }

        public bool TryGetChapterRange(
            int chapterSequence,
            out OSMemoryChapterProgressRange range)
        {
            EnsureLookup();
            for (int i = 0; i < orderedChapterProgressRanges.Count; i++)
            {
                OSMemoryChapterProgressRange candidate =
                    orderedChapterProgressRanges[i];
                if (candidate.ChapterSequence != chapterSequence)
                    continue;

                range = candidate;
                return true;
            }

            range = null;
            return false;
        }

        public float CalculateOverallProgress(
            int currentChapterIndex,
            int currentDataIndex,
            IReadOnlyList<int> triggeredDataIndices)
        {
            EnsureLookup();
            int chapterCount = orderedChapterProgressRanges.Count;
            if (chapterCount == 0)
                return 0f;

            int completedChapterCount = Mathf.Clamp(
                currentChapterIndex,
                0,
                chapterCount);
            if (completedChapterCount >= chapterCount)
                return 1f;

            float currentChapterProgress = 0f;
            if (TryGetChapterRange(
                    currentChapterIndex,
                    out OSMemoryChapterProgressRange currentChapter)
                && currentChapter.HasMemoryDataRange)
            {
                currentChapterProgress = CalculateChapterProgress(
                    currentChapter,
                    currentDataIndex,
                    triggeredDataIndices);
            }

            return Mathf.Clamp01(
                (completedChapterCount + currentChapterProgress)
                / chapterCount);
        }

        public float CalculateOverallProgress(
            int currentDataIndex,
            IReadOnlyList<int> triggeredDataIndices)
        {
            EnsureLookup();
            if (orderedChapterProgressRanges.Count == 0)
                return 0f;

            int chapterIndex = FindChapterIndex(currentDataIndex);
            if (chapterIndex < 0 && triggeredDataIndices != null)
            {
                for (int i = 0; i < triggeredDataIndices.Count; i++)
                {
                    chapterIndex = Mathf.Max(
                        chapterIndex,
                        FindChapterIndex(triggeredDataIndices[i]));
                }
            }

            if (chapterIndex < 0)
                return 0f;

            float chapterProgress = CalculateChapterProgress(
                orderedChapterProgressRanges[chapterIndex],
                currentDataIndex,
                triggeredDataIndices);
            return Mathf.Clamp01(
                (chapterIndex + chapterProgress)
                / orderedChapterProgressRanges.Count);
        }

        int FindChapterIndex(int dataIndex)
        {
            if (dataIndex == InvalidDataIndex
                || orderedChapterProgressRanges == null)
            {
                return -1;
            }

            for (int i = 0; i < orderedChapterProgressRanges.Count; i++)
            {
                if (orderedChapterProgressRanges[i].Contains(dataIndex))
                    return i;
            }

            return -1;
        }

        float CalculateChapterProgress(
            OSMemoryChapterProgressRange range,
            int currentDataIndex,
            IReadOnlyList<int> triggeredDataIndices)
        {
            // Chapter three advances through room events; a single interaction's
            // terminal memory node is not the ending of this chapter.
            if (range?.ChapterSequence == 3)
            {
                var story = GameDataManager.Instance?.OSProcessData.memory.chapter03Story;
                if (story?.version > 0) return story.Progress;
            }
            // A staged opening must not treat its temporary stopping point as
            // the chapter ending. Existing ranges default to complete graphs.
            if (range != null && range.ProgressGraphIncomplete)
                return 0f;
            BuildChapterDepths(
                range,
                out Dictionary<int, int> depths,
                out int maximumDepth);
            if (depths.Count == 0)
                return 0f;

            int deepestReached = -1;
            if (depths.TryGetValue(currentDataIndex, out int currentDepth))
            {
                if (IsChapterTerminal(currentDataIndex, range))
                    return 1f;

                deepestReached = currentDepth;
            }

            if (triggeredDataIndices != null)
            {
                for (int i = 0; i < triggeredDataIndices.Count; i++)
                {
                    int dataIndex = triggeredDataIndices[i];
                    if (!depths.TryGetValue(dataIndex, out int depth))
                        continue;

                    if (IsChapterTerminal(dataIndex, range))
                        return 1f;

                    deepestReached = Mathf.Max(deepestReached, depth);
                }
            }

            if (deepestReached < 0)
                return 0f;
            if (maximumDepth <= 0)
                return 1f;

            return Mathf.Clamp01((float)deepestReached / maximumDepth);
        }

        void BuildChapterDepths(
            OSMemoryChapterProgressRange range,
            out Dictionary<int, int> depths,
            out int maximumDepth)
        {
            depths = new Dictionary<int, int>();
            maximumDepth = 0;
            int startIndex = ResolveChapterStartDataIndex(range);
            if (startIndex == InvalidDataIndex)
                return;

            var pending = new Queue<int>();
            depths.Add(startIndex, 0);
            pending.Enqueue(startIndex);

            while (pending.Count > 0)
            {
                int dataIndex = pending.Dequeue();
                int nextDepth = depths[dataIndex] + 1;
                maximumDepth = Mathf.Max(maximumDepth, depths[dataIndex]);
                if (!lookup.TryGetValue(dataIndex, out OSMemoryTextData data))
                    continue;

                TryEnqueueChapterTarget(
                    data.NextDataIndex,
                    nextDepth,
                    range,
                    depths,
                    pending);
                TryEnqueueChapterTarget(
                    GetDirectorProgressTarget(dataIndex),
                    nextDepth,
                    range,
                    depths,
                    pending);
                IReadOnlyList<OSMemoryOptionData> options = data.Options;
                for (int i = 0; i < options.Count; i++)
                {
                    OSMemoryOptionData option = options[i];
                    if (option == null)
                        continue;

                    TryEnqueueChapterTarget(
                        option.TargetDataIndex,
                        nextDepth,
                        range,
                        depths,
                        pending);
                }
            }
        }

        int ResolveChapterStartDataIndex(
            OSMemoryChapterProgressRange range)
        {
            if (range != null
                && range.Contains(range.FirstDataIndex)
                && lookup.ContainsKey(range.FirstDataIndex))
            {
                return range.FirstDataIndex;
            }

            if (range == null || dataList == null)
                return InvalidDataIndex;

            for (int i = 0; i < dataList.Count; i++)
            {
                OSMemoryTextData data = dataList[i];
                if (data != null && range.Contains(data.DataIndex))
                    return data.DataIndex;
            }

            return InvalidDataIndex;
        }

        void TryEnqueueChapterTarget(
            int targetDataIndex,
            int depth,
            OSMemoryChapterProgressRange range,
            Dictionary<int, int> depths,
            Queue<int> pending)
        {
            if (!IsValidChapterTarget(targetDataIndex, range)
                || depths.ContainsKey(targetDataIndex))
            {
                return;
            }

            depths.Add(targetDataIndex, depth);
            pending.Enqueue(targetDataIndex);
        }

        bool IsChapterTerminal(
            int dataIndex,
            OSMemoryChapterProgressRange range)
        {
            if (!lookup.TryGetValue(dataIndex, out OSMemoryTextData data))
                return false;

            if (IsValidChapterTarget(data.NextDataIndex, range))
                return false;

            if (IsValidChapterTarget(GetDirectorProgressTarget(dataIndex), range))
                return false;

            IReadOnlyList<OSMemoryOptionData> options = data.Options;
            for (int i = 0; i < options.Count; i++)
            {
                OSMemoryOptionData option = options[i];
                if (option != null
                    && IsValidChapterTarget(
                        option.TargetDataIndex,
                        range))
                {
                    return false;
                }
            }

            return true;
        }

        // Count the authored continuation beyond the character experiment in
        // chapter progress, while leaving 5400 without a clickable next link.
        // Only the completed heart-talk director may perform this transition.
        static int GetDirectorProgressTarget(int dataIndex) =>
            dataIndex == Chapter02CollectionWaitingDataIndex
                ? Chapter02PostHeartTalkDataIndex : InvalidDataIndex;

        bool IsValidChapterTarget(
            int targetDataIndex,
            OSMemoryChapterProgressRange range)
        {
            return targetDataIndex != InvalidDataIndex
                && range != null
                && range.Contains(targetDataIndex)
                && lookup.ContainsKey(targetDataIndex);
        }

        void RebuildChapterProgressRanges()
        {
            orderedChapterProgressRanges =
                new List<OSMemoryChapterProgressRange>();
            if (chapterProgressRanges == null)
                return;

            for (int i = 0; i < chapterProgressRanges.Count; i++)
            {
                OSMemoryChapterProgressRange range =
                    chapterProgressRanges[i];
                if (range != null && range.IsValid)
                    orderedChapterProgressRanges.Add(range);
            }

            orderedChapterProgressRanges.Sort((left, right) =>
                left.ChapterSequence.CompareTo(right.ChapterSequence));

            for (int i = 1; i < orderedChapterProgressRanges.Count; i++)
            {
                OSMemoryChapterProgressRange previous =
                    orderedChapterProgressRanges[i - 1];
                OSMemoryChapterProgressRange current =
                    orderedChapterProgressRanges[i];
                if (previous.ChapterSequence == current.ChapterSequence)
                {
                    Debug.LogWarning(
                        "[OSMemoryData] Duplicate chapter progress sequence "
                        + current.ChapterSequence
                        + "; list order decides which range is used first.",
                        this);
                }

                bool overlaps = previous.Contains(current.FirstDataIndex)
                    || previous.Contains(current.LastDataIndex)
                    || current.Contains(previous.FirstDataIndex)
                    || current.Contains(previous.LastDataIndex);
                if (overlaps)
                {
                    Debug.LogWarning(
                        "[OSMemoryData] Chapter progress ranges overlap at "
                        + previous.ChapterSequence
                        + " and "
                        + current.ChapterSequence
                        + ".",
                        this);
                }
            }
        }

        void OnEnable()
        {
            RebuildLookup();
        }

        void OnValidate()
        {
            RebuildLookup();
        }

        void EnsureLookup()
        {
            if (lookup == null)
                RebuildLookup();
        }

        void RebuildLookup()
        {
            lookup = new Dictionary<int, OSMemoryTextData>();
            RebuildChapterProgressRanges();
            if (dataList == null)
                return;

            for (int i = 0; i < dataList.Count; i++)
            {
                OSMemoryTextData data = dataList[i];
                if (data == null)
                    continue;

                if (!lookup.TryAdd(data.DataIndex, data))
                {
                    Debug.LogWarning(
                        $"[OSMemoryData] Duplicate dataIndex {data.DataIndex} at list position {i}; the first entry is used.",
                        this);
                }
            }
        }
    }
}
