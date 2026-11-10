using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
public sealed partial class OSPaperPushWindowController : MonoBehaviour
{
    public enum MatchPhase { Title, Approaching, Choosing, Resolving, Finished, Pushing, Curtain, Sliding }
    [SerializeField] OSWindowUnit window;
    [SerializeField] RectTransform stage;
    [SerializeField] Image playerImage;
    [SerializeField] Image characterImage;
    [SerializeField] Sprite[] playerPoses = new Sprite[7];
    [SerializeField] Sprite[] characterPoses = new Sprite[7];
    [SerializeField] TMP_Text statusLabel;
    [SerializeField] TMP_Text commentLabel;
    [SerializeField] TMP_Text chargeLabel;
    [SerializeField] RectTransform playerStaminaFill;
    [SerializeField] RectTransform characterStaminaFill;
    [SerializeField] RectTransform playerMarker;
    [SerializeField] TMP_Text playerReactionLabel;
    [SerializeField] TMP_Text characterReactionLabel;
    [SerializeField] GameObject titlePanel;
    [SerializeField] TMP_Text recordLabel;
    [SerializeField] Button startButton;
    [SerializeField] TMP_Text startLabel;
    [SerializeField] GameObject choicesRoot;
    [SerializeField] Button defendButton;
    [SerializeField] Button lightButton;
    [SerializeField] Button heavyButton;
    [SerializeField] OSLinkMessageController linkMessages;
    [SerializeField] OSPaperPushStoryDirector story;
    [SerializeField] OSPaperPushStagePresentation presentation;
    [SerializeField, Min(1f)] float stepPixels = 14f;
    [SerializeField, Min(0f)] float hopHeight = 3f;
    [SerializeField, Min(.1f)] float pushIntervalSeconds = OSPaperPushRules.PushIntervalSeconds;
    [SerializeField] OSPaperPushAudio sound = new OSPaperPushAudio();

    MatchPhase phase;
    int round;
    int characterLossStreak;
    int matchChapter;
    int matchStage = 1;
    int observedChapter = -1;
    OSEnergyLoopSaveData observedSave;
    const float ContactDistance = 46f;
    const float StaminaWidth = 76f;
    float playerX = -60f;
    float characterX = 60f;
    float playerTilt;
    float characterTilt;
    float stamina = 1f;
    float hopPlayer;
    float hopCharacter;
    float visualTime;
    float markerTime;
    float hopClock;
    const float StandFootY = 13f;
    const float ReactionLabelSeconds = OSPaperPushRules.ReactionSeconds * 4f;
    OSPaperPushActionState playerReaction, characterReaction;
    float playerReactionRemaining, characterReactionRemaining;
    bool wasSimulating;
    Color? startLabelNormalColor;
    readonly OSPaperPushPawnMotion playerMotion = new OSPaperPushPawnMotion();
    readonly OSPaperPushPawnMotion characterMotion = new OSPaperPushPawnMotion();
    OSPaperPushActionState playerState;
    OSPaperPushActionState characterState;
    OSPaperPushPose playerPose;
    OSPaperPushPose characterPose;
    OSPaperPushMove committedCharacterMove;
    OSPaperPushMove previousPlayerMove;

    public MatchPhase Phase => phase;
    public int RoundNumber => round;
    public float PositionSteps => (playerX + characterX) * .5f / stepPixels;
    public float PlayerPosition => playerX;
    public float CharacterPosition => characterX;
    public float StaminaNormalized => stamina;
    public OSPaperPushActionState PlayerState => playerState;
    public OSPaperPushActionState CharacterState => characterState;
    public int MatchStage => matchStage;
    float CurrentPushIntervalSeconds => Mathf.Max(.1f, pushIntervalSeconds
        * (matchChapter == 2 ? OSPaperPushRules.Chapter02PushIntervalMultiplier : 1f));
    // Scripted rematches may briefly lock input while the character is already playing.
    public bool IsCharacterWorking => isActiveAndEnabled && matchChapter == 1
        && GameDataManager.Instance?.CurrentChapterIndex == 1 && !OSSessionPersistence.IsRestoring
        && window != null && window.IsOpen && !window.IsMinimized && !window.IsClosing
        && window.gameObject.activeInHierarchy && OS_Handle.Instance?.IsOSVisible == true
        && (phase == MatchPhase.Approaching || phase == MatchPhase.Choosing
            || phase == MatchPhase.Resolving || phase == MatchPhase.Pushing || phase == MatchPhase.Curtain);
    public bool CanSimulate => window != null && window.IsOpen && !window.IsMinimized
        && OS_Handle.Instance?.IsOSVisible == true && OS_Handle.Instance?.IsSystemInputLocked != true
        && (matchChapter != 2 || CanChapter02Simulate);
    bool CanAnimate => CanSimulate || (phase == MatchPhase.Finished && story?.IsTakingOver == true
        && window != null && window.IsOpen && !window.IsMinimized && OS_Handle.Instance?.IsOSVisible == true);

    void OnEnable()
    {
        startButton?.onClick.AddListener(BeginMatch);
        defendButton?.onClick.AddListener(ChooseDefend);
        lightButton?.onClick.AddListener(ChooseLight);
        heavyButton?.onClick.AddListener(ChooseHeavy);
        if (window != null) { window.OpenStarted += OnWindowOpened;window.Closed += OnWindowClosed; }
        ShowTitle();
    }

    void OnDisable()
    {
        startButton?.onClick.RemoveListener(BeginMatch);
        defendButton?.onClick.RemoveListener(ChooseDefend);
        lightButton?.onClick.RemoveListener(ChooseLight);
        heavyButton?.onClick.RemoveListener(ChooseHeavy);
        if (window != null) { window.OpenStarted -= OnWindowOpened;window.Closed -= OnWindowClosed; }
        StopAllCoroutines();
        ResetChapter02Tilt();
        sound.StopAll();
    }

    // The controller lives on the OS root: minimizing deactivates the window GameObject.
    // Keeping the coroutines here preserves an in-flight push and pauses it via CanSimulate.
    void OnWindowOpened(OSWindowUnit source) { StopAllCoroutines();ShowTitle(); }
    void OnWindowClosed(OSWindowUnit source) { StopAllCoroutines();ShowTitle(); }

