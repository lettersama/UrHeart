using DG.Tweening;
using System;
using MrTGame.Narrative;
using TMPro;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class OSMemoryTextUnit : MonoBehaviour
{
    const string ScrambleOpenTag = "<link=\"memory.scramble\">";
    const string ScrambleCloseTag = "</link>";
    const string ScrambleCharacters = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789#!?%/[]{}";
    const float ScrambleInterval = 0.09f;
    const float WaitingDotsInterval = 0.4f;

    [Header("References")]
    [SerializeField] TMP_Text label;

    [Header("Text Playback")]
    [SerializeField, Min(1f)] float charactersPerSecond = 40f;
    [SerializeField, Min(0f)] float minimumDuration = 0.1f;
    [SerializeField] Color historyColor =
        new Color32(0x62, 0x5C, 0x5B, 0xFF);
    [SerializeField] Color latestColor =
        new Color32(0x87, 0x82, 0x7A, 0xFF);

    Tween textTween;
    readonly OSChapter03KeywordScramble chapter03Scramble = new OSChapter03KeywordScramble();
    bool chapter03Paused;
    bool isTextComplete;
    char[] scrambleBuffer;
    int scrambleStart;
    int scrambleEnd;
    int originalVisibleCharacters;
    uint scrambleState;
    float nextScrambleTime;
    bool isLatestText;
    bool waitingDotsPrepared;
    int waitingDotsStart;
    int waitingDotsCount;
    int originalWaitingVisibleCharacters;
    float nextWaitingDotsTime;

    public int DataIndex { get; private set; } = OSMemoryData.InvalidDataIndex;
    public OSMemoryReadData BoundData { get; private set; }
    public bool IsTextComplete => isTextComplete;
    public bool IsPlaying => textTween != null && textTween.IsActive();
    public TMP_Text Label => label;

    public event Action<OSMemoryTextUnit, OSMemoryReadData>
        PlaybackCompleted;
    public event Action<OSMemoryTextUnit, OSMemoryReadData> DataBound;

    void Awake()
    {
        ResolveReferences();
    }

    void LateUpdate()
    {
        var story = OS_Handle.Instance?.Chapter03Story;
        bool pause = story?.IsActive == true && !story.IsAvailable;
        if (pause != chapter03Paused)
        {
            chapter03Paused = pause;
            if (pause) textTween?.Pause(); else textTween?.Play();
        }
        if (pause) return;
        if (chapter03Scramble.Active && label != null)
        { chapter03Scramble.Tick(); label.text = chapter03Scramble.Text; }
        if (scrambleBuffer != null && Time.unscaledTime >= nextScrambleTime)
            RefreshScramble();
        if (waitingDotsPrepared && isTextComplete && isLatestText
            && Time.unscaledTime >= nextWaitingDotsTime)
            RefreshWaitingDots();
    }

    void OnDisable()
    {
        KillTextTween();
    }

    void OnDestroy()
    {
        KillTextTween();
    }


    public void Bind(
        OSMemoryReadData data,
        bool isLatest,
        bool animate)
    {
        ResolveReferences();
        if (data == null)
        {
            Clear();
            return;
        }

        KillTextTween();
        ClearScramble();
        ClearWaitingDots();
        BoundData = data;
        DataIndex = data.DataIndex;
        isTextComplete = false;

        if (!gameObject.activeSelf)
            gameObject.SetActive(true);

        SetLatestVisual(isLatest);

        DataBound?.Invoke(this, data);
        string text = DisplayText(data);
        text = chapter03Scramble.Prepare(text, DataIndex, MeasureCharacter);
        PrepareScramble(text);
        PrepareWaitingDots(text);
        if (animate)
            PlayText(text);
        else
            ShowTextImmediately(text);
    }

    public void Bind(OSMemoryReadData data)
    {
        Bind(data, true, false);
    }

    public void SetLatestVisual(bool isLatest)
    {
        ResolveReferences();
        isLatestText = isLatest;
        if (!isLatest && waitingDotsPrepared && label != null)
            label.maxVisibleCharacters = originalWaitingVisibleCharacters;
        if (label != null)
            label.color = isLatest ? latestColor : historyColor;
    }

    public void Clear()
    {
        KillTextTween();
        ClearScramble();
        ClearWaitingDots();
        chapter03Scramble.Clear();
        BoundData = null;
        DataIndex = OSMemoryData.InvalidDataIndex;
        isTextComplete = false;

        ResolveReferences();
        if (label != null)
            label.text = string.Empty;

        if (gameObject.activeSelf)
            gameObject.SetActive(false);
    }


    /// <summary>Optional silent beat before a node's line starts typing (presentation only).</summary>
    public static System.Func<int, float> PlaybackDelayProvider;

    /// <summary>Guiding lines, e.g. chapter 3's questions (presentation only). A line whose text
    /// already marks its guiding part with a color keeps that; an unmarked one is drawn whole in red.</summary>
    public static System.Func<int, bool> HighlightProvider;

    string DisplayText(OSMemoryReadData data)
    {
        string source = data.Text;
        string text = OSPalette.TintRichText(source);
        return HighlightProvider != null && HighlightProvider(data.DataIndex)
               && (source == null || source.IndexOf("<color", System.StringComparison.OrdinalIgnoreCase) < 0)
            ? "<color=" + OSPalette.RedHex + ">" + text + "</color>" : text;
    }

    float MeasureCharacter(char c) => label != null ? label.GetPreferredValues(c.ToString()).x : 0f;

    void PlayText(string text)
    {
        string targetText = scrambleBuffer != null
            ? new string(scrambleBuffer) : text ?? string.Empty;
        if (label == null || targetText.Length == 0)
        {
            if (label != null)
                label.text = targetText;

            CompletePlayback();
            return;
        }

        label.text = targetText;
        label.ForceMeshUpdate();
        int visibleCharacterCount =
            Mathf.Max(1, label.textInfo.characterCount);
        if (scrambleBuffer != null || waitingDotsPrepared || chapter03Scramble.Active)
            label.maxVisibleCharacters = 0;
        else
            label.text = string.Empty;

        float speed = Mathf.Max(1f, charactersPerSecond);
        float duration = Mathf.Max(
            minimumDuration,
            visibleCharacterCount / speed);
        if (duration <= 0f)
        {
            label.text = targetText;
            CompletePlayback();
            return;
        }

        // Dynamic lines keep their full text and reveal through a visibility
        // limit; ordinary prose retains its existing DOText behavior.
        Tween tween = (scrambleBuffer != null || waitingDotsPrepared || chapter03Scramble.Active
            ? (Tween)DOTween.To(() => label.maxVisibleCharacters,
                value => label.maxVisibleCharacters = value,
                visibleCharacterCount, duration)
            : label.DOText(targetText, duration, true))
            .SetEase(Ease.Linear)
            .SetDelay(PlaybackDelayProvider != null ? Mathf.Max(0f, PlaybackDelayProvider(DataIndex)) : 0f)
            .SetUpdate(true)
            .SetLink(gameObject, LinkBehaviour.KillOnDisable);
        textTween = tween;
        tween.OnComplete(() =>
        {
            if (textTween != tween)
                return;

            textTween = null;
            CompletePlayback();
        });
    }

    void ShowTextImmediately(string text)
    {
        if (label != null)
        {
            if (scrambleBuffer != null)
            {
                label.maxVisibleCharacters = originalVisibleCharacters;
                RefreshScramble();
            }
            else
                label.text = chapter03Scramble.Active ? chapter03Scramble.Text : text ?? string.Empty;
            if (chapter03Scramble.Active) label.maxVisibleCharacters = int.MaxValue;
            label.ForceMeshUpdate();
        }

        isTextComplete = true;
        StartWaitingDots();
    }

    public void CompleteImmediately()
    {
        if (isTextComplete || BoundData == null)
            return;
        KillTextTween();
        ShowTextImmediately(DisplayText(BoundData));
        CompletePlayback();
    }

    void CompletePlayback()
    {
        if (scrambleBuffer != null && label != null)
            label.maxVisibleCharacters = originalVisibleCharacters;
        if (chapter03Scramble.Active && label != null) label.maxVisibleCharacters = int.MaxValue;
        isTextComplete = true;
        StartWaitingDots();
        if (BoundData != null)
            PlaybackCompleted?.Invoke(this, BoundData);
    }

    void KillTextTween()
    {
        if (textTween == null)
            return;

        Tween tween = textTween;
        textTween = null;
        tween.Kill(false);
    }

    void PrepareScramble(string text)
    {
        if (label == null || string.IsNullOrEmpty(text))
            return;
        int start = text.IndexOf(ScrambleOpenTag, StringComparison.Ordinal);
        if (start < 0)
            return;
        start += ScrambleOpenTag.Length;
        int end = text.IndexOf(ScrambleCloseTag, start, StringComparison.Ordinal);
        if (end <= start)
            return;

        scrambleStart = start;
        scrambleEnd = end;
        scrambleBuffer = text.ToCharArray();
        originalVisibleCharacters = label.maxVisibleCharacters;
        // Same independent generator as the OS fault text; never consumes the
        // gameplay random sequence or changes the localized source/save data.
        scrambleState = unchecked((uint)DataIndex * 747796405u
            + (uint)(Time.unscaledTime * 1000f) + 2891336453u);
        RefreshScramble();
    }

    void RefreshScramble()
    {
        if (label == null || scrambleBuffer == null)
            return;
        bool insideTag = false;
        for (int i = scrambleStart; i < scrambleEnd; i++)
        {
            char current = scrambleBuffer[i];
            if (current == '<') insideTag = true;
            if (insideTag)
            {
                if (current == '>') insideTag = false;
                continue;
            }
            if (char.IsWhiteSpace(current))
                continue;
            scrambleState = unchecked(scrambleState * 1664525u + 1013904223u);
            scrambleBuffer[i] = ScrambleCharacters[(int)(scrambleState % ScrambleCharacters.Length)];
        }
        label.text = new string(scrambleBuffer);
        nextScrambleTime = Time.unscaledTime + ScrambleInterval;
    }

    void ClearScramble()
    {
        if (scrambleBuffer != null && label != null)
            label.maxVisibleCharacters = originalVisibleCharacters;
        scrambleBuffer = null;
    }

    void PrepareWaitingDots(string text)
    {
        if (DataIndex != OSMemoryData.Chapter02CollectionWaitingDataIndex
            || label == null || string.IsNullOrEmpty(text)
            || !text.EndsWith("...", StringComparison.Ordinal))
            return;

        originalWaitingVisibleCharacters = label.maxVisibleCharacters;
        waitingDotsPrepared = true;
    }

    void StartWaitingDots()
    {
        if (!waitingDotsPrepared || !isLatestText || label == null)
            return;

        label.ForceMeshUpdate();
        waitingDotsStart = Mathf.Max(0, label.textInfo.characterCount - 3);
        waitingDotsCount = 0;
        RefreshWaitingDots();
    }

    void RefreshWaitingDots()
    {
        if (label == null)
            return;

        // Hide only the trailing dots. The localized words and the full text's
        // layout stay intact; no new history entry or completion event is made.
        waitingDotsCount = waitingDotsCount % 3 + 1;
        label.maxVisibleCharacters = waitingDotsStart + waitingDotsCount;
        nextWaitingDotsTime = Time.unscaledTime + WaitingDotsInterval;
    }

    void ClearWaitingDots()
    {
        if (waitingDotsPrepared && label != null)
            label.maxVisibleCharacters = originalWaitingVisibleCharacters;
        waitingDotsPrepared = false;
    }

    void ResolveReferences()
    {
        if (label == null)
            label = GetComponent<TMP_Text>();

        OSLocalizedFontUtility.ApplyCurrent(label);
    }

    void Reset()
    {
        ResolveReferences();
    }
}
