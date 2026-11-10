using MrTGame.OS;
using UnityEngine;

public partial class AdditionHandle
{
    UnityEngine.UI.Image recordMapImage;
    bool newspaperWasActive;
    GameSaveData recordAttachmentSave;
    OSChapter03StoryPresentation recordLetterPresentation;
    OS_Handle recordLetterOwner;
    OSWindowUnit recordLetterSettingsWindow;
    GameSaveData recordLetterSave;
    PlayerCtrlManager recordLetterControlOwner;
    bool recordLetterInputHeld, recordLetterPreviousCanMove, recordLetterPreviousCanInteractive;

    public bool IsMapRecordOpen => isPaperOpen && recordMapImage != null
        && recordMapImage.gameObject.activeSelf;
    public bool IsLetterRecordOpen => recordLetterInputHeld && recordLetterPresentation != null
        && recordLetterPresentation.IsModalOpen;
    public OSChapter03StoryPresentation RecordLetterPresentation => recordLetterPresentation;

    RectTransform PresentedPaperImage => recordMapImage != null && recordMapImage.gameObject.activeSelf
        ? recordMapImage.rectTransform : paperImage;

    public bool TryOpenMapFromRecord(int recordIndex)
    {
        GameDataManager data = GameDataManager.Instance;
        OS_Handle os = OS_Handle.Instance;
        if (data == null || !data.IsRecordDataUnlocked(recordIndex)
            || !data.TryGetRecordData(recordIndex, out RecordData record)
            || record.AttachmentType != RecordAttachmentType.Map || record.RecordSprite == null
            || os == null || !os.IsOSVisible || os.IsSystemInputLocked)
            return false;

        return TryOpenPaper(true, record.RecordSprite);
    }

    public bool TryOpenLetterFromRecord(int recordIndex)
    {
        GameDataManager data = GameDataManager.Instance;
        OS_Handle os = OS_Handle.Instance;
        if (!isActiveAndEnabled || BlocksPaperInput || data == null
            || recordIndex != OSStoryRecordUnlocks.MotherLetterRecordIndex
            || !data.IsRecordDataUnlocked(recordIndex)
            || !data.TryGetRecordData(recordIndex, out RecordData record)
            || record.AttachmentType != RecordAttachmentType.Letter
            || os == null || !os.IsOSVisible || os.IsSystemInputLocked)
            return false;

        var content = OSChapter03StoryContent.Instance;
        string body = content.PaperBody("final_letter");
        if (string.IsNullOrWhiteSpace(body)) return false;

        if (recordLetterPresentation == null)
        {
            var host = new GameObject("RecordLetterPresentation", typeof(RectTransform));
            host.layer = gameObject.layer;
            host.transform.SetParent(transform, false);
            recordLetterPresentation = host.AddComponent<OSChapter03StoryPresentation>();
        }
        recordLetterPresentation.gameObject.SetActive(true);
        recordLetterPresentation.enabled = true;
        recordLetterPresentation.Initialize(os);
        recordLetterPresentation.Paused = false;
        recordLetterOwner = os;
        recordLetterSettingsWindow = os.transform.Find("Windows/WindowUnit_Option")?.GetComponent<OSWindowUnit>();
        recordLetterSave = data.SaveData;
        recordLetterInputHeld = true;
        recordLetterControlOwner = PlayerCtrlManager.Instance;
        if (recordLetterControlOwner != null)
        {
            recordLetterPreviousCanMove = recordLetterControlOwner.CanMove;
            recordLetterPreviousCanInteractive = recordLetterControlOwner.CanInteractive;
            recordLetterControlOwner.SetControlState(false, false);
        }
        try
        {
            // Reuse only the paper renderer. The story's envelope, pointer and flags remain its own.
            recordLetterPresentation.ShowPaper(content.Text("ui.paper.letter"), body,
                ReleaseRecordLetterInput, "letter");
            UpdateRecordLetterPresentation();
            return IsLetterRecordOpen;
        }
        catch
        {
            CancelRecordLetterPresentation();
            throw;
        }
    }

