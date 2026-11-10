using MrTGame.Core;
using TMPro;
using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(TMP_Text))]
public sealed class OSLocalizedFontSwitcher : MonoBehaviour
{
    [SerializeField] TMP_Text targetText;
    [SerializeField] TMP_FontAsset simplifiedChineseFont;
    [SerializeField] TMP_FontAsset otherLanguagesFont;
    [SerializeField] bool preserveAuthoringFont;

    TMP_FontAsset authoringFont;
    Material authoringMaterial;
    bool hasCapturedAuthoringFont;

    public TMP_FontAsset SimplifiedChineseFont => simplifiedChineseFont;
    public TMP_FontAsset OtherLanguagesFont => otherLanguagesFont;
    public bool PreserveAuthoringFont => preserveAuthoringFont;

    void Awake()
    {
        ResolveReferences();
        CaptureAuthoringFont();
    }

    void Reset()
    {
        ResolveReferences();
        ApplyFont(LanguageState.zh_CN);
    }

    void OnValidate()
    {
        ResolveReferences();
        if (!Application.isPlaying)
            ApplyFont(LanguageState.zh_CN);
    }

    void OnEnable()
    {
        GameEvents.LanguageChanged += ApplyFont;
        Refresh();
    }

    void OnDisable()
    {
        GameEvents.LanguageChanged -= ApplyFont;
    }

    public void Refresh()
    {
        GameDataManager dataManager = GameDataManager.Instance;
        ApplyFont(
            dataManager != null
                ? dataManager.CurrentLanguageState
                : LanguageState.zh_CN,
            dataManager != null && dataManager.LocalizationData != null
                ? dataManager.LocalizationData.KoreanFont
                : null);
    }

    public void ApplyFont(LanguageState language)
    {
        GameDataManager dataManager = GameDataManager.Instance;
        ApplyFont(
            language,
            dataManager != null && dataManager.LocalizationData != null
                ? dataManager.LocalizationData.KoreanFont
                : null);
    }

    public void ApplyFont(
        LanguageState language,
        TMP_FontAsset koreanFont)
    {
        ResolveReferences();
        CaptureAuthoringFont();

        TMP_FontAsset localizedFont;
        Material localizedMaterial;
        if (preserveAuthoringFont)
        {
            localizedFont = authoringFont;
            localizedMaterial = authoringMaterial;
        }
        else if (language == LanguageState.ko && koreanFont != null)
        {
            localizedFont = koreanFont;
            localizedMaterial = koreanFont.material;
        }
        else
        {
            localizedFont = language == LanguageState.zh_CN
                ? simplifiedChineseFont
                : otherLanguagesFont;
            if (localizedFont == null)
            {
                localizedFont = authoringFont;
                localizedMaterial = authoringMaterial;
            }
            else
            {
                localizedMaterial = localizedFont.material;
            }
        }

        if (targetText == null || localizedFont == null)
            return;

        if (targetText.font != localizedFont)
            targetText.font = localizedFont;

        if (localizedMaterial != null
            && targetText.fontSharedMaterial != localizedMaterial)
        {
            targetText.fontSharedMaterial = localizedMaterial;
        }
    }

    public void SetPreserveAuthoringFont(bool preserve)
    {
        ResolveReferences();
        CaptureAuthoringFont();
        preserveAuthoringFont = preserve;
        if (preserve)
            ApplyFont(LanguageState.zh_CN, null);
    }

    void ResolveReferences()
    {
        if (targetText == null)
            targetText = GetComponent<TMP_Text>();
    }

    void CaptureAuthoringFont()
    {
        if (hasCapturedAuthoringFont || targetText == null)
            return;

        authoringFont = targetText.font;
        authoringMaterial = targetText.fontSharedMaterial;
        hasCapturedAuthoringFont = true;
    }
}