    void Update()
    {
        var savedState = GameDataManager.Instance?.OSEnergyLoopState;
        int chapter = GameDataManager.Instance != null ? GameDataManager.Instance.CurrentChapterIndex : -1;
        if (savedState != observedSave || observedChapter != chapter)
        {
            observedSave = savedState;observedChapter = chapter;StopAllCoroutines();ShowTitle();
        }
        bool simulating = CanSimulate;
        if (!simulating && wasSimulating) sound.StopAll();
        wasSimulating = simulating;
        if (phase == MatchPhase.Title || phase == MatchPhase.Finished) RefreshStartButton();
        TickChapter02Rematch();
        if (CanAnimate && phase != MatchPhase.Choosing) visualTime += Time.unscaledDeltaTime;
        if (CanAnimate) markerTime += Time.unscaledDeltaTime;
        if (CanAnimate)
        {
            playerReactionRemaining = Mathf.Max(0f, playerReactionRemaining - Time.unscaledDeltaTime);
            characterReactionRemaining = Mathf.Max(0f, characterReactionRemaining - Time.unscaledDeltaTime);
        }
        SetStaminaWidth(playerStaminaFill);SetStaminaWidth(characterStaminaFill);
        if (chargeLabel != null && GameDataManager.Instance != null)
        {
            var state = GameDataManager.Instance.OSEnergyLoopState;
            int seconds = Mathf.CeilToInt(state.remainingChargeSeconds);
            int cooldownSeconds = Mathf.CeilToInt(state.remainingCooldownSeconds);
            OSLocalizedText.SetRuntime(chargeLabel, state.isCharging
                ? Text("charging", "充电 {0:00}:{1:00}", seconds / 60, seconds % 60)
                : state.chargeReadyToClaim ? Text("charge_claim", "充电完成 · 待领取")
                : GameDataManager.Instance.IsEnergyChargeCoolingDown
                    ? Text("charge_cooldown", "充电冷却 {0:00}:{1:00}", cooldownSeconds / 60, cooldownSeconds % 60)
                : state.completedChargeCount > 0
                    ? Text("charge_complete", "充电完成 +{0}", state.lastEnergyGained)
                    : Text("charge_idle", "充电器待机"));
        }
    }

    void RefreshStartButton()
    {
        bool enabled = GameDataManager.Instance?.IsPaperPushPlayable == true && story?.IsWaitingForMatchComment != true
            && (matchChapter != 2 || CanBeginChapter02Match);
        if (startButton != null) startButton.interactable = enabled;
        if (startLabel == null) return;
        if (!startLabelNormalColor.HasValue) startLabelNormalColor = startLabel.color;
        startLabel.color = !enabled && matchChapter == 2
            ? new Color(.5f, .5f, .5f, startLabelNormalColor.Value.a) : startLabelNormalColor.Value;
    }

    void LateUpdate()
    {
        // Sliding translates both stands without a hop, walking pose or body sway.
        if (phase == MatchPhase.Sliding) { RenderPawns();return; }
        float delta = CanAnimate && phase != MatchPhase.Choosing && phase != MatchPhase.Title ? Time.unscaledDeltaTime : 0f;
        float clock = phase == MatchPhase.Pushing ? hopClock : visualTime;
        playerMotion.Tick(playerState, playerPose, delta, clock, hopPlayer, playerTilt, true);
        characterMotion.Tick(characterState, characterPose, delta, clock, hopCharacter, characterTilt, false);
        RenderPawns();
    }

    void ShowTitle()
    {
        presentation?.ResetPresentation();
        ResetChapter02Tilt();
        sound.StopAll();
        phase = MatchPhase.Title;playerX = -60f;characterX = 60f;
        playerReactionRemaining = characterReactionRemaining = 0f;
        hopPlayer = hopCharacter = playerTilt = characterTilt = 0f;stamina = 1f;
        playerMotion.Reset();characterMotion.Reset();
        SetStates(OSPaperPushActionState.Ready, OSPaperPushActionState.Ready);
        SetPoses(OSPaperPushPose.Idle, OSPaperPushPose.Idle);
        titlePanel?.SetActive(true);choicesRoot?.SetActive(false);
        var data = GameDataManager.Instance;
        var saved = data?.OSEnergyLoopState.paperPushMatch;
        bool continuing = saved != null && saved.active && saved.chapter == data.CurrentChapterIndex;
        matchChapter = data != null ? data.CurrentChapterIndex : 0;
        matchStage = matchChapter == 1 ? Mathf.Min(3, data.OSEnergyLoopState.chapter01PaperPushWins + 1)
            : matchChapter == 2 ? ResolveChapter02MatchStage(continuing ? saved : null) : 1;
        chapter02BraceReleased = false;
        chapter02GearRevealed = matchChapter == 2 && matchStage == 2
            && (data.OSEnergyLoopState.chapter02PaperPushHintStage > 0
                || data.OSEnergyLoopState.chapter02PaperPushCueHistory.Contains("m2_hint1") || continuing && saved.phase == 4);
        chapter02ResultPending = matchChapter == 2 && !continuing
            && ((data.OSEnergyLoopState.chapter02PaperPushStep == 3 && data.OSEnergyLoopState.chapter02PaperPushMatch1Won > 0)
                || (data.OSEnergyLoopState.chapter02PaperPushStep == 5 && data.OSEnergyLoopState.chapter02PaperPushTiltWin > 0)
                || data.OSEnergyLoopState.chapter02PaperPushStep >= 6);
        if (matchChapter == 2) presentation?.SetStage(matchChapter, matchStage);
        if (startLabel != null) OSLocalizedText.SetRuntime(startLabel, continuing ? Text("continue", "继续对局") : Text("start", "开始"));
        RefreshStartButton();
        if (statusLabel != null) OSLocalizedText.SetRuntime(statusLabel, Text("title", "纸偶对推"));
        if (commentLabel != null) OSLocalizedText.SetRuntime(commentLabel, StageFlavor(true));
        if (continuing)
        {
            matchChapter = saved.chapter;
            matchStage = matchChapter == 1 ? Mathf.Min(3, data.OSEnergyLoopState.chapter01PaperPushWins + 1)
                : matchChapter == 2 ? ResolveChapter02MatchStage(saved) : 1;
            round = saved.round;playerX = saved.playerX;characterX = saved.characterX;
            stamina = saved.phase == 1 || saved.phase == 2 || saved.phase == 4 ? 0f
                : 1f - Mathf.Clamp01(saved.pushElapsed / CurrentPushIntervalSeconds);
            presentation?.SetStage(matchChapter, matchStage);
            var pose = saved.phase != 4 && characterX - playerX <= ContactDistance + 1f ? OSPaperPushPose.Straining : OSPaperPushPose.Idle;
            SetPoses(pose, pose);
            if (saved.phase == 2) SetPoses(PreparationPose((OSPaperPushMove)saved.playerMove), PreparationPose((OSPaperPushMove)saved.characterMove));
            OSLocalizedText.SetRuntime(statusLabel, Text("paused_round", "第 {0} 回合 · 已暂停", Mathf.Max(1, round)));
            OSLocalizedText.SetRuntime(commentLabel, Text("resume_hint", "继续这局对推。"));
        }
        SetStaminaWidth(playerStaminaFill);SetStaminaWidth(characterStaminaFill);
        RenderPawns();
        RefreshRecord();
    }

