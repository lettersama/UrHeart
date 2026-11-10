using System.Collections.Generic;
using MrTGame.OS;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
[RequireComponent(typeof(OSWindowUnit))]
public sealed class OSRecordWindowController : MonoBehaviour
{
    const int TestRecordIndex = 9999;
    const string TestRecordName = "测试记录";
    const string TestRecordDescription = "这是对该记录的文本描述。";

    [Header("Data")]
    [SerializeField] RecordSO recordSO;

    [Header("Record List")]
    [SerializeField] RectTransform recordList;
    [SerializeField] RecordUnit recordUnitTemplate;

    [Header("Details")]
    [SerializeField] Image detailImage;
    [SerializeField] TMP_Text detailText;
    [SerializeField] TMP_Text detailTitle;
    [SerializeField] RectTransform attachmentsRoot;
    [SerializeField] Button attachmentButton;
    [SerializeField] TMP_Text attachmentLabel;
    [SerializeField] TMP_Text attachmentHeading;
    [SerializeField] Image attachmentIcon;
    [SerializeField] TMP_Text emptyState;
    [SerializeField] Sprite defaultRecordIcon;

    readonly List<RecordUnit> recordUnits = new List<RecordUnit>();
    readonly HashSet<int> resolvedIndices = new HashSet<int>();

    RecordUnit selectedRecord;
    bool hasWarnedMissingDataManager;
    bool hasWarnedMissingReferences;
    bool initializingRecords;
    GameDataManager boundData;
    OSWindowUnit window;
    OSRecordPaperDoll paperDoll;

    public IReadOnlyList<RecordUnit> RecordUnits => recordUnits;
    public OSRecordPaperDoll PaperDoll => paperDoll;
    public RecordUnit SelectedRecord => selectedRecord;
    public RecordData CurrentData => selectedRecord != null
        ? selectedRecord.Data
        : null;
    public bool CanInteract => isActiveAndEnabled && window != null
        && window.IsOpen && !window.IsMinimized
        && !GameTestConsole.BlocksGameplayInput
        && (OS_Handle.Instance == null || (OS_Handle.Instance.IsOSVisible && !OS_Handle.Instance.IsSystemInputLocked));

    void OnEnable()
    {
        ResolveReferences();
        boundData = GameDataManager.Instance;
        if (boundData != null)
        {
            boundData.RecordUnlocked += HandleRecordUnlocked;
            boundData.RecordUpdated += HandleRecordUnlocked;
        }
        if (attachmentButton != null)
            attachmentButton.onClick.AddListener(HandleAttachmentClicked);
        if (boundData != null && window != null && window.IsOpen)
            HandleRecordUnlocked(RecordUnit.InvalidRecordIndex);
        else
            RefreshLocalizedData();
    }

    void OnDisable()
    {
        paperDoll?.ResetPose();
        if (boundData != null)
        {
            boundData.RecordUnlocked -= HandleRecordUnlocked;
            boundData.RecordUpdated -= HandleRecordUnlocked;
        }
        boundData = null;
        if (attachmentButton != null)
            attachmentButton.onClick.RemoveListener(HandleAttachmentClicked);
    }

    void HandleRecordUnlocked(int index)
    {
        if (initializingRecords) return;
        int selectedIndex = selectedRecord != null ? selectedRecord.RecordIndex : RecordUnit.InvalidRecordIndex;
        var scroll = detailText != null ? detailText.GetComponentInParent<ScrollRect>(true) : null;
        float scrollPosition = scroll != null ? scroll.verticalNormalizedPosition : 1f;
        InitializeFromSave();
        RecordUnit previous = recordUnits.Find(unit => unit.RecordIndex == selectedIndex);
        if (previous != null)
        {
            SelectRecord(previous);
            // A background unlock/update refreshes the list without moving the record
            // currently being read. OnEnable uses InvalidRecordIndex and starts at the top.
            if (index != RecordUnit.InvalidRecordIndex && scroll != null)
                scroll.verticalNormalizedPosition = scrollPosition;
        }
    }

    void HandleAttachmentClicked() => TryOpenAttachment();

