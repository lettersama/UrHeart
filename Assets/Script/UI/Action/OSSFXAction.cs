using System;
using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class OSSFXAction : MonoBehaviour
{
    [Serializable]
    sealed class PartButton
    {
        [SerializeField, ReadOnly] OSSFXPart part;

        public string InspectorLabel => part != null
            ? part.PartName
            : "Missing Part";

        public OSSFXPart Part => part;

        public PartButton()
        {
        }

        public PartButton(OSSFXPart target)
        {
            part = target;
        }

        [HorizontalGroup("Controls")]
        [Button("Enable", ButtonSizes.Small)]
        [DisableInEditorMode]
        void Enable()
        {
            part?.SetEffectEnabled(true);
        }

        [HorizontalGroup("Controls")]
        [Button("Disable", ButtonSizes.Small)]
        [DisableInEditorMode]
        void Disable()
        {
            part?.SetEffectEnabled(false);
        }
    }

    [SerializeField] OSCloseAndReOpenPart closeAndReOpenPart;

    [Title("SFX Part Buttons")]
    [SerializeField]
    [ListDrawerSettings(
        DraggableItems = false,
        HideAddButton = true,
        HideRemoveButton = true,
        ListElementLabelName = "InspectorLabel")]
    List<PartButton> partButtons = new List<PartButton>();

    void Awake()
    {
        ResolveReferences();
        SynchronizePartButtons();
    }

    void Reset()
    {
        ResolveReferences();
        SynchronizePartButtons();
    }

    void OnValidate()
    {
        ResolveReferences();
        SynchronizePartButtons();
    }

    public void SetCloseAndReOpenPartEnabled(bool enabled)
    {
        ResolveReferences();
        closeAndReOpenPart?.SetEffectEnabled(enabled);
    }

    public void PlayCloseAndReOpenPart()
    {
        SetCloseAndReOpenPartEnabled(true);
    }

    public bool SetPartEnabled(string partName, bool enabled)
    {
        if (string.IsNullOrWhiteSpace(partName))
            return false;

        OSSFXPart[] parts = GetComponentsInChildren<OSSFXPart>(true);
        for (int i = 0; i < parts.Length; i++)
        {
            OSSFXPart part = parts[i];
            if (part == null
                || !string.Equals(
                    part.PartName,
                    partName,
                    StringComparison.Ordinal))
            {
                continue;
            }

            part.SetEffectEnabled(enabled);
            return true;
        }

        return false;
    }

    [Button("Disable All Parts", ButtonSizes.Medium)]
    [DisableInEditorMode]
    public void DisableAllParts()
    {
        OSSFXPart[] parts = GetComponentsInChildren<OSSFXPart>(true);
        for (int i = 0; i < parts.Length; i++)
            parts[i]?.SetEffectEnabled(false);
    }

    void ResolveReferences()
    {
        if (closeAndReOpenPart == null)
        {
            closeAndReOpenPart =
                GetComponentInChildren<OSCloseAndReOpenPart>(true);
        }
    }

    void SynchronizePartButtons()
    {
        OSSFXPart[] parts = GetComponentsInChildren<OSSFXPart>(true);
        bool alreadySynchronized = partButtons.Count == parts.Length;
        if (alreadySynchronized)
        {
            for (int i = 0; i < parts.Length; i++)
            {
                if (partButtons[i] == null
                    || partButtons[i].Part != parts[i])
                {
                    alreadySynchronized = false;
                    break;
                }
            }
        }

        if (alreadySynchronized)
            return;

        partButtons.Clear();
        for (int i = 0; i < parts.Length; i++)
            partButtons.Add(new PartButton(parts[i]));
    }
}
