using System;
using System.Collections.Generic;
using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using MrTGame.Core;
using MrTGame.Heartbeat;

namespace MrTGame.SideScroll
{
    public enum WalkMode
    {
        Directional,   // 方向键行走(保底)
        BeatWalk       // 节拍步行:每次有效心跳前进一步——"心脏驱动这个身体"(设计文档 §3.1 草案)
    }

    /// <summary>横板2D主角:行走(双模式)/ 交互(E)/ 蹲下(序章救人、终章放花)。</summary>
    [RequireComponent(typeof(Rigidbody2D))]
    public class PlayerController2D : MonoBehaviour
    {
        sealed class DialoguePresentationState
        {
            public TMP_Text DialogueText;
            public GameObject Root;
            public SpriteRenderer PanelRenderer;
            public SpriteRenderer ETipRenderer;
            public RectTransform ETipRectTransform;
            public ContentSizeFitter DialogueTextSizeFitter;
            public TMP_Text NameText;
            public RectTransform DialogueTextRectTransform;
            public RectTransform NameRectTransform;
            public Vector2 DialogueTextBaseAnchoredPosition;
            public Vector2 NameBaseAnchoredPosition;
            public bool OffsetNameWhenWrapped;
            public Tween PanelFadeTween;
            public Tween NameFadeTween;
            public Tween ETipFadeTween;
            public Tween ETipFloatTween;
            public bool PanelHasAppeared;
        }

        [Header("控制权限")]
        [SerializeField] bool canMove = true;
        [SerializeField] bool canInteractive = true;

        [Header("移动")]
        [SerializeField] WalkMode walkMode = WalkMode.Directional;
        [SerializeField] float walkSpeed = 2.2f;
        [SerializeField] float beatStepDistance = 0.6f;

        [Header("交互")]
        [SerializeField] float interactRadius = 0.8f;
        [SerializeField] LayerMask interactMask = ~0;

        [Header("事件对话")]
        [SerializeField] TMP_Text playerDialogueText;
        [SerializeField] GameObject playerDialogueRoot;
        [SerializeField, Min(1f)] float dialogueCharactersPerSecond = 20f;
        [SerializeField] SpriteRenderer dialoguePanelRenderer;
        [SerializeField, Min(0.01f)] float dialoguePanelFadeDuration = 0.1f;
        [SerializeField] SpriteRenderer dialogueETipRenderer;
        [SerializeField, Min(0.01f)] float dialogueETipFadeDuration = 0.1f;
        [SerializeField] float dialogueETipStartY = -0.1f;
        [SerializeField] float dialogueETipEndY = -0.2f;
        [SerializeField, Min(0.01f)] float dialogueETipFloatDuration = 0.4f;

        const float DialoguePanelMinWidth = 7f;
        const float DialoguePanelMaxWidth = 9f;
        const float DialogueTextMaxWidth = 8.3f;
        const float DialoguePanelSingleLineHeight = 0.9f;
        const float DialoguePanelWrappedHeight = 1.3f;
        const float DialoguePanelWidthAdjustment = 0.4f;
        const float DialogueTextWrappedYOffset = 0.2f;
        const float DialogueNameWrappedYOffset = 0.4f;
        const string PlayerDialogueName = "???";
        const string PlayerDialogueCharacterId = "0";
        const string ElderDialogueCharacterId = "1";
        const string PlayerDialogueVoiceSfxPrefix = "Character_00_";
        const string ElderDialogueVoiceSfxPrefix = "Character_01_";

        [Header("表现")]
        [SerializeField] Animator animator;
        [SerializeField] SpriteRenderer sprite;

        Rigidbody2D _body;
        InputAction _moveAction;
        InputAction _interactAction;
        float _moveInput;
        int _facing = 1;
        bool _isLowHeart;
        Quaternion _rightFacingRotation;

        SceneEventBase _currentEvent;
        SceneEventBase _activeDialogueEvent;
        EventData _activeEventData;
        TMP_Text _npcDialogueText;
        GameObject _npcDialogueRoot;
        TMP_Text _activeDialogueText;
        DialoguePresentationState _activeDialoguePresentation;
        Tween _dialogueTween;
        AudioSource _dialogueVoiceSource;
        AudioClip _dialogueVoiceClip;
        AudioData[] _activeDialogueVoiceSfx;
        AudioData[] _playerDialogueVoiceSfx;
        AudioData[] _elderDialogueVoiceSfx;
        int _lastDialogueVoiceIndex = -1;
        bool _dialogueVoiceGroupsResolved;
        string _currentDialogueText = string.Empty;
        int _dialogueIndex;
        bool _isTypingDialogue;
        bool _isMovementLockedByEventDialogue;
        readonly DialoguePresentationState _playerDialoguePresentation =
            new DialoguePresentationState();
        readonly DialoguePresentationState _npcDialoguePresentation =
            new DialoguePresentationState();

        static readonly int IdleParam = Animator.StringToHash("InIdle");
        static readonly int WalkDirectionParam = Animator.StringToHash("WalkDirection");
        static readonly int EnterLowHeartParam = Animator.StringToHash("EnterLowHeart");
        static readonly int IdleState = Animator.StringToHash("Base Layer.Player_Idle");
        static readonly int LowHeartState = Animator.StringToHash("Base Layer.Player_WalkLowHeart");
        static readonly int CrouchParam = Animator.StringToHash("Crouch");

        public bool CanMove => canMove;
        public bool CanInteractive => canInteractive;
        public SceneEventBase CurrentEvent => _currentEvent;
        public bool IsInEventDialogue => _activeEventData != null;