    public bool TryOpenAttachment()
    {
        if (!CanInteract || CurrentData == null)
            return false;
        if (CurrentData.AttachmentType == RecordAttachmentType.Newspaper)
            return AdditionHandle.Instance != null && AdditionHandle.Instance.TryOpenPaperFromRecord();
        if (CurrentData.AttachmentType == RecordAttachmentType.Map)
            return AdditionHandle.Instance != null && AdditionHandle.Instance.TryOpenMapFromRecord(CurrentData.RecordIndex);
        if (CurrentData.AttachmentType == RecordAttachmentType.Letter)
            return AdditionHandle.Instance != null && AdditionHandle.Instance.TryOpenLetterFromRecord(CurrentData.RecordIndex);
        return false;
    }

    void Awake()
    {
        ResolveReferences();
    }

    void OnDestroy()
    {
        recordUnits.Clear();
        resolvedIndices.Clear();
        selectedRecord = null;
    }

    public void PrepareRuntime()
    {
        ResolveReferences();
        ClearGeneratedUnits();
        PrepareTemplate();
        ClearDetails();
    }

    public void InitializeFromSave()
    {
        if (initializingRecords) return;
        initializingRecords = true;
        try { InitializeFromSaveCore(); }
        finally { initializingRecords = false; }
    }

    void InitializeFromSaveCore()
    {
        ResolveReferences();
        ClearGeneratedUnits();
        PrepareTemplate();
        ClearDetails();

        if (!HasRequiredReferences())
            return;

        if (GameSetup.IsTestMode)
        {
            RecordData testData = RecordData.CreateRuntime(
                TestRecordIndex,
                TestRecordName,
                defaultRecordIcon,
                TestRecordDescription,
                null);
            TryAddRecordUnit(
                TestRecordIndex,
                testData,
                true);
        }

        GameDataManager dataManager = GameDataManager.Instance;
        if (dataManager == null)
        {
            if (!hasWarnedMissingDataManager)
            {
                hasWarnedMissingDataManager = true;
                Debug.LogWarning(
                    "[OS Record] GameDataManager is missing; saved records cannot be initialized.",
                    this);
            }

            SelectFirstRecord();
            return;
        }

        hasWarnedMissingDataManager = false;
        // Old saves already contain the collected paper ids. Reconstruct their
        // single diary entry without replaying any pickup notification.
        OSStoryRecordUnlocks.RefreshDiary(dataManager);
        OSStoryRecordUnlocks.RefreshMotherLetter(dataManager);
        IReadOnlyList<int> unlockedIndices =
            dataManager.UnlockedRecordDataIndices;
        if (unlockedIndices == null)
        {
            SelectFirstRecord();
            return;
        }

        for (int i = 0; i < unlockedIndices.Count; i++)
        {
            int recordIndex = unlockedIndices[i];
            if (resolvedIndices.Contains(recordIndex)
                || !TryGetRecordData(
                    dataManager,
                    recordIndex,
                    out RecordData data))
            {
                continue;
            }

            TryAddRecordUnit(recordIndex, data, false);
        }

        SelectFirstRecord();
    }

    public void SelectRecord(RecordUnit recordUnit)
    {
        if (recordUnit == null
            || !recordUnit.IsBound
            || !recordUnits.Contains(recordUnit))
        {
            return;
        }

        bool changed = selectedRecord == null || selectedRecord.RecordIndex != recordUnit.RecordIndex;
        selectedRecord = recordUnit;
        for (int i = 0; i < recordUnits.Count; i++)
        {
            RecordUnit unit = recordUnits[i];
            if (unit != null)
                unit.SetSelected(unit == recordUnit);
        }

        UpdateDetails(recordUnit.Data);
        if (changed)
            ResetDetailScrollPosition();
    }

    void ResetDetailScrollPosition()
    {
        var scroll = detailText != null ? detailText.GetComponentInParent<ScrollRect>(true) : null;
        if (scroll == null || scroll.content == null) return;
        // Text and optional illustrations change the content height. Resolve it
        // before returning a newly selected record to its title.
        LayoutRebuilder.ForceRebuildLayoutImmediate(scroll.content);
        scroll.StopMovement();
        scroll.verticalNormalizedPosition = 1f;
    }

    public bool UpdateDetails(int recordIndex)
    {
        GameDataManager dataManager = GameDataManager.Instance;
        if (dataManager == null
            || !TryGetRecordData(
                dataManager,
                recordIndex,
                out RecordData data))
        {
            ClearDetails();
            return false;
        }

        UpdateDetails(data);
        return true;
    }

