using MrTGame.Core;
using TMPro;
using UnityEngine;

/// <summary>Claw-machine window, localized labels and gameplay presentation.</summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(OSWindowUnit))]
public sealed partial class OSClawMachineGameWindowController : MonoBehaviour
{
    public const string GrabLocalizationKey = "ui.os.claw_machine.grab";
    public const string ClaimLocalizationKey = "ui.os.claw_machine.claim";

    [Header("Window and Labels")]
    [SerializeField] OSWindowUnit window;
    [SerializeField] TMP_Text windowTitle;
    [SerializeField] RectTransform titleTape;
    [SerializeField] TMP_Text grabLabel;
    [SerializeField] TMP_Text claimLabel;

    [Header("Tweened Movement Parts")]
    [SerializeField] RectTransform carriage;
    [SerializeField] RectTransform cable;
    [SerializeField] RectTransform claw;
    [SerializeField] RectTransform leftFinger;
    [SerializeField] RectTransform rightFinger;
    [SerializeField] RectTransform prizeArea;
    [SerializeField] RectTransform claimArea;

    bool titleLayoutPending;

    public OSWindowUnit Window => window != null ? window : GetComponent<OSWindowUnit>();
    public RectTransform Carriage => carriage;
    public RectTransform Cable => cable;
    public RectTransform Claw => claw;
    public RectTransform LeftFinger => leftFinger;
    public RectTransform RightFinger => rightFinger;
    public RectTransform PrizeArea => prizeArea;
    public RectTransform ClaimArea => claimArea;

    void Awake()
    {
        if (window == null) window = GetComponent<OSWindowUnit>();
        InitializeGameplay();
    }

    void OnEnable()
    {
        trapHasFocus = Application.isFocused;
        RefreshLanguage();
        GameEvents.LanguageChanged += HandleLanguageChanged;
        RestoreGameplay();
    }

    void OnDisable()
    {
        motorAudio?.Stop();
        SuspendCursorTrap();
        GameEvents.LanguageChanged -= HandleLanguageChanged;
        HidePrizeTooltip();
        wasPresentationVisible = false;
    }

    void HandleLanguageChanged(LanguageState language) => RefreshLanguage();

    public void RefreshLanguage()
    {
        ApplyLabel(windowTitle, OSClawMachineAppWindowController.NameLocalizationKey, "抓娃娃机");
        ApplyLabel(grabLabel, GrabLocalizationKey, "夹取");
        ApplyLabel(claimLabel, ClaimLocalizationKey, "领取");
        RefreshGameplayLanguage();
        RefreshTitleLayout();
        // Font switchers also receive LanguageChanged; measure once more after those callbacks.
        titleLayoutPending = true;
    }

    void LateUpdate()
    {
        PresentCapturedCursor();
        if (!titleLayoutPending) return;
        titleLayoutPending = false;
        RefreshTitleLayout();
        FitPrizeTooltip(prizeTooltipLabel, prizeTooltip);
    }

    void RefreshTitleLayout()
    {
        if (titleTape == null) titleTape = transform.Find("TitleTape") as RectTransform;
        FitTitleTape(windowTitle, titleTape);
    }

    // Shared with the isolated localization preview: no locale or save state is changed here.
    public static void FitTitleTape(TMP_Text title, RectTransform tape)
    {
        if (title == null || tape == null || title.font == null) return;
        const float sideInset = 7f;
        const float maxTapeWidth = 188f;

        // Start from the authoring size, not the previous language's auto-shrunk result.
        if (title.enableAutoSizing) title.fontSize = title.fontSizeMax;
        float textWidth = title.GetPreferredValues(title.text, Mathf.Infinity, Mathf.Infinity).x;
        float tapeWidth = Mathf.Clamp(Mathf.Ceil(textWidth) + sideInset * 2f, 32f, maxTapeWidth);
        tape.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, tapeWidth);

        var label = title.rectTransform;
        label.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, tapeWidth - sideInset * 2f);
        var position = label.anchoredPosition;
        position.x = tape.anchoredPosition.x + tapeWidth * .5f;
        label.anchoredPosition = position;
    }

    static void ApplyLabel(TMP_Text target, string key, string fallback)
    {
        if (target == null) return;
        target.text = OSToolProgramLibrary.Localize(key, fallback);
        OSLocalizedFontUtility.ApplyCurrent(target);
    }
}

