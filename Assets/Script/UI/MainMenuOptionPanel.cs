using System;
using MrTGame.Core;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
public sealed class MainMenuOptionPanel : MonoBehaviour
{
    static readonly LanguageState[] SupportedLanguages =
        (LanguageState[])Enum.GetValues(typeof(LanguageState));

    MainMenuHandle owner;
    MainMenuOptionVolumeControl[] volumeControls =
        Array.Empty<MainMenuOptionVolumeControl>();
    MainMenuOptionActionButton[] actionButtons =
        Array.Empty<MainMenuOptionActionButton>();

    void Awake()
    {
        ResolveAndConfigureControls();
    }

    void OnEnable()
    {
        GameEvents.LanguageChanged -= HandleLanguageChanged;
        GameEvents.LanguageChanged += HandleLanguageChanged;
        ResolveAndConfigureControls();
        RefreshFromSave();
    }

    void OnDisable()
    {
        GameEvents.LanguageChanged -= HandleLanguageChanged;
    }

    public void Initialize(MainMenuHandle menuHandle)
    {
        owner = menuHandle;
        ResolveAndConfigureControls();
        RefreshFromSave();
    }

    public void RefreshFromSave()
    {
        GameDataManager dataManager = GameDataManager.Instance;
        if (dataManager == null)
            return;

        for (int i = 0; i < volumeControls.Length; i++)
        {
            MainMenuOptionVolumeControl control = volumeControls[i];
            if (control != null)
            {
                control.SetValue(dataManager.GetGameOptionVolume(
                    control.VolumeType));
            }
        }

        RefreshActionValues(dataManager);
    }

    public void SetVolume(GameOptionVolumeType type, int value)
    {
        GameDataManager dataManager = GameDataManager.Instance;
        if (dataManager == null)
            return;

        int savedValue = dataManager.SetGameOptionVolume(type, value);
        MainMenuOptionVolumeControl control = FindVolumeControl(type);
        control?.SetValue(savedValue);
    }

    public void HandleAction(MainMenuOptionAction action)
    {
        if (owner == null)
            owner = MainMenuHandle.Instance;

        owner?.PlayOptionButtonSfx();

        GameDataManager dataManager = GameDataManager.Instance;
        switch (action)
        {
            case MainMenuOptionAction.Language:
                if (dataManager != null)
                {
                    dataManager.SetCurrentLanguage(GetNextLanguage(
                        dataManager.CurrentLanguageState));
                    RefreshActionValues(dataManager);
                }
                break;

            case MainMenuOptionAction.ScreenMode:
                if (dataManager != null)
                {
                    dataManager.SetFullScreenOption(
                        !dataManager.GameOptions.isFullScreen);
                    RefreshActionValues(dataManager);
                }
                break;

            case MainMenuOptionAction.PostProcessing:
                if (dataManager != null)
                {
                    dataManager.SetOSPostProcessingOption(
                        !dataManager.GameOptions.isOSPostProcessingEnabled);
                    RefreshActionValues(dataManager);
                }
                break;

            case MainMenuOptionAction.Return:
                owner?.UnloadOptionPanel();
                break;
        }
    }

    void ResolveAndConfigureControls()
    {
        volumeControls = new[]
        {
            ConfigureVolume("Master", GameOptionVolumeType.Master),
            ConfigureVolume("Music", GameOptionVolumeType.Music),
            ConfigureVolume("SFX", GameOptionVolumeType.Sfx),
            ConfigureVolume("Ambient", GameOptionVolumeType.Ambient)
        };

        actionButtons = new[]
        {
            ConfigureAction("Langague", MainMenuOptionAction.Language),
            ConfigureAction("ScreenMode", MainMenuOptionAction.ScreenMode),
            ConfigureAction(
                "PostProcessing",
                MainMenuOptionAction.PostProcessing),
            ConfigureAction("Return", MainMenuOptionAction.Return)
        };
    }

    MainMenuOptionVolumeControl ConfigureVolume(
        string rowName,
        GameOptionVolumeType type)
    {
        Transform row = transform.Find(rowName);
        Transform track = FindVolumeTrack(row);
        if (track == null)
            return null;

        MainMenuOptionVolumeControl control =
            track.GetComponent<MainMenuOptionVolumeControl>();
        if (control == null)
            control = track.gameObject.AddComponent<MainMenuOptionVolumeControl>();

        RectTransform hand = track.Find("Hand") as RectTransform;
        TMP_Text valueLabel = FindDirectChildLabel(row, "Num");
        control.Configure(this, type, hand, valueLabel);
        return control;
    }

