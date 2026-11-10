using MrTGame.Core;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public sealed partial class OSClawMachineGameWindowController
{
    public enum ClawPhase { Sweeping, Descending, Closing, Returning, Delivering, Outlet,
        CursorTurning, CursorLunging, CursorClosing, CursorReturning, CursorSuspended }

    [Header("Claw Gameplay")]
    [SerializeField] Button lowerButton;
    [SerializeField] Button gripButton;
    [SerializeField] TMP_Text lowerLabel;
    [SerializeField] UnityEngine.UI.Image[] prizes = new UnityEngine.UI.Image[0];
    [SerializeField] UnityEngine.UI.Image targetOutline;
    [SerializeField] UnityEngine.UI.Image secondTargetOutline;
    [SerializeField] UnityEngine.UI.Image targetArrow;
    [SerializeField] UnityEngine.UI.Image outletImage;
    [SerializeField] RectTransform prizeTooltip;
    [SerializeField] TMP_Text prizeTooltipLabel;
    [SerializeField, Min(1f)] float sweepSpeed = 138f;
    [SerializeField, Min(1f)] float descendSpeed = 96f;
    [SerializeField, Min(1f)] float retractSpeed = 148f;
    [SerializeField, Min(0f)] float bottomHoldSeconds = 1f;

    [Header("Empty Grip Animation")]
    [SerializeField] UnityEngine.UI.Image emptyGripImage;
    [SerializeField] UnityEngine.UI.Image clawHubImage;
    [SerializeField] Sprite[] emptyGripFrames = new Sprite[0];

    const float HomeX = 124f;
    const float MinX = 27f;
    const float MaxX = 221f;
    // Keep only a short connector visible at rest. The extra travel preserves
    // the original prize-row reach when the claw retracts closer to its carriage.
    const float RestGripY = 37f;
    const float RestClawTop = 21f;
    const float RestCableLength = 3f;
    const float MaxDrop = 73f;
    const float OutletDeliverySeconds = 1.12f;
    const float TargetHintAttentionDuration = .9f;
    ClawPhase phase;
    float headX = HomeX, drop, direction = -1f, phaseTime, capturedDrop;
    float targetHintElapsed = TargetHintAttentionDuration;
    int hintedPrize = -1;
    int caughtPrize = -1;
    Vector2[] prizeHomes;
    Vector2 outletPrizeHome;
    GameSaveData gameplaySave;
    bool initializedGameplay, wasPresentationVisible;
    bool emptyGripAnimating;
    bool outletReady;
    Chapter02AudioLoop motorAudio;

    public ClawPhase Phase => phase;
    public float ClawCenterX => headX;
    public float ClawDrop => drop;
    public RectTransform OutletPrize => outletImage != null ? outletImage.rectTransform : null;
    // Prize delivery and the moving claw are independent after the ejection.
    // The receipt is written at capture time, before the prize reaches the tray.
    public bool IsOutletPrizeReady => outletReady && Progress != null
        && Progress.chapter02ClawOutletPrize > 0 && outletImage != null
        && outletImage.gameObject.activeSelf;
    public bool IsPresentationVisible => isActiveAndEnabled && Window.IsOpen && !Window.IsMinimized
        && !Window.IsOpening && !Window.IsClosing && OS_Handle.Instance != null
        && OS_Handle.Instance.IsOSVisible && !GameTestConsole.BlocksGameplayInput;

    OSMemoryProcessSaveData Progress => GameDataManager.Instance != null
        ? GameDataManager.Instance.OSProcessData.memory : null;

    void InitializeGameplay()
    {
        if (initializedGameplay) return;
        initializedGameplay = true;
        prizeHomes = new Vector2[prizes.Length];
        for (int i = 0; i < prizes.Length; i++)
            if (prizes[i] != null) prizeHomes[i] = prizes[i].rectTransform.anchoredPosition;
        if (lowerButton != null) lowerButton.onClick.AddListener(LowerClaw);
        if (gripButton != null) gripButton.onClick.AddListener(GripClaw);
        HidePrizeTooltip();
    }

    void Update()
    {
        InitializeGameplay();
        if (GameDataManager.Instance == null) { motorAudio?.Stop(); return; }
        if (!ReferenceEquals(gameplaySave, GameDataManager.Instance.SaveData)) RestoreGameplay();
        bool visible = IsPresentationVisible;
        if (visible && !wasPresentationVisible)
        {
            OS_Handle.Instance.ClawMachineStory?.NotifyGameOpened(this);
            if (IsOutletPrizeReady)
                OS_Handle.Instance.ClawMachineStory?.NotifyPrizeDelivered(this,
                    Progress.chapter02ClawOutletPrize - 1, OutletPrize);
        }
        wasPresentationVisible = visible;
        float dt = Mathf.Min(Time.unscaledDeltaTime, .1f);
        if (TickCursorTrap(dt, visible))
        {
            TickMotorAudio(visible && trapHasFocus && IsCursorTrapControlling);
            return;
        }
        if (!visible || OS_Handle.Instance.IsSystemInputLocked || OSSessionPersistence.IsRestoring)
        {
            HidePrizeTooltip();
            motorAudio?.Stop();
            return;
        }
        AdvanceGameplay(dt);
        TickMotorAudio(trapHasFocus);
        if (hintedPrize >= 0)
        {
            targetHintElapsed += dt;
            ApplyTargetHintAlpha();
            ApplyTargetArrowPosition();
        }
    }

    void AdvanceGameplay(float dt)
    {
        float previousTime = phaseTime;
        phaseTime += dt;
        switch (phase)
        {
            case ClawPhase.Sweeping:
                headX += direction * sweepSpeed * dt;
                if (headX <= MinX)
                {
                    headX = MinX + (MinX - headX); direction = 1f;
                    Chapter02Audio.Play("Chapter02_ClawTurn", .34f, .94f, .3f);
                }
                if (headX >= MaxX)
                {
                    headX = MaxX - (headX - MaxX); direction = -1f;
                    Chapter02Audio.Play("Chapter02_ClawTurn", .34f, 1.02f, .3f);
                }
                break;
            case ClawPhase.Descending:
                drop = Mathf.MoveTowards(drop, MaxDrop, descendSpeed * dt);
                if (cursorTrapDescent && drop >= 90f - RestGripY && BeginCursorTrap()) return;
                if (drop < MaxDrop) phaseTime = 0f;
                else if (phaseTime >= bottomHoldSeconds) SetPhase(ClawPhase.Returning);
                break;
            case ClawPhase.Closing:
                if (emptyGripAnimating)
                {
                    if (phaseTime >= .44f)
                    {
                        Chapter02Audio.Play("Chapter02_ClawGrip", .38f, 1.15f);
                        SetFingers(false);
                        SetPhase(ClawPhase.Returning);
                    }
                    else
                    {
                        // Hold the closed pose longer than the approach and rebound.
                        int frame = phaseTime < .08f ? 0 : phaseTime < .17f ? 1 : phaseTime < .33f ? 2 : 3;
                        emptyGripImage.sprite = emptyGripFrames[frame];
                    }
                }
                else if (phaseTime >= .24f) SetPhase(ClawPhase.Returning);
                break;
            case ClawPhase.Returning:
                // Start the short hand-off pause only after the fully retracted
                // claw and its prize have actually been presented together.
                if (drop > 0f) phaseTime = 0f;
                drop = Mathf.MoveTowards(drop, 0f, retractSpeed * dt);
                if (caughtPrize >= 0 && prizes[caughtPrize] != null)
                    prizes[caughtPrize].rectTransform.anchoredPosition = prizeHomes[caughtPrize]
                        + new Vector2(0f, capturedDrop - drop);
                if (drop <= 0f)
                {
                    if (caughtPrize >= 0)
                    {
                        if (phaseTime >= .12f)
                        {
                            prizes[caughtPrize].gameObject.SetActive(false);
                            ShowOutlet(caughtPrize, false);
                            SetPhase(ClawPhase.Delivering);
                        }
                    }
                    else
                    {
                        cursorTrapDescent = false;
                        SetFingers(false);
                        SetPhase(ClawPhase.Sweeping);
                    }
                }
                break;
            case ClawPhase.Delivering:
                RenderOutletDelivery(phaseTime);
                // The toy's feet reach the chute here; the later forward slide
                // is quiet, keeping the small physical delivery readable.
                if (previousTime < .48f && phaseTime >= .48f)
                    Chapter02Audio.Play("Chapter02_PrizeDrop", .78f);
                if (phaseTime >= OutletDeliverySeconds)
                {
                    StopOutletReveal();
                    if (outletImage != null) outletImage.rectTransform.anchoredPosition = outletPrizeHome;
                    outletReady = true;
                    SetFingers(false);
                    SetPhase(ClawPhase.Sweeping);
                    OS_Handle.Instance.ClawMachineStory?.NotifyPrizeDelivered(this, caughtPrize, OutletPrize);
                    caughtPrize = -1;
                }
                break;
            // Retain the enum value for debug/Inspector compatibility. Outlet
            // occupancy no longer freezes the mechanism in this legacy phase.
            case ClawPhase.Outlet:
                SetPhase(ClawPhase.Sweeping);
                break;
        }
        ApplyClawPosition();
        RefreshButtons();
    }

    public void LowerClaw()
    {
        if (!CanUseControls() || phase != ClawPhase.Sweeping) return;
        caughtPrize = -1;
        cursorTrapDescent = ShouldTrapAtCurrentPosition();
        if (cursorTrapDescent) OS_Handle.Instance.ClawMachineStory?.AcceptSecondRequestForTrap();
        SetFingers(false);
        SetPhase(ClawPhase.Descending);
        RefreshButtons();
    }

    public void GripClaw()
    {
        if (!CanUseControls() || (phase != ClawPhase.Descending && phase != ClawPhase.Sweeping)) return;
        if (cursorTrapDescent) return;
        Chapter02Audio.Play("Chapter02_ClawGrip", .65f);
        // An idle click still visibly closes/opens the claw, but cannot catch
        // a toy through the empty space above it.
        caughtPrize = phase == ClawPhase.Descending ? FindCatch() : -1;
        if (caughtPrize >= 0)
        {
            capturedDrop = drop;
            Progress.chapter02ClawCapturedMask |= 1 << caughtPrize;
            Progress.chapter02ClawOutletPrize = caughtPrize + 1;
            RefreshTargetOutline();
            SetFingers(true);
            SetPhase(ClawPhase.Returning);
        }
        else
        {
            SetFingers(true);
            BeginEmptyGripAnimation();
            SetPhase(ClawPhase.Closing);
        }
        RefreshButtons();
    }

    bool CanUseControls() => IsPresentationVisible && Progress != null
        && !OS_Handle.Instance.IsSystemInputLocked && !OSSessionPersistence.IsRestoring;

    int FindCatch()
    {
        var progress = Progress;
        // Only the first requested doll is an ordinary prize. Once it is caught,
        // all later attempts stay empty until the robot's advertised cursor trap.
        // This also prevents another toy replacing an occupied outlet receipt.
        if (progress == null || progress.chapter02ClawCursorCaptured
            || progress.chapter02ClawFirstPrizeTaken || progress.chapter02ClawOutletPrize != 0
            || (progress.chapter02ClawCapturedMask & 1) != 0) return -1;
        float tipY = RestGripY + drop;
        int best = -1;
        float bestDistance = float.PositiveInfinity;
        for (int i = 0; i < prizes.Length; i++)
        {
            // The remaining toys are decoration/bait, not alternate story prizes.
            if (i != 0) continue;
            if (prizes[i] == null || (progress.chapter02ClawCapturedMask & (1 << i)) != 0) continue;
            var rt = prizes[i].rectTransform;
            float center = prizeHomes[i].x + rt.rect.width * .5f;
            float top = 80f - prizeHomes[i].y;
            float distance = Mathf.Abs(headX - center);
            if (tipY >= top + 2f && tipY <= top + 24f
                && distance <= Mathf.Min(15f, rt.rect.width * .31f) && distance < bestDistance)
            {
                best = i;
                bestDistance = distance;
            }
        }
        return best;
    }

    void SetPhase(ClawPhase next) { phase = next; phaseTime = 0f; }

    void TickMotorAudio(bool available)
    {
        // Only moving machinery owns a loop. A held button, bottom pause,
        // captured cursor or hidden window must never leave a motor running.
        float gain = 0f, pitch = 1f;
        if (available)
        {
            if (phase == ClawPhase.Sweeping) { gain = .28f; pitch = .92f; }
            else if (phase == ClawPhase.Descending && drop < MaxDrop) { gain = .38f; pitch = .82f; }
            else if ((phase == ClawPhase.Returning && drop > 0f) || phase == ClawPhase.CursorReturning)
            { gain = .42f; pitch = 1.08f; }
        }
        if (gain <= 0f) { motorAudio?.Stop(); return; }
        if (motorAudio == null) motorAudio = new Chapter02AudioLoop(this);
        motorAudio.Set("Chapter02_ClawMotor", gain, pitch);
    }

    void ApplyClawPosition()
    {
        float x = Mathf.Round(headX - HomeX), y = Mathf.Round(drop);
        if (carriage != null) carriage.anchoredPosition = new Vector2(104f + x, 0f);
        if (cable != null)
        {
            cable.anchoredPosition = new Vector2(123f + x, -19f);
            cable.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, RestCableLength + y);
        }
        if (claw != null) claw.anchoredPosition = new Vector2(111f + x, -RestClawTop - y);
    }

    void SetFingers(bool closed)
    {
        emptyGripAnimating = false;
        if (emptyGripImage != null) emptyGripImage.gameObject.SetActive(false);
        if (clawHubImage != null) clawHubImage.enabled = true;
        if (leftFinger != null)
        {
            leftFinger.gameObject.SetActive(true);
            leftFinger.anchoredPosition = new Vector2(closed ? 5f : 0f, closed ? -6f : -5f);
        }
        if (rightFinger != null)
        {
            rightFinger.gameObject.SetActive(true);
            rightFinger.anchoredPosition = new Vector2(closed ? 11f : 16f, closed ? -6f : -5f);
        }
    }

    void BeginEmptyGripAnimation()
    {
        // Older prefab instances retain the original static empty-grip behavior.
        if (emptyGripImage == null || clawHubImage == null || emptyGripFrames == null || emptyGripFrames.Length < 4) return;
        for (int i = 0; i < 4; i++) if (emptyGripFrames[i] == null) return;
        if (leftFinger != null) leftFinger.gameObject.SetActive(false);
        if (rightFinger != null) rightFinger.gameObject.SetActive(false);
        clawHubImage.enabled = false;
        emptyGripImage.sprite = emptyGripFrames[0];
        emptyGripImage.enabled = true;
        emptyGripImage.gameObject.SetActive(true);
        emptyGripAnimating = true;
    }

    void RefreshButtons()
    {
        if (lowerButton != null) lowerButton.interactable = phase == ClawPhase.Sweeping;
        if (gripButton != null) gripButton.interactable = !cursorTrapDescent
            && (phase == ClawPhase.Descending || phase == ClawPhase.Sweeping);
    }

    public void RestoreGameplay()
    {
        motorAudio?.Stop();
        InitializeGameplay();
        StopOutletReveal();
        if (Progress?.chapter02ClawCursorCaptured != true) RestoreMachineShell();
        SuspendCursorTrap();
        gameplaySave = GameDataManager.Instance != null ? GameDataManager.Instance.SaveData : null;
        caughtPrize = -1; headX = HomeX; drop = 0f; direction = -1f;
        SetFingers(false); ApplyClawPosition(); HidePrizeTooltip();
        outletReady = false;
        if (outletImage != null) outletImage.gameObject.SetActive(false);
        var progress = Progress;
        RestoreCursorBait(progress);
        for (int i = 0; i < prizes.Length; i++) if (prizes[i] != null)
        {
            prizes[i].rectTransform.anchoredPosition = prizeHomes[i];
            prizes[i].gameObject.SetActive(progress == null || (progress.chapter02ClawCapturedMask & (1 << i)) == 0);
        }
        int outlet = progress != null ? progress.chapter02ClawOutletPrize - 1 : -1;
        if (outlet >= 0 && outlet < prizes.Length)
        {
            ShowOutlet(outlet, true);
        }
        SetPhase(ClawPhase.Sweeping);
        RestoreCursorTrapCheckpoint();
        // Reopening/restoring keeps the established target visible without
        // replaying the attention flash or relying on the global sine phase.
        targetHintElapsed = TargetHintAttentionDuration;
        RefreshTargetOutline(); RefreshButtons(); wasPresentationVisible = false;
    }

    public void RevealTargetHint()
    {
        var progress = Progress;
        if (progress == null || progress.chapter02ClawTargetRevealed) return;
        progress.chapter02ClawTargetRevealed = true;
        targetHintElapsed = 0f;
        RefreshTargetOutline();
    }

    public void RevealSecondTargetHint()
    {
        var progress = Progress;
        if (progress == null || progress.chapter02ClawSecondTargetRevealed) return;
        progress.chapter02ClawSecondTargetRevealed = true;
        targetHintElapsed = 0f;
        RefreshTargetOutline();
    }

    void RefreshTargetOutline()
    {
        var progress = Progress;
        // The completed-message receipts support saves made before the separate
        // typing-complete cue receipts existed. New hints begin on that typed line.
        bool first = progress != null && !progress.chapter02ClawCursorCaptured
            && (progress.chapter02ClawTargetRevealed || progress.chapter02ClawIntroShown)
            && (progress.chapter02ClawCapturedMask & 1) == 0;
        bool second = progress != null && !progress.chapter02ClawCursorCaptured
            && progress.chapter02ClawFirstPrizeTaken
            && (progress.chapter02ClawSecondTargetRevealed || progress.chapter02ClawNextRequestShown)
            && (progress.chapter02ClawCapturedMask & 1) != 0;
        hintedPrize = first ? 0 : second ? CursorBaitPrizeIndex : -1;
        if (targetOutline != null) targetOutline.gameObject.SetActive(first);
        if (secondTargetOutline != null)
        {
            if (prizes.Length > CursorBaitPrizeIndex && prizes[CursorBaitPrizeIndex] != null)
            {
                var robot = prizes[CursorBaitPrizeIndex].rectTransform;
                var outline = secondTargetOutline.rectTransform;
                if (outline.parent != robot) outline.SetParent(robot, false);
                outline.anchorMin = outline.anchorMax = new Vector2(0f, 1f);
                outline.pivot = new Vector2(0f, 1f);
                outline.anchoredPosition = new Vector2(-1f, 1f);
                outline.sizeDelta = robot.rect.size + Vector2.one * 2f;
                outline.localScale = Vector3.one;
            }
            secondTargetOutline.gameObject.SetActive(second);
        }
        if (targetArrow != null) targetArrow.gameObject.SetActive(hintedPrize >= 0);
        if (hintedPrize < 0) return;
        ApplyTargetHintAlpha();
        ApplyTargetArrowPosition();
    }

    void ApplyTargetHintAlpha()
    {
        // Two distinct short flashes link the newly completed line to this toy;
        // then a slower two-second breath leaves a persistent, readable cue.
        float alpha = targetHintElapsed < TargetHintAttentionDuration
            ? (targetHintElapsed % .45f < .25f ? 1f : .08f)
            : .35f + .65f * (.5f + .5f * Mathf.Cos((targetHintElapsed - TargetHintAttentionDuration) * Mathf.PI));
        SetHintAlpha(targetOutline, alpha);
        SetHintAlpha(secondTargetOutline, alpha);
        SetHintAlpha(targetArrow, alpha);
    }

    static void SetHintAlpha(UnityEngine.UI.Image hint, float alpha)
    {
        if (hint == null) return;
        Color color = hint.color;
        color.a = alpha;
        hint.color = color;
    }

    void ApplyTargetArrowPosition()
    {
        if (targetArrow == null || hintedPrize < 0 || hintedPrize >= prizes.Length || prizes[hintedPrize] == null) return;
        var prize = prizes[hintedPrize].rectTransform;
        var arrow = targetArrow.rectTransform;
        if (arrow.parent != prize) arrow.SetParent(prize, false);
        arrow.anchorMin = arrow.anchorMax = new Vector2(.5f, 1f);
        arrow.pivot = new Vector2(.5f, 0f);
        arrow.sizeDelta = new Vector2(7f, 4f);
        arrow.localScale = Vector3.one;
        // One native pixel of movement either side of the six-pixel gap.
        // Quantizing only the motion preserves crisp placement for odd-width sprites.
        float bob = Mathf.Round(Mathf.Sin(targetHintElapsed * (Mathf.PI * 2f / 1.5f)));
        float headOffset = hintedPrize == CursorBaitPrizeIndex ? -6f : 0f;
        arrow.anchoredPosition = new Vector2(headOffset, 6f + bob);
    }

    void ShowOutlet(int index, bool immediate)
    {
        if (outletImage == null || index < 0 || index >= prizes.Length) return;
        var source = prizes[index];
        if (source == null) return;
        StopOutletReveal();
        outletReady = immediate;
        outletImage.sprite = source.sprite;
        Vector2 size = source.rectTransform.sizeDelta;
        outletImage.rectTransform.sizeDelta = size;
        outletImage.rectTransform.anchorMin = outletImage.rectTransform.anchorMax = new Vector2(0f, 1f);
        outletImage.rectTransform.pivot = new Vector2(.5f, 0f);
        // Place the original-size toy's feet on the outlet tray rather than
        // aligning its top below the label and leaving it below the cabinet.
        float trayWidth = claimArea != null ? claimArea.rect.width : 70f;
        float trayHeight = claimArea != null ? claimArea.rect.height : 39f;
        outletPrizeHome = new Vector2(Mathf.Round(trayWidth * .5f), -Mathf.Round(trayHeight) + 3f);
        outletImage.rectTransform.anchoredPosition = outletPrizeHome;
        outletImage.rectTransform.localScale = Vector3.one;
        outletImage.enabled = true;
        // In its finished front position the solid toy may overlap the sign.
        // During the chute drop the reveal shader confines it to the dark hole.
        outletImage.transform.SetAsLastSibling();
        outletImage.gameObject.SetActive(true);
        if (!immediate) BeginOutletReveal();
    }

    public void CompleteCharacterPickup(int prizeIndex)
    {
        if (Progress == null || Progress.chapter02ClawOutletPrize != prizeIndex + 1) return;
        Progress.chapter02ClawOutletPrize = 0;
        outletReady = false;
        StopOutletReveal();
        if (outletImage != null) outletImage.gameObject.SetActive(false);
        // The player may be lowering or empty-gripping while Character takes
        // the prize. Consuming the tray must not reset that independent action.
        HidePrizeTooltip();
    }

    public void SetPrizeHovered(bool hovered)
    {
        bool show = hovered && IsOutletPrizeReady && IsPresentationVisible
            && outletImage.enabled && outletImage.gameObject.activeInHierarchy;
        if (prizeTooltip != null) prizeTooltip.gameObject.SetActive(show);
    }

    void HidePrizeTooltip()
    {
        if (prizeTooltip != null) prizeTooltip.gameObject.SetActive(false);
    }

    void RefreshGameplayLanguage()
    {
        ApplyLabel(lowerLabel, "ui.os.claw_machine.lower", "伸下");
        ApplyLabel(prizeTooltipLabel, "ui.os.claw_machine.cute_prize", "可爱玩偶");
        FitPrizeTooltip(prizeTooltipLabel, prizeTooltip);
    }

    public static void FitPrizeTooltip(TMP_Text text, RectTransform tooltip)
    {
        if (text != null && tooltip != null)
        {
            if (text.enableAutoSizing) text.fontSize = text.fontSizeMax;
            float width = Mathf.Clamp(Mathf.Ceil(text.GetPreferredValues(text.text).x) + 8f, 36f, 146f);
            tooltip.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, width);
        }
    }
}
