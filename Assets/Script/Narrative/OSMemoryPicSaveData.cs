using System;
using System.Collections.Generic;

/// <summary>Only chapter-local logical checkpoints; animations and Unity objects are never saved.</summary>
[Serializable]
public sealed class OSMemoryPicSaveData
{
    static readonly string[] NailDoneFlags = {
        "nail_0_done", "nail_1_done", "nail_2_done", "nail_3_done", "nail_4_done", "nail_5_done"
    };
    public string scenarioId;
    public string currentSceneId;
    public List<string> flags = new List<string>();
    public List<string> inventory = new List<string>();
    public string selectedItemId;
    public bool initialized;
    public bool exited;

    public void EnsureData()
    {
        if (flags == null) flags = new List<string>();
        if (inventory == null) inventory = new List<string>();
        // Planks and nails now share one pickup. Older saves may have collected
        // only the planks; restore the missing nails before their old hotspot is hidden.
        // An exited/completed puzzle must not regain already-consumed materials.
        if (scenarioId == "chapter01_home" && initialized && !exited && Has("wood_taken"))
        {
            Set("nails_taken");
            bool allNailsDone = true;
            for (int i = 0; i < NailDoneFlags.Length; i++)
                if (!Has(NailDoneFlags[i])) allNailsDone = false;
            if (!Has("window_fixed") && !allNailsDone && !inventory.Contains("nails"))
                inventory.Add("nails");
        }
    }

    public bool Has(string flag) => !string.IsNullOrEmpty(flag) && flags != null && flags.Contains(flag);
    public void Set(string flag)
    {
        if (!string.IsNullOrEmpty(flag) && !flags.Contains(flag)) flags.Add(flag);
    }
    public void Exit()
    {
        inventory.Clear();
        selectedItemId = null;
        exited = true;
    }
}
