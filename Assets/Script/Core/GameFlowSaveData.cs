using System;
using System.Collections.Generic;
using MrTGame.Core;
using UnityEngine;

/// <summary>Scene and narrative resume state. Boot is a menu, never a playable checkpoint.</summary>
[Serializable]
public sealed class GameFlowSaveData
{
    public GamePhase phase = GamePhase.Prologue;
    public bool hasSideScrollReturnPhase;
    public GamePhase sideScrollReturnPhase = GamePhase.Prologue;
    public LanguageState language = LanguageState.zh_CN;
    public List<string> narrativeFlags = new List<string>();
    public int bondLevel;
    public RevealStage revealStage;
    public bool hasPlayerPosition;
    public Vector3 playerPosition;
    public int playerFacing = 1;
    public string dialogueEventId;
    public int dialogueLineIndex;
    public bool ayaFlewAway;

    public void EnsureData()
    {
        // These save enums are contiguous. Validate without boxing on every
        // ordinary state read; keep Boot excluded from playable checkpoints.
        if (phase < GamePhase.Prologue || phase > GamePhase.Ending)
            phase = GamePhase.Prologue;
        if (sideScrollReturnPhase != GamePhase.Prologue
            && sideScrollReturnPhase != GamePhase.Interlude
            && sideScrollReturnPhase != GamePhase.Ending)
        {
            sideScrollReturnPhase = GamePhase.Prologue;
            hasSideScrollReturnPhase = false;
        }
        if (language < LanguageState.zh_CN || language > LanguageState.ru) language = LanguageState.zh_CN;
        if (revealStage < RevealStage.Concealed || revealStage > RevealStage.HeartKnown) revealStage = RevealStage.Concealed;
        if (narrativeFlags == null) narrativeFlags = new List<string>();
        narrativeFlags.RemoveAll(flag => string.IsNullOrWhiteSpace(flag));
        bondLevel = Mathf.Clamp(bondLevel, 0, 3);
        playerFacing = playerFacing < 0 ? -1 : 1;
        dialogueLineIndex = Mathf.Max(0, dialogueLineIndex);
        if (float.IsNaN(playerPosition.x) || float.IsInfinity(playerPosition.x)
            || float.IsNaN(playerPosition.y) || float.IsInfinity(playerPosition.y)
            || float.IsNaN(playerPosition.z) || float.IsInfinity(playerPosition.z))
        {
            hasPlayerPosition = false;
            playerPosition = Vector3.zero;
        }
    }
}