    public void BeginMatch() => BeginMatchCore(false);

    public bool BeginScriptedRematch(OSPaperPushStoryDirector owner)
    {
        if (owner == null || owner != story || !owner.IsTakingOver
            || CursorHandle.Instance?.IsScriptControlledBy(owner) != true
            || window == null || !window.IsOpen || window.IsMinimized) return false;
        BeginMatchCore(true);
        return phase == MatchPhase.Pushing || phase == MatchPhase.Curtain || phase == MatchPhase.Choosing || phase == MatchPhase.Resolving;
    }

    void BeginMatchCore(bool scripted)
    {
        if ((!scripted && (!CanSimulate || story?.IsWaitingForMatchComment == true))
            || GameDataManager.Instance?.IsPaperPushPlayable != true
            || (GameDataManager.Instance.CurrentChapterIndex == 2 && !CanBeginChapter02Match)) return;
        observedSave = GameDataManager.Instance.OSEnergyLoopState;
        StopAllCoroutines();round = 0;characterLossStreak = 0;playerX = -67f;characterX = 67f;
        playerReactionRemaining = characterReactionRemaining = 0f;
        sound.StopAll();sound.Play(sound.choice, .65f);
        hopPlayer = hopCharacter = playerTilt = characterTilt = 0f;visualTime = 0f;stamina = 1f;
        playerMotion.Reset();characterMotion.Reset();
        previousPlayerMove = OSPaperPushMove.LightPush;
        matchChapter = GameDataManager.Instance.CurrentChapterIndex;
        observedChapter = matchChapter;
        matchStage = matchChapter == 1 ? Mathf.Min(3, observedSave.chapter01PaperPushWins + 1)
            : matchChapter == 2 ? ResolveChapter02MatchStage(observedSave.paperPushMatch.active ? observedSave.paperPushMatch : null) : 1;
        presentation?.SetStage(matchChapter, matchStage);
        titlePanel?.SetActive(false);choicesRoot?.SetActive(false);
        phase = MatchPhase.Approaching;SetPoses(OSPaperPushPose.Idle, OSPaperPushPose.Idle);
        if (statusLabel != null) OSLocalizedText.SetRuntime(statusLabel, Text("approach", "准备……"));
        if (commentLabel != null) OSLocalizedText.SetRuntime(commentLabel, StageFlavor(false));
        var saved = GameDataManager.Instance.OSEnergyLoopState.paperPushMatch;
        bool restoring = saved.active && saved.chapter == matchChapter;
        BeginChapter02Runtime(restoring, saved);
        if (matchChapter == 1 && matchStage == 2)
        { observedSave.paperPushFirstRematchDone = true;observedSave.paperPushFirstRematchPending = false; }
        if (saved.active && saved.chapter == matchChapter)
        {
            round = saved.round;characterLossStreak = saved.characterLossStreak;
            previousPlayerMove = (OSPaperPushMove)saved.previousPlayerMove;
            committedCharacterMove = (OSPaperPushMove)saved.characterMove;
            playerX = saved.playerX;characterX = saved.characterX;
            if (matchChapter == 2 && matchStage == 2 && chapter02GearRevealed)
            { BeginChapter02Sliding();return; }
            if (saved.phase == 1) { ShowChoice();return; }
            if (saved.phase == 2)
            {
                phase = MatchPhase.Resolving;stamina = 0f;
                StartCoroutine(ResolveRound((OSPaperPushMove)saved.playerMove, committedCharacterMove));return;
            }
        }
        else saved.characterHadLead = false;
        if (matchChapter == 2 && matchStage == 2 && chapter02GearRevealed)
        { BeginChapter02Sliding();return; }
        if (matchChapter == 1 && matchStage == 3 && (!restoring || saved.phase == 3))
        { SaveMatch(3);StartCoroutine(CurtainIntro());return; }
        if (restoring && saved.phase == 0 && saved.pushElapsed > 0f)
            StartCoroutine(PushInterval(saved.pushElapsed, saved.pushStartPlayerX, saved.pushStartCharacterX));
        else StartCoroutine(PushInterval());
    }

    IEnumerator CurtainIntro()
    {
        phase = MatchPhase.Curtain;
        if (presentation != null) yield return presentation.PlayIntro();
        yield return PushInterval();
    }

