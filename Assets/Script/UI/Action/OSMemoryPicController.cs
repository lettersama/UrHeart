using System.Collections;
using MrTGame.Core;
using MrTGame.Narrative;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

[DisallowMultipleComponent]
public sealed class OSMemoryPicController : MonoBehaviour
{
    public OSMemoryPicScenarioData scenario;
    public OSMemoryWindowController memory;
    public OSWindowUnit pictureWindow;
    public RectTransform sceneRoot;
    public Image blackout;
    public RectTransform tooltip;
    public TMP_Text tooltipLabel;
    public OSMemoryPicInventory inventory;
    public TMP_Text windowTitle;
    public AudioSource audioSource;
    public UnityEvent onGlassBreak;
    public UnityEvent onReadyForDoorJumpscare;

    OSMemoryPicSaveData state;
    OSMemoryPicSceneView view;
    OSMemoryPicSceneData currentScene;
    OSMemoryPicEffect runningEffect;
    Object tipOwner;
    string tipKey;
    Vector2 tipPosition;
    int tipEnergyCost;
    float spentTipHoldUntil = -1f;
    bool IsSpentTipHeld => Time.unscaledTime < spentTipHoldUntil;
    bool busy;
    int selectedWire = -1;
    int lastSubmitFrame = -1;
    public OSMemoryPicSaveData State => state;
    public OSMemoryPicSceneData CurrentScene => currentScene;
    public bool IsBusy => busy;
    public bool CanInteract => IsContextVisible() && state != null && !busy;
    public int SelectedWire => selectedWire;

    void OnEnable() => GameEvents.LanguageChanged += HandleLanguageChanged;
    void OnDisable()
    {
        GameEvents.LanguageChanged -= HandleLanguageChanged;
        StopAllCoroutines();
        runningEffect?.Cancel();
        runningEffect = null;
        busy = false;
        selectedWire = -1;
        if (blackout != null) blackout.gameObject.SetActive(false);
        if (sceneRoot != null) sceneRoot.anchoredPosition = Vector2.zero;
        HideTip(null, true);
    }

    bool IsContextVisible()
    {
        GameDataManager data = GameDataManager.Instance;
        OS_Handle os = OS_Handle.Instance;
        OSWindowUnit parentWindow = memory != null ? memory.GetComponent<OSWindowUnit>() : null;
        return isActiveAndEnabled && scenario != null && data != null
            && data.IsMemoryInteractiveSceneActive
            && data.OSProcessData.memory.interactiveSceneDataIndex == scenario.entryMemoryIndex
            && pictureWindow != null && pictureWindow.IsOpen && !pictureWindow.IsOpening && !pictureWindow.IsClosing
            && parentWindow != null && parentWindow.IsOpen && !parentWindow.IsMinimized && !parentWindow.IsOpening
            && (os == null || (os.IsOSVisible && !os.IsSystemInputLocked));
    }

    void Update()
    {
        if (!IsContextVisible())
        {
            if (busy)
            {
                StopAllCoroutines(); runningEffect?.Cancel(); runningEffect = null; busy = false;
                if (blackout != null) blackout.gameObject.SetActive(false);
                if (sceneRoot != null) sceneRoot.anchoredPosition = Vector2.zero;
            }
            selectedWire = -1;
            HideTip(null, true);
            return;
        }
        if (spentTipHoldUntil >= 0f && !IsSpentTipHeld) HideTip(null, true);
        var saved = GameDataManager.Instance.OSProcessData.memory.pictureScenario;
        if (state != saved)
        {
            HideTip(null, true);
            StopAllCoroutines(); runningEffect?.Cancel(); runningEffect = null;
            state = saved; state.EnsureData(); busy = false; selectedWire = -1;
            // Older saves kept the repair's power-off flag after a successful test.
            if (state.Has("robot_repaired")) state.flags.Remove("power_off");
            if (blackout != null) blackout.gameObject.SetActive(false);
            if (sceneRoot != null) sceneRoot.anchoredPosition = Vector2.zero;
            if (!state.initialized || state.scenarioId != scenario.scenarioId || state.exited)
            {
                state.scenarioId = scenario.scenarioId;
                state.currentSceneId = scenario.firstSceneId;
                state.flags.Clear(); state.inventory.Clear();
                state.inventory.AddRange(scenario.initialItems);
                state.initialized = true; state.exited = false;
                state.selectedItemId = null;
                if (memory.CurrentData?.DataIndex == scenario.entryMemoryIndex) Speak(scenario.firstMemoryIndex);
            }
            RenderScene(true);
        }
        if (!busy && (currentScene == null || currentScene.sceneId != state.currentSceneId)) RenderScene();
        if (!busy && state.Has("glass_pending")) StartCoroutine(PlayPendingEvent(false));
        else if (!busy && state.Has("knock_pending")) StartCoroutine(PlayPendingEvent(true));
    }

