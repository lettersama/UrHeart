using DG.Tweening;
using UnityEngine;

public sealed partial class OSStateCheckWindowController
{
    // A display-only route. Never put its states into unitOpen/unitPowered:
    // autosave and the real completion event must see the player's board.
    static readonly int[] FaultGuideRoute = { 0, 1, 2, 6, 7, 11, 15 };
    [Header("Fault Guidance")]
    [SerializeField] Color faultHeartUnpoweredColor = new Color32(0x98, 0x42, 0x3c, 0xff);
    [SerializeField, Min(1f)] float faultHintInterval = 4f;

    Sequence faultGuideSequence;
    Sequence faultHintSequence;
    bool faultGuideSeen;
    bool faultBoardInteracted;
    bool faultBoardInitialized;
    bool faultGuideHeartPowered;
    float faultHintRemaining;
    int faultHoveredNode = -1;
    int faultHintNode = -1;
    LineState faultHintLine;
    Vector3 faultHintBasePosition;
    UnityEngine.UI.Image faultHeartImage;
    Material faultHeartMaterial;
    Color faultHeartOriginalColor;
    bool faultHeartColorCached;

    bool IsFaultBoardVisible => isActiveAndEnabled && initializedForOpen
        && stageIndex == 1 && window != null && window.IsOpen
        && !window.IsMinimized && !window.IsClosing
        && (osHandle == null || osHandle.IsOSVisible)
        && !GameTestConsole.BlocksGameplayInput
        && (Application.isEditor || Application.isFocused);

    public bool IsFaultGuidePlaying => IsFaultBoardVisible && !puzzleCompleted
        && (faultGuideSequence != null || (!faultGuideSeen && !faultBoardInteracted));
    public bool IsAwaitingFirstFaultInteraction => IsFaultBoardVisible
        && !puzzleCompleted && !faultBoardInteracted;

    void PersistFaultBoardInMemory()
    {
        var data = GameDataManager.Instance?.SaveData;
        if (data == null || !faultBoardInitialized || stageIndex != 1) return;
        if (data.osResume == null) data.osResume = new OSResumeSaveData();
        CapturePersistentBoard(data.osResume);
    }

    void UpdateFaultGuidance()
    {
        if (!IsFaultBoardVisible)
        {
            if (faultGuideSequence != null) StopFaultGuidance();
            StopFaultHint();
            SetFaultNodeHover(-1);
            return;
        }
        if (OSSessionPersistence.IsRestoring) return;
        if (gameplayReady && !faultGuideSeen && !faultBoardInteracted)
            TryStartFaultGuide();
        if (!gameplayReady || !IsAwaitingFirstFaultInteraction
            || faultGuideSequence != null || OS_Handle.Instance?.IsSystemInputLocked == true)
            return;
        faultHintRemaining -= Time.unscaledDeltaTime;
        if (faultHintRemaining <= 0f && faultHintSequence == null)
        {
            PlayFaultHint();
            faultHintRemaining = Mathf.Max(1f, faultHintInterval);
        }
    }