        public void CaptureResumeState(GameFlowSaveData state)
        {
            if (state == null) return;
            state.hasPlayerPosition = true;
            state.playerPosition = transform.position;
            state.playerFacing = _facing;
            state.dialogueEventId = _activeDialogueEvent != null ? _activeDialogueEvent.EventId : null;
            state.dialogueLineIndex = _activeDialogueEvent != null ? _dialogueIndex : 0;
        }

        public void RestoreResumeState(GameFlowSaveData state, bool resumeDialogue)
        {
            if (state == null) return;
            if (!resumeDialogue)
            {
                CancelEventDialogue();
                if (state.hasPlayerPosition) TeleportTo(state.playerPosition);
                SetFacing(state.playerFacing);
                return;
            }
            if (string.IsNullOrEmpty(state.dialogueEventId)) return;
            foreach (SceneEventBase sceneEvent in UnityEngine.Object.FindObjectsByType<SceneEventBase>(FindObjectsInactive.Include))
            {
                if (sceneEvent.EventId != state.dialogueEventId || sceneEvent.IsCompleted
                    || !sceneEvent.gameObject.activeInHierarchy) continue;
                _currentEvent = sceneEvent;
                TryStartEventDialogue(state.dialogueLineIndex);
                return;
            }
        }

        void Awake()
        {
            _body = GetComponent<Rigidbody2D>();
            if (animator == null)
                animator = GetComponentInChildren<Animator>(true);
            if (sprite == null && animator != null)
                sprite = animator.GetComponent<SpriteRenderer>();
            if (sprite != null)
            {
                _rightFacingRotation = sprite.transform.localRotation;
                sprite.flipX = false;
            }

            _moveAction = new InputAction("Move");
            _moveAction.AddCompositeBinding("1DAxis")
                .With("Negative", "<Keyboard>/leftArrow")
                .With("Positive", "<Keyboard>/rightArrow");
            _moveAction.AddCompositeBinding("1DAxis")
                .With("Negative", "<Keyboard>/a")
                .With("Positive", "<Keyboard>/d");

            _interactAction = new InputAction("Interact", binding: "<Keyboard>/e");
            _interactAction.performed += _ => TryInteract();

            ResolvePlayerDialogueView();
            ResolvePlayerDialoguePresentation();
            ResetDialoguePresentations();
            SetDialogueViewActive(playerDialogueRoot, playerDialogueText, false);
        }

        void OnEnable()
        {
            _moveAction.Enable();
            _interactAction.Enable();
            GameEvents.Beat += OnBeat;
            if (_isLowHeart && animator != null)
                animator.Play(LowHeartState, 0, 0f);
        }

        void OnDisable()
        {
            _moveAction.Disable();
            _interactAction.Disable();
            GameEvents.Beat -= OnBeat;
            CancelEventDialogue();
            _currentEvent = null;
        }

        void Update()
        {
            UpdateDialogueVoicePlayback();
            _moveInput = canMove && !GameTestConsole.BlocksGameplayInput
                && GameSessionFlow.Instance?.IsBusy != true ? _moveAction.ReadValue<float>() : 0f;

            if (!Mathf.Approximately(_moveInput, 0f))
            {
                SetFacing(_moveInput > 0f ? 1 : -1);
            }

            SetWalkingAnimation(canMove
                && walkMode == WalkMode.Directional
                && !Mathf.Approximately(_moveInput, 0f));
        }

        void SetFacing(int facing)
        {
            if (_facing == facing)
                return;

            _facing = facing;
            if (sprite == null)
                return;

            // DialoguePanel is a child of Character; preserve its world pose
            // while rotating only the character's visual facing.
            Transform dialogue = playerDialogueRoot != null
                ? playerDialogueRoot.transform : null;
            bool preserveDialogue = dialogue != null
                && dialogue != sprite.transform
                && dialogue.IsChildOf(sprite.transform);
            Vector3 dialoguePosition = preserveDialogue ? dialogue.position : Vector3.zero;
            Quaternion dialogueRotation = preserveDialogue ? dialogue.rotation : Quaternion.identity;
            sprite.transform.localRotation = _rightFacingRotation
                * Quaternion.Euler(0f, facing < 0 ? 180f : 0f, 0f);
            if (preserveDialogue)
                dialogue.SetPositionAndRotation(dialoguePosition, dialogueRotation);
        }

        void SetWalkingAnimation(bool walking)
        {
            if (animator == null)
                return;

            animator.SetBool(IdleParam, !walking);
            animator.SetFloat(WalkDirectionParam, _facing);
        }

        public void SetLowHeart(bool isLowHeart, bool immediate = false)
        {
            bool changed = _isLowHeart != isLowHeart;
            _isLowHeart = isLowHeart;
            if (animator == null || !animator.isActiveAndEnabled)
                return;
            if (isLowHeart && !changed && !immediate)
                return;

            animator.ResetTrigger(EnterLowHeartParam);
            if (immediate || !isLowHeart)
                animator.Play(isLowHeart ? LowHeartState : IdleState, 0, 0f);
            else if (changed)
                animator.SetTrigger(EnterLowHeartParam);
        }

        void FixedUpdate()
        {
            if (!canMove || GameTestConsole.BlocksGameplayInput || GameSessionFlow.Instance?.IsBusy == true)
            {
                StopHorizontalMovement();
                return;
            }

            if (walkMode != WalkMode.Directional) return;
            _body.linearVelocity = new Vector2(_moveInput * walkSpeed, _body.linearVelocity.y);
        }

