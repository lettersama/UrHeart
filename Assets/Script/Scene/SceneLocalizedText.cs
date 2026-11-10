using MrTGame.Core;
using TMPro;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class SceneLocalizedText : MonoBehaviour
{
    [SerializeField] string localizationKey;
    [SerializeField, TextArea] string fallbackText;
    [SerializeField] TMP_Text targetText;

    public string LocalizationKey => localizationKey;
    public string FallbackText => fallbackText;
    public TMP_Text TargetText => targetText;

    void Reset()
    {
        ResolveReferences();
        CaptureFallbackText();
    }

    void OnValidate()
    {
        ResolveReferences();
        CaptureFallbackText();
    }

    void Awake()
    {
        ResolveReferences();
        CaptureFallbackText();
    }

    void OnEnable()
    {
        GameEvents.LanguageChanged -= OnLanguageChanged;
        GameEvents.LanguageChanged += OnLanguageChanged;
        Refresh();
    }

    void OnDisable()
    {
        GameEvents.LanguageChanged -= OnLanguageChanged;
    }

    public void Initialize()
    {
        ResolveReferences();
        CaptureFallbackText();
        Refresh();
    }

    public void Refresh()
    {
        ResolveReferences();
        if (targetText == null)
            return;

        string fallback = string.IsNullOrEmpty(fallbackText)
            ? targetText.text
            : fallbackText;
        GameDataManager dataManager = GameDataManager.Instance;
        OSLocalizedFontUtility.ApplyCurrent(targetText);
        targetText.text = dataManager != null
            ? dataManager.GetLocalizedText(localizationKey, fallback)
            : fallback;
    }

    public void SetLocalization(string key, string fallback)
    {
        localizationKey = key ?? string.Empty;
        fallbackText = fallback ?? string.Empty;
        Initialize();
    }

    void OnLanguageChanged(LanguageState language)
    {
        Refresh();
    }

    void ResolveReferences()
    {
        if (targetText == null)
            targetText = GetComponent<TMP_Text>();
    }

    void CaptureFallbackText()
    {
        if (string.IsNullOrEmpty(fallbackText) && targetText != null)
            fallbackText = targetText.text;
    }
}
