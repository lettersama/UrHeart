using TMPro;
using System;
using System.Globalization;
using UnityEngine;
using UnityEngine.Serialization;

[DisallowMultipleComponent]
public sealed class OSLocalizedText : MonoBehaviour
{
    public readonly struct Line
    {
        public readonly string Key, Fallback;
        public readonly object[] Arguments;
        public Line(string key, string fallback, params object[] arguments)
        { Key = key;Fallback = fallback;Arguments = arguments; }
        public static Line Empty => new Line(string.Empty, string.Empty);
    }
    [SerializeField] string localizationKey;
    [SerializeField, TextArea] string fallbackText;
    [SerializeField] TMP_Text targetText;
    [FormerlySerializedAs("garbleMarkedText")]
    [SerializeField] bool redactMarkedText;
    [SerializeField] bool preserveAuthoringFont;
    [SerializeField] string[] formatArguments = Array.Empty<string>();

    // Runtime bindings retain the current meaning during a language switch. They never
    // replace the serialized authoring key, so GameTest previews can still restore it.
    bool hasRuntimeText;
    string runtimeKey, runtimeFallback;
    object[] runtimeArguments;

    string resolvedText;
    OSLocalizedRedaction markedTextRedaction;
    OSLocalizedFontSwitcher localizedFontSwitcher;
    bool warnedMissingRedaction;

    public string LocalizationKey => localizationKey;
    public TMP_Text TargetText => targetText;
    public bool RedactMarkedText => redactMarkedText;
    public bool PreserveAuthoringFont => preserveAuthoringFont;

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

    public void Initialize()
    {
        ResolveReferences();
        CaptureFallbackText();
    }

    public void Refresh(OS_Handle osHandle)
    {
        ResolveReferences();
        if (targetText == null)
            return;

        GameDataManager dataManager = GameDataManager.Instance;
        ApplyLocalizedFont(
            dataManager != null ? dataManager.LocalizationData : null,
            dataManager != null
                ? dataManager.CurrentLanguageState
                : LanguageState.zh_CN);
        string fallback = string.IsNullOrEmpty(fallbackText)
            ? targetText.text
            : fallbackText;
        bool runtime = Application.isPlaying && hasRuntimeText;
        if (runtime) fallback = runtimeFallback;
        resolvedText = osHandle != null
            ? osHandle.GetLocalizedText(runtime ? runtimeKey : localizationKey, fallback)
            : fallback;
        resolvedText = Format(resolvedText, runtime);
        ApplyResolvedText();
    }

    public void Refresh(
        LocalizationData_SO localizationData,
        LanguageState language)
    {
        ResolveReferences();
        if (targetText == null)
            return;

        ApplyLocalizedFont(localizationData, language);
        string fallback = string.IsNullOrEmpty(fallbackText)
            ? targetText.text
            : fallbackText;
        bool runtime = Application.isPlaying && hasRuntimeText;
        if (runtime) fallback = runtimeFallback;
        resolvedText = localizationData != null
            ? localizationData.GetText(runtime ? runtimeKey : localizationKey, language, fallback)
            : fallback;
        resolvedText = Format(resolvedText, runtime);
        ApplyResolvedText();
    }

#if UNITY_EDITOR
    public void RestoreAuthoringText(
        LocalizationData_SO localizationData,
        LanguageState language)
    {
        ResolveReferences();
        if (targetText == null)
            return;

        ApplyLocalizedFont(localizationData, language);
        string authoringText = !string.IsNullOrEmpty(fallbackText)
            ? fallbackText
            : localizationData != null
                ? localizationData.GetText(
                    localizationKey,
                    language,
                    targetText.text)
                : targetText.text;
        authoringText = Format(authoringText, false);
        resolvedText = authoringText;

        if (markedTextRedaction != null)
            markedTextRedaction.ClearOverlay();

        targetText.text = redactMarkedText
            ? OSLocalizedRedaction.RemoveMarkers(authoringText)
            : authoringText;
    }
#endif

    public void SetLocalization(
        string key,
        string fallback,
        OS_Handle osHandle = null)
    {
        localizationKey = key ?? string.Empty;
        fallbackText = fallback ?? string.Empty;
        hasRuntimeText = false;runtimeArguments = null;
        ResolveReferences();

        if (osHandle == null)
            osHandle = GetComponentInParent<OS_Handle>(true);

        Refresh(osHandle);
    }