    public void RefreshLocalizedData()
    {
        UpdateDetails(selectedRecord != null
            ? selectedRecord.Data
            : null);
    }

    public void UpdateDetails(RecordData data)
    {
        ResolveReferences();
        GameDataManager dataManager = GameDataManager.Instance;
        if (detailImage != null)
            detailImage.gameObject.SetActive(false);

        if (detailTitle != null)
        {
            detailTitle.gameObject.SetActive(data != null);
            OSLocalizedFontUtility.ApplyCurrent(detailTitle);
            detailTitle.text = dataManager != null ? dataManager.GetLocalizedRecordName(data) : data?.RecordName ?? string.Empty;
        }

        if (detailText != null)
        {
            OSLocalizedFontUtility.ApplyCurrent(detailText);
            string description = data?.RecordIndex == OSChapter03DiaryRecord.RecordIndex
                ? OSChapter03DiaryRecord.BuildBody(OSChapter03DiaryRecord.GetStory(dataManager))
                : dataManager != null ? dataManager.GetLocalizedRecordDescription(data)
                : data?.RecordDescription ?? string.Empty;
            detailText.gameObject.SetActive(data != null);
            detailText.text = data == null ? string.Empty : string.IsNullOrWhiteSpace(description)
                ? Localize("ui.os.record.description_pending", "Description to be added") : description;
        }

        RefreshPaperDoll(data);

        bool isMap = data != null && data.AttachmentType == RecordAttachmentType.Map && data.RecordSprite != null;
        bool isLetter = data != null && data.AttachmentType == RecordAttachmentType.Letter;
        bool hasAttachment = data != null && (data.AttachmentType == RecordAttachmentType.Newspaper || isMap || isLetter);
        if (attachmentsRoot != null)
            attachmentsRoot.gameObject.SetActive(hasAttachment);
        if (attachmentHeading != null)
        {
            OSLocalizedFontUtility.ApplyCurrent(attachmentHeading);
            attachmentHeading.text = Localize("ui.os.record.attachments", "Attachments");
        }
        if (attachmentLabel != null)
        {
            OSLocalizedFontUtility.ApplyCurrent(attachmentLabel);
            attachmentLabel.text = isMap
                ? Localize("ui.os.record.open_map", "View map")
                : isLetter ? Localize("ui.os.record.open_letter", "Read letter")
                : Localize("ui.os.record.open_newspaper", "Read newspaper");
        }
        if (attachmentIcon != null)
        {
            attachmentIcon.sprite = hasAttachment ? data.RecordIcon : null;
            attachmentIcon.preserveAspect = true;
        }
        if (emptyState != null)
        {
            emptyState.gameObject.SetActive(data == null);
            OSLocalizedFontUtility.ApplyCurrent(emptyState);
            emptyState.text = Localize("ui.os.record.empty", "No records collected yet");
        }
    }

    static string Localize(string key, string fallback) => GameDataManager.Instance != null
        ? GameDataManager.Instance.GetLocalizedText(key, fallback) : fallback;

    void RefreshPaperDoll(RecordData data)
    {
        bool visible = data != null && data.DetailPresentationType == RecordDetailPresentationType.PaperDoll
            && data.RecordSprite != null;
        if (paperDoll == null && detailText != null)
            paperDoll = detailText.transform.parent?.Find("PaperDoll/Doll")?.GetComponent<OSRecordPaperDoll>();
        if (paperDoll == null && visible && Application.isPlaying && detailText != null
            && detailText.transform.parent is RectTransform content)
        {
            // One layout child follows the description. Its inner figure rotates
            // independently; the existing VLG/fitter still own content height.
            var row = new GameObject("PaperDoll", typeof(RectTransform), typeof(UnityEngine.UI.LayoutElement));
            row.layer = content.gameObject.layer;
            var rowRect = (RectTransform)row.transform;
            rowRect.SetParent(content, false);
            rowRect.SetSiblingIndex(detailText.transform.GetSiblingIndex() + 1);
            var layout = row.GetComponent<UnityEngine.UI.LayoutElement>();
            layout.minHeight = layout.preferredHeight = OSRecordPaperDoll.RowHeight;
            layout.flexibleHeight = 0f;
            var figure = new GameObject("Doll", typeof(RectTransform), typeof(UnityEngine.UI.Image), typeof(OSRecordPaperDoll));
            figure.layer = row.layer;
            figure.transform.SetParent(rowRect, false);
            paperDoll = figure.GetComponent<OSRecordPaperDoll>();
        }
        if (paperDoll == null) return;
        var paperRow = paperDoll.transform.parent.gameObject;
        if (visible)
        {
            paperRow.SetActive(true);
            paperDoll.Bind(this, window, data.RecordIndex, data.RecordSprite);
        }
        else
        {
            paperDoll.Clear();
            paperRow.SetActive(false);
        }
    }