    MainMenuOptionActionButton ConfigureAction(
        string rowName,
        MainMenuOptionAction action)
    {
        Transform row = transform.Find(rowName);
        Transform raycast = row?.Find("Raycast");
        if (raycast == null)
            return null;

        MainMenuOptionActionButton button =
            raycast.GetComponent<MainMenuOptionActionButton>();
        if (button == null)
            button = raycast.gameObject.AddComponent<MainMenuOptionActionButton>();

        TMP_Text[] rowTexts = row.GetComponentsInChildren<TMP_Text>(true);
        Graphic tip = row.Find("RaycastTip")?.GetComponent<Graphic>();
        TMP_Text valueLabel = null;
        for (int i = 0; i < rowTexts.Length; i++)
        {
            TMP_Text text = rowTexts[i];
            if (text != null && text.transform != row)
            {
                valueLabel = text;
                break;
            }
        }

        button.Configure(this, action, rowTexts, tip, valueLabel);
        return button;
    }

    void RefreshActionValues(GameDataManager dataManager)
    {
        FindActionButton(MainMenuOptionAction.Language)?.SetValueText(
            GetLanguageLabel(dataManager.CurrentLanguageState));
        FindActionButton(MainMenuOptionAction.ScreenMode)?.SetValueText(
            dataManager.GameOptions.isFullScreen
                ? dataManager.GetLocalizedText(
                    "ui.os.option.fullscreen",
                    "\u5168\u5C4F")
                : dataManager.GetLocalizedText(
                    "ui.main_menu.option.windowed",
                    "\u7A97\u53E3"));
        FindActionButton(MainMenuOptionAction.PostProcessing)?.SetValueText(
            dataManager.GameOptions.isOSPostProcessingEnabled
                ? dataManager.GetLocalizedText(
                    "ui.os.option.state.on",
                    "\u5F00\u542F")
                : dataManager.GetLocalizedText(
                    "ui.os.option.state.off",
                    "\u5173\u95ED"));
    }

    MainMenuOptionVolumeControl FindVolumeControl(
        GameOptionVolumeType type)
    {
        for (int i = 0; i < volumeControls.Length; i++)
        {
            MainMenuOptionVolumeControl control = volumeControls[i];
            if (control != null && control.VolumeType == type)
                return control;
        }

        return null;
    }

    MainMenuOptionActionButton FindActionButton(
        MainMenuOptionAction action)
    {
        for (int i = 0; i < actionButtons.Length; i++)
        {
            MainMenuOptionActionButton button = actionButtons[i];
            if (button != null && button.Action == action)
                return button;
        }

        return null;
    }

    void HandleLanguageChanged(LanguageState language)
    {
        GameDataManager dataManager = GameDataManager.Instance;
        if (dataManager != null)
            RefreshActionValues(dataManager);
    }

    static Transform FindVolumeTrack(Transform row)
    {
        if (row == null)
            return null;

        for (int i = 0; i < row.childCount; i++)
        {
            Transform child = row.GetChild(i);
            if (child.GetComponent<Image>() != null
                && child.Find("Hand") != null)
            {
                return child;
            }
        }

        return null;
    }

    static TMP_Text FindDirectChildLabel(Transform row, string prefix)
    {
        if (row == null)
            return null;

        for (int i = 0; i < row.childCount; i++)
        {
            Transform child = row.GetChild(i);
            if (child.name.StartsWith(prefix))
            {
                TMP_Text label = child.GetComponent<TMP_Text>();
                if (label != null)
                    return label;
            }
        }

        return null;
    }

    static LanguageState GetNextLanguage(LanguageState current)
    {
        int currentIndex = Array.IndexOf(SupportedLanguages, current);
        int nextIndex = currentIndex < 0
            ? 0
            : (currentIndex + 1) % SupportedLanguages.Length;
        return SupportedLanguages[nextIndex];
    }

    static string GetLanguageLabel(LanguageState language)
    {
        switch (language)
        {
            case LanguageState.zh_CN:
                return "\u7B80\u4F53\u4E2D\u6587";
            case LanguageState.zh_TW:
                return "\u7E41\u9AD4\u4E2D\u6587";
            case LanguageState.en:
                return "English";
            case LanguageState.ja:
                return "\u65E5\u672C\u8A9E";
            case LanguageState.ko:
                return "\uD55C\uAD6D\uC5B4";
            case LanguageState.fr:
                return "Fran\u00E7ais";
            case LanguageState.de:
                return "Deutsch";
            case LanguageState.es:
                return "Espa\u00F1ol";
            case LanguageState.pt_BR:
                return "Portugu\u00EAs (Brasil)";
            case LanguageState.ru:
                return "\u0420\u0443\u0441\u0441\u043A\u0438\u0439";
            default:
                return language.ToString();
        }
    }
}