    public static void SetRuntime(TMP_Text label, Line line) => SetRuntime(label, line.Key, line.Fallback, line.Arguments);

    public static void SetRuntime(TMP_Text label, string key, string fallback, params object[] arguments)
    {
        if (label == null) return;
        arguments = arguments ?? Array.Empty<object>();
        var binding = label.GetComponent<OSLocalizedText>();
        if (binding == null)
        {
            // Older scene instances remain readable until their bindings are installed.
            string value = GameDataManager.Instance?.GetLocalizedText(key, fallback) ?? fallback;
            label.text = arguments != null && arguments.Length > 0 ? string.Format(value, arguments) : value;
            return;
        }
        bool same = binding.hasRuntimeText && binding.runtimeKey == key && binding.runtimeFallback == fallback
            && binding.runtimeArguments != null && binding.runtimeArguments.Length == arguments.Length;
        if (same) for (int i = 0; i < arguments.Length; i++)
            if (!Equals(binding.runtimeArguments[i], arguments[i])) { same = false;break; }
        if (same) return;
        binding.hasRuntimeText = true;binding.runtimeKey = key;binding.runtimeFallback = fallback;
        binding.runtimeArguments = arguments;
        var data = GameDataManager.Instance;
        binding.Refresh(data != null ? data.LocalizationData : null, data != null ? data.CurrentLanguageState : LanguageState.zh_CN);
    }

    string Format(string value, bool runtime)
    {
        if (runtime) return runtimeArguments != null && runtimeArguments.Length > 0
            ? string.Format(value, runtimeArguments) : value;
        if (formatArguments == null || formatArguments.Length == 0) return value;
        var arguments = new object[formatArguments.Length];
        for (int i = 0; i < arguments.Length; i++)
            arguments[i] = double.TryParse(formatArguments[i], NumberStyles.Float, CultureInfo.InvariantCulture, out double number)
                ? (object)number : formatArguments[i];
        return string.Format(value, arguments);
    }

    void ApplyResolvedText()
    {
        if (targetText == null)
            return;

        if (!redactMarkedText)
        {
            if (markedTextRedaction != null)
                markedTextRedaction.ClearOverlay();

            targetText.text = resolvedText;
            return;
        }

        ResolveReferences();
        if (markedTextRedaction != null)
        {
            warnedMissingRedaction = false;
            markedTextRedaction.ApplyMarkedText(resolvedText);
            return;
        }

        targetText.text = OSLocalizedRedaction.RemoveMarkers(resolvedText);
        if (warnedMissingRedaction)
            return;

        warnedMissingRedaction = true;
        Debug.LogWarning(
            "[OSLocalizedText] Marked text needs an "
            + nameof(OSLocalizedRedaction)
            + " component.",
            this);
    }

    void ApplyLocalizedFont(
        LocalizationData_SO localizationData,
        LanguageState language)
    {
        if (targetText == null)
            return;

        if (preserveAuthoringFont)
        {
            if (localizedFontSwitcher != null)
                localizedFontSwitcher.SetPreserveAuthoringFont(true);
            else
            {
                OSLocalizedFontUtility.Apply(
                    targetText,
                    language,
                    localizationData,
                    true);
            }
            return;
        }

        if (localizedFontSwitcher == null)
        {
            OSLocalizedFontUtility.Apply(
                targetText,
                language,
                localizationData);
            return;
        }

        localizedFontSwitcher.SetPreserveAuthoringFont(false);
        localizedFontSwitcher.ApplyFont(
            language,
            localizationData != null ? localizationData.KoreanFont : null);
    }

    void ResolveReferences()
    {
        if (targetText == null)
            targetText = GetComponent<TMP_Text>();

        if (localizedFontSwitcher == null)
            localizedFontSwitcher = GetComponent<OSLocalizedFontSwitcher>();

        if (redactMarkedText && markedTextRedaction == null)
            markedTextRedaction = GetComponent<OSLocalizedRedaction>();
    }

    void CaptureFallbackText()
    {
        if (string.IsNullOrEmpty(fallbackText) && targetText != null)
            fallbackText = targetText.text;
    }
}