        void OnBeat(BeatResult result)
        {
            if (!canMove || GameTestConsole.BlocksGameplayInput || GameSessionFlow.Instance?.IsBusy == true) return;
            if (walkMode != WalkMode.BeatWalk) return;
            if (result.Judgement == BeatJudgement.Miss) return;

            // 有效心跳 → 向面朝方向迈一步(TODO: 换成 MoveTowards 的平滑步进 + 步行动画单步触发)
            _body.MovePosition(_body.position + Vector2.right * (_facing * beatStepDistance));
        }

        void TryInteract()
        {
            if (!canInteractive || GameTestConsole.BlocksGameplayInput || GameSessionFlow.Instance?.IsBusy == true)
                return;

            if (IsInEventDialogue)
            {
                HandleDialogueInteraction();
                NotifyValidEventInteraction();
                return;
            }

            if (_currentEvent != null)
            {
                if (TryStartEventDialogue())
                    NotifyValidEventInteraction();
                return;
            }

            var hits = Physics2D.OverlapCircleAll(transform.position, interactRadius, interactMask);
            foreach (var hit in hits)
            {
                if (hit.TryGetComponent<Interactable>(out var interactable))
                {
                    interactable.Interact(this);
                    return;
                }
            }
        }

        public void SetCurrentEvent(SceneEventBase sceneEvent)
        {
            if (sceneEvent == null)
                return;

            _currentEvent = sceneEvent;

            if (sceneEvent.TryGetNpcDialogueView(out TMP_Text dialogueText, out GameObject dialogueRoot))
                SetDialogueViewActive(dialogueRoot, dialogueText, false);
        }

        public void ClearCurrentEvent(SceneEventBase sceneEvent)
        {
            if (_currentEvent == sceneEvent)
                _currentEvent = null;
        }

        public void CancelEventDialogue(SceneEventBase sceneEvent)
        {
            if (_activeDialogueEvent == sceneEvent)
                CancelEventDialogue();
        }

        bool TryStartEventDialogue(int resumeLine = 0)
        {
            SceneEventBase sceneEvent = _currentEvent;
            if (sceneEvent == null)
                return false;

            EventData_SO eventDataSource = GameDataManager.Instance != null
                ? GameDataManager.Instance.EventData
                : null;

            if (eventDataSource == null)
            {
                Debug.LogWarning("[PlayerController2D] GameDataManager.EventData is not assigned.", this);
                return false;
            }

            if (!eventDataSource.TryGetEvent(sceneEvent.EventId, out EventData eventData))
            {
                Debug.LogWarning(
                    $"[PlayerController2D] No EventData was found for eventID '{sceneEvent.EventId}'.",
                    sceneEvent);
                return false;
            }

            if (eventData.DialogueCount <= 0)
            {
                if (sceneEvent.CanCompleteWithoutDialogue)
                {
                    sceneEvent.CompleteEvent();
                    return true;
                }

                Debug.LogWarning(
                    $"[PlayerController2D] EventData '{sceneEvent.EventId}' has no dialogue entries.",
                    sceneEvent);
                return false;
            }

            ResolvePlayerDialogueView();
            _activeDialogueEvent = sceneEvent;
            _activeEventData = eventData;
            _dialogueIndex = Mathf.Clamp(resumeLine, 0, eventData.DialogueCount - 1);

            _npcDialogueText = null;
            _npcDialogueRoot = null;
            sceneEvent.TryGetNpcDialogueView(out _npcDialogueText, out _npcDialogueRoot);

            PrepareDialoguePresentations();
            LockMovementForEventDialogue();
            HideDialogueViews();
            // These callbacks only stage the existing scene effects (the elder's
            // pose and the graveyard failure effect), never complete the event.
            // Reapply prior cues so resuming a later line keeps its visual context.
            for (int i = 0; i < _dialogueIndex; i++)
                if (eventData.TryGetDialogue(i, out EventDialogueData earlier))
                    sceneEvent.OnDialogueLineEntered(i, earlier);
            ShowCurrentDialogue();
            return IsInEventDialogue;
        }

        void NotifyValidEventInteraction()
        {
            // 只能在 Event 已确认消费本次交互后调用。
            // 若未来从原始 E 输入或空交互分支调用，这是逻辑错误，必须向项目作者报告。
            if (MainGameHandle.Instance == null
                || !MainGameHandle.Instance.IsInMainScene)
            {
                return;
            }

            AdditionHandle.Instance?.HighlightNextHeartbeat();
        }


        void HandleDialogueInteraction()
        {
            if (_isTypingDialogue)
            {
                CompleteCurrentDialogueText();
                return;
            }

            _dialogueIndex++;
            ShowCurrentDialogue();
        }

        void ShowCurrentDialogue()
        {
            if (_activeEventData == null)
                return;

            if (_dialogueIndex >= _activeEventData.DialogueCount)
            {
                CompleteEventDialogue();
                return;
            }

            if (!_activeEventData.TryGetDialogue(
                    _dialogueIndex,
                    out EventDialogueData dialogue))
            {
                Debug.LogWarning(
                    $"[PlayerController2D] Event '{_activeDialogueEvent.EventId}' dialogue " +
                    $"{_dialogueIndex} is null or invalid. The event was not completed.",
                    _activeDialogueEvent);
                CancelEventDialogue();
                return;
            }

            if (!TryGetDialogueView(
                    dialogue.characterID,
                    out TMP_Text dialogueText,
                    out GameObject dialogueRoot))
            {
                CancelEventDialogue();
                return;
            }

            _activeDialogueEvent?.OnDialogueLineEntered(
                _dialogueIndex,
                dialogue);

            string resolvedText = _activeEventData.ResolveDialogueText(
                _dialogueIndex,
                GameDataManager.Instance);
            PlayDialogueText(
                dialogueText,
                dialogueRoot,
                resolvedText,
                dialogue.characterID);
        }