    static OSLocalizedText.Line StageFlavor(bool title)
    {
        var data = GameDataManager.Instance;
        if (data?.CurrentChapterIndex == 2) return data.OSEnergyLoopState.chapter02PaperPushStep >= 4
            ? Chapter02Text("m2_title", "她把纸偶扶正，又往它身后挪了挪。")
            : Chapter02Text("m1_title", "她换了个坐姿，看起来练过。");
        int stage = data?.CurrentChapterIndex == 1 ? Mathf.Min(3, data.OSEnergyLoopState.chapter01PaperPushWins + 1) : 1;
        if (stage == 2) return Text("confident_2", "她收起了笑，打算认真一点。");
        if (stage == 3) return Text("confident_3", "她盯紧了场地。这次总该赢了吧。");
        return title ? Text("intro", "把她推出边缘。她看起来很有把握。")
            : Text("confident", "她一副轻松的样子，准备和你较量。");
    }

    IEnumerator PushInterval(float resumeElapsed = 0f, float? resumePlayerStart = null, float? resumeCharacterStart = null)
    {
        SaveMatch(0);
        float duration = CurrentPushIntervalSeconds;
        float elapsed = Mathf.Clamp(resumeElapsed, 0f, duration), lastTick = Time.unscaledTime;
        phase = MatchPhase.Pushing;stamina = 1f - elapsed / duration;hopClock = elapsed;
        choicesRoot?.SetActive(false);
        SetStates(OSPaperPushActionState.Pushing, OSPaperPushActionState.Pushing);
        SetPoses(OSPaperPushPose.Straining, OSPaperPushPose.Straining);
        if (round > 0 && statusLabel != null) OSLocalizedText.SetRuntime(statusLabel, Text("round", "第 {0} 回合", round));
        int soundedBeat = -1;
        float pStart = resumePlayerStart ?? playerX, cStart = resumeCharacterStart ?? characterX;
        float gapPerPawn = Mathf.Max(0f, cStart - pStart - ContactDistance) * .5f;
        var progress = GameDataManager.Instance.OSEnergyLoopState.paperPushMatch;
        progress.pushStartPlayerX = pStart;progress.pushStartCharacterX = cStart;progress.pushElapsed = elapsed;
        while (elapsed < duration)
        {
            yield return null;
            float delta = FrameDelta(ref lastTick);
            if (delta <= 0f) continue;
            if (phase == MatchPhase.Finished) yield break;
            elapsed = Mathf.Min(duration, elapsed + delta);
            hopClock = elapsed;
            stamina = Mathf.Clamp01(1f - elapsed / duration);
            float beats = elapsed / OSPaperPushRules.HopSeconds;
            int wholeBeats = Mathf.FloorToInt(beats);
            float u = beats - wholeBeats;
            float covered = Mathf.Min(gapPerPawn, wholeBeats * stepPixels);
            float travel = Mathf.Min(stepPixels, gapPerPawn - covered);
            playerX = pStart + covered + travel * OSPaperPushPawnMotion.HopTravel(u);
            characterX = cStart - covered - travel * OSPaperPushPawnMotion.HopTravel(u);
            hopPlayer = hopCharacter = travel > .01f ? OSPaperPushPawnMotion.HopArc(u) * hopHeight : 0f;
            SetPoses(travel > .01f ? OSPaperPushPose.Idle : OSPaperPushPose.Straining,
                travel > .01f ? OSPaperPushPose.Idle : OSPaperPushPose.Straining);
            progress.pushElapsed = elapsed;progress.playerX = playerX;progress.characterX = characterX;
            if (travel > .01f && u >= .87f && soundedBeat != wholeBeats)
            { sound.Play(sound.hop, .4f);presentation?.TableKnock();soundedBeat = wholeBeats; }
        }
        stamina = 0f;hopPlayer = hopCharacter = 0f;BeginChoice();
    }

    void BeginChoice()
    {
        round++;
        committedCharacterMove = OSPaperPushRules.ChooseCharacter(round, characterLossStreak,
            matchChapter, previousPlayerMove);
        SaveMatch(1);
        ShowChoice();
    }

    void ShowChoice()
    {
        phase = MatchPhase.Choosing;hopPlayer = hopCharacter = 0f;stamina = 0f;
        sound.Play(sound.choice, .65f);
        SetStates(OSPaperPushActionState.Choosing, OSPaperPushActionState.Choosing);
        SetPoses(OSPaperPushPose.Straining, OSPaperPushPose.Straining);
        choicesRoot?.SetActive(true);SetChoicesEnabled(matchChapter != 2 || !chapter02Paused);
        if (statusLabel != null) OSLocalizedText.SetRuntime(statusLabel, Text("choose", "第 {0} 回合 · 请选择", round));
        if (commentLabel != null) OSLocalizedText.SetRuntime(commentLabel, matchChapter == 2
            ? Chapter02Text("choosing", "她盯着你的手，看得很认真。") : characterLossStreak >= 2
            ? Text("cautious", "她谨慎了起来，正等着你出招。") : Text("thinking", "她已经想好了，正等着你出招。"));
        if (matchChapter == 2 && matchStage == 1 && chapter02ShowPreparationTell)
            StartCoroutine(Chapter02PreparationTell());
    }

    void ChooseDefend() => Choose(OSPaperPushMove.Defend);
    void ChooseLight() => Choose(OSPaperPushMove.LightPush);
    void ChooseHeavy() => Choose(OSPaperPushMove.HeavyPush);

    public bool Choose(OSPaperPushMove move)
    {
        if (phase != MatchPhase.Choosing || !CanSimulate || !System.Enum.IsDefined(typeof(OSPaperPushMove), move)) return false;
        phase = MatchPhase.Resolving;SetChoicesEnabled(false);
        committedCharacterMove = OSPaperPushRules.AssistChapter01(move, committedCharacterMove, matchChapter,
            round, playerX, OSPaperPushRules.OutSteps * stepPixels + ContactDistance * .5f, stepPixels);
        if (matchChapter == 2 && matchStage == 1 && round > OSPaperPushRules.Chapter02FirstMatchAssistAfterRound)
            committedCharacterMove = OSPaperPushRules.AssistChapter02FirstMatch(move, committedCharacterMove,
                round, UnityEngine.Random.value);
        SaveMatch(2, move);
        OS_Handle.Instance?.PlayInteractionFeedback();
        StartCoroutine(ResolveRound(move, committedCharacterMove));return true;
    }