    public void FocusPicture()
    {
        if (pictureWindow != null) OS_Handle.Instance?.NotifyWindowPointerPressed(pictureWindow);
    }

    public bool HasNavigationTarget(OSMemoryPicHotspotData hotspot)
    {
        if (state == null || scenario == null || hotspot == null
            || hotspot.kind != OSMemoryPicHotspotKind.Action || !hotspot.visibleWhen.Matches(state)) return false;
        // Use the first matching outcome, including a story gate that only speaks.
        // Looking for any later destination would let an arrow bypass that gate.
        var outcome = hotspot.FindOutcome(state);
        return outcome != null && !string.IsNullOrEmpty(outcome.targetScene)
            && scenario.FindScene(outcome.targetScene)?.contentPrefab != null;
    }

    public bool TryNavigate(string hotspotId, OSMemoryPicEffect effect = null)
    {
        var hotspot = currentScene?.FindHotspot(hotspotId);
        if (state == null || hotspot == null || hotspot.kind != OSMemoryPicHotspotKind.Action) return false;
        if (!HasNavigationTarget(hotspot))
        {
            var outcome = hotspot.FindOutcome(state);
            // Only explicitly authored story gates speak from an unavailable arrow.
            if (outcome == null || !outcome.speakWhenNavigationBlocked
                || !string.IsNullOrEmpty(outcome.targetScene)) return false;
        }
        return TryInteract(hotspotId, effect);
    }

    public bool TryInteract(string hotspotId, OSMemoryPicEffect effect = null)
    {
        if (!CanInteract || lastSubmitFrame == Time.frameCount || currentScene == null) return false;
        var hotspot = currentScene.FindHotspot(hotspotId);
        if (hotspot == null || !hotspot.visibleWhen.Matches(state)) return false;
        if (hotspot.kind != OSMemoryPicHotspotKind.Action) return InteractWire(hotspot);
        var ending = OS_Handle.Instance?.Chapter01Ending;
        if (ending != null && ending.TryBeginFromRoomExit(this, hotspotId))
        {
            lastSubmitFrame = Time.frameCount;
            HideTip(null, true);
            return true;
        }
        OSMemoryPicOutcome outcome = hotspot.FindOutcome(state);
        if (outcome == null || !Speak(outcome.memoryIndex)) return false;
        lastSubmitFrame = Time.frameCount;
        if (outcome.energyCost > 0 && view != null)
            foreach (var hotspotView in view.GetComponentsInChildren<OSMemoryPicHotspot>(true))
                if (hotspotView.Data == hotspot) { ShowHotspotTip(hotspotView); break; }
        Commit(outcome);
        if (SpendEnergy(outcome.energyCost) && tooltip != null && tooltip.gameObject.activeSelf
            && OSUIEnergySpendPresenter.Show(pictureWindow, tooltip, outsideRight: true))
            spentTipHoldUntil = Time.unscaledTime + pictureWindow.GetComponent<OSUIEnergySpendEffect>().TotalDuration;
        HideTip(null);
        PlaySound(outcome.soundCue);
        if (string.IsNullOrEmpty(outcome.targetScene) && outcome.effectDuration <= 0f && effect == null)
        {
            RefreshView();
            return true;
        }
        busy = true;
        StartCoroutine(PlayOutcome(outcome, effect));
        return true;
    }

