using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

public enum LanguageState
{
    zh_CN = 0,
    zh_TW = 1,
    en = 2,
    ja = 3,
    ko = 4,
    fr = 5,
    de = 6,
    es = 7,
    pt_BR = 8,
    ru = 9
}

[Serializable]
public sealed class LocalizedTextValue
{
    [SerializeField] LanguageState language = LanguageState.zh_CN;
    [SerializeField, TextArea] string text;

    public LanguageState Language => language;
    public string Text => text;
}

[Serializable]
public sealed class LocalizationEntry
{
    [SerializeField] string key;
    [SerializeField] List<LocalizedTextValue> values = new List<LocalizedTextValue>();

    public string Key => key;
    public IReadOnlyList<LocalizedTextValue> Values => values;
}

[CreateAssetMenu(menuName = "MrTGame/LocalizationData", fileName = "LocalizationData")]
public sealed class LocalizationData_SO : ScriptableObject
{
    [Header("Localized Fonts")]
    [SerializeField] TMP_FontAsset koreanFont;

    [SerializeField] List<LocalizationEntry> entries = new List<LocalizationEntry>();

    Dictionary<string, Dictionary<LanguageState, string>> lookup;

    public TMP_FontAsset KoreanFont => koreanFont;
    public IReadOnlyList<LocalizationEntry> Entries => entries;

    public bool TryGetText(string key, LanguageState language, out string text)
    {
        text = null;
        if (string.IsNullOrWhiteSpace(key))
            return false;

        EnsureLookup();
        if (!lookup.TryGetValue(key, out Dictionary<LanguageState, string> translations))
            return false;

        if (TryGetNonEmptyText(translations, language, out text))
            return true;

        return language != LanguageState.zh_CN
            && TryGetNonEmptyText(translations, LanguageState.zh_CN, out text);
    }

    public string GetText(
        string key,
        LanguageState language,
        string fallback = null)
    {
        if (TryGetText(key, language, out string text))
            return text;

        if (!string.IsNullOrEmpty(fallback))
            return fallback;

        return key ?? string.Empty;
    }

    void OnEnable()
    {
        RebuildLookup();
    }

    void OnValidate()
    {
        RebuildLookup();
    }

    void EnsureLookup()
    {
        if (lookup == null)
            RebuildLookup();
    }

    void RebuildLookup()
    {
        lookup = new Dictionary<string, Dictionary<LanguageState, string>>(
            StringComparer.Ordinal);

        if (entries == null)
            return;

        for (int entryIndex = 0; entryIndex < entries.Count; entryIndex++)
        {
            LocalizationEntry entry = entries[entryIndex];
            if (entry == null || string.IsNullOrWhiteSpace(entry.Key))
                continue;

            if (lookup.ContainsKey(entry.Key))
            {
                Debug.LogWarning(
                    $"[LocalizationData] Duplicate key '{entry.Key}' at index {entryIndex}; the first entry is used.",
                    this);
                continue;
            }

            var translations = new Dictionary<LanguageState, string>();
            IReadOnlyList<LocalizedTextValue> values = entry.Values;
            if (values != null)
            {
                for (int valueIndex = 0; valueIndex < values.Count; valueIndex++)
                {
                    LocalizedTextValue value = values[valueIndex];
                    if (value == null || string.IsNullOrEmpty(value.Text))
                        continue;

                    if (!translations.TryAdd(value.Language, value.Text))
                    {
                        Debug.LogWarning(
                            $"[LocalizationData] Duplicate language '{value.Language}' for key '{entry.Key}'; the first value is used.",
                            this);
                    }
                }
            }

            lookup.Add(entry.Key, translations);
        }
    }

    static bool TryGetNonEmptyText(
        Dictionary<LanguageState, string> translations,
        LanguageState language,
        out string text)
    {
        return translations.TryGetValue(language, out text)
            && !string.IsNullOrEmpty(text);
    }
}
