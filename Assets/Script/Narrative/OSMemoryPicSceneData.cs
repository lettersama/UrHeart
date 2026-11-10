using System.Collections.Generic;
using UnityEngine;

namespace MrTGame.Narrative
{
    [CreateAssetMenu(menuName = "MrTGame/Memory/Picture Scene")]
    public sealed class OSMemoryPicSceneData : ScriptableObject
    {
        public string sceneId;
        public string titleKey;
        public OSMemoryPicSceneView contentPrefab;
        public List<OSMemoryPicHotspotData> hotspots = new List<OSMemoryPicHotspotData>();
        public OSMemoryPicHotspotData FindHotspot(string id) => hotspots.Find(x => x.hotspotId == id);
    }
}
