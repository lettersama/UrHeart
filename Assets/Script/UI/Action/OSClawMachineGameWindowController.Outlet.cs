using UnityEngine;

public sealed partial class OSClawMachineGameWindowController
{
    [Header("Outlet Ejection")]
    [SerializeField] Material outletRevealTemplate;
    // The 70 x 39 hatch's actual black opening excludes its title, side walls,
    // and sloping tray. Coordinates are top-left native pixels, not screen pixels.
    static readonly Vector4 AuthoredOutletAperture = new Vector4(10f, 14f, 60f, 30f);
    static readonly int OutletUvId = Shader.PropertyToID("_SpriteUVRect");
    static readonly int OutletPrizeRectId = Shader.PropertyToID("_PrizeRect");
    static readonly int OutletApertureId = Shader.PropertyToID("_ApertureRect");
    static readonly int OutletFrontCutId = Shader.PropertyToID("_FrontCutY");
    Material outletRevealRuntime, outletPreviousMaterial;

    void BeginOutletReveal()
    {
        if (outletImage == null) return;
        if (outletRevealTemplate == null)
            outletRevealTemplate = Resources.Load<Material>("UI/ClawOutletReveal");
        outletPreviousMaterial = outletImage.material;
        if (outletRevealTemplate != null && outletRevealTemplate.shader != null && outletRevealTemplate.shader.isSupported)
        {
            outletRevealRuntime = new Material(outletRevealTemplate) { name = "Claw outlet reveal (runtime)", hideFlags = HideFlags.DontSave };
            outletImage.material = outletRevealRuntime;
        }
        else
        {
            // A missing import must not expose the whole toy above the cabinet.
            // The ready/restored prize still uses ordinary UI rendering.
            outletImage.enabled = false;
            Debug.LogWarning("[ClawMachine] Missing UI/ClawOutletReveal material; keeping the travelling prize hidden until it reaches the tray.", this);
        }
        RenderOutletDelivery(0f);
    }

    void RenderOutletDelivery(float seconds)
    {
        if (outletImage == null) return;
        float width = claimArea != null ? claimArea.rect.width : 70f;
        float height = claimArea != null ? claimArea.rect.height : 39f;
        float sx = width / 70f, sy = height / 39f;
        float trayY = -outletPrizeHome.y;
        float bottomY;
        // 0-.48: drop from behind the header into the aperture, feet first.
        // .48-.60: a readable seated hold behind the hatch.
        // .60-1.00: push forwards over the lip, revealing the original-size toy.
        // 1.00-1.12: a two-pixel settle onto the tray, with no size change.
        if (seconds < .48f)
        {
            float fall = Mathf.Clamp01(seconds / .48f);
            bottomY = Mathf.Lerp(AuthoredOutletAperture.y * sy - 2f, trayY - 2f, fall * fall);
        }
        else if (seconds < .60f) bottomY = trayY - 2f;
        else if (seconds < 1f)
            bottomY = Mathf.Lerp(trayY - 2f, trayY + 2f, Mathf.SmoothStep(0f, 1f, (seconds - .60f) / .40f));
        else bottomY = Mathf.Lerp(trayY + 2f, trayY, Mathf.SmoothStep(0f, 1f, (seconds - 1f) / .12f));
        var rect = outletImage.rectTransform;
        rect.anchoredPosition = new Vector2(outletPrizeHome.x, -Mathf.Round(bottomY));
        rect.localScale = Vector3.one;
        if (outletRevealRuntime == null) return;

        Vector2 size = rect.rect.size;
        // Derive the clip position from sprite UVs rather than batched UI vertex
        // coordinates, so dragging/scaling the whole window cannot move the mask.
        Vector4 uv = UnityEngine.Sprites.DataUtility.GetOuterUV(outletImage.sprite);
        outletRevealRuntime.SetVector(OutletUvId, new Vector4(uv.x, uv.y, uv.z - uv.x, uv.w - uv.y));
        outletRevealRuntime.SetVector(OutletPrizeRectId,
            new Vector4(rect.anchoredPosition.x - size.x * .5f, -rect.anchoredPosition.y - size.y, size.x, size.y));
        outletRevealRuntime.SetVector(OutletApertureId, new Vector4(
            Mathf.Round(AuthoredOutletAperture.x * sx), Mathf.Round(AuthoredOutletAperture.y * sy),
            Mathf.Round(AuthoredOutletAperture.z * sx), Mathf.Round(AuthoredOutletAperture.w * sy)));
        // This front plane is a hard pixel edge, not an alpha fade. The header
        // stays intact until the toy physically passes in front of the hatch.
        float forward = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((seconds - .60f) / .40f));
        float frontCut = forward <= 0f ? 100000f
            : Mathf.Round(Mathf.Lerp(trayY, trayY - size.y - 2f, forward));
        outletRevealRuntime.SetFloat(OutletFrontCutId, frontCut);
        outletImage.SetMaterialDirty();
    }

    void StopOutletReveal()
    {
        if (outletImage != null)
        {
            if (outletRevealRuntime != null && outletImage.material == outletRevealRuntime)
                outletImage.material = outletPreviousMaterial;
            outletImage.enabled = true;
        }
        if (outletRevealRuntime != null) Destroy(outletRevealRuntime);
        outletRevealRuntime = null;
        outletPreviousMaterial = null;
    }

#if UNITY_EDITOR
    /// <summary>Isolated paused-editor sampling; does not change receipts or notify story listeners.</summary>
    public void DebugPreviewOutletDelivery(int prizeIndex, float normalizedTime)
    {
        InitializeGameplay();
        ShowOutlet(prizeIndex, false);
        RenderOutletDelivery(Mathf.Clamp01(normalizedTime) * OutletDeliverySeconds);
        if (normalizedTime >= 1f)
        {
            StopOutletReveal();
            outletImage.rectTransform.anchoredPosition = outletPrizeHome;
        }
    }
#endif
}
