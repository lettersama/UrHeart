using UnityEngine;

[VolFx.ShaderName("Hidden/MrTGame/ScreenFracture")]
public sealed class ScreenFracturePass : global::VolFx.VolFx.Pass
{
    static readonly int FractureSettingsId = Shader.PropertyToID("_FractureSettings");
    static readonly int CrackSettingsId = Shader.PropertyToID("_CrackSettings");
    static readonly int SafeAreaId = Shader.PropertyToID("_SafeArea");
    static readonly int SafeFeatherId = Shader.PropertyToID("_SafeFeather");

    public override string ShaderName => string.Empty;

    public override bool Validate(Material material)
    {
        ScreenFractureVolume settings = Stack.GetComponent<ScreenFractureVolume>();
        if (material == null || settings == null || !settings.IsActive())
            return false;

        Vector2 center = settings.safeCenter.value;
        Vector2 radius = settings.safeRadius.value;
        radius.x = Mathf.Max(0.01f, radius.x);
        radius.y = Mathf.Max(0.01f, radius.y);

        material.SetVector(
            FractureSettingsId,
            new Vector4(
                settings.intensity.value,
                settings.displacement.value,
                settings.rotation.value * Mathf.Deg2Rad,
                settings.zoom.value));
        material.SetVector(
            CrackSettingsId,
            new Vector4(
                settings.chromaticAberration.value,
                settings.crackWidth.value,
                settings.crackDarkness.value,
                settings.crackHighlight.value));
        material.SetVector(SafeAreaId, new Vector4(center.x, center.y, radius.x, radius.y));
        material.SetFloat(SafeFeatherId, settings.safeFeather.value);

        return true;
    }
}
