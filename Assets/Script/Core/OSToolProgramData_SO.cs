using System;
using System.Collections.Generic;
using UnityEngine;

public static class OSToolProgramIds
{
    public const string Gramophone = "gramophone";
    public const string EnergyLoop = "energy-loop";
    public const string MemoryRepair01 = "memory-repair-1";
    public const string MemoryRepair02 = "memory-repair-2";
    public const string MemoryRepair03 = "memory-repair-3";
    public const string MemoryRepair04 = "memory-repair-4";
    public const string TV = "tv";
}

public enum OSToolInstallResult
{
    Success,
    AlreadyInstalled,
    InsufficientBattery,
    Unavailable
}

[Serializable]
public sealed class OSToolProgramDefinition
{
    [SerializeField] string id;
    [SerializeField] string localizationKey;
    [SerializeField] string fallbackName;
    [SerializeField] Sprite icon;
    [SerializeField, Min(0)] int batteryCost;

    public string Id => id;
    public Sprite Icon => icon != null ? icon : id == OSToolProgramIds.TV ? OSTVProgramAssets.Icon : null;
    public int BatteryCost => Mathf.Max(0, batteryCost);
    public string InstallCommand => "tool install " + id;
    public string DisplayName => GameDataManager.Instance != null
        ? GameDataManager.Instance.GetLocalizedText(localizationKey, fallbackName)
        : fallbackName;

    public static OSToolProgramDefinition CreateTV() => new OSToolProgramDefinition
    {
        id = OSToolProgramIds.TV,
        localizationKey = "ui.os.tv.title",
        fallbackName = "TV",
        batteryCost = 0
    };
}

[CreateAssetMenu(menuName = "MrTGame/OSToolProgramData", fileName = "OSToolProgramData")]
public sealed class OSToolProgramData_SO : ScriptableObject
{
    [SerializeField] Sprite batteryIcon;
    [SerializeField] List<OSToolProgramDefinition> programs = new List<OSToolProgramDefinition>();
    [NonSerialized] OSToolProgramDefinition runtimeTV;
    [NonSerialized] List<OSToolProgramDefinition> runtimePrograms;

    public Sprite BatteryIcon => batteryIcon;
    public IReadOnlyList<OSToolProgramDefinition> Programs
    {
        get
        {
            if (programs.Exists(candidate => candidate != null && candidate.Id == OSToolProgramIds.TV))
                return programs;
            OSToolProgramDefinition tv = RuntimeTV;
            if (runtimePrograms == null || runtimePrograms.Count != programs.Count + 1
                || !ReferenceEquals(runtimePrograms[runtimePrograms.Count - 1], tv))
            {
                runtimePrograms = new List<OSToolProgramDefinition>(programs);
                runtimePrograms.Add(tv);
            }
            return runtimePrograms;
        }
    }
    OSToolProgramDefinition RuntimeTV
    {
        get
        {
            // Unity hot reload can restore an inline serializable cache as an
            // empty instance. Repair that stale object as well as a null cache.
            if (runtimeTV == null || runtimeTV.Id != OSToolProgramIds.TV)
                runtimeTV = OSToolProgramDefinition.CreateTV();
            return runtimeTV;
        }
    }

    public bool TryGetProgram(string id, out OSToolProgramDefinition program)
    {
        program = programs.Find(candidate => candidate != null
            && string.Equals(candidate.Id, id, StringComparison.OrdinalIgnoreCase));
        if (program == null && string.Equals(id, OSToolProgramIds.TV, StringComparison.OrdinalIgnoreCase))
            program = RuntimeTV;
        return program != null;
    }
}