    void ResolveReferences()
    {
        if (window == null)
            window = GetComponent<OSWindowUnit>();
        Transform recordsArea = transform.Find("RecordsArea");
        if (recordList == null && recordsArea != null)
        {
            recordList = recordsArea.Find("RecordList")
                as RectTransform;
        }

        if (recordUnitTemplate == null && recordList != null)
        {
            Transform template = recordList.Find("RecordUnit");
            if (template != null)
                recordUnitTemplate = template.GetComponent<RecordUnit>();

            if (recordUnitTemplate == null)
            {
                recordUnitTemplate = recordList
                    .GetComponentInChildren<RecordUnit>(true);
            }
        }

        Transform detailsArea = transform.Find("DetailsArea");
        if (detailsArea == null)
            return;

        if (detailImage == null)
        {
            detailImage = detailsArea.Find("DetailImage")
                ?.GetComponent<Image>();
        }

        if (detailText == null)
        {
            detailText = detailsArea.Find("DetailText")
                ?.GetComponent<TMP_Text>();
        }
    }

    bool HasRequiredReferences()
    {
        bool isValid = recordList != null && recordUnitTemplate != null;
        if (!isValid && !hasWarnedMissingReferences)
        {
            hasWarnedMissingReferences = true;
            Debug.LogWarning(
                "[OS Record] RecordList or RecordUnit template is missing.",
                this);
        }

        if (isValid)
            hasWarnedMissingReferences = false;

        return isValid;
    }

    bool TryGetRecordData(
        GameDataManager dataManager,
        int recordIndex,
        out RecordData data)
    {
        // The saved unlock flag is not permission to reveal uncollected pages.
        if (recordIndex == OSChapter03DiaryRecord.RecordIndex
            && !OSChapter03DiaryRecord.HasCollectedDiary(OSChapter03DiaryRecord.GetStory(dataManager)))
        {
            data = null;
            return false;
        }
        if (recordSO != null)
            return recordSO.TryGetRecordData(recordIndex, out data);

        return dataManager.TryGetRecordData(recordIndex, out data);
    }

    bool TryAddRecordUnit(
        int recordIndex,
        RecordData data,
        bool showSolidColorWhenIconMissing)
    {
        if (data == null || !resolvedIndices.Add(recordIndex))
            return false;

        RecordUnit unit = Instantiate(
            recordUnitTemplate,
            recordList,
            false);
        unit.name = $"RecordUnit_{recordIndex}";
        unit.Initialize(
            this,
            recordIndex,
            data,
            true,
            showSolidColorWhenIconMissing);
        if (unit.RecordIcon != null && unit.RecordIcon.sprite == null && defaultRecordIcon != null)
        {
            unit.RecordIcon.sprite = defaultRecordIcon;
            unit.RecordIcon.enabled = true;
        }
        recordUnits.Add(unit);
        return true;
    }

    void SelectFirstRecord()
    {
        if (recordUnits.Count > 0)
            SelectRecord(recordUnits.Find(unit => !IsTestRecord(unit.Data)) ?? recordUnits[0]);
    }

    static bool IsTestRecord(RecordData data)
    {
        return GameSetup.IsTestMode
            && data != null
            && data.RecordIndex == TestRecordIndex;
    }

    void PrepareTemplate()
    {
        if (recordUnitTemplate != null)
            recordUnitTemplate.Clear();
    }

    void ClearGeneratedUnits()
    {
        selectedRecord = null;
        resolvedIndices.Clear();

        for (int i = 0; i < recordUnits.Count; i++)
        {
            RecordUnit unit = recordUnits[i];
            if (unit == null)
                continue;

            unit.Clear();
            Destroy(unit.gameObject);
        }

        recordUnits.Clear();
    }

    void ClearDetails()
    {
        UpdateDetails(null);
    }

    void Reset()
    {
        ResolveReferences();
    }
}
