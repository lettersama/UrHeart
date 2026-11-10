using System;
using System.Collections.Generic;
using System.Globalization;
using Sirenix.OdinInspector;
using UnityEngine;

namespace MrTGame.OS
{
    public enum RecordAttachmentType { None, Newspaper, Map, Letter }
    public enum RecordDetailPresentationType { None, PaperDoll }

    public static class OSRecordLocalizationKeys
    {
        const string Prefix = "os.record.";

        public static string GetNameKey(int recordIndex)
        {
            return GetKey(recordIndex, "name");
        }

        public static string GetDescriptionKey(int recordIndex)
        {
            return GetKey(recordIndex, "description");
        }

        static string GetKey(int recordIndex, string suffix)
        {
            return string.Concat(
                Prefix,
                recordIndex.ToString(CultureInfo.InvariantCulture),
                ".",
                suffix);
        }
    }

    [Serializable]
    public sealed class RecordData
    {
        [SerializeField] int recordIndex;
        [SerializeField] string recordName;
        [SerializeField] Sprite recordIcon;
        [SerializeField, TextArea(3, 10)] string recordDescription;
        [SerializeField] Sprite recordSprite;
        [SerializeField] RecordAttachmentType attachmentType;
        [SerializeField] RecordDetailPresentationType detailPresentationType;

        string InspectorLabel => $"{recordIndex} {recordName}";

        public static RecordData CreateRuntime(
            int index,
            string name,
            Sprite icon,
            string description,
            Sprite sprite)
        {
            return new RecordData
            {
                recordIndex = index,
                recordName = name ?? string.Empty,
                recordIcon = icon,
                recordDescription = description ?? string.Empty,
                recordSprite = sprite
            };
        }

        public int RecordIndex => recordIndex;
        public string RecordName => recordName;
        public string RecordNameLocalizationKey =>
            OSRecordLocalizationKeys.GetNameKey(recordIndex);
        public Sprite RecordIcon => recordIcon;
        public string RecordDescription => recordDescription;
        public string RecordDescriptionLocalizationKey =>
            OSRecordLocalizationKeys.GetDescriptionKey(recordIndex);
        public Sprite RecordSprite => recordSprite;
        public RecordAttachmentType AttachmentType => attachmentType;
        public RecordDetailPresentationType DetailPresentationType => detailPresentationType;
    }

    [CreateAssetMenu(
        menuName = "MrTGame/OS/Record Data",
        fileName = "RecordSO")]
    public sealed class RecordSO : ScriptableObject
    {
        [ListDrawerSettings(ListElementLabelName = "InspectorLabel")]
        [SerializeField]
        List<RecordData> recordDataList = new List<RecordData>();

        Dictionary<int, RecordData> lookup;

        public bool TryGetRecordData(int recordIndex, out RecordData data)
        {
            EnsureLookup();
            return lookup.TryGetValue(recordIndex, out data);
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
            lookup = new Dictionary<int, RecordData>();
            if (recordDataList == null)
                return;

            for (int i = 0; i < recordDataList.Count; i++)
            {
                RecordData data = recordDataList[i];
                if (data == null)
                    continue;

                if (!lookup.TryAdd(data.RecordIndex, data))
                {
                    Debug.LogWarning(
                        $"[RecordSO] Duplicate recordIndex {data.RecordIndex} at list position {i}; the first entry is used.",
                        this);
                }
            }
        }
    }
}
