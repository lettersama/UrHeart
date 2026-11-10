using UnityEngine;

[DisallowMultipleComponent]
public class AyaFlyAwayTrigger : MonoBehaviour
{
    const string FlyAwaySfxKey = "fly-away-wing";

    [Header("Trigger")]
    [SerializeField] float triggerX = 28f;

    [Header("AYA")]
    [SerializeField] Aya aya00;
    [SerializeField] Aya aya01;
    [SerializeField] Aya aya02;
    [SerializeField] Aya aya03;

    bool _consumed;
    public bool HasTriggered => _consumed;

    public void RestoreState(bool consumed)
    {
        _consumed = consumed;
        if (!consumed) return;
        // The flight itself is decorative; a loaded checkpoint keeps the birds
        // gone rather than repeating its sound and takeoff animation.
        if (aya00 != null) aya00.gameObject.SetActive(false);
        if (aya01 != null) aya01.gameObject.SetActive(false);
        if (aya02 != null) aya02.gameObject.SetActive(false);
        if (aya03 != null) aya03.gameObject.SetActive(false);
    }

    void Update()
    {
        if (_consumed)
            return;

        var playerController = PlayerCtrlManager.Instance?.PlayerController;
        if (playerController == null || !playerController.gameObject.activeInHierarchy)
            return;

        if (playerController.transform.position.x >= triggerX)
            TriggerFlyAway();
    }

    public void TriggerFlyAway()
    {
        if (_consumed)
            return;

        _consumed = true;

        if (HasActiveAya())
            PlayFlyAwaySfx();

        FlyIfActive(aya00);
        FlyIfActive(aya01);
        FlyIfActive(aya02);
        FlyIfActive(aya03);
    }

    public void ResetState()
    {
        _consumed = false;
    }

    bool HasActiveAya()
    {
        return IsActive(aya00)
            || IsActive(aya01)
            || IsActive(aya02)
            || IsActive(aya03);
    }

    static bool IsActive(Aya aya)
    {
        return aya != null && aya.gameObject.activeInHierarchy;
    }

    static void PlayFlyAwaySfx()
    {
        AudioManager.Instance?.PlaySfx(FlyAwaySfxKey);
    }

    static void FlyIfActive(Aya aya)
    {
        if (IsActive(aya))
            aya.FlyAway();
    }
}