    bool Speak(int index) => index >= 0 && memory != null && memory.ShowInteractionData(index);

    bool SpendEnergy(int cost)
    {
        var data = GameDataManager.Instance;
        if (cost <= 0 || data == null) return false;
        int before = data.GetOSResourceValue(ResourceType.Battery);
        var resources = MrTGame.Systems.GameResourceSystem.Instance;
        if (resources != null) resources.Modify(ResourceType.Battery, -cost);
        else
        {
            int value = data.ModifyOSResourceValue(ResourceType.Battery, -cost);
            GameEvents.RaiseResourceChanged(ResourceType.Battery, value, 100);
        }
        return data.GetOSResourceValue(ResourceType.Battery) < before;
    }

    void Commit(OSMemoryPicOutcome outcome)
    {
        foreach (string flag in outcome.removeFlags) state.flags.Remove(flag);
        foreach (string flag in outcome.addFlags) state.Set(flag);
        foreach (string item in outcome.removeItems) state.inventory.Remove(item);
        foreach (string item in outcome.addItems) if (!state.inventory.Contains(item)) state.inventory.Add(item);
        if (!state.inventory.Contains(state.selectedItemId)) state.selectedItemId = null;
        if (!string.IsNullOrEmpty(outcome.targetScene)) state.currentSceneId = outcome.targetScene;
        if (outcome.glassStartle) state.Set("glass_pending");
        if (outcome.finishRepair)
        {
            state.flags.Remove("power_off");
            state.Set("robot_repaired");
            state.Set("knock_pending");
        }
        bool allNails = true;
        for (int i = 0; i < 6; i++) if (!state.Has("nail_" + i + "_done")) allNails = false;
        if (allNails) { state.Set("window_fixed"); state.inventory.Remove("nails"); }
    }

    IEnumerator PlayOutcome(OSMemoryPicOutcome outcome, OSMemoryPicEffect effect)
    {
        runningEffect = effect;
        if (effect != null) yield return effect.Play(this);
        runningEffect = null;
        if (outcome.effectDuration > 0f) yield return new WaitForSecondsRealtime(outcome.effectDuration);
        if (!string.IsNullOrEmpty(outcome.targetScene))
        {
            blackout.gameObject.SetActive(true);
            yield return new WaitForSecondsRealtime(.14f);
            RenderScene();
            yield return new WaitForSecondsRealtime(.10f);
            blackout.gameObject.SetActive(false);
        }
        else RefreshView();
        busy = false;
    }

    void RenderScene(bool force = false)
    {
        var next = scenario.FindScene(state.currentSceneId);
        if (next == null || next.contentPrefab == null)
        {
            Debug.LogError("[Memory Picture] Missing scene/prefab: " + state.currentSceneId, this);
            return;
        }
        if (force || next != currentScene || view == null)
        {
            HideTip(null); selectedWire = -1;
            if (view != null) { view.gameObject.SetActive(false); Destroy(view.gameObject); }
            currentScene = next;
            view = Instantiate(next.contentPrefab, sceneRoot, false);
            view.name = next.contentPrefab.name;
            view.Bind(this, next, state);
        }
        RefreshView();
        RefreshTitle();
        if (scenario.scenarioId == "chapter01_home")
        {
            StoryAchievementProgress.Record(StoryAchievementMilestone.RobotBroughtHome);
            OSGramophonePlayer.Instance?.NotifyChapter01HomeEntered();
        }
    }

    void RefreshView()
    {
        view?.Refresh(state);
        inventory?.Refresh(this, scenario, state);
    }

    void PlaySound(string cue)
    {
        if (string.IsNullOrEmpty(cue) || audioSource == null) return;
        AudioClip clip = scenario.FindSound(cue);
        if (clip != null) audioSource.PlayOneShot(clip);
    }