        bool TryGetDialogueView(
            string characterId,
            out TMP_Text dialogueText,
            out GameObject dialogueRoot)
        {
            switch (characterId)
            {
                case PlayerDialogueCharacterId:
                    ResolvePlayerDialogueView();
                    dialogueText = playerDialogueText;
                    dialogueRoot = playerDialogueRoot;
                    break;

                case ElderDialogueCharacterId:
                    dialogueText = _npcDialogueText;
                    dialogueRoot = _npcDialogueRoot;
                    break;

                default:
                    dialogueText = null;
                    dialogueRoot = null;
                    Debug.LogWarning(
                        $"[PlayerController2D] Event '{_activeDialogueEvent.EventId}' dialogue " +
                        $"{_dialogueIndex} has unsupported characterID '{characterId}'.",
                        _activeDialogueEvent);
                    return false;
            }

            if (dialogueText != null)
                return true;

            Debug.LogWarning(
                $"[PlayerController2D] Event '{_activeDialogueEvent.EventId}' dialogue " +
                $"{_dialogueIndex} cannot find the characterID '{characterId}' TMP view.",
                _activeDialogueEvent);
            return false;
        }

        void PlayDialogueText(
            TMP_Text dialogueText,
            GameObject dialogueRoot,
            string dialogueTextValue,
            string characterId)
        {
            HideDialogueETips();
            KillDialogueTween();
            HideDialogueViews();

            _activeDialogueText = dialogueText;
            _activeDialoguePresentation = GetDialoguePresentation(dialogueText);
            _currentDialogueText = dialogueTextValue ?? string.Empty;
            SetDialogueViewActive(dialogueRoot, dialogueText, true);
            ShowDialoguePanel(_activeDialoguePresentation);

            // Keep the complete line in TMP while revealing glyphs. Replacing
            // the string per letter makes ContentSizeFitter lag behind the
            // 3D text mesh, briefly wrapping a single line (and changing its
            // auto-size/baseline) depending on the player's render order.
            dialogueText.maxVisibleCharacters = int.MaxValue;
            dialogueText.useMaxVisibleDescender = false;
            dialogueText.text = _currentDialogueText;
            dialogueText.ForceMeshUpdate();
            LayoutRebuilder.ForceRebuildLayoutImmediate(
                dialogueText.rectTransform);
            dialogueText.ForceMeshUpdate();
            ResizeDialoguePanel(_activeDialoguePresentation);
            int visibleCharacterCount = dialogueText.textInfo.characterCount;
            dialogueText.maxVisibleCharacters = 0;

            if (visibleCharacterCount == 0)
            {
                _isTypingDialogue = false;
                ShowActiveDialogueETip();
                return;
            }

            float duration = visibleCharacterCount /
                Mathf.Max(1f, dialogueCharactersPerSecond);
            Tween tween = DOTween.To(
                    () => dialogueText.maxVisibleCharacters,
                    value => dialogueText.maxVisibleCharacters = value,
                    visibleCharacterCount,
                    duration)
                .SetEase(Ease.Linear)
                .SetLink(dialogueText.gameObject, LinkBehaviour.KillOnDisable);

            _dialogueTween = tween;
            _isTypingDialogue = true;
            StartDialogueVoice(characterId);
            tween
                .OnComplete(() => OnDialogueTweenEnded(tween))
                .OnKill(() => OnDialogueTweenEnded(tween));
        }

        void CompleteCurrentDialogueText()
        {
            TMP_Text dialogueText = _activeDialogueText;
            string fullText = _currentDialogueText;

            KillDialogueTween();
            if (dialogueText != null)
                dialogueText.text = fullText;

            _isTypingDialogue = false;
            ShowActiveDialogueETip();
        }

        void OnDialogueTweenEnded(Tween tween)
        {
            if (_dialogueTween != tween)
                return;

            _dialogueTween = null;
            if (_activeDialogueText != null)
                _activeDialogueText.maxVisibleCharacters = int.MaxValue;
            _isTypingDialogue = false;
            StopDialogueVoice();
            ShowActiveDialogueETip();
        }

        void StartDialogueVoice(string characterId)
        {
            StopDialogueVoice();

            AudioData[] voiceGroup = GetDialogueVoiceGroup(characterId);
            if (voiceGroup == null || voiceGroup.Length == 0)
                return;

            _activeDialogueVoiceSfx = voiceGroup;
            _lastDialogueVoiceIndex = -1;
            PlayNextDialogueVoice();
        }

        void UpdateDialogueVoicePlayback()
        {
            if (!_isTypingDialogue || _activeDialogueVoiceSfx == null)
                return;

            if (_dialogueVoiceSource != null
                && _dialogueVoiceSource.isPlaying
                && _dialogueVoiceSource.clip == _dialogueVoiceClip)
            {
                return;
            }

            _dialogueVoiceSource = null;
            _dialogueVoiceClip = null;
            PlayNextDialogueVoice();
        }

