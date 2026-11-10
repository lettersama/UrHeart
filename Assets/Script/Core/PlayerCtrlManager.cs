using UnityEngine;
using MrTGame.SideScroll;

public class PlayerCtrlManager : Singleton<PlayerCtrlManager>
{
    [Header("References")]
    [SerializeField] PlayerController2D playerController;
    [SerializeField] GameObject characterRoot;

    [Header("Settings")]
    [SerializeField] bool escapeClosesFrontWindow = true;

    [Header("Runtime Control")]
    [SerializeField] bool canMove = true;
    [SerializeField] bool canInteractive = true;

    public PlayerController2D PlayerController => playerController;
    public bool EscapeClosesFrontWindow => escapeClosesFrontWindow;
    public bool CanMove => canMove;
    public bool CanInteractive => canInteractive;

    protected override void Awake()
    {
        base.Awake();
        if (!IsSingletonInstance)
            return;

        ResolveReferences();
        ApplyControlState();
    }

    public void SetPlayerActive(bool active)
    {
        ResolveReferences();
        if (characterRoot != null)
            characterRoot.SetActive(active);

        if (playerController != null)
            playerController.gameObject.SetActive(active);
    }

    public void SetPlayerPosition(Vector3 position)
    {
        if (playerController != null)
            playerController.TeleportTo(position);
    }

    public void SetCanMove(bool value)
    {
        canMove = value;
        ApplyCanMove();
    }

    public void SetCanInteractive(bool value)
    {
        canInteractive = value;
        if (playerController != null)
            playerController.SetCanInteractive(canInteractive);
    }

    public void SetControlState(bool canMove, bool canInteractive)
    {
        SetCanMove(canMove);
        SetCanInteractive(canInteractive);
    }

    void ApplyCanMove()
    {
        if (playerController != null)
            playerController.SetCanMove(canMove);
    }

    void ApplyControlState()
    {
        ApplyCanMove();
        SetCanInteractive(canInteractive);
    }

    void ResolveReferences()
    {
        if (characterRoot == null && playerController != null)
        {
            characterRoot = playerController.transform
                .Find("Character")
                ?.gameObject;
        }
    }
}