    IEnumerator PlayPendingEvent(bool knock)
    {
        busy = true;
        HideTip(null);
        yield return new WaitForSecondsRealtime(knock ? 1.1f : .65f);
        if (!IsContextVisible()) { busy = false; yield break; }
        if (!Speak(knock ? scenario.knockMemoryIndex : scenario.glassMemoryIndex))
        {
            Debug.LogError("[Memory Picture] Missing event memory data.", this);
            busy = false;
            yield break;
        }
        state.flags.Remove(knock ? "knock_pending" : "glass_pending");
        if (knock)
        {
            state.Set("knock_heard");
            PlaySound("knock");
            onReadyForDoorJumpscare?.Invoke();
        }
        else
        {
            state.Set("glass_broken");
            PlaySound("glass");
            onGlassBreak?.Invoke();
            blackout.gameObject.SetActive(true);
            yield return new WaitForSecondsRealtime(.06f);
            blackout.gameObject.SetActive(false);
            sceneRoot.anchoredPosition = new Vector2(2, 0);
            yield return new WaitForSecondsRealtime(.06f);
            sceneRoot.anchoredPosition = new Vector2(-2, 0);
            yield return new WaitForSecondsRealtime(.06f);
            sceneRoot.anchoredPosition = Vector2.zero;
        }
        RefreshView();
        busy = false;
    }

    bool InteractWire(OSMemoryPicHotspotData hotspot)
    {
        int index = hotspot.socketIndex;
        if (!state.Has("power_off")) return RecordPuzzleMessage(scenario.powerRequiredMemoryIndex);
        if (hotspot.kind == OSMemoryPicHotspotKind.WirePlug)
        {
            if (state.Has("wire_" + index)) return RecordPuzzleMessage(scenario.alreadyConnectedMemoryIndex);
            if (!RecordPuzzleMessage(scenario.selectWireMemoryIndex)) return false;
            selectedWire = index;
            return true;
        }
        if (selectedWire < 0) return RecordPuzzleMessage(scenario.cableRequiredMemoryIndex);
        if (selectedWire != index) return RecordPuzzleMessage(scenario.wrongSocketMemoryIndex);
        if (state.Has("wire_" + index)) return RecordPuzzleMessage(scenario.alreadyConnectedMemoryIndex);
        if (!RecordPuzzleMessage(scenario.connectedWireMemoryIndex)) return false;
        state.Set("wire_" + index);
        selectedWire = -1;
        bool complete = true;
        for (int i = 0; i < 4; i++) if (!state.Has("wire_" + i)) complete = false;
        if (complete) state.Set("wires_connected");
        PlaySound("connect");
        RefreshView();
        return true;
    }

    bool RecordPuzzleMessage(int index)
    {
        if (!Speak(index)) return false;
        lastSubmitFrame = Time.frameCount;
        return true;
    }

    public bool SelectInventoryItem(string itemId)
    {
        if (!CanInteract || lastSubmitFrame == Time.frameCount || !state.inventory.Contains(itemId)) return false;
        var item = scenario.FindItem(itemId);
        if (item == null || !Speak(item.memoryIndex)) return false;
        lastSubmitFrame = Time.frameCount;
        state.selectedItemId = itemId;
        inventory.Refresh(this, scenario, state);
        return true;
    }

    public void ShowHotspotTip(OSMemoryPicHotspot hotspot)
    {
        if (hotspot.Data == null) return;
        RectTransform target = hotspot.transform as RectTransform;
        if (!hotspot.navigationOnly)
        {
            // A state swap can move the visible prop (for example, clean dishes).
            // Anchor its caption to the visible artwork, not both states' hit area.
            foreach (Image graphic in hotspot.highlightedGraphics)
                if (graphic != null && graphic.gameObject.activeInHierarchy)
                {
                    target = graphic.rectTransform;
                    break;
                }
        }
        var outcome = hotspot.Data.FindOutcome(state);
        string label = outcome != null && !string.IsNullOrEmpty(outcome.tooltipLabelKey)
            ? outcome.tooltipLabelKey : hotspot.Data.labelKey;
        ShowTip(hotspot, label, target, outcome != null ? outcome.energyCost : 0);
    }

    public void ShowTip(Object source, string key, RectTransform target, int energyCost = 0)
    {
        if (IsSpentTipHeld || !CanInteract || tooltip == null || tooltipLabel == null || target == null) return;
        tipOwner = source; tipKey = key; tipEnergyCost = Mathf.Max(0, energyCost);
        tipPosition = (Vector2)sceneRoot.InverseTransformPoint(target.position)
            + Vector2.up * (target.rect.height * .5f + 5f);
        RefreshTip();
    }