        void PlayNextDialogueVoice()
        {
            if (_activeDialogueVoiceSfx == null
                || _activeDialogueVoiceSfx.Length == 0)
            {
                return;
            }

            AudioManager audioManager = AudioManager.Instance;
            if (audioManager == null)
            {
                _activeDialogueVoiceSfx = null;
                return;
            }

            int voiceIndex = GetNextDialogueVoiceIndex(
                _activeDialogueVoiceSfx.Length);
            AudioData voiceSfx = _activeDialogueVoiceSfx[voiceIndex];
            AudioSource source = audioManager.PlaySfx(voiceSfx);
            if (source == null)
            {
                _activeDialogueVoiceSfx = null;
                return;
            }

            _dialogueVoiceSource = source;
            _dialogueVoiceClip = voiceSfx.audioclip;
        }

        int GetNextDialogueVoiceIndex(int voiceCount)
        {
            if (voiceCount <= 1)
            {
                _lastDialogueVoiceIndex = 0;
                return 0;
            }

            if (_lastDialogueVoiceIndex < 0
                || _lastDialogueVoiceIndex >= voiceCount)
            {
                _lastDialogueVoiceIndex = UnityEngine.Random.Range(
                    0,
                    voiceCount);
                return _lastDialogueVoiceIndex;
            }

            int nextIndex = UnityEngine.Random.Range(0, voiceCount - 1);
            if (nextIndex >= _lastDialogueVoiceIndex)
                nextIndex++;

            _lastDialogueVoiceIndex = nextIndex;
            return nextIndex;
        }

        AudioData[] GetDialogueVoiceGroup(string characterId)
        {
            ResolveDialogueVoiceGroups();
            switch (characterId)
            {
                case PlayerDialogueCharacterId:
                    return _playerDialogueVoiceSfx;
                case ElderDialogueCharacterId:
                    return _elderDialogueVoiceSfx;
                default:
                    return null;
            }
        }

        void ResolveDialogueVoiceGroups()
        {
            if (_dialogueVoiceGroupsResolved)
                return;

            List<SfxAudioData> allSfx = GameDataManager.Instance
                ?.AudioData
                ?.SFX;
            if (allSfx == null)
                return;

            _playerDialogueVoiceSfx = CollectDialogueVoiceSfx(
                allSfx,
                PlayerDialogueVoiceSfxPrefix);
            _elderDialogueVoiceSfx = CollectDialogueVoiceSfx(
                allSfx,
                ElderDialogueVoiceSfxPrefix);
            _dialogueVoiceGroupsResolved = true;
        }

        static AudioData[] CollectDialogueVoiceSfx(
            List<SfxAudioData> allSfx,
            string keyPrefix)
        {
            var matches = new List<AudioData>(8);
            for (int i = 0; i < allSfx.Count; i++)
            {
                SfxAudioData sfx = allSfx[i];
                if (sfx != null
                    && sfx.audioclip != null
                    && !string.IsNullOrEmpty(sfx.keyid)
                    && sfx.keyid.StartsWith(
                        keyPrefix,
                        StringComparison.Ordinal))
                {
                    matches.Add(sfx);
                }
            }

            return matches.ToArray();
        }

        void StopDialogueVoice()
        {
            AudioSource source = _dialogueVoiceSource;
            AudioClip clip = _dialogueVoiceClip;
            _activeDialogueVoiceSfx = null;
            _dialogueVoiceSource = null;
            _dialogueVoiceClip = null;
            _lastDialogueVoiceIndex = -1;

            if (source == null || source.clip != clip)
                return;

            source.Stop();
            source.clip = null;
        }

        void CompleteEventDialogue()
        {
            SceneEventBase completedEvent = _activeDialogueEvent;
            bool shouldRestoreMovement = TakeEventDialogueMovementLock();
            ResetDialogueState();

            try
            {
                if (completedEvent != null)
                    completedEvent.CompleteEvent();
            }
            finally
            {
                if (shouldRestoreMovement)
                    SetManagedCanMove(true);
            }
        }

        void CancelEventDialogue()
        {
            bool shouldRestoreMovement = TakeEventDialogueMovementLock();

            if (!IsInEventDialogue && _dialogueTween == null)
            {
                ResetDialoguePresentations();
                HideDialogueViews();
                if (shouldRestoreMovement)
                    SetManagedCanMove(true);
                return;
            }

            ResetDialogueState();
            if (shouldRestoreMovement)
                SetManagedCanMove(true);
        }

        void LockMovementForEventDialogue()
        {
            _isMovementLockedByEventDialogue = true;
            SetManagedCanMove(false);
        }

        bool TakeEventDialogueMovementLock()
        {
            if (!_isMovementLockedByEventDialogue)
                return false;

            _isMovementLockedByEventDialogue = false;
            return true;
        }

        void SetManagedCanMove(bool value)
        {
            if (PlayerCtrlManager.Instance != null)
                PlayerCtrlManager.Instance.SetCanMove(value);
            else
                SetCanMove(value);
        }

        void ResetDialogueState()
        {
            KillDialogueTween();
            ResetDialoguePresentations();
            HideDialogueViews();

            _activeDialogueEvent = null;
            _activeEventData = null;
            _npcDialogueText = null;
            _npcDialogueRoot = null;
            _activeDialogueText = null;
            _activeDialoguePresentation = null;
            _currentDialogueText = string.Empty;
            _dialogueIndex = 0;
            _isTypingDialogue = false;
        }

        void PrepareDialoguePresentations()
        {
            ResolvePlayerDialoguePresentation();
            ResolveNpcDialoguePresentation();
            ResetDialoguePresentation(_playerDialoguePresentation);
            ResetDialoguePresentation(_npcDialoguePresentation);
        }