    void TryStartFaultGuide()
    {
        if (!IsFaultBoardVisible || puzzleCompleted || faultGuideSeen
            || faultBoardInteracted || faultGuideSequence != null
            || !materialsConfigured || OSSessionPersistence.IsRestoring) return;
        gameplayReady = false;
        SetBoardInteraction(false);
        StopFaultHint();
        SetFaultNodeHover(-1);
        KillChargeTweens(false);
        KillStateTweens(true);
        SetAllChargesImmediate(0f);
        StopTargetHeartbeat();

        Sequence sequence = DOTween.Sequence();
        faultGuideSequence = sequence;
        // Open only the displayed route, then travel along it one segment at a time.
        for (int step = 1; step < FaultGuideRoute.Length; step++)
        {
            int source = FaultGuideRoute[step - 1];
            int node = FaultGuideRoute[step];
            LineState line = FindFaultGuideLine(source, node);
            if (line == null || line.Material == null) continue;
            ConfigureLineDirection(line, source, node);
            if (IsNormalUnit(node))
            {
                ConfigureNodeDirection(node, source);
                unitMaterials[node]?.SetFloat(StateId, 1f);
            }
            sequence.Append(DOVirtual.Float(0f, 1f, 0.12f,
                value => line.Material.SetFloat(ChargeId, value)).SetEase(Ease.Linear));
            if (IsNormalUnit(node) && unitMaterials[node] != null)
            {
                Material material = unitMaterials[node];
                sequence.Append(DOVirtual.Float(0f, 1f, 0.055f,
                    value => material.SetFloat(ChargeId, value)).SetEase(Ease.Linear));
            }
        }
        sequence.AppendCallback(() =>
        {
            faultGuideHeartPowered = true;
            StartTargetHeartbeat();
        });
        sequence.AppendInterval(0.7f);
        sequence.AppendCallback(() =>
        {
            faultGuideHeartPowered = false;
            StopTargetHeartbeat();
            ApplyFaultHeartColor(false);
        });
        if (errorPartGroup != null)
        {
            sequence.Append(errorPartGroup.DOFade(0.28f, 0.04f));
            sequence.Append(errorPartGroup.DOFade(0.9f, 0.04f));
            sequence.AppendCallback(RestoreFaultBoardVisuals);
            sequence.Append(errorPartGroup.DOFade(0.18f, 0.05f));
            sequence.Append(errorPartGroup.DOFade(1f, 0.08f));
        }
        sequence.AppendCallback(() =>
        {
            RestoreFaultBoardVisuals();
            faultGuideSeen = true;
            gameplayReady = true;
            SetBoardInteraction(true);
            faultHintRemaining = 0.55f;
            StartTargetHeartbeat();
            PersistFaultBoardInMemory();
        });
        sequence.SetUpdate(true).SetLink(gameObject, LinkBehaviour.KillOnDisable)
            .OnKill(() => { if (faultGuideSequence == sequence) faultGuideSequence = null; });
    }

    LineState FindFaultGuideLine(int from, int to)
    {
        for (int i = 0; i < lines.Count; i++)
            if ((lines[i].From == from && lines[i].To == to)
                || (lines[i].To == from && lines[i].From == to)) return lines[i];
        return null;
    }

    void RestoreFaultBoardVisuals()
    {
        KillChargeTweens(false);
        KillStateTweens(true);
        for (int i = 0; i < UnitCount; i++) ApplyUnitState(i, false);
        ApplyPowerVisuals(false);
    }

    void StopFaultGuidance()
    {
        bool wasPlaying = faultGuideSequence != null;
        var sequence = faultGuideSequence;
        faultGuideSequence = null;
        sequence?.Kill(false);
        faultGuideHeartPowered = false;
        StopFaultHint();
        SetFaultNodeHover(-1);
        if (wasPlaying)
        {
            StopTargetHeartbeat();
            ApplyFaultHeartColor(unitPowered[TargetIndex]);
            RestoreFaultBoardVisuals();
            if (errorPartGroup != null) errorPartGroup.alpha = 1f;
            gameplayReady = stageIndex == 1 && !puzzleCompleted;
            SetBoardInteraction(gameplayReady);
        }
    }

    void PlayFaultHint()
    {
        // Continue from the lit entry segment to the first closed gate. Older
        // untouched saves may still begin at the source rather than node 1.
        int source = -1;
        int node = -1;
        for (int step = 1; step < FaultGuideRoute.Length; step++)
        {
            int from = FaultGuideRoute[step - 1];
            int to = FaultGuideRoute[step];
            if (!unitPowered[from] || !IsNormalUnit(to) || unitOpen[to]) continue;
            source = from;
            node = to;
            break;
        }
        if (node < 0) return;
        LineState line = FindFaultGuideLine(source, node);
        if (line?.Material == null || unitImages[node] == null) return;
        StopFaultHint();
        faultHintNode = node;
        faultHintLine = line;
        RectTransform rect = unitImages[node].rectTransform;
        faultHintBasePosition = rect.localPosition;
        ConfigureLineDirection(line, source, node);
        line.Material.SetFloat(ChargeId, 0f);
        Sequence sequence = DOTween.Sequence();
        faultHintSequence = sequence;
        sequence.Append(DOVirtual.Float(0f, 1f, 0.4f,
            value => line.Material.SetFloat(ChargeId, value)).SetEase(Ease.Linear));
        sequence.AppendCallback(() => SetFaultHintNodeBrightness(0.5f));
        sequence.Append(rect.DOPunchPosition(new Vector3(1.5f, 0f, 0f), 0.22f, 4, 0.2f));
        sequence.Join(DOVirtual.Float(1f, 0f, 0.22f,
            value => line.Material.SetColor(OnColorId, Color.Lerp(unpoweredColor, poweredColor, value))));
        sequence.AppendCallback(() => SetFaultHintNodeBrightness(0f));
        sequence.AppendInterval(0.15f);
        sequence.SetUpdate(true).SetLink(gameObject, LinkBehaviour.KillOnDisable)
            .OnComplete(StopFaultHint);
    }

