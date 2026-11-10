using MrTGame.Core;
using UnityEngine;

public sealed class Event_01 : WorldTipEventBase
{
    protected override void EventAction()
    {
        GameDataManager dataManager = GameDataManager.Instance;
        if (dataManager != null)
            dataManager.SetCurrentHeartHealProcessIndex(1);
        else
            Debug.LogWarning("[Event_01] GameDataManager is missing.", this);

        OS_Handle osHandle = OS_Handle.Instance;
        if (osHandle != null)
        {
            osHandle.ActivateCharacterInteractionSequence(
                OSCharacterInteractionIds.PrologueEvent01HeartHeal);
            osHandle.SetCharacterWindowState(
                OSCharacterWindowState.Anima,
                false);
        }
        else
        {
            Debug.LogWarning(
                "[Event_01] OS_Handle is missing; character window state was not set.",
                this);
        }

        GameManager gameManager = GameManager.Instance;
        if (gameManager != null)
        {
            MainGameHandle mainGame = MainGameHandle.Instance;
            if (mainGame != null)
                mainGame.SuspendSceneAmbientForOS();
            else
                Debug.LogWarning(
                    "[Event_01] MainGameHandle is missing; scene ambient was not suspended.",
                    this);

            gameManager.SwitchPhase(GamePhase.OSMain);
        }
        else
            Debug.LogWarning("[Event_01] GameManager is missing.", this);
    }
}
