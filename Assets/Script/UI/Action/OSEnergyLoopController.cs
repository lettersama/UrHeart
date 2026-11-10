using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Lives on the OS root, not on the closable charger window: Live options remain valid after closing it.
[DisallowMultipleComponent]
public sealed class OSEnergyLoopController : MonoBehaviour
{
    public const string DiscoverySignal = "energy.charge_started";
    public const string DiscoveryEntry = "energy_find_minigame";
    public const string FindOption = "energy_find";
    [SerializeField] OSWindowUnit window;
    [SerializeField] OSLinkMessageController linkMessages;
    [SerializeField] Button startButton;
    [SerializeField] TMP_Text startLabel;
    [SerializeField] RectTransform chargingRoot;
    [SerializeField] Image batteryFill;
    [SerializeField] OSEnergyChargeGraphic chargeGraphic;
    [SerializeField] OSUIEnergyClaimEffect claimEffect;
    [SerializeField] float batteryFillWidth = 53f;
    [SerializeField] TMP_Text statusLabel;
    [SerializeField] TMP_Text timeLabel;
    [SerializeField] TMP_Text chargeRewardLabel;
    [SerializeField] RectTransform discoveryRoot;
    [SerializeField] Button discoveryButton;
    [SerializeField] TMP_Text discoveryLabel;
    [SerializeField, Min(1f)] float chargeDurationSeconds = GameDataManager.EnergyChargeDurationSeconds;
    bool promptQueued;
    bool popupVisible;
    Sequence popupTween;

    public float ChargeDurationSeconds => chargeDurationSeconds;
    public bool IsDiscoveryVisible => discoveryRoot != null && discoveryRoot.gameObject.activeSelf;

    void OnEnable()
    {
        startButton?.onClick.AddListener(StartCharging);
        discoveryButton?.onClick.AddListener(UseDiscoveredGame);
        if (linkMessages != null) linkMessages.OptionSelected += OnLiveOption;
        if (window != null) window.OpenStarted += OnWindowOpened;
        Refresh();
    }

    void OnDisable()
    {
        startButton?.onClick.RemoveListener(StartCharging);
        discoveryButton?.onClick.RemoveListener(UseDiscoveredGame);
        if (linkMessages != null) linkMessages.OptionSelected -= OnLiveOption;
        if (window != null) window.OpenStarted -= OnWindowOpened;
        popupTween?.Kill();popupTween = null;
    }

    void Update()
    {
        var data = GameDataManager.Instance;
        if (data == null) return;
        var state = data.OSEnergyLoopState;
        bool visible = window != null && window.IsOpen && !window.IsMinimized && OS_Handle.Instance?.IsOSVisible == true;
        if (state.isCharging || state.completedChargeCount > 0) TryQueueDiscovery();
        if (visible) Refresh();
    }

    public void StartCharging()
    {
        if (OS_Handle.Instance?.IsSystemInputLocked == true || window == null || !window.IsOpen) return;
        var data = GameDataManager.Instance;
        if (data != null && data.OSEnergyLoopState.chargeReadyToClaim)
        {
            Vector3 origin = startButton.transform.TransformPoint(((RectTransform)startButton.transform).rect.center);
            if (!data.TryClaimEnergyCharge(out int gained)) return;
            if (gained > 0) claimEffect?.Play(window, origin);
            OS_Handle.Instance?.PlayInteractionFeedback();
            Refresh();return;
        }
        if (GameDataManager.Instance?.TryBeginEnergyCharge(chargeDurationSeconds) != true) return;
        RearmDismissedDiscovery();
        TryQueueDiscovery();
        OS_Handle.Instance?.PlayInteractionFeedback();
        Refresh();
    }

    void OnWindowOpened(OSWindowUnit source)
    { RearmDismissedDiscovery();Refresh(); }

    void TryQueueDiscovery()
    {
        var data = GameDataManager.Instance;
        var os = OS_Handle.Instance;
        if (promptQueued || linkMessages == null || data == null || data.CurrentChapterIndex != 1
            || data.OSEnergyLoopState.minigameDiscovered || data.IsPaperPushUnlocked
            || os == null || !os.IsOSVisible || os.IsSystemInputLocked
            || OSSessionPersistence.IsRestoring || GameSessionFlow.Instance?.IsBusy == true) return;
        promptQueued = linkMessages.NotifyGlobalExternalSignal(DiscoverySignal);
    }

    void RearmDismissedDiscovery()
    {
        var data = GameDataManager.Instance;
        if (data == null || data.CurrentChapterIndex != 1 || data.OSEnergyLoopState.minigameDiscovered || data.IsPaperPushUnlocked) return;
        var live = data.OSCharacterInteractionState;
        if (live.presentingGlobalEntryId == DiscoveryEntry || live.pendingGlobalEntryId == DiscoveryEntry) return;
        // Dismissing Live must not permanently remove the only way to find the game.
        // Retry only on the next deliberate charger open/start, not repeatedly while it stays open.
        live.SetGlobalEntryConsumed(DiscoveryEntry, false);
        promptQueued = false;
    }

    void OnLiveOption(string sequence, OSCharacterInteractionEntryData entry, OSCharacterInteractionOptionData option)
    {
        if (GameDataManager.Instance?.CurrentChapterIndex != 1 || entry == null || option == null || entry.EntryId != DiscoveryEntry || option.OptionId != FindOption) return;
        GameDataManager.Instance?.DiscoverPaperPush();
        ShowDiscovery(true);
    }

