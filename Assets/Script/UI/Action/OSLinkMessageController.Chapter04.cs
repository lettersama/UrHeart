using UnityEngine;

public sealed partial class OSLinkMessageController
{
    // Chapter 4's first line is authored with that chapter. Open the existing
    // dialogue frame without manufacturing an entry, a hint, or a Memory node.
    public bool OpenChapter04DialogueShell(UnityEngine.Object owner)
    {
        ResolveReferences();
        if (ownerCharacterWindow == null || !ownerCharacterWindow.IsOpen || ownerCharacterWindow.IsOpening
            || ownerCharacterWindow.IsMinimized || linkWindow == null || textPart == null || textLabel == null) return false;
        windowLayout?.SetTemporaryLeftDialogue(owner, true);
        if (IsPresenting || hasDisplayedMessage) return true;
        ClearOptionBindings();
        SetOnlyPartActive(textPart);
        textLabel.text = string.Empty;
        textLabel.maxVisibleCharacters = int.MaxValue;
        windowLayout?.SetMeasurementText(textLabel, string.Empty);
        if (!linkWindow.IsOpen) linkWindow.OpenStandalone();
        windowLayout?.RefreshLayout();
        return true;
    }
}