    IEnumerator ResolveRound(OSPaperPushMove player, OSPaperPushMove character)
    {
        var result = OSPaperPushRules.Resolve(player, character, matchChapter);
        bool blocked = IsChapter02Bracing && result.Winner > 0;
        int outcome = blocked ? 0 : result.Winner;
        story?.ObserveRound(matchChapter, round, outcome, playerX, characterX, false);
        choicesRoot?.SetActive(false);
        if (statusLabel != null) OSLocalizedText.SetRuntime(statusLabel, Text("round", "第 {0} 回合", round));
        SetPoses(PreparationPose(player), PreparationPose(character));
        SetStates(PreparationState(player), PreparationState(character));
        bool interrupted = player == OSPaperPushMove.LightPush && character == OSPaperPushMove.HeavyPush
            || character == OSPaperPushMove.LightPush && player == OSPaperPushMove.HeavyPush;
        bool parried = player == OSPaperPushMove.Defend && character == OSPaperPushMove.LightPush
            || character == OSPaperPushMove.Defend && player == OSPaperPushMove.LightPush;
        bool hasHeavy = player == OSPaperPushMove.HeavyPush || character == OSPaperPushMove.HeavyPush;
        if (player == OSPaperPushMove.Defend || character == OSPaperPushMove.Defend) sound.Play(sound.guard, .65f);
        if (hasHeavy) sound.BeginWindup();

        if (blocked)
        {
            if (hasHeavy) yield return Wait(OSPaperPushRules.HeavyWindupSeconds);
            else yield return Wait(OSPaperPushRules.LightStartupSeconds);
            sound.StopWindup();
            yield return Chapter02BlockedPush();
            if (phase == MatchPhase.Finished) yield break;
        }
        else if (parried)
        {
            // Light meets the guard after 0.05 s; the physical reaction lasts 0.45 s.
            // The defender's physical position remains unchanged throughout the full 0.5 s.
            yield return Wait(OSPaperPushRules.LightStartupSeconds);
            sound.Play(sound.parry);
            presentation?.ReactToRound(result.Winner);
            bool playerDefends = player == OSPaperPushMove.Defend;
            SetStates(playerDefends ? OSPaperPushActionState.GuardSucceeded : OSPaperPushActionState.Stunned,
                playerDefends ? OSPaperPushActionState.Stunned : OSPaperPushActionState.GuardSucceeded);
            SetPoses(playerDefends ? OSPaperPushPose.Defend : OSPaperPushPose.Straining,
                playerDefends ? OSPaperPushPose.Straining : OSPaperPushPose.Defend);
            if (commentLabel != null) OSLocalizedText.SetRuntime(commentLabel, playerDefends
                ? Text("parry_win", "你防住了她的轻推，将她弹开！")
                : Text("parry_lose", "你的轻推被她防住，你被弹开了！"));
            yield return KnockBack(!playerDefends, result.Winner * result.PushSteps * stepPixels, false);
            if (phase == MatchPhase.Finished) yield break;
        }
        else if (interrupted)
        {
            ShowWindup(player, character);
            yield return Wait(OSPaperPushRules.LightStartupSeconds);
            sound.StopWindup();sound.Play(sound.light, .85f);
            presentation?.ReactToRound(result.Winner);
            bool playerFalls = player == OSPaperPushMove.HeavyPush;
            SetStates(playerFalls ? OSPaperPushActionState.KnockedDown : OSPaperPushActionState.Attacking,
                playerFalls ? OSPaperPushActionState.Attacking : OSPaperPushActionState.KnockedDown);
            SetPoses(playerFalls ? OSPaperPushPose.Straining : OSPaperPushPose.LightPush,
                playerFalls ? OSPaperPushPose.LightPush : OSPaperPushPose.Straining);
            if (commentLabel != null) OSLocalizedText.SetRuntime(commentLabel, playerFalls
                ? Text("interrupt_lose", "她用轻推打断蓄力，把你推倒了！")
                : Text("interrupt_win", "你用轻推打断蓄力，把她推倒了！"));
            yield return KnockBack(playerFalls, result.Winner * result.PushSteps * stepPixels, true);
            if (phase == MatchPhase.Finished) yield break;
            yield return GetUp(playerFalls);
        }
        else if (hasHeavy)
        {
            ShowWindup(player, character);
            yield return Wait(OSPaperPushRules.GuardSeconds);
            // A guard expires at 0.5 s; heavy is still winding up until 1 s.
            if (player == OSPaperPushMove.Defend) { playerState = OSPaperPushActionState.Ready;SetSprite(playerImage, playerPoses, OSPaperPushPose.Idle); }
            if (character == OSPaperPushMove.Defend) { characterState = OSPaperPushActionState.Ready;SetSprite(characterImage, characterPoses, OSPaperPushPose.Idle); }
            yield return Wait(OSPaperPushRules.HeavyWindupSeconds - OSPaperPushRules.GuardSeconds);
            sound.StopWindup();sound.Play(sound.heavy);
            presentation?.ReactToRound(result.Winner);
            if (result.Winner != 0)
            {
                bool playerWins = result.Winner > 0;
                SetStates(playerWins ? OSPaperPushActionState.Attacking : OSPaperPushActionState.GuardBroken,
                    playerWins ? OSPaperPushActionState.GuardBroken : OSPaperPushActionState.Attacking);
                SetPoses(playerWins ? OSPaperPushPose.HeavyPush : OSPaperPushPose.Straining,
                    playerWins ? OSPaperPushPose.Straining : OSPaperPushPose.HeavyPush);
                if (commentLabel != null) OSLocalizedText.SetRuntime(commentLabel, playerWins
                    ? Text("break_guard_win", "你用重推突破防御，将她顶开！")
                    : Text("break_guard_lose", "她用重推突破防御，将你顶开！"));
                if (playerWins) characterTilt = -12f;else playerTilt = 12f;
                yield return MoveTogether(result.Winner * result.PushSteps, .18f, playerWins, !playerWins);
                if (phase == MatchPhase.Finished) yield break;
                yield return Wait(OSPaperPushRules.ReactionSeconds - .18f);
            }
            else
            {
                SetStates(OSPaperPushActionState.Attacking, OSPaperPushActionState.Attacking);
                SetPoses(OSPaperPushPose.HeavyPush, OSPaperPushPose.HeavyPush);
                if (commentLabel != null) OSLocalizedText.SetRuntime(commentLabel, Text("equal", "你和她势均力敌，谁也没退。"));
                yield return Wait(.18f);
            }
        }
        else
        {
            presentation?.ReactToRound(0);
            if (player == OSPaperPushMove.Defend)
            {
                if (commentLabel != null) OSLocalizedText.SetRuntime(commentLabel, Text("both_guard", "你和她都选择防御，留在原地。"));
                yield return Wait(OSPaperPushRules.GuardSeconds);
            }
            else
            {
                yield return Wait(OSPaperPushRules.LightStartupSeconds);
                sound.Play(sound.light, .85f);
                SetPoses(OSPaperPushPose.Straining, OSPaperPushPose.Straining);
                SetStates(OSPaperPushActionState.Stunned, OSPaperPushActionState.Stunned);
                if (commentLabel != null) OSLocalizedText.SetRuntime(commentLabel, Text("equal", "你和她势均力敌，谁也没退。"));
                yield return Wait(OSPaperPushRules.ReactionSeconds);
            }
        }

        playerTilt = characterTilt = 0f;
        if (!blocked && (result.PlayerPanting || result.CharacterPanting))
        {
            SetStates(result.PlayerPanting ? OSPaperPushActionState.Panting : OSPaperPushActionState.Pushing,
                result.CharacterPanting ? OSPaperPushActionState.Panting : OSPaperPushActionState.Pushing);
            SetPoses(result.PlayerPanting ? OSPaperPushPose.Panting : OSPaperPushPose.LightPush,
                result.CharacterPanting ? OSPaperPushPose.Panting : OSPaperPushPose.LightPush);
            if (statusLabel != null) OSLocalizedText.SetRuntime(statusLabel, result.PlayerPanting && result.CharacterPanting
                ? Text("both_tired", "你和她都在喘气……")
                : result.PlayerPanting ? Text("tired_you", "你在喘气，她趁机反推")
                : Text("tired_her", "她在喘气，你趁机反推"));
            for (int i = 0; i < OSPaperPushRules.RecoveryHops; i++)
            {
                if (result.PlayerPanting && result.CharacterPanting) yield return Wait(OSPaperPushRules.HopSeconds);
                else yield return MoveTogether((result.PlayerPanting ? -1f : 1f) * OSPaperPushRules.RecoveryPushPerHop,
                    OSPaperPushRules.HopSeconds, !result.PlayerPanting, !result.CharacterPanting);
                if (phase == MatchPhase.Finished) yield break;
            }
        }
        previousPlayerMove = player;
        characterLossStreak = outcome > 0 ? characterLossStreak + 1 : outcome < 0 ? 0 : characterLossStreak;
        story?.ObserveRound(matchChapter, round, outcome, playerX, characterX, true);
        Chapter02RoundCompleted();
        hopPlayer = hopCharacter = playerTilt = characterTilt = 0f;
        if (matchChapter == 2 && matchStage == 2 && chapter02GearRevealed)
        { BeginChapter02Sliding();yield break; }
        // Keep the outcome readable throughout the next push interval.
        yield return PushInterval();
    }

