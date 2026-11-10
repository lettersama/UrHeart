using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Story continuation footer and gesture readout, with no selectable or keyboard input.</summary>
[DisallowMultipleComponent]
public sealed class OSCursorExperimentAnswerView : MonoBehaviour
{
    const string EntryPrefix = "cursor_test_ch02_";
    const float ContinueFooterHeight = 15f, AnswerFooterHeight = 35f;
    const float TipFadeSeconds = .1f, TipMoveSeconds = .7f;
    const float TipTopY = 7.5f, TipBottomY = 5.5f;
    static readonly Color Ink = new Color32(0x87, 0x82, 0x7e, 0xff);

    OSLinkMessageController messages;
    OSLinkMessageWindowLayout reservedLayout;
    RectTransform root;
    RectTransform answerFrame;
    Image continueTip;
    TextMeshProUGUI answerLabel;
    int answer;
    float tipElapsed;
    bool isShown, showAnswer, ready;
    string entryId;
    Color tipColor = Color.white;

    public bool IsShown => isShown && root != null && root.gameObject.activeInHierarchy;
    public TMP_Text AnswerLabel => answerLabel;
    public Image ContinueTip => continueTip;
    public bool IsAnswerShown => IsShown && showAnswer;
    public bool IsReadyToContinue => IsShown && ready;

    // answer: 0 = blank, 1 = horizontal shake, 2 = vertical nod.
    public void Show(OSLinkMessageController source, int selectedAnswer, bool showAnswer, bool readyToContinue)
    {
        if (source == null || source.WindowLayout == null || source.DisplayedTextLabel == null
            || !IsStoryEntry(source.DisplayedEntryId))
        {
            Hide();
            return;
        }

        if (messages != source || reservedLayout != source.WindowLayout)
        {
            Hide();
            DestroyView();
            messages = source;
            reservedLayout = source.WindowLayout;
        }

        if (root == null)
            CreateView();
        if (root == null)
            return;

        bool changedEntry = entryId != source.DisplayedEntryId;
        if (changedEntry || ready != readyToContinue || !isShown) tipElapsed = 0f;
        entryId = source.DisplayedEntryId;
        answer = Mathf.Clamp(selectedAnswer, 0, 2);
        this.showAnswer = showAnswer;
        ready = readyToContinue;
        isShown = true;
        root.gameObject.SetActive(true);
        Render();
    }

    public void Hide()
    {
        bool ownedFooter = isShown;
        isShown = false;
        ready = false;
        tipElapsed = 0f;
        entryId = null;
        if (root != null)
            root.gameObject.SetActive(false);
        if (ownedFooter && reservedLayout != null)
            reservedLayout.SetTextFooterHeight(0f);
    }

    void LateUpdate()
    {
        if (!isShown)
            return;
        // A replaced/interrupted message cannot retain the answer footer.
        if (messages == null || !IsStoryEntry(messages.DisplayedEntryId) || messages.DisplayedEntryId != entryId
            || messages.LinkWindow == null || !messages.LinkWindow.IsOpen
            || messages.LinkWindow.IsMinimized || !messages.LinkWindow.gameObject.activeInHierarchy)
        {
            Hide();
            return;
        }

        if (ready && !messages.LinkWindow.IsOpening && !messages.LinkWindow.IsClosing)
            tipElapsed += Mathf.Min(Time.unscaledDeltaTime, .05f);
        Render();
    }

    static bool IsStoryEntry(string id) => !string.IsNullOrEmpty(id)
        && id.StartsWith(EntryPrefix, System.StringComparison.Ordinal);

