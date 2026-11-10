using System.Collections.Generic;
using MrTGame.SideScroll;
using TMPro;
using UnityEngine;

[DisallowMultipleComponent]
public abstract class SceneEventBase : MonoBehaviour
{
    [Header("Event")]
    [SerializeField] string eventId;

    [Header("NPC Dialogue (Optional)")]
    [SerializeField] TMP_Text npcDialogueText;
    [SerializeField] GameObject npcDialogueRoot;

    readonly HashSet<Collider2D> _playerColliders = new HashSet<Collider2D>();
    PlayerController2D _currentPlayer;
    bool _reopenAfterCompletion;

    public string EventId => eventId;
    public bool IsCompleted { get; private set; }
    public virtual bool CanCompleteWithoutDialogue => false;

protected void OnTriggerEnter2D(Collider2D other)
    {
        PlayerController2D player = other.GetComponentInParent<PlayerController2D>();
        if (player == null || !_playerColliders.Add(other))
            return;

        if (_playerColliders.Count != 1)
            return;

        _currentPlayer = player;
        player.SetCurrentEvent(this);
        SetHeartbeatEventRegionActive(true);
        OnPlayerEntered(player);
    }

protected void OnTriggerExit2D(Collider2D other)
    {
        if (!_playerColliders.Remove(other) || _playerColliders.Count > 0)
            return;

        PlayerController2D player = _currentPlayer;
        _currentPlayer = null;

        if (player != null)
        {
            player.ClearCurrentEvent(this);
            player.CancelEventDialogue(this);
        }

        SetHeartbeatEventRegionActive(false);
        OnPlayerExited(player);
    }

protected virtual void OnDisable()
    {
        PlayerController2D player = _currentPlayer;
        _playerColliders.Clear();
        _currentPlayer = null;

        if (player != null)
        {
            player.ClearCurrentEvent(this);
            player.CancelEventDialogue(this);
        }

        SetHeartbeatEventRegionActive(false);
    }

void SetHeartbeatEventRegionActive(bool isActive)
    {
        if (isActive
            && (MainGameHandle.Instance == null
                || !MainGameHandle.Instance.IsInMainScene))
        {
            return;
        }

        AdditionHandle.Instance?.SetHeartbeatEventRegionActive(this, isActive);
    }


    public bool TryGetNpcDialogueView(
        out TMP_Text dialogueText,
        out GameObject dialogueRoot)
    {
        if (npcDialogueText == null)
            npcDialogueText = GetComponentInChildren<TMP_Text>(true);

        OSLocalizedFontUtility.ApplyCurrent(npcDialogueText);

        if (npcDialogueText == null)
        {
            dialogueText = null;
            dialogueRoot = null;
            return false;
        }

        if (npcDialogueRoot == gameObject)
        {
            Debug.LogWarning(
                $"[{GetType().Name}] NPC dialogue root cannot be the Event GameObject. " +
                "Using the TMP hierarchy instead.",
                this);
            npcDialogueRoot = null;
        }

        if (npcDialogueRoot == null)
        {
            Transform textParent = npcDialogueText.transform.parent;
            npcDialogueRoot = textParent != null && textParent != transform
                ? textParent.gameObject
                : npcDialogueText.gameObject;
        }

        dialogueText = npcDialogueText;
        dialogueRoot = npcDialogueRoot;
        return true;
    }

    protected abstract void OnPlayerEntered(PlayerController2D player);

    protected abstract void OnPlayerExited(PlayerController2D player);

    public virtual void OnDialogueLineEntered(
        int dialogueIndex,
        EventDialogueData dialogue)
    {
    }

    public void CompleteEvent()
    {
        _reopenAfterCompletion = false;
        EventAction();

        bool remainsAvailable = _reopenAfterCompletion;

        IsCompleted = !remainsAvailable;

        if (GameDataManager.Instance != null)
        {
            GameDataManager.Instance.SetEventState(
                eventId,
                remainsAvailable,
                IsCompleted);
        }
        else
            Debug.LogWarning(
                $"[{GetType().Name}] GameDataManager is missing; event completion was not saved.",
                this);

        if (!remainsAvailable)
            gameObject.SetActive(false);
    }

    public void ApplySavedState(bool isActive, bool isCompleted)
    {
        IsCompleted = isCompleted;
        gameObject.SetActive(isActive);
    }

    protected void ReopenEvent()
    {
        _reopenAfterCompletion = true;
    }

    protected abstract void EventAction();
}