    public void UseDiscoveredGame()
    {
        var data = GameDataManager.Instance;
        if (data == null || data.CurrentChapterIndex != 1 || OS_Handle.Instance?.IsSystemInputLocked == true) return;
        if (data.UnlockPaperPush())
        {
            AdditionHandle.Instance?.ShowTip(Text("unlocked", "找到了！「纸偶对推」已放到桌面。"));
            OS_Handle.Instance?.PlayInteractionFeedback();
        }
        // Opening through the same desktop entry point also consumes its New badge.
        if (data.IsPaperPushUnlocked) OS_Handle.Instance?.OpenPaperPushWindow();
        Refresh();
    }

    void ShowDiscovery(bool animate)
    {
        if (discoveryRoot == null) return;
        discoveryRoot.gameObject.SetActive(true);popupVisible = true;
        popupTween?.Kill();popupTween = null;
        if (!animate)
        { discoveryRoot.anchoredPosition = new Vector2(0f, -22f);discoveryRoot.localScale = Vector3.one;return; }
        discoveryRoot.anchoredPosition = new Vector2(0f, 4f);
        discoveryRoot.localScale = new Vector3(.6f, .25f, 1f);
        popupTween = DOTween.Sequence().SetUpdate(true)
            .Append(discoveryRoot.DOAnchorPosY(-22f, .5f).SetEase(Ease.OutBounce))
            .Join(discoveryRoot.DOScale(Vector3.one, .35f).SetEase(Ease.OutBack, 1.6f));
    }

    void Refresh()
    {
        var data = GameDataManager.Instance;if (data == null) return;
        var s = data.OSEnergyLoopState;
        bool coolingDown = data.IsEnergyChargeCoolingDown;
        if (startButton != null)
        {
            startButton.gameObject.SetActive(!s.isCharging);
            startButton.interactable = !s.isCharging && (s.chargeReadyToClaim || !coolingDown);
            ((RectTransform)startButton.transform).anchoredPosition = new Vector2(0f, s.chargeReadyToClaim || s.completedChargeCount > 0 ? -58f : 0f);
        }
        if (chargingRoot != null) chargingRoot.gameObject.SetActive(s.isCharging || s.chargeReadyToClaim || coolingDown || s.completedChargeCount > 0);
        float progress = s.isCharging ? 1f - s.remainingChargeSeconds / Mathf.Max(1f, s.chargeDurationSeconds) : 1f;
        if (chargeGraphic != null) chargeGraphic.SetCharge(progress, s.isCharging);
        OSLocalizedText.SetRuntime(startLabel, s.chargeReadyToClaim
            ? Line("claim", "领取能量 +{0}", GameDataManager.EnergyChargeReward)
            : coolingDown ? Line("cooldown", "冷却中") : Line("start", "开始充电"));
        if (batteryFill != null)
        {
            batteryFill.rectTransform.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal,
                Mathf.Round(batteryFillWidth * Mathf.Clamp01(progress)));
            batteryFill.color = new Color32(135, 130, 126, (byte)(s.isCharging ? 185 + 70 * (.5f + .5f * Mathf.Sin(Time.unscaledTime * 4f)) : 255));
        }
        int seconds = Mathf.CeilToInt(s.remainingChargeSeconds);
        int cooldownSeconds = Mathf.CeilToInt(s.remainingCooldownSeconds);
        OSLocalizedText.SetRuntime(timeLabel, s.isCharging
            ? Line("remaining", "本次充电剩余 {0:00}:{1:00}", seconds / 60, seconds % 60)
            : coolingDown ? Line("cooldown_remaining", "再次充电还需 {0:00}:{1:00}", cooldownSeconds / 60, cooldownSeconds % 60)
            : s.chargeReadyToClaim ? Line("claim_hint", "点击下方按钮领取")
            : Line("claimed", "已领取，能量 +{0}", s.lastEnergyGained));
        if (statusLabel != null) statusLabel.gameObject.SetActive(!s.isCharging);
        if (timeLabel != null) timeLabel.rectTransform.anchoredPosition = new Vector2(0, s.isCharging ? -18f : -37f);
        if (chargeRewardLabel != null)
        {
            chargeRewardLabel.gameObject.SetActive(s.isCharging);
            OSLocalizedText.SetRuntime(chargeRewardLabel,
                Line("charge_reward", "一次充电恢复 {0} 点能量", GameDataManager.EnergyChargeReward));
        }
        OSLocalizedText.SetRuntime(statusLabel, s.chargeReadyToClaim
            ? Line("ready_to_claim", "充电完成") : Line("ready", "电池已补充"));
        if (data.CurrentChapterIndex == 1 && s.minigameDiscovered && !s.minigameUnlocked)
        {
            if (!popupVisible) ShowDiscovery(false);
            OSLocalizedText.SetRuntime(discoveryLabel, Line("found_start", "开始"));
        }
        else if (discoveryRoot != null)
        { popupTween?.Kill();popupTween = null;popupVisible = false;discoveryRoot.gameObject.SetActive(false); }
    }

    static string Text(string id, string fallback) => GameDataManager.Instance?.GetLocalizedText("ui.os.energy." + id, fallback) ?? fallback;
    static OSLocalizedText.Line Line(string id, string fallback, params object[] arguments) => new OSLocalizedText.Line("ui.os.energy." + id, fallback, arguments);
}