    IEnumerator KnockBack(bool playerIsVictim, float distance, bool falls)
    {
        float start = playerIsVictim ? playerX : characterX;
        float tilt = (playerIsVictim ? 1f : -1f) * (falls ? 72f : 26f);
        float lastTick = Time.unscaledTime;
        bool landed = false;
        for (float t = 0f; t < OSPaperPushRules.ReactionSeconds;)
        {
            yield return null;
            float delta = FrameDelta(ref lastTick);
            if (delta <= 0f) continue;
            t = Mathf.Min(OSPaperPushRules.ReactionSeconds, t + delta);
            float u = Mathf.Clamp01(t / .18f);
            if (falls && !landed && u >= 1f) { sound.Play(sound.fall, .9f);landed = true; }
            float eased = falls ? 1f - (1f - u) * (1f - u) : 1f - Mathf.Pow(1f - u, 3f);
            float recoil = falls ? eased : t < .14f ? Mathf.Sin(t / .28f * Mathf.PI)
                : Mathf.Lerp(1f, .15f, Mathf.SmoothStep(0f, 1f, (t - .14f) / .31f));
            float lift = falls ? 0f : Mathf.Sin(Mathf.Clamp01(t / .30f) * Mathf.PI) * 4f;
            if (playerIsVictim) { playerX = start + distance * eased;playerTilt = tilt * recoil;hopPlayer = lift; }
            else { characterX = start + distance * eased;characterTilt = tilt * recoil;hopCharacter = lift; }
            if (CheckOut()) yield break;
        }
    }

    IEnumerator GetUp(bool player)
    {
        sound.Play(sound.getUp, .55f);
        if (player) playerState = OSPaperPushActionState.GettingUp;else characterState = OSPaperPushActionState.GettingUp;
        float tilt = player ? playerTilt : characterTilt;
        float lastTick = Time.unscaledTime;
        for (float t = 0f; t < .15f;)
        {
            yield return null;
            t += FrameDelta(ref lastTick);float u = Mathf.Clamp01(t / .15f);
            float ease = OSPaperPushPawnMotion.GetUpEase(u);
            if (player) playerTilt = Mathf.LerpUnclamped(tilt, 0f, ease);else characterTilt = Mathf.LerpUnclamped(tilt, 0f, ease);
        }
    }