    void UpdateRecordLetterPresentation()
    {
        if (!recordLetterInputHeld) return;
        if (recordLetterPresentation == null || !recordLetterPresentation.isActiveAndEnabled
            || !recordLetterPresentation.IsModalOpen || OSSessionPersistence.IsRestoring
            || !ReferenceEquals(recordLetterSave, GameDataManager.Instance?.SaveData)
            || recordLetterOwner == null || recordLetterOwner != OS_Handle.Instance
            || !recordLetterOwner.IsOSVisible)
        {
            CancelRecordLetterPresentation();
            return;
        }

        // IsSystemInputLocked includes this reader's own lock; use only external pause sources here.
        recordLetterPresentation.Paused = GameSessionFlow.Instance?.IsBusy == true
            || GameTestConsole.BlocksGameplayInput
            || (recordLetterSettingsWindow != null && recordLetterSettingsWindow.IsOpen
                && !recordLetterSettingsWindow.IsMinimized);
    }

    void RefreshRecordLetterLocalization()
    {
        if (!IsLetterRecordOpen) return;
        var content = OSChapter03StoryContent.Instance;
        recordLetterPresentation.RefreshModalText(content.Text("ui.paper.letter"), content.PaperBody("final_letter"));
    }

    void CancelRecordLetterPresentation()
    {
        if (recordLetterPresentation != null) recordLetterPresentation.HideModal(false);
        ReleaseRecordLetterInput();
    }

    void ReleaseRecordLetterInput()
    {
        if (!recordLetterInputHeld) return;
        recordLetterInputHeld = false;
        paperClosedFrame = Time.frameCount;
        if (recordLetterControlOwner != null)
            recordLetterControlOwner.SetControlState(recordLetterPreviousCanMove, recordLetterPreviousCanInteractive);
        recordLetterControlOwner = null;
        recordLetterSave = null;
        recordLetterOwner = null;
        recordLetterSettingsWindow = null;
    }

    void DestroyRecordLetterPresentation()
    {
        CancelRecordLetterPresentation();
        if (recordLetterPresentation != null) Destroy(recordLetterPresentation.gameObject);
        recordLetterPresentation = null;
    }

    void PrepareRecordAttachmentContent(Sprite mapSprite)
    {
        recordAttachmentSave = GameDataManager.Instance?.SaveData;
        if (mapSprite == null) return;

        if (recordMapImage == null)
        {
            var map = new GameObject("RecordMapImage", typeof(RectTransform),
                typeof(CanvasRenderer), typeof(UnityEngine.UI.Image));
            map.layer = paper.layer;
            map.transform.SetParent(paper.transform, false);
            map.transform.SetSiblingIndex(paperImage.GetSiblingIndex() + 1);
            recordMapImage = map.GetComponent<UnityEngine.UI.Image>();
            recordMapImage.raycastTarget = false;
            recordMapImage.preserveAspect = true;
            RectTransform rect = recordMapImage.rectTransform;
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(.5f, .5f);
        }

        newspaperWasActive = paperImage.gameObject.activeSelf;
        paperImage.gameObject.SetActive(false);
        recordMapImage.sprite = mapSprite;
        recordMapImage.color = Color.white;
        Vector2 nativeSize = mapSprite.rect.size;
        Rect bounds = ((RectTransform)paper.transform).rect;
        // Whole source pixels only; the 216 x 162 map fits at 2x in the 640 x 360 canvas.
        float scale = Mathf.Clamp(Mathf.Floor(Mathf.Min((bounds.width - 32f) / nativeSize.x,
            (bounds.height - 32f) / nativeSize.y)), 1f, 2f);
        recordMapImage.rectTransform.sizeDelta = nativeSize * scale;
        recordMapImage.rectTransform.anchoredPosition = Vector2.zero;
        recordMapImage.gameObject.SetActive(true);
    }

    bool ShouldCancelRecordAttachment()
    {
        return paperOpenedFromRecord && (OSSessionPersistence.IsRestoring
            || !ReferenceEquals(recordAttachmentSave, GameDataManager.Instance?.SaveData)
            || OS_Handle.Instance == null || !OS_Handle.Instance.IsOSVisible);
    }

    void RestoreRecordAttachmentContent()
    {
        if (recordMapImage != null && recordMapImage.gameObject.activeSelf)
        {
            recordMapImage.gameObject.SetActive(false);
            recordMapImage.sprite = null;
            if (paperImage != null) paperImage.gameObject.SetActive(newspaperWasActive);
        }
        recordAttachmentSave = null;
    }
}