    void ReserveFooter()
    {
        float height = showAnswer ? AnswerFooterHeight : ContinueFooterHeight;
        float answerHeight = 16f;
        if (showAnswer && HasLongAnswers && answerLabel != null)
        {
            // Reserve both replies up front. Switching gestures must not resize
            // the window, and longer translations must remain fully readable.
            float width = Mathf.Max(40f, ((RectTransform)reservedLayout.transform).rect.width - 18f);
            for (int choice = 1; choice <= 2; choice++)
                answerHeight = Mathf.Max(answerHeight, Mathf.Ceil(answerLabel.GetPreferredValues(
                    AnswerText(choice), width, Mathf.Infinity).y) + 6f);
            height = answerHeight + 19f;
        }
        if (answerFrame != null) answerFrame.sizeDelta = new Vector2(0f, answerHeight);
        root.sizeDelta = new Vector2(-8f, height - 4f);
        if (answerFrame != null) answerFrame.gameObject.SetActive(showAnswer);
        // Reserve from the beginning of typing, so revealing the arrow never moves the text.
        reservedLayout.SetTextFooterHeight(height);
    }

    void OnDisable() => Hide();

    void OnDestroy()
    {
        Hide();
        DestroyView();
    }

    void CreateView()
    {
        Transform parent = reservedLayout.transform.Find("MessageArea");
        if (parent == null)
            return;

        root = NewRect("CursorStoryFooter", parent);
        root.anchorMin = new Vector2(0f, 0f);
        root.anchorMax = new Vector2(1f, 0f);
        root.pivot = new Vector2(0.5f, 0f);
        root.anchoredPosition = new Vector2(0f, 3f);
        root.sizeDelta = new Vector2(-8f, ContinueFooterHeight - 4f);
        CanvasGroup input = root.gameObject.AddComponent<CanvasGroup>();
        input.interactable = false;
        input.blocksRaycasts = false;

        answerFrame = NewImage("AnswerFrame", root, Ink);
        HorizontalBand(answerFrame, 14f, 16f);
        RectTransform answerInner = NewImage("AnswerInterior", answerFrame, Color.black);
        Inset(answerInner, 1f);
        RectTransform labelRect = NewRect("AnswerText", answerInner);
        Inset(labelRect, 0f);
        labelRect.offsetMin = new Vector2(3f, 0f);
        labelRect.offsetMax = new Vector2(-3f, 0f);
        answerLabel = labelRect.gameObject.AddComponent<TextMeshProUGUI>();
        answerLabel.raycastTarget = false;
        answerLabel.color = Ink;
        answerLabel.alignment = TextAlignmentOptions.Center;
        answerLabel.textWrappingMode = TextWrappingModes.NoWrap;
        answerLabel.overflowMode = TextOverflowModes.Ellipsis;
        answerLabel.enableAutoSizing = false;
        answerLabel.richText = false;
        answerLabel.margin = Vector4.zero;

        RectTransform tipRect = NewImage("ContinueTip", root, Color.white);
        tipRect.anchorMin = tipRect.anchorMax = new Vector2(.5f, 0f);
        tipRect.pivot = new Vector2(.5f, .5f);
        tipRect.anchoredPosition = new Vector2(0f, TipTopY);
        tipRect.sizeDelta = new Vector2(10f, 7f);
        continueTip = tipRect.GetComponent<Image>();
        continueTip.type = Image.Type.Simple;
        continueTip.preserveAspect = true;
        ResolveContinueSprite();
    }

    void ResolveContinueSprite()
    {
        if (continueTip == null || continueTip.sprite != null) return;
        OSMemoryWindowController memory = OS_Handle.Instance?.MemoryWindowController;
        Image source = memory != null ? memory.transform.Find("TextArea/ContinueTip")?.GetComponent<Image>() : null;
        if (source == null || source.sprite == null) return;
        continueTip.sprite = source.sprite;
        tipColor = source.color;
        tipColor.a = 1f;
    }