    IEnumerator MoveTogether(float steps, float seconds, bool playerHops, bool characterHops)
    {
        float pStart = playerX, cStart = characterX;
        float lastTick = Time.unscaledTime;
        bool landed = false;
        for (float t = 0f; t < seconds;)
        {
            yield return null;
            float frameDelta = FrameDelta(ref lastTick);
            if (frameDelta <= 0f) continue;
            t += frameDelta;float u = Mathf.Clamp01(t / seconds);
            float delta = steps * stepPixels * (u * u * (3f - 2f * u));
            playerX = pStart + delta;characterX = cStart + playerX - pStart;
            float hop = OSPaperPushPawnMotion.HopArc(u) * hopHeight;
            if (!landed && u >= .87f && seconds >= .5f) { sound.Play(sound.hop, .4f);presentation?.TableKnock();landed = true; }
            hopPlayer = playerHops ? hop : 0f;hopCharacter = characterHops ? hop : 0f;
            if (CheckOut()) yield break;
        }
        playerX = pStart + steps * stepPixels;characterX = cStart + playerX - pStart;
        hopPlayer = hopCharacter = 0f;
        CheckOut();
    }

    bool CheckOut()
    {
        story?.ObserveRound(matchChapter, round, 0, playerX, characterX, true);
        float edge = OSPaperPushRules.OutSteps * stepPixels + ContactDistance * .5f;
        if (characterX >= edge) { FinishMatch(true);return true; }
        if (playerX <= -edge)
        {
            if (matchChapter == 1)
            {
                // Last-resort edge clamp for restored positions / future balance edits.
                playerX = -edge + 1f;
                characterX = Mathf.Max(characterX, playerX + ContactDistance);
            }
            else { FinishMatch(false);return true; }
        }
        return false;
    }

    void SaveMatch(int savedPhase, OSPaperPushMove player = OSPaperPushMove.LightPush)
    {
        var saved = GameDataManager.Instance?.OSEnergyLoopState.paperPushMatch;
        if (saved == null) return;
        saved.active = true;saved.chapter = matchChapter;saved.phase = savedPhase;saved.round = round;
        saved.matchStage = matchStage;saved.chapter02BraceReleased = chapter02BraceReleased;
        saved.playerX = playerX;saved.characterX = characterX;saved.characterLossStreak = characterLossStreak;
        saved.previousPlayerMove = (int)previousPlayerMove;saved.characterMove = (int)committedCharacterMove;saved.playerMove = (int)player;
        saved.pushElapsed = 0f;saved.pushStartPlayerX = playerX;saved.pushStartCharacterX = characterX;
    }

    IEnumerator Wait(float seconds)
    {
        float lastTick = Time.unscaledTime;
        for (float t = 0f; t < seconds;)
        { yield return null;t += FrameDelta(ref lastTick); }
    }

    // Never inherit a delta from before a stage began, or include time spent minimized.
    float FrameDelta(ref float previous)
    {
        float now = Time.unscaledTime;
        float elapsed = Mathf.Max(0f, now - previous);
        previous = now;
        return CanAnimate ? elapsed : 0f;
    }

    void FinishMatch(bool playerWon)
    {
        if (phase == MatchPhase.Finished) return;
        sound.StopWindup();
        phase = MatchPhase.Finished;hopPlayer = hopCharacter = 0f;
        SetStates(playerWon ? OSPaperPushActionState.Ready : OSPaperPushActionState.Out,
            playerWon ? OSPaperPushActionState.Out : OSPaperPushActionState.Ready);
        choicesRoot?.SetActive(false);titlePanel?.SetActive(true);
        story?.ObserveRound(matchChapter, round, playerWon ? 1 : -1, playerX, characterX, true);
        GameDataManager.Instance?.RecordPaperPushResult(playerWon);RefreshRecord();
        FinishChapter02Runtime(playerWon);
        RefreshStartButton();
        if (startLabel != null) OSLocalizedText.SetRuntime(startLabel, matchChapter == 2 && matchStage == 2
            ? Text("start", "开始") : Text("again", "再来一局"));
        if (statusLabel != null) OSLocalizedText.SetRuntime(statusLabel, playerWon ? Text("win", "你赢了！") : Text("lose", "这次她赢了"));
        // Chapter 1 now reacts through Live; do not repeat the old first-win line
        // underneath the result when she is supposed to be increasingly frustrated.
        if (commentLabel != null) OSLocalizedText.SetRuntime(commentLabel, matchChapter == 1 || matchChapter == 2 ? OSLocalizedText.Line.Empty : playerWon
            ? Text("win_comment", "咦……这个东西有这么难赢吗？") : Text("lose_comment", "嘿嘿，再来一次？"));
        SetPoses(playerWon ? OSPaperPushPose.Idle : OSPaperPushPose.Panting,
            playerWon ? OSPaperPushPose.Panting : OSPaperPushPose.Idle);
        if (playerWon) presentation?.Celebrate();
        if (playerWon && matchChapter == 1) story?.OnChapter01Victory();
        else if (playerWon && matchChapter != 2) linkMessages?.NotifyGlobalExternalSignal("paper_push.player_won");
        StartCoroutine(FallOff(playerWon));
    }

    IEnumerator FallOff(bool playerWon)
    {
        var loser = playerWon ? characterImage : playerImage;
        if (loser == null) yield break;
        float startTilt = playerWon ? characterTilt : playerTilt;
        float lastTick = Time.unscaledTime;
        bool landed = false;
        for (float t = 0; t < .45f;)
        {
            yield return null;
            float delta = FrameDelta(ref lastTick);
            if (delta <= 0f) continue;
            t += delta;float u = Mathf.Clamp01(t / .45f);
            if (!landed && u >= .55f) { sound.Play(sound.fall, .9f);landed = true; }
            if (playerWon) hopCharacter = -36f * u * u;else hopPlayer = -36f * u * u;
            if (playerWon) characterTilt = Mathf.Lerp(startTilt, -100f, u);else playerTilt = Mathf.Lerp(startTilt, 100f, u);
        }
        sound.Play(playerWon ? sound.win : sound.lose, playerWon ? .75f : .65f);
        if (matchChapter == 2)
        {
            ResetChapter02Tilt();
            if (matchStage == 1)
            {
                // Keep the fall/result readable, then restore the actual start
                // screen. The director dispatches the invitation at that boundary.
                yield return Wait(.35f);
                if (phase == MatchPhase.Finished) ShowTitle();
            }
        }
    }