    void SetFaultHintNodeBrightness(float amount)
    {
        if (faultHintNode < 0 || unitMaterials[faultHintNode] == null) return;
        unitMaterials[faultHintNode].SetColor(OffColorId,
            Color.Lerp(unpoweredColor, new Color(0.75f, 0.68f, 0.38f), amount));
    }

    void StopFaultHint()
    {
        var sequence = faultHintSequence;
        faultHintSequence = null;
        sequence?.Kill(false);
        if (faultHintNode >= 0)
        {
            if (unitImages[faultHintNode] != null)
                unitImages[faultHintNode].rectTransform.localPosition = faultHintBasePosition;
            unitMaterials[faultHintNode]?.SetColor(OffColorId, unpoweredColor);
        }
        if (faultHintLine?.Material != null)
        {
            faultHintLine.Material.SetColor(OnColorId, poweredColor);
            faultHintLine.Material.SetFloat(ChargeId, faultHintLine.TargetCharge);
        }
        faultHintNode = -1;
        faultHintLine = null;
        RefreshFaultHoverColors();
    }

    void ApplyFaultHeartColor(bool powered)
    {
        if (targetHeart == null) return;
        if (faultHeartImage == null) faultHeartImage = targetHeart.GetComponent<UnityEngine.UI.Image>();
        if (faultHeartImage == null) return;
        if (!faultHeartColorCached)
        {
            faultHeartOriginalColor = faultHeartImage.color;
            faultHeartColorCached = true;
        }
        // The authored heart sprite is red. A UI tint only multiplies those
        // pixels, so use the board's alpha-based material to turn it gold.
        if (faultHeartMaterial == null && chargeMaterialTemplate != null)
            faultHeartMaterial = CreateRuntimeMaterial(faultHeartImage, false,
                "StateCheck Heart Power (Runtime)");
        if (faultHeartMaterial != null)
        {
            faultHeartMaterial.SetColor(OffColorId, faultHeartUnpoweredColor);
            faultHeartMaterial.SetColor(OnColorId, poweredColor);
            faultHeartMaterial.SetFloat(ChargeId, powered ? 1f : 0f);
        }
        else
            faultHeartImage.color = powered ? faultHeartOriginalColor : faultHeartUnpoweredColor;
    }

    void EnsureFaultNodePointers()
    {
        if (!Application.isPlaying) return;
        for (int i = 0; i < UnitCount; i++)
        {
            if (!IsNormalUnit(i) || unitImages[i] == null) continue;
            var pointer = unitImages[i].GetComponent<OSStateCheckFaultNodePointer>();
            if (pointer == null) pointer = unitImages[i].gameObject.AddComponent<OSStateCheckFaultNodePointer>();
            pointer.Initialize(this, i);
        }
    }

    internal void HandleFaultNodePointer(int node, bool entered)
    {
        if (entered && gameplayReady && IsFaultBoardVisible
            && OS_Handle.Instance?.IsSystemInputLocked != true)
            SetFaultNodeHover(node);
        else if (!entered && faultHoveredNode == node) SetFaultNodeHover(-1);
    }

    void SetFaultNodeHover(int node)
    {
        if (faultHoveredNode == node) return;
        faultHoveredNode = node;
        RefreshFaultHoverColors();
    }

    void RefreshFaultHoverColors()
    {
        int mask = IsNormalUnit(faultHoveredNode) ? NodeToggleMasks[faultHoveredNode] : 0;
        for (int i = 0; i < UnitCount; i++)
        {
            if (!IsNormalUnit(i) || unitMaterials[i] == null) continue;
            float strength = i == faultHoveredNode ? 0.45f : (mask & (1 << i)) != 0 ? 0.22f : 0f;
            unitMaterials[i].SetColor(OffColorId, Color.Lerp(unpoweredColor, Color.white, strength));
            unitMaterials[i].SetColor(OnColorId, Color.Lerp(poweredColor, Color.white, strength));
        }
    }
}