    void Render()
    {
        if (answerLabel == null || messages == null)
            return;

        TMP_Text source = messages.DisplayedTextLabel;
        if (source != null)
        {
            // The existing message owns the language-specific font/material.
            // Mirror it instead of caching whichever language built this view.
            if (answerLabel.font != source.font)
                answerLabel.font = source.font;
            if (answerLabel.fontSharedMaterial != source.fontSharedMaterial)
                answerLabel.fontSharedMaterial = source.fontSharedMaterial;
            answerLabel.fontSize = source.fontSize;
            answerLabel.fontStyle = source.fontStyle;
            answerLabel.fontWeight = source.fontWeight;
            answerLabel.characterSpacing = source.characterSpacing;
            answerLabel.wordSpacing = source.wordSpacing;
        }

        answerLabel.textWrappingMode = HasLongAnswers ? TextWrappingModes.Normal : TextWrappingModes.NoWrap;
        answerLabel.overflowMode = HasLongAnswers ? TextOverflowModes.Overflow : TextOverflowModes.Ellipsis;
        string text = answer == 0 ? string.Empty : AnswerText(answer);
        if (answerLabel.text != text)
            answerLabel.text = text;
        ReserveFooter();

        ResolveContinueSprite();
        if (continueTip == null) return;
        bool canShowTip = ready && continueTip.sprite != null && messages.LinkWindow != null
            && !messages.LinkWindow.IsOpening && !messages.LinkWindow.IsClosing;
        continueTip.enabled = canShowTip;
        Color color = tipColor;
        color.a = canShowTip ? Mathf.Clamp01(tipElapsed / TipFadeSeconds) : 0f;
        continueTip.color = color;
        // Match the memory area's gentle 0.7-second, two-pixel up/down cue.
        float phase = Mathf.PingPong(tipElapsed / TipMoveSeconds, 1f);
        float eased = .5f - .5f * Mathf.Cos(Mathf.PI * phase);
        continueTip.rectTransform.anchoredPosition = new Vector2(0f, Mathf.Lerp(TipTopY, TipBottomY, eased));
    }

    bool IsSafetyQuestion => entryId == EntryPrefix + "safety_question";
    bool IsSecondTestQuestion => entryId == EntryPrefix + "second_question";
    bool IsThirdTestQuestion => entryId == EntryPrefix + "third_question";
    bool HasLongAnswers => IsSafetyQuestion || IsSecondTestQuestion || IsThirdTestQuestion;

    string AnswerText(int choice)
    {
        string suffix = IsThirdTestQuestion ? (choice == 1 ? "answer_third_deny" : "answer_third_confirm")
            : IsSecondTestQuestion ? (choice == 1 ? "answer_second_deny" : "answer_second_confirm")
            : IsSafetyQuestion ? (choice == 1 ? "answer_safety_deny" : "answer_safety_confirm")
            : choice == 1 ? "answer_not_bug" : "answer_human";
        string fallback = IsThirdTestQuestion ? (choice == 1 ? "对对对，都是你电脑自己动的。" : "不然呢?我还在想出来怎么收拾你!")
            : IsSecondTestQuestion ? (choice == 1 ? "这不是随便挪挪就出来了？" : "我是正常人！又不是瞎子！")
            : IsSafetyQuestion ? (choice == 1 ? "我要是病毒!我第一件事情把你电脑拆了!" : "哼,我可是人类!快把我放出来")
            : choice == 1 ? "我不是Bug" : "我是人!";
        return GameDataManager.Instance != null
            ? GameDataManager.Instance.GetLocalizedText("character.cursor_test." + suffix, fallback) : fallback;
    }

    void DestroyView()
    {
        if (root != null)
            Destroy(root.gameObject);
        root = null;
        answerLabel = null;
        answerFrame = null;
        continueTip = null;
    }

    static RectTransform NewRect(string objectName, Transform parent)
    {
        var node = new GameObject(objectName, typeof(RectTransform));
        node.layer = parent.gameObject.layer;
        var rect = (RectTransform)node.transform;
        rect.SetParent(parent, false);
        rect.localScale = Vector3.one;
        return rect;
    }

    static RectTransform NewImage(string objectName, Transform parent, Color color)
    {
        RectTransform rect = NewRect(objectName, parent);
        Image image = rect.gameObject.AddComponent<Image>();
        image.color = color;
        image.raycastTarget = false;
        return rect;
    }

    static void HorizontalBand(RectTransform rect, float bottom, float height)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = new Vector2(1f, 0f);
        rect.pivot = new Vector2(0.5f, 0f);
        rect.anchoredPosition = new Vector2(0f, bottom);
        rect.sizeDelta = new Vector2(0f, height);
    }

    static void Inset(RectTransform rect, float inset)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = new Vector2(inset, inset);
        rect.offsetMax = new Vector2(-inset, -inset);
    }
}