    void SetPoses(OSPaperPushPose player, OSPaperPushPose character)
    {
        playerPose = player;characterPose = character;
        SetSprite(playerImage, playerPoses, player);SetSprite(characterImage, characterPoses, character);
    }

    static void SetSprite(Image image, Sprite[] poses, OSPaperPushPose pose)
    { if (image != null && poses != null && (int)pose < poses.Length && poses[(int)pose] != null) image.sprite = poses[(int)pose]; }

    void RenderPawns()
    {
        float footY = StandFootY + (matchChapter == 2 ? chapter02PawnYOffset : 0f);
        if (playerImage != null)
        {
            playerImage.rectTransform.anchoredPosition = new Vector2(Mathf.Round(playerX + playerMotion.Offset.x), footY + Mathf.Round(hopPlayer + playerMotion.Offset.y));
            playerImage.rectTransform.localEulerAngles = new Vector3(0, 0, playerMotion.Angle);
            playerImage.rectTransform.localScale = new Vector3(playerMotion.Scale.x, playerMotion.Scale.y, 1f);
        }
        if (characterImage != null)
        {
            characterImage.rectTransform.anchoredPosition = new Vector2(Mathf.Round(characterX + characterMotion.Offset.x), footY + Mathf.Round(hopCharacter + characterMotion.Offset.y));
            characterImage.rectTransform.localEulerAngles = new Vector3(0, 0, characterMotion.Angle);
            characterImage.rectTransform.localScale = new Vector3(characterMotion.Scale.x, characterMotion.Scale.y, 1f);
        }
        if (playerMarker != null)
        {
            playerMarker.gameObject.SetActive(playerState != OSPaperPushActionState.Out);
            Vector3 head = playerImage != null && playerMarker.parent != null
                ? playerMarker.parent.InverseTransformPoint(playerImage.rectTransform.TransformPoint(new Vector3(0f, playerImage.rectTransform.rect.yMax, 0f)))
                : new Vector3(playerX, 82f, 0f);
            playerMarker.localPosition = new Vector3(
                Mathf.Round(head.x), Mathf.Round(head.y + 9f + Mathf.Sin(markerTime * 2.4f) * 2f), 0f);
        }
        RenderReaction(playerReactionLabel, playerReaction, playerReactionRemaining, playerX);
        RenderReaction(characterReactionLabel, characterReaction, characterReactionRemaining, characterX);
    }

    void SetStaminaWidth(RectTransform fill)
    { if (fill != null) fill.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, Mathf.Round(StaminaWidth * stamina)); }

    void SetStates(OSPaperPushActionState player, OSPaperPushActionState character)
    {
        HoldReaction(playerState, player, ref playerReaction, ref playerReactionRemaining);
        HoldReaction(characterState, character, ref characterReaction, ref characterReactionRemaining);
        playerState = player;characterState = character;
    }

    static void HoldReaction(OSPaperPushActionState previous, OSPaperPushActionState next,
        ref OSPaperPushActionState reaction, ref float remaining)
    {
        if (previous == next) return;
        if (next != OSPaperPushActionState.GuardSucceeded && next != OSPaperPushActionState.GuardBroken
            && next != OSPaperPushActionState.KnockedDown) return;
        reaction = next;remaining = ReactionLabelSeconds;
    }

    static void RenderReaction(TMP_Text label, OSPaperPushActionState state, float remaining, float x)
    {
        if (label == null) return;
        label.rectTransform.anchoredPosition = new Vector2(Mathf.Round(Mathf.Clamp(x,-105f,105f)), 105f);
        OSLocalizedText.SetRuntime(label, remaining <= 0f ? OSLocalizedText.Line.Empty
            : state == OSPaperPushActionState.GuardSucceeded ? Text("guard_success", "防御成功！")
            : state == OSPaperPushActionState.GuardBroken ? Text("guard_failed", "防御失败！")
            : state == OSPaperPushActionState.KnockedDown ? Text("knocked_down", "被推倒！") : OSLocalizedText.Line.Empty);
    }

    void ShowWindup(OSPaperPushMove player, OSPaperPushMove character)
    {
        if (commentLabel == null) return;
        OSLocalizedText.SetRuntime(commentLabel, player == OSPaperPushMove.HeavyPush && character == OSPaperPushMove.HeavyPush
            ? Text("windup_both", "你和她都在为重推蓄力……")
            : player == OSPaperPushMove.HeavyPush ? Text("windup_you", "你正在为重推蓄力……")
            : Text("windup_her", "她正在为重推蓄力……"));
    }

    void SetChoicesEnabled(bool value)
    { if (defendButton != null) defendButton.interactable = value;if (lightButton != null) lightButton.interactable = value;if (heavyButton != null) heavyButton.interactable = value; }

    void RefreshRecord()
    {
        var s = GameDataManager.Instance?.OSEnergyLoopState;
        if (recordLabel != null) OSLocalizedText.SetRuntime(recordLabel, Text("record", "胜 {0}  /  负 {1}", s != null ? s.paperPushWins : 0, s != null ? s.paperPushLosses : 0));
    }

    static OSPaperPushPose PreparationPose(OSPaperPushMove move) => move == OSPaperPushMove.HeavyPush ? OSPaperPushPose.Windup : ActionPose(move);
    static OSPaperPushActionState PreparationState(OSPaperPushMove move) => move == OSPaperPushMove.HeavyPush
        ? OSPaperPushActionState.WindingUp : move == OSPaperPushMove.Defend ? OSPaperPushActionState.Guarding : OSPaperPushActionState.Attacking;
    static OSPaperPushPose ActionPose(OSPaperPushMove move) => move == OSPaperPushMove.Defend ? OSPaperPushPose.Defend
        : move == OSPaperPushMove.LightPush ? OSPaperPushPose.LightPush : OSPaperPushPose.HeavyPush;
    static OSLocalizedText.Line Text(string key, string fallback, params object[] arguments) => new OSLocalizedText.Line("ui.os.paper_push." + key, fallback, arguments);
}
