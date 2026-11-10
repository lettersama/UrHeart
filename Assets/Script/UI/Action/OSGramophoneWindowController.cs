using System.Collections.Generic;
using MrTGame.Core;
using TMPro;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class OSGramophoneWindowController : MonoBehaviour
{
    const string NoSelectionKey = "ui.os.gramophone.no_selection";
    const string RepeatKey = "ui.os.gramophone.repeat_one";
    const float RowHeight = 18f;
    static readonly Color ReadColor = new Color32(135, 130, 126, 255);
    static readonly Color NewColor = new Color32(191, 185, 175, 255);

    [SerializeField] OSWindowUnit window;
    [SerializeField] UnityEngine.UI.Button previousButton;
    [SerializeField] UnityEngine.UI.Button playButton;
    [SerializeField] UnityEngine.UI.Button nextButton;
    [SerializeField] UnityEngine.UI.Button muteButton;
    [SerializeField] UnityEngine.UI.Slider seekSlider;
    [SerializeField] UnityEngine.UI.Slider volumeSlider;
    [SerializeField] TMP_Text volumeLabel;
    [SerializeField] UnityEngine.UI.Image speakerIcon;
    [SerializeField] Sprite volumeIcon;
    [SerializeField] Sprite mutedIcon;
    [SerializeField, Range(0f, 1f)] float volume = .7f;
    [SerializeField] Sprite playIcon;
    [SerializeField] Sprite pauseIcon;

    sealed class TrackRow
    {
        public string Id;
        public RectTransform Rect;
        public UnityEngine.UI.Button Button;
        public TMP_Text Title;
        public UnityEngine.UI.Image Dot;
        public bool IsNew;
    }

    readonly List<TrackRow> rows = new List<TrackRow>();
    OS_Handle osHandle;
    OSGramophonePlayer player;
    RectTransform platter;
    RectTransform rowsRoot;
    UnityEngine.UI.ScrollRect libraryScroll;
    UnityEngine.UI.Image playbackIcon;
    TMP_Text selectedTrack;
    TMP_Text repeatStatus;
    TMP_Text elapsedLabel;
    TMP_Text durationLabel;
    TMP_Text emptyLabel;
    TMP_Text libraryCount;
    TMP_FontAsset rowFont;
    Material rowFontMaterial;
    Texture2D dotTexture;
    Sprite dotSprite;
    string displayedTrackId;
    bool playerSubscribed;
    bool inputAllowed;
    bool rowsLayoutDirty;
    float previousRowsWidth = -1f;
    float nextPlayerResolveTime;
    int displayedElapsed = -2;
    int displayedDuration = -2;
    int displayedVolume = -1;
    bool observedPlaying, observedMuted;

    public float NormalizedVolume => Mathf.Clamp01(player != null ? player.Volume : volume);
    public bool IsMuted => player != null ? player.IsMuted : NormalizedVolume <= 0f;

    void Awake()
    {
        ResolveReferences();
        CaptureRowFont();
    }

    void OnEnable()
    {
        previousButton?.onClick.AddListener(PlayPrevious);
        playButton?.onClick.AddListener(TogglePlayback);
        nextButton?.onClick.AddListener(PlayNext);
        muteButton?.onClick.AddListener(ToggleMute);
        if (seekSlider != null)
        {
            seekSlider.minValue = 0f;
            seekSlider.maxValue = 1f;
            seekSlider.wholeNumbers = false;
            seekSlider.onValueChanged.AddListener(Seek);
        }
        if (volumeSlider != null)
        {
            volumeSlider.minValue = 0f;
            volumeSlider.maxValue = 1f;
            volumeSlider.wholeNumbers = false;
            volumeSlider.onValueChanged.AddListener(SetVolume);
        }
        GameEvents.LanguageChanged += HandleLanguageChanged;
        BindPlayer();
        RefreshDisplay(true);
    }

    void OnDisable()
    {
        previousButton?.onClick.RemoveListener(PlayPrevious);
        playButton?.onClick.RemoveListener(TogglePlayback);
        nextButton?.onClick.RemoveListener(PlayNext);
        muteButton?.onClick.RemoveListener(ToggleMute);
        seekSlider?.onValueChanged.RemoveListener(Seek);
        volumeSlider?.onValueChanged.RemoveListener(SetVolume);
        GameEvents.LanguageChanged -= HandleLanguageChanged;
        UnbindPlayer();
    }

    void OnDestroy()
    {
        UnbindPlayer();
        if (dotSprite != null) Destroy(dotSprite);
        if (dotTexture != null) Destroy(dotTexture);
    }

    void Update()
    {
        // The OS can install its player after an inactive window was initialized.
        if (player == null && Time.unscaledTime >= nextPlayerResolveTime)
        {
            nextPlayerResolveTime = Time.unscaledTime + .5f;
            BindPlayer();
            RefreshDisplay(true);
        }
        if (inputAllowed != CanAcceptInput()) RefreshControls();
        if (window != null && (!window.IsOpen || window.IsMinimized)) return;
        bool playing = player != null && player.IsPlaying;
        if (observedPlaying != playing || observedMuted != IsMuted)
        {
            observedPlaying = playing;observedMuted = IsMuted;
            RefreshControls();RefreshVolume();
        }

        if (player != null && player.IsPlaying && platter != null)
            platter.Rotate(0f, 0f, -100f * Time.unscaledDeltaTime);
        RefreshPosition();
    }

    void LateUpdate()
    {
        if (rowsRoot == null || !rowsRoot.gameObject.activeInHierarchy) return;
        float width = rowsRoot.rect.width;
        if (!rowsLayoutDirty && Mathf.Approximately(width, previousRowsWidth)) return;
        previousRowsWidth = width;
        rowsLayoutDirty = false;
        UnityEngine.UI.LayoutRebuilder.ForceRebuildLayoutImmediate(rowsRoot);
        for (int i = 0; i < rows.Count; i++) PositionNewDot(rows[i]);
    }

    void ResolveReferences()
    {
        if (window == null) window = GetComponent<OSWindowUnit>();
        osHandle = GetComponentInParent<OS_Handle>(true);
        if (previousButton == null) previousButton = Find<UnityEngine.UI.Button>("Content/Playback/Previous");
        if (playButton == null) playButton = Find<UnityEngine.UI.Button>("Content/Playback/Play");
        if (nextButton == null) nextButton = Find<UnityEngine.UI.Button>("Content/Playback/Next");
        if (muteButton == null) muteButton = Find<UnityEngine.UI.Button>("Content/Playback/Mute");
        if (seekSlider == null) seekSlider = Find<UnityEngine.UI.Slider>("Content/Playback/Seek");
        if (volumeSlider == null) volumeSlider = Find<UnityEngine.UI.Slider>("Content/Playback/Volume");
        if (volumeLabel == null) volumeLabel = Find<TMP_Text>("Content/Playback/VolumeValue");
        if (speakerIcon == null) speakerIcon = Find<UnityEngine.UI.Image>("Content/Playback/Mute/Icon");
        playbackIcon = Find<UnityEngine.UI.Image>("Content/Playback/Play/Icon");
        if (playIcon == null && playbackIcon != null) playIcon = playbackIcon.sprite;
        platter = Find<RectTransform>("Content/RecordDeck/Turntable/Platter");
        selectedTrack = Find<TMP_Text>("Content/RecordDeck/SelectedTrack");
        repeatStatus = Find<TMP_Text>("Content/RecordDeck/RepeatStatus");
        elapsedLabel = Find<TMP_Text>("Content/Playback/Elapsed");
        durationLabel = Find<TMP_Text>("Content/Playback/Duration");
        rowsRoot = Find<RectTransform>("Content/RecordDeck/Library/Viewport/Rows");
        libraryScroll = Find<UnityEngine.UI.ScrollRect>("Content/RecordDeck/Library");
        emptyLabel = Find<TMP_Text>("Content/RecordDeck/Library/Viewport/Rows/EmptyLabel");
        libraryCount = Find<TMP_Text>("Content/RecordDeck/Library/Count");
        if (selectedTrack != null) selectedTrack.richText = false;
        if (repeatStatus != null) repeatStatus.richText = false;
    }

    T Find<T>(string path) where T : Component
    {
        Transform child = transform.Find(path);
        return child != null ? child.GetComponent<T>() : null;
    }

    void CaptureRowFont()
    {
        TMP_Text source = selectedTrack != null ? selectedTrack : emptyLabel;
        if (source == null) return;
        // Recover the authoring font before copying a label that may already be
        // displaying Korean, so new rows retain the right non-Korean fallback.
        var data = GameDataManager.Instance;
        OSLocalizedFontUtility.Apply(source, LanguageState.zh_CN, data != null ? data.LocalizationData : null);
        rowFont = source.font;
        rowFontMaterial = source.fontSharedMaterial;
        OSLocalizedFontUtility.ApplyCurrent(source);
    }

    void BindPlayer()
    {
        if (playerSubscribed && player != null) return;
        if (osHandle == null) osHandle = OS_Handle.Instance;
        player = osHandle != null ? osHandle.GetComponent<OSGramophonePlayer>() : null;
        playerSubscribed = player != null;
        if (playerSubscribed) player.Changed += HandlePlayerChanged;
    }

    void UnbindPlayer()
    {
        if (playerSubscribed && player != null) player.Changed -= HandlePlayerChanged;
        playerSubscribed = false;
    }

    void HandlePlayerChanged() => RefreshDisplay(false);
    void HandleLanguageChanged(LanguageState language) => RefreshDisplay(true);

    void RefreshDisplay(bool refreshLocalization)
    {
        SyncRows(refreshLocalization);
        string currentId = player != null ? player.CurrentTrackId : null;
        if (refreshLocalization || displayedTrackId != currentId)
        {
            displayedTrackId = currentId;
            bool hasTrack = !string.IsNullOrEmpty(currentId);
            SetLocalized(selectedTrack,
                hasTrack ? OSGramophonePlayer.TrackNameKey(currentId) : NoSelectionKey,
                hasTrack ? OSGramophonePlayer.TrackFallback(currentId) : "未选择曲目");
            SetLocalized(repeatStatus, RepeatKey, "单曲循环");
            if (repeatStatus != null) repeatStatus.gameObject.SetActive(hasTrack);
        }
        if (refreshLocalization)
        {
            OSLocalizedFontUtility.ApplyCurrent(elapsedLabel);
            OSLocalizedFontUtility.ApplyCurrent(durationLabel);
            OSLocalizedFontUtility.ApplyCurrent(volumeLabel);
            SetLocalized(emptyLabel, "ui.os.gramophone.empty", "暂无曲目");
        }
        RefreshControls();
        RefreshVolume();
        RefreshPosition();
    }

    void RefreshControls()
    {
        inputAllowed = CanAcceptInput();
        bool hasPlayer = player != null;
        bool hasLibrary = hasPlayer && player.AvailableTracks != null && player.AvailableTracks.Count > 0;
        bool hasTrack = hasPlayer && !string.IsNullOrEmpty(player.CurrentTrackId);
        if (previousButton != null) previousButton.interactable = inputAllowed && hasLibrary;
        if (playButton != null) playButton.interactable = inputAllowed && (hasTrack || hasLibrary);
        if (nextButton != null) nextButton.interactable = inputAllowed && hasLibrary;
        if (seekSlider != null) seekSlider.interactable = inputAllowed && hasTrack && player.DurationSeconds > 0f;
        if (muteButton != null) muteButton.interactable = inputAllowed && hasPlayer;
        if (volumeSlider != null) volumeSlider.interactable = inputAllowed && hasPlayer;
        if (libraryScroll != null) libraryScroll.enabled = inputAllowed;
        for (int i = 0; i < rows.Count; i++) rows[i].Button.interactable = inputAllowed && hasPlayer;
        if (playbackIcon != null)
            playbackIcon.sprite = hasPlayer && player.IsPlaying && pauseIcon != null ? pauseIcon : playIcon;
    }

    void RefreshVolume()
    {
        volume = NormalizedVolume;
        volumeSlider?.SetValueWithoutNotify(volume);
        int percentage = Mathf.RoundToInt(volume * 100f);
        if (volumeLabel != null && percentage != displayedVolume)
        {
            displayedVolume = percentage;
            volumeLabel.SetText("{0}%", percentage);
        }
        if (speakerIcon != null) speakerIcon.sprite = IsMuted ? mutedIcon : volumeIcon;
    }

    void RefreshPosition()
    {
        bool hasTrack = player != null && !string.IsNullOrEmpty(player.CurrentTrackId);
        float duration = hasTrack ? Mathf.Max(0f, player.DurationSeconds) : 0f;
        float position = hasTrack ? Mathf.Clamp(player.PositionSeconds, 0f, duration) : 0f;
        seekSlider?.SetValueWithoutNotify(duration > 0f ? position / duration : 0f);
        SetClock(elapsedLabel, hasTrack ? Mathf.FloorToInt(position) : -1, ref displayedElapsed);
        SetClock(durationLabel, duration > 0f ? Mathf.FloorToInt(duration) : -1, ref displayedDuration);
    }

    static void SetClock(TMP_Text label, int seconds, ref int previous)
    {
        if (label == null || previous == seconds) return;
        previous = seconds;
        label.text = seconds < 0 ? "--:--" : (seconds / 60).ToString("00") + ":" + (seconds % 60).ToString("00");
    }

    bool CanAcceptInput()
    {
        return isActiveAndEnabled
            && window != null && window.IsOpen && !window.IsMinimized && !window.IsClosing
            && (osHandle != null ? osHandle : OS_Handle.Instance)?.Chapter02PaperPush?.IsHeartTalkAudioTransition != true
            && (osHandle != null ? !osHandle.IsSystemInputLocked : OS_Handle.Instance?.IsSystemInputLocked != true);
    }

    void PlayPrevious() => ChangeTrack(-1);
    void PlayNext() => ChangeTrack(1);

    void ChangeTrack(int direction)
    {
        if (!CanAcceptInput() || player == null) return;
        player.NextTrack(direction);
        PlayFeedback();
    }

    void TogglePlayback()
    {
        if (!CanAcceptInput() || player == null) return;
        player.TogglePlayback();
        PlayFeedback();
    }

    public void ToggleMute()
    {
        if (!CanAcceptInput() || player == null) return;
        player.ToggleMute();
        PlayFeedback();
    }

    void SetVolume(float value)
    {
        if (!CanAcceptInput() || player == null)
        {
            RefreshVolume();
            return;
        }
        player.SetVolume(Mathf.Clamp01(value));
    }

    void Seek(float normalizedPosition)
    {
        if (!CanAcceptInput() || player == null || player.DurationSeconds <= 0f)
        {
            RefreshPosition();
            return;
        }
        player.Seek(Mathf.Clamp01(normalizedPosition) * player.DurationSeconds);
    }

    void SelectTrack(string id)
    {
        if (!CanAcceptInput() || player == null) return;
        // Only this explicit library click consumes the new-track marker.
        player.PlayTrack(id, true);
        PlayFeedback();
    }

    void PlayFeedback() => (osHandle != null ? osHandle : OS_Handle.Instance)?.PlayInteractionFeedback();

    void SyncRows(bool refreshLocalization)
    {
        if (rowsRoot == null) return;
        IReadOnlyList<string> tracks = player != null ? player.AvailableTracks : null;
        int count = tracks != null ? tracks.Count : 0;
        for (int i = rows.Count - 1; i >= 0; i--)
        {
            bool exists = false;
            for (int j = 0; j < count; j++)
                if (rows[i].Id == tracks[j]) { exists = true; break; }
            if (exists) continue;
            rows[i].Rect.gameObject.SetActive(false);
            Destroy(rows[i].Rect.gameObject);
            rows.RemoveAt(i);
            rowsLayoutDirty = true;
        }
        for (int i = 0; i < count; i++)
        {
            TrackRow row = null;
            for (int j = 0; j < rows.Count; j++)
                if (rows[j].Id == tracks[i]) { row = rows[j]; break; }
            if (row == null)
            {
                row = CreateRow(tracks[i]);
                rows.Add(row);
                LocalizeRow(row);
            }
            else if (refreshLocalization) LocalizeRow(row);
            row.Rect.SetSiblingIndex(i + (emptyLabel != null ? 1 : 0));
            bool isNew = player.IsNew(row.Id);
            row.Title.color = isNew ? NewColor : ReadColor;
            if (row.IsNew != isNew)
            {
                row.IsNew = isNew;
                row.Dot.gameObject.SetActive(isNew);
                rowsLayoutDirty = true;
            }
        }
        if (emptyLabel != null && emptyLabel.gameObject.activeSelf != (count == 0))
        {
            emptyLabel.gameObject.SetActive(count == 0);
            rowsLayoutDirty = true;
        }
        if (libraryCount != null) libraryCount.SetText("{0}", count);
    }

    TrackRow CreateRow(string id)
    {
        var row = new TrackRow { Id = id };
        var root = new GameObject("Track_" + id, typeof(RectTransform), typeof(UnityEngine.UI.Image), typeof(UnityEngine.UI.Button), typeof(UnityEngine.UI.LayoutElement));
        root.layer = rowsRoot.gameObject.layer;
        root.transform.SetParent(rowsRoot, false);
        row.Rect = (RectTransform)root.transform;
        var layout = root.GetComponent<UnityEngine.UI.LayoutElement>();
        layout.minHeight = RowHeight;
        layout.preferredHeight = RowHeight;
        layout.flexibleHeight = 0f;
        var hit = root.GetComponent<UnityEngine.UI.Image>();
        hit.color = new Color(0f, 0f, 0f, .01f);
        hit.raycastTarget = true;

        var titleObject = new GameObject("Title", typeof(RectTransform), typeof(TextMeshProUGUI));
        titleObject.layer = root.layer;
        titleObject.transform.SetParent(root.transform, false);
        row.Title = titleObject.GetComponent<TextMeshProUGUI>();
        row.Title.font = rowFont;
        if (rowFontMaterial != null) row.Title.fontSharedMaterial = rowFontMaterial;
        row.Title.fontSize = 8f;
        row.Title.richText = false;
        row.Title.textWrappingMode = TextWrappingModes.NoWrap;
        row.Title.overflowMode = TextOverflowModes.Ellipsis;
        row.Title.alignment = TextAlignmentOptions.MidlineLeft;
        row.Title.raycastTarget = false;
        RectTransform textRect = row.Title.rectTransform;
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.pivot = new Vector2(0f, .5f);
        textRect.offsetMin = Vector2.zero;
        textRect.offsetMax = new Vector2(-8f, 0f);

        var dotObject = new GameObject("NewDot", typeof(RectTransform), typeof(UnityEngine.UI.Image));
        dotObject.layer = root.layer;
        dotObject.transform.SetParent(root.transform, false);
        row.Dot = dotObject.GetComponent<UnityEngine.UI.Image>();
        row.Dot.sprite = GetDotSprite();
        row.Dot.color = NewColor;
        row.Dot.raycastTarget = false;
        row.Dot.rectTransform.anchorMin = row.Dot.rectTransform.anchorMax = new Vector2(0f, .5f);
        row.Dot.rectTransform.pivot = new Vector2(0f, .5f);
        row.Dot.rectTransform.sizeDelta = new Vector2(4f, 4f);
        dotObject.SetActive(false);

        row.Button = root.GetComponent<UnityEngine.UI.Button>();
        row.Button.targetGraphic = row.Title;
        row.Button.navigation = new UnityEngine.UI.Navigation { mode = UnityEngine.UI.Navigation.Mode.None };
        var colors = row.Button.colors;
        colors.normalColor = Color.white;
        colors.highlightedColor = Color.white;
        colors.selectedColor = Color.white;
        colors.pressedColor = new Color(.75f, .75f, .75f, 1f);
        colors.disabledColor = new Color(.5f, .5f, .5f, 1f);
        colors.fadeDuration = .08f;
        row.Button.colors = colors;
        row.Button.onClick.AddListener(() => SelectTrack(id));
        rowsLayoutDirty = true;
        return row;
    }

    void LocalizeRow(TrackRow row)
    {
        SetLocalized(row.Title, OSGramophonePlayer.TrackNameKey(row.Id), OSGramophonePlayer.TrackFallback(row.Id));
        rowsLayoutDirty = true;
    }

    void SetLocalized(TMP_Text label, string key, string fallback)
    {
        if (label == null) return;
        OSLocalizedText.SetRuntime(label, key, fallback);
        var binding = label.GetComponent<OSLocalizedText>();
        if (binding != null) binding.Refresh(osHandle != null ? osHandle : OS_Handle.Instance);
        OSLocalizedFontUtility.ApplyCurrent(label);
    }

    void PositionNewDot(TrackRow row)
    {
        if (!row.IsNew) return;
        row.Title.ForceMeshUpdate();
        TMP_TextInfo textInfo = row.Title.textInfo;
        float right = 0f;
        bool hasVisibleCharacter = false;
        for (int i = 0; i < textInfo.characterCount; i++)
        {
            var character = textInfo.characterInfo[i];
            if (!character.isVisible) continue;
            hasVisibleCharacter = true;
            right = Mathf.Max(right, character.topRight.x);
        }
        row.Dot.enabled = hasVisibleCharacter;
        // Follow rendered glyphs, including an ellipsis for a long translation.
        row.Dot.rectTransform.anchoredPosition = new Vector2(Mathf.Round(right + 4f), .5f);
    }

    Sprite GetDotSprite()
    {
        if (dotSprite != null) return dotSprite;
        dotTexture = new Texture2D(4, 4, TextureFormat.RGBA32, false)
        {
            name = "GramophoneNewDot",
            filterMode = FilterMode.Point,
            wrapMode = TextureWrapMode.Clamp,
            hideFlags = HideFlags.HideAndDontSave
        };
        var clear = new Color32(255, 255, 255, 0);
        var white = new Color32(255, 255, 255, 255);
        dotTexture.SetPixels32(new[] { clear, white, white, clear, white, white, white, white,
            white, white, white, white, clear, white, white, clear });
        dotTexture.Apply(false, true);
        dotSprite = Sprite.Create(dotTexture, new Rect(0f, 0f, 4f, 4f), new Vector2(.5f, .5f), 1f, 0, SpriteMeshType.FullRect);
        dotSprite.name = "GramophoneNewDot";
        dotSprite.hideFlags = HideFlags.HideAndDontSave;
        return dotSprite;
    }
}
