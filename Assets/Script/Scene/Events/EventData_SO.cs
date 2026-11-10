using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

public static class EventDialogueLocalizationKeys
{
    const string Prefix = "scene.";

    public static string GetLineKey(string eventID, int dialogueIndex)
    {
        if (string.IsNullOrWhiteSpace(eventID) || dialogueIndex < 0)
            return string.Empty;

        return string.Concat(
            Prefix,
            eventID.Trim().ToLowerInvariant(),
            ".line.",
            dialogueIndex.ToString("D2", CultureInfo.InvariantCulture));
    }
}

public interface IEventDialogueTextResolver
{
    string Resolve(
        string eventID,
        int dialogueIndex,
        string characterID,
        string fallbackText);
}

[Serializable]
public class EventDialogueData
{
    public string characterID;
    [TextArea(2, 6)] public string dialogueText;
}

[Serializable]
public class EventData
{
    public string eventID;
    public List<EventDialogueData> eventDialogueList = new List<EventDialogueData>();

    public int DialogueCount => eventDialogueList != null ? eventDialogueList.Count : 0;

    public bool TryGetDialogue(int dialogueIndex, out EventDialogueData dialogue)
    {
        dialogue = null;
        if (eventDialogueList == null ||
            dialogueIndex < 0 ||
            dialogueIndex >= eventDialogueList.Count)
            return false;

        dialogue = eventDialogueList[dialogueIndex];
        return dialogue != null;
    }

    public string ResolveDialogueText(
        int dialogueIndex,
        IEventDialogueTextResolver textResolver = null)
    {
        if (!TryGetDialogue(dialogueIndex, out EventDialogueData dialogue))
            return string.Empty;

        string fallbackText = dialogue.dialogueText ?? string.Empty;
        if (textResolver == null)
            return fallbackText;

        string resolvedText = textResolver.Resolve(
            eventID,
            dialogueIndex,
            dialogue.characterID,
            fallbackText);
        return string.IsNullOrEmpty(resolvedText) ? fallbackText : resolvedText;
    }
}

[CreateAssetMenu(menuName = "MrTGame/EventData", fileName = "EventData")]
public class EventData_SO : ScriptableObject
{
    public List<EventData> eventDataList = new List<EventData>();

    public EventData Find(string eventID)
    {
        if (string.IsNullOrEmpty(eventID) || eventDataList == null)
            return null;

        return eventDataList.Find(data =>
            data != null && string.Equals(data.eventID, eventID, StringComparison.Ordinal));
    }

    public bool TryGetEvent(string eventID, out EventData eventData)
    {
        eventData = Find(eventID);
        return eventData != null;
    }
}
