using System.Runtime.CompilerServices;
using TMPro;
using UnityEngine;

public static class OSLocalizedFontUtility
{
    sealed class FontSnapshot
    {
        public TMP_FontAsset Font;
        public Material Material;
    }

    static ConditionalWeakTable<TMP_Text, FontSnapshot> fontSnapshots =
        new ConditionalWeakTable<TMP_Text, FontSnapshot>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetRuntimeState()
    {
        fontSnapshots = new ConditionalWeakTable<TMP_Text, FontSnapshot>();
    }

    // Runtime windows can be created while their template is displaying Korean.
    // Inherit the captured authoring face, so later language changes can restore it.
    public static TMP_FontAsset GetAuthoringFont(TMP_Text target) => target == null ? null
        : fontSnapshots.TryGetValue(target, out var snapshot) ? snapshot.Font : target.font;
    public static Material GetAuthoringMaterial(TMP_Text target) => target == null ? null
        : fontSnapshots.TryGetValue(target, out var snapshot) ? snapshot.Material : target.fontSharedMaterial;

    public static void ApplyCurrent(TMP_Text target)
    {
        GameDataManager dataManager = GameDataManager.Instance;
        Apply(
            target,
            dataManager != null
                ? dataManager.CurrentLanguageState
                : LanguageState.zh_CN,
            dataManager != null ? dataManager.LocalizationData : null);
    }

    public static void Apply(
        TMP_Text target,
        LanguageState language,
        LocalizationData_SO localizationData,
        bool preserveAuthoringFont = false)
    {
        if (target == null)
            return;

        FontSnapshot snapshot = fontSnapshots.GetValue(
            target,
            text => new FontSnapshot
            {
                Font = text.font,
                Material = text.fontSharedMaterial
            });

        TMP_FontAsset koreanFont = localizationData != null
            ? localizationData.KoreanFont
            : null;
        bool useKoreanFont = !preserveAuthoringFont
                             && language == LanguageState.ko
                             && koreanFont != null;
        TMP_FontAsset targetFont = useKoreanFont
            ? koreanFont
            : snapshot.Font;
        Material targetMaterial = useKoreanFont
            ? koreanFont.material
            : snapshot.Material;
        if (targetFont == null)
            return;

        if (target.font != targetFont)
            target.font = targetFont;

        if (targetMaterial != null
            && target.fontSharedMaterial != targetMaterial)
        {
            target.fontSharedMaterial = targetMaterial;
        }
    }
}