        void ShowDialoguePanel(DialoguePresentationState presentation)
        {
            if (presentation == null || presentation.PanelRenderer == null)
                return;

            if (presentation.PanelHasAppeared)
            {
                KillDialoguePanelFade(presentation);
                KillDialogueNameFade(presentation);
                SetSpriteAlpha(presentation.PanelRenderer, 1f);
                SetTextAlpha(presentation.NameText, 1f);
                return;
            }

            presentation.PanelHasAppeared = true;
            KillDialoguePanelFade(presentation);
            KillDialogueNameFade(presentation);
            SetSpriteAlpha(presentation.PanelRenderer, 0f);
            SetTextAlpha(presentation.NameText, 0f);

            Tween fade = presentation.PanelRenderer
                .DOFade(1f, dialoguePanelFadeDuration)
                .SetEase(Ease.Linear)
                .SetLink(presentation.PanelRenderer.gameObject, LinkBehaviour.KillOnDisable);
            presentation.PanelFadeTween = fade;
            fade.OnKill(() =>
            {
                if (presentation.PanelFadeTween == fade)
                    presentation.PanelFadeTween = null;
            });

            if (presentation.NameText == null)
                return;

            Tween nameFade = presentation.NameText
                .DOFade(1f, dialoguePanelFadeDuration)
                .SetEase(Ease.Linear)
                .SetLink(
                    presentation.NameText.gameObject,
                    LinkBehaviour.KillOnDisable);
            presentation.NameFadeTween = nameFade;
            nameFade.OnKill(() =>
            {
                if (presentation.NameFadeTween == nameFade)
                    presentation.NameFadeTween = null;
            });
        }

        void ShowActiveDialogueETip()
        {
            DialoguePresentationState presentation = _activeDialoguePresentation;
            if (presentation == null || presentation.ETipRenderer == null)
                return;

            HideDialogueETip(presentation);

            Tween fade = presentation.ETipRenderer
                .DOFade(1f, dialogueETipFadeDuration)
                .SetEase(Ease.Linear)
                .SetLink(presentation.ETipRenderer.gameObject, LinkBehaviour.KillOnDisable);
            presentation.ETipFadeTween = fade;
            fade.OnKill(() =>
            {
                if (presentation.ETipFadeTween == fade)
                    presentation.ETipFadeTween = null;
            });

            if (presentation.ETipRectTransform == null)
                return;

            Tween movement = presentation.ETipRectTransform
                .DOAnchorPosY(dialogueETipEndY, dialogueETipFloatDuration)
                .SetEase(Ease.InOutSine)
                .SetLoops(-1, LoopType.Yoyo)
                .SetLink(presentation.ETipRenderer.gameObject, LinkBehaviour.KillOnDisable);
            presentation.ETipFloatTween = movement;
            movement.OnKill(() =>
            {
                if (presentation.ETipFloatTween == movement)
                    presentation.ETipFloatTween = null;
            });
        }

        void HideDialogueETips()
        {
            HideDialogueETip(_playerDialoguePresentation);
            HideDialogueETip(_npcDialoguePresentation);
        }

        void HideDialogueETip(DialoguePresentationState presentation)
        {
            if (presentation == null)
                return;

            KillDialogueETipTweens(presentation);
            SetSpriteAlpha(presentation.ETipRenderer, 0f);

            if (presentation.ETipRectTransform == null)
                return;

            Vector2 anchoredPosition = presentation.ETipRectTransform.anchoredPosition;
            anchoredPosition.y = dialogueETipStartY;
            presentation.ETipRectTransform.anchoredPosition = anchoredPosition;
        }

        void ResetDialoguePresentations()
        {
            ResolvePlayerDialoguePresentation();
            ResolveNpcDialoguePresentation();
            ResetDialoguePresentation(_playerDialoguePresentation);
            ResetDialoguePresentation(_npcDialoguePresentation);
        }

        void ResetDialoguePresentation(DialoguePresentationState presentation)
        {
            KillDialoguePanelFade(presentation);
            KillDialogueNameFade(presentation);
            presentation.PanelHasAppeared = false;
            SetSpriteAlpha(presentation.PanelRenderer, 0f);
            SetTextAlpha(presentation.NameText, 0f);
            HideDialogueETip(presentation);
            ApplyDialogueWrappedOffsets(presentation, false);
        }

        void ResolvePlayerDialoguePresentation()
        {
            ResolvePlayerDialogueView();

            if (dialoguePanelRenderer == null && playerDialogueRoot != null)
                dialoguePanelRenderer = playerDialogueRoot.GetComponent<SpriteRenderer>();

            if (dialogueETipRenderer == null && playerDialogueText != null)
            {
                Transform eTipTransform = playerDialogueText.transform.Find("Etip");
                if (eTipTransform != null)
                    dialogueETipRenderer = eTipTransform.GetComponent<SpriteRenderer>();
            }

            _playerDialoguePresentation.DialogueText = playerDialogueText;
            _playerDialoguePresentation.Root = playerDialogueRoot;
            _playerDialoguePresentation.PanelRenderer = dialoguePanelRenderer;
            _playerDialoguePresentation.ETipRenderer = dialogueETipRenderer;
            _playerDialoguePresentation.ETipRectTransform = dialogueETipRenderer != null
                ? dialogueETipRenderer.transform as RectTransform
                : null;
            _playerDialoguePresentation.DialogueTextSizeFitter =
                playerDialogueText != null
                    ? playerDialogueText.GetComponent<ContentSizeFitter>()
                    : null;
            _playerDialoguePresentation.NameText = ResolveDialogueName(
                playerDialogueRoot,
                "DialogueName");
            if (_playerDialoguePresentation.NameText != null)
            {
                _playerDialoguePresentation.NameText.text =
                    PlayerDialogueName;
            }

            CacheDialogueLayoutAnchors(
                _playerDialoguePresentation,
                true);
        }

