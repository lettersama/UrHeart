using System;
using MrTGame.Narrative;
using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
public sealed class OSMemoryPicSceneView : MonoBehaviour
{
    [Serializable]
    public sealed class Layer
    {
        public string id;
        public Image graphic;
        public OSMemoryPicCondition visibleWhen = new OSMemoryPicCondition();
    }
    public Layer[] layers = Array.Empty<Layer>();
    public OSMemoryPicHotspot[] hotspots = Array.Empty<OSMemoryPicHotspot>();

    public void Bind(OSMemoryPicController owner, OSMemoryPicSceneData scene, OSMemoryPicSaveData state)
    {
        foreach (OSMemoryPicHotspot hotspot in hotspots)
            hotspot.Bind(owner, scene.FindHotspot(hotspot.hotspotId));
        GetComponent<OSMemoryPicWiringView>()?.Bind(owner);
        Refresh(state);
    }

    public void Refresh(OSMemoryPicSaveData state)
    {
        foreach (Layer layer in layers)
            if (layer.graphic != null)
                layer.graphic.gameObject.SetActive(layer.visibleWhen.Matches(state));
        foreach (OSMemoryPicHotspot hotspot in hotspots) hotspot.Refresh(state);
    }
}