    void RefreshTip()
    {
        if (tipOwner == null || tooltip == null || tooltipLabel == null) return;
        OSLocalizedFontUtility.ApplyCurrent(tooltipLabel);
        tooltipLabel.text = OSResourceInlineIcons.Format(tooltipLabel, Localize(tipKey), energy: -tipEnergyCost);
        // Measure with the actual wrap width; translated labels can need several lines.
        const float paddingX = 12f, paddingY = 6f, edge = 4f;
        float batterySpace = tipEnergyCost > 0 ? OSUIEnergySpendEffect.OutsideRightSpace : 0f;
        Rect area = ((RectTransform)tooltip.parent).rect;
        float maxWidth = Mathf.Min(202f, area.width - edge * 2f - batterySpace);
        float width = Mathf.Clamp(Mathf.Ceil(tooltipLabel.GetPreferredValues(tooltipLabel.text).x)
            + paddingX, 28f, maxWidth);
        Vector2 preferred = tooltipLabel.GetPreferredValues(tooltipLabel.text, width - paddingX, Mathf.Infinity);
        float height = Mathf.Max(17f, Mathf.Ceil(preferred.y) + paddingY);
        RectTransform textRect = tooltipLabel.rectTransform;
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = new Vector2(paddingX * .5f, paddingY * .5f);
        textRect.offsetMax = -textRect.offsetMin;
        tooltip.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, width);
        tooltip.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, height);
        tooltip.anchoredPosition = new Vector2(
            Mathf.Round(Mathf.Clamp(tipPosition.x, area.xMin + edge + width * tooltip.pivot.x,
                area.xMax - edge - batterySpace - width * (1f - tooltip.pivot.x))),
            Mathf.Round(Mathf.Clamp(tipPosition.y, area.yMin + edge + height * tooltip.pivot.y,
                area.yMax - edge - height * (1f - tooltip.pivot.y))));
        tooltip.gameObject.SetActive(true);
    }

    public void HideTip(Object source, bool force = false)
    {
        if (!force && IsSpentTipHeld) return;
        if (source != null && tipOwner != source) return;
        tipOwner = null; tipKey = null; tipEnergyCost = 0; spentTipHoldUntil = -1f;
        if (tooltip != null) tooltip.gameObject.SetActive(false);
    }

    public string Localize(string key) => GameDataManager.Instance != null
        ? GameDataManager.Instance.GetLocalizedText(key) : key;

    void RefreshTitle()
    {
        if (windowTitle == null || currentScene == null) return;
        OSLocalizedFontUtility.ApplyCurrent(windowTitle);
        windowTitle.text = Localize(currentScene.titleKey);
    }

    void HandleLanguageChanged(LanguageState language)
    {
        RefreshTitle();
        RefreshTip();
    }

    // Also callable while this window is inactive, e.g. by a later chapter's node.
    public void NotifyMemoryNode(int dataIndex)
    {
        var data = GameDataManager.Instance;
        if (scenario == null || data == null || !data.IsMemoryInteractiveSceneActive) return;
        var saved = data.OSProcessData.memory;
        if (saved.interactiveSceneDataIndex != scenario.entryMemoryIndex) return;
        if (scenario.OwnsMemory(dataIndex))
        {
            bool chapterEnded = false;
            foreach (var chapter in data.OSMemoryChapterProgressRanges)
                if (chapter.Contains(scenario.entryMemoryIndex) && data.CurrentChapterIndex > chapter.ChapterSequence)
                    chapterEnded = true;
            if (!chapterEnded) return;
        }
        saved.pictureScenario.Exit();
        saved.interactiveSceneDataIndex = OSMemoryProcessSaveData.InvalidDataIndex;
    }

    public bool ExitScenario(int nextMemoryIndex)
    {
        if (scenario == null || scenario.OwnsMemory(nextMemoryIndex)) return false;
        return memory != null && memory.ShowData(nextMemoryIndex);
    }
}