        void ResolveNpcDialoguePresentation()
        {
            _npcDialoguePresentation.DialogueText = _npcDialogueText;
            _npcDialoguePresentation.Root = _npcDialogueRoot;
            _npcDialoguePresentation.PanelRenderer = _npcDialogueRoot != null
                ? _npcDialogueRoot.GetComponent<SpriteRenderer>()
                : null;

            Transform eTipTransform = _npcDialogueText != null
                ? _npcDialogueText.transform.Find("Etip")
                : null;
            _npcDialoguePresentation.ETipRenderer = eTipTransform != null
                ? eTipTransform.GetComponent<SpriteRenderer>()
                : null;
            _npcDialoguePresentation.ETipRectTransform =
                _npcDialoguePresentation.ETipRenderer != null
                    ? _npcDialoguePresentation.ETipRenderer.transform as RectTransform
                    : null;
            _npcDialoguePresentation.DialogueTextSizeFitter =
                _npcDialogueText != null
                    ? _npcDialogueText.GetComponent<ContentSizeFitter>()
                    : null;
            _npcDialoguePresentation.NameText = ResolveDialogueName(
                _npcDialogueRoot,
                "NPCName");
            CacheDialogueLayoutAnchors(
                _npcDialoguePresentation,
                true);
        }

        DialoguePresentationState GetDialoguePresentation(TMP_Text dialogueText)
        {
            if (dialogueText == playerDialogueText)
            {
                ResolvePlayerDialoguePresentation();
                return _playerDialoguePresentation;
            }

            if (dialogueText == _npcDialogueText)
            {
                ResolveNpcDialoguePresentation();
                return _npcDialoguePresentation;
            }

            return null;
        }

        static void KillDialoguePanelFade(DialoguePresentationState presentation)
        {
            if (presentation == null || presentation.PanelFadeTween == null)
                return;

            Tween fade = presentation.PanelFadeTween;
            presentation.PanelFadeTween = null;
            fade.Kill(false);
        }

        static void KillDialogueNameFade(DialoguePresentationState presentation)
        {
            if (presentation == null || presentation.NameFadeTween == null)
                return;

            Tween fade = presentation.NameFadeTween;
            presentation.NameFadeTween = null;
            fade.Kill(false);
        }

        static void KillDialogueETipTweens(DialoguePresentationState presentation)
        {
            if (presentation.ETipFadeTween != null)
            {
                Tween fade = presentation.ETipFadeTween;
                presentation.ETipFadeTween = null;
                fade.Kill(false);
            }

            if (presentation.ETipFloatTween != null)
            {
                Tween movement = presentation.ETipFloatTween;
                presentation.ETipFloatTween = null;
                movement.Kill(false);
            }
        }

        static void SetSpriteAlpha(SpriteRenderer renderer, float alpha)
        {
            if (renderer == null)
                return;

            Color color = renderer.color;
            color.a = alpha;
            renderer.color = color;
        }

        static void SetTextAlpha(TMP_Text text, float alpha)
        {
            if (text == null)
                return;

            Color color = text.color;
            color.a = alpha;
            text.color = color;
        }

        static TMP_Text ResolveDialogueName(
            GameObject dialogueRoot,
            string preferredName)
        {
            if (dialogueRoot == null)
                return null;

            Transform nameTransform = dialogueRoot.transform.Find(preferredName);
            if (nameTransform == null)
                nameTransform = dialogueRoot.transform.Find("DialogueName");
            if (nameTransform == null)
                nameTransform = dialogueRoot.transform.Find("NPCName");

            return nameTransform != null
                ? nameTransform.GetComponent<TMP_Text>()
                : null;
        }

        static void CacheDialogueLayoutAnchors(
            DialoguePresentationState presentation,
            bool offsetNameWhenWrapped)
        {
            RectTransform dialogueTextRect = presentation.DialogueText != null
                ? presentation.DialogueText.rectTransform
                : null;
            if (presentation.DialogueTextRectTransform != dialogueTextRect)
            {
                if (presentation.DialogueTextRectTransform != null)
                {
                    presentation.DialogueTextRectTransform.anchoredPosition =
                        presentation.DialogueTextBaseAnchoredPosition;
                }

                presentation.DialogueTextRectTransform = dialogueTextRect;
                if (dialogueTextRect != null)
                {
                    presentation.DialogueTextBaseAnchoredPosition =
                        dialogueTextRect.anchoredPosition;
                }
            }

            RectTransform nameRect = presentation.NameText != null
                ? presentation.NameText.rectTransform
                : null;
            if (presentation.NameRectTransform != nameRect)
            {
                if (presentation.NameRectTransform != null)
                {
                    presentation.NameRectTransform.anchoredPosition =
                        presentation.NameBaseAnchoredPosition;
                }

                presentation.NameRectTransform = nameRect;
                if (nameRect != null)
                {
                    presentation.NameBaseAnchoredPosition =
                        nameRect.anchoredPosition;
                }
            }

            presentation.OffsetNameWhenWrapped = offsetNameWhenWrapped;
        }

