using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

/// <summary>Local modal-only font materials. Never changes a font asset, atlas,
/// or material used by the mirror, reasoning text, or another OS window.</summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(TextMeshProUGUI))]
public sealed class OSChapter03PixelText : MonoBehaviour
{
    const string ShaderResource = "Chapter03Story/Chapter03PixelText";
    static Shader pixelShader;
    readonly Dictionary<Material, Material> copies = new Dictionary<Material, Material>();
    TMP_Text label;
    float? inkDilate;

    /// <summary>Dark ink on paper reads better at the font's own weight; the
    /// minimum weight below is for light text on black.</summary>
    public void UseInkWeight(float dilate)
    {
        inkDilate = dilate;
        foreach (var copy in copies.Values) if (copy != null) copy.SetFloat("_FaceDilate", dilate);
    }

    void OnEnable()
    {
        label = GetComponent<TMP_Text>();
        label.OnPreRenderText += BeforeTextRender;
    }

    void OnDisable()
    {
        if (label != null) label.OnPreRenderText -= BeforeTextRender;
    }

    public void RefreshMaterial()
    {
        if (label == null) label = GetComponent<TMP_Text>();
        if (pixelShader == null) pixelShader = Resources.Load<Shader>(ShaderResource);
        if (pixelShader == null) throw new InvalidOperationException("Missing Chapter 3 modal pixel-text shader: " + ShaderResource);
        // OSLocalizedFontUtility must run first so its snapshot retains the
        // original font/material, including the separate Korean font.
        var source = label.fontSharedMaterial;
        if (source != null && source.shader != pixelShader) label.fontSharedMaterial = LocalCopy(source);
    }

    void BeforeTextRender(TMP_TextInfo info)
    {
        RefreshMaterial();
        // TMP normally derives CJK/multi-atlas fallback materials from our
        // preset. Bitmap fallbacks can instead keep their source shader;
        // replace only this label's references when that happens.
        // TMP's fontSharedMaterials setter ignores non-default fallback
        // submaterials. Bind each owned font submesh directly instead.
        foreach (var submesh in label.GetComponentsInChildren<TMP_SubMeshUI>(true))
        {
            if (submesh.textComponent != label || submesh.fontAsset == null) continue;
            var source = submesh.sharedMaterial;
            if (source == null || source.shader == pixelShader) continue;
            submesh.sharedMaterial = LocalCopy(source);
        }
    }

    Material LocalCopy(Material source)
    {
        if (copies.TryGetValue(source, out var copy) && copy != null) return copy;
        copy = new Material(source)
        {
            shader = pixelShader,
            name = source.name + " (Chapter03 modal pixels)",
            hideFlags = HideFlags.HideAndDontSave
        };
        copy.SetColor("_FaceColor", Color.white);
        // The Korean font's unweighted hairlines fall between logical pixels.
        // Match the existing main font preset's minimum ink weight locally.
        copy.SetFloat("_FaceDilate", inkDilate ?? Mathf.Max(.2f, source.HasProperty("_FaceDilate") ? source.GetFloat("_FaceDilate") : 0f));
        copy.SetFloat("_OutlineWidth", 0f);
        copy.SetFloat("_OutlineSoftness", 0f);
        copy.SetFloat("_MaskSoftnessX", 0f);
        copy.SetFloat("_MaskSoftnessY", 0f);
        copy.DisableKeyword("OUTLINE_ON");
        copy.DisableKeyword("UNDERLAY_ON");
        copy.DisableKeyword("UNDERLAY_INNER");
        copies.Add(source, copy);
        return copy;
    }

    void OnDestroy()
    {
        foreach (var copy in copies.Values) if (copy != null) Destroy(copy);
        copies.Clear();
    }
}
