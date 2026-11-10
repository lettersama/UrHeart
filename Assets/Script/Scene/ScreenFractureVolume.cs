using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

[Serializable]
[VolumeComponentMenu("MrTGame/Screen Fracture")]
public sealed class ScreenFractureVolume : VolumeComponent, IPostProcessComponent
{
    [Tooltip("Overall blend of the fracture and shard remapping.")]
    public ClampedFloatParameter intensity = new(0f, 0f, 1f);

    [Tooltip("Per-shard UV translation in screen-height units.")]
    public ClampedFloatParameter displacement = new(0.014f, 0f, 0.06f);

    [Tooltip("Maximum per-shard rotation in degrees.")]
    public ClampedFloatParameter rotation = new(1.1f, 0f, 5f);

    [Tooltip("Maximum per-shard zoom difference.")]
    public ClampedFloatParameter zoom = new(0.008f, 0f, 0.05f);

    [Tooltip("RGB separation around displaced shard edges.")]
    public ClampedFloatParameter chromaticAberration = new(0.0012f, 0f, 0.01f);

    [Tooltip("Width of the main cracks in normalized screen-height units.")]
    public ClampedFloatParameter crackWidth = new(0.00125f, 0.0002f, 0.012f);

    [Tooltip("Darkness of each crack core.")]
    public ClampedFloatParameter crackDarkness = new(0.74f, 0f, 1f);

    [Tooltip("Brightness of the thin reflected rim beside each crack.")]
    public ClampedFloatParameter crackHighlight = new(0.1f, 0f, 1f);

    [Tooltip("Center of the protected dialogue area in viewport coordinates.")]
    public Vector2Parameter safeCenter = new(new Vector2(0.5f, 0.5f));

    [Tooltip("Horizontal and vertical radii of the protected dialogue area.")]
    public Vector2Parameter safeRadius = new(new Vector2(0.22f, 0.34f));

    [Tooltip("Soft transition outside the protected dialogue area.")]
    public ClampedFloatParameter safeFeather = new(0.16f, 0.01f, 0.5f);

    public bool IsActive()
    {
        return active && intensity.value > 0.0001f;
    }

    public bool IsTileCompatible()
    {
        return false;
    }
}