        static void ApplyDialogueWrappedOffsets(
            DialoguePresentationState presentation,
            bool shouldWrap)
        {
            if (presentation == null)
                return;

            if (presentation.DialogueTextRectTransform != null)
            {
                Vector2 textPosition =
                    presentation.DialogueTextBaseAnchoredPosition;
                if (shouldWrap)
                    textPosition.y += DialogueTextWrappedYOffset;

                presentation.DialogueTextRectTransform.anchoredPosition =
                    textPosition;
            }

            if (presentation.NameRectTransform != null)
            {
                Vector2 namePosition = presentation.NameBaseAnchoredPosition;
                if (shouldWrap && presentation.OffsetNameWhenWrapped)
                    namePosition.y += DialogueNameWrappedYOffset;

                presentation.NameRectTransform.anchoredPosition = namePosition;
            }
        }

        static void ResizeDialoguePanel(
            DialoguePresentationState presentation)
        {
            if (presentation == null
                || presentation.PanelRenderer == null
                || presentation.DialogueText == null)
            {
                return;
            }

            float eTipWidth = presentation.ETipRectTransform != null
                ? presentation.ETipRectTransform.rect.width
                : 0f;
            RectTransform textRect = presentation.DialogueText.rectTransform;
            ContentSizeFitter textSizeFitter =
                presentation.DialogueTextSizeFitter;

            if (textSizeFitter != null)
            {
                textSizeFitter.horizontalFit =
                    ContentSizeFitter.FitMode.PreferredSize;
            }

            LayoutRebuilder.ForceRebuildLayoutImmediate(textRect);
            presentation.DialogueText.ForceMeshUpdate();

            bool shouldWrap = textRect.rect.width > DialogueTextMaxWidth;
            if (shouldWrap)
            {
                if (textSizeFitter != null)
                {
                    textSizeFitter.horizontalFit =
                        ContentSizeFitter.FitMode.Unconstrained;
                }

                textRect.SetSizeWithCurrentAnchors(
                    RectTransform.Axis.Horizontal,
                    DialogueTextMaxWidth);
                LayoutRebuilder.ForceRebuildLayoutImmediate(textRect);
                presentation.DialogueText.ForceMeshUpdate();
            }

            float contentWidth = shouldWrap
                ? DialoguePanelMaxWidth
                : textRect.rect.width
                    + eTipWidth
                    + DialoguePanelWidthAdjustment;

            Vector2 panelSize = presentation.PanelRenderer.size;
            panelSize.x = Mathf.Clamp(
                contentWidth,
                DialoguePanelMinWidth,
                DialoguePanelMaxWidth);
            panelSize.y = shouldWrap
                ? DialoguePanelWrappedHeight
                : DialoguePanelSingleLineHeight;
            presentation.PanelRenderer.size = panelSize;
            ApplyDialogueWrappedOffsets(presentation, shouldWrap);
        }

        void HideDialogueViews()
        {
            SetDialogueViewActive(playerDialogueRoot, playerDialogueText, false);
            SetDialogueViewActive(_npcDialogueRoot, _npcDialogueText, false);
        }

        void KillDialogueTween()
        {
            StopDialogueVoice();
            if (_activeDialogueText != null)
                _activeDialogueText.maxVisibleCharacters = int.MaxValue;
            if (_dialogueTween == null)
                return;

            Tween tween = _dialogueTween;
            _dialogueTween = null;
            tween.Kill(false);
        }

        void ResolvePlayerDialogueView()
        {
            if (playerDialogueText == null)
                playerDialogueText = GetComponentInChildren<TMP_Text>(true);

            OSLocalizedFontUtility.ApplyCurrent(playerDialogueText);

            if (playerDialogueRoot == gameObject)
            {
                Debug.LogWarning(
                    "[PlayerController2D] Player dialogue root cannot be the Player GameObject. " +
                    "Using the TMP hierarchy instead.",
                    this);
                playerDialogueRoot = null;
            }

            if (playerDialogueText == null || playerDialogueRoot != null)
                return;

            Transform textParent = playerDialogueText.transform.parent;
            playerDialogueRoot = textParent != null && textParent != transform
                ? textParent.gameObject
                : playerDialogueText.gameObject;
        }

        static void SetDialogueViewActive(
            GameObject dialogueRoot,
            TMP_Text dialogueText,
            bool isActive)
        {
            GameObject target = dialogueRoot != null
                ? dialogueRoot
                : dialogueText != null
                    ? dialogueText.gameObject
                    : null;

            if (target != null && target.activeSelf != isActive)
                target.SetActive(isActive);
        }

        public void SetCanMove(bool value)
        {
            canMove = value;
            if (canMove)
                return;

            _moveInput = 0f;
            StopHorizontalMovement();
            SetWalkingAnimation(false);
        }

        public void SetCanInteractive(bool value)
        {
            canInteractive = value;
        }

        public void TeleportTo(Vector3 position)
        {
            _moveInput = 0f;
            transform.position = position;

            if (_body == null)
                _body = GetComponent<Rigidbody2D>();

            _body.position = new Vector2(position.x, position.y);
            _body.linearVelocity = Vector2.zero;
            _body.angularVelocity = 0f;

            SetWalkingAnimation(false);
        }

        void StopHorizontalMovement()
        {
            if (_body != null)
                _body.linearVelocity = new Vector2(0f, _body.linearVelocity.y);
        }

        /// <summary>蹲下动画:序章救人、终章墓前放花。</summary>
        public void PlayCrouch()
        {
            if (animator != null) animator.SetTrigger(CrouchParam);
        }
    }
}
