using System;
using System.Collections.Generic;
using UnityEngine;

namespace MrTGame.Narrative
{
    [CreateAssetMenu(menuName = "MrTGame/Memory/Picture Scenario")]
    public sealed class OSMemoryPicScenarioData : ScriptableObject
    {
        public string scenarioId = "chapter01_home";
        public int entryMemoryIndex = 2680;
        public string firstSceneId = "entrance";
        public int firstMemoryIndex = 2700;
        public List<OSMemoryPicSceneData> scenes = new List<OSMemoryPicSceneData>();
        public List<OSMemoryPicItemData> items = new List<OSMemoryPicItemData>();
        public List<int> memoryIndices = new List<int>();
        public List<string> initialItems = new List<string> { "robot" };
        public List<OSMemoryPicSound> sounds = new List<OSMemoryPicSound>();
        public int wrongSocketMemoryIndex;
        public int selectWireMemoryIndex;
        public int connectedWireMemoryIndex;
        public int alreadyConnectedMemoryIndex;
        public int powerRequiredMemoryIndex;
        public int cableRequiredMemoryIndex;
        public int knockMemoryIndex;
        public int glassMemoryIndex;

        public OSMemoryPicSceneData FindScene(string id) => scenes.Find(x => x != null && x.sceneId == id);
        public OSMemoryPicItemData FindItem(string id) => items.Find(x => x.itemId == id);
        public AudioClip FindSound(string id) => sounds.Find(x => x.cueId == id)?.clip;
        public bool OwnsMemory(int index) => index == entryMemoryIndex || memoryIndices.Contains(index);
    }

    [Serializable]
    public sealed class OSMemoryPicItemData
    {
        public string itemId;
        public string labelKey;
        public int memoryIndex;
        public Sprite icon;
    }

    [Serializable]
    public sealed class OSMemoryPicSound
    {
        public string cueId;
        public AudioClip clip;
    }

    [Serializable]
    public sealed class OSMemoryPicCondition
    {
        public List<string> allFlags = new List<string>();
        public List<string> noFlags = new List<string>();
        public List<string> items = new List<string>();

        public bool Matches(OSMemoryPicSaveData state)
        {
            if (state == null) return false;
            foreach (string flag in allFlags) if (!state.Has(flag)) return false;
            foreach (string flag in noFlags) if (state.Has(flag)) return false;
            foreach (string item in items) if (!state.inventory.Contains(item)) return false;
            return true;
        }
    }

    [Serializable]
    public sealed class OSMemoryPicOutcome
    {
        public OSMemoryPicCondition when = new OSMemoryPicCondition();
        public int memoryIndex = -1;
        public List<string> addFlags = new List<string>();
        public List<string> removeFlags = new List<string>();
        public List<string> addItems = new List<string>();
        public List<string> removeItems = new List<string>();
        public string targetScene;
        public string soundCue;
        [Tooltip("Optional hover label for this outcome; empty uses the hotspot label.")]
        public string tooltipLabelKey;
        [Tooltip("Play this outcome from an unavailable direction arrow without changing scenes.")]
        public bool speakWhenNavigationBlocked;
        public bool glassStartle;
        public bool finishRepair;
        [Min(0)] public int energyCost;
        [Min(0)] public float effectDuration;
    }

    [Serializable]
    public sealed class OSMemoryPicHotspotData
    {
        public string hotspotId;
        public string labelKey;
        public OSMemoryPicCondition visibleWhen = new OSMemoryPicCondition();
        public List<OSMemoryPicOutcome> outcomes = new List<OSMemoryPicOutcome>();
        // Plugs/sockets use a small common puzzle rule, all other actions are authored outcomes.
        public OSMemoryPicHotspotKind kind;
        public int socketIndex;

        public OSMemoryPicOutcome FindOutcome(OSMemoryPicSaveData state) => outcomes.Find(x => x.when.Matches(state));
    }

    public enum OSMemoryPicHotspotKind { Action, WirePlug, WireSocket }
}
