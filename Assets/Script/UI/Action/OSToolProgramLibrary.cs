using System;
using System.Collections.Generic;
using MrTGame.Core;
using UnityEngine;
using UnityEngine.InputSystem;

[DisallowMultipleComponent]
public sealed class OSToolProgramLibrary : MonoBehaviour
{
    public const string UnpackCommand = "tool unpack --memory";
    [SerializeField] OSToolWindowController terminal;
    [SerializeField] RectTransform libraryRoot;
    [SerializeField] OSToolProgramUnit[] programUnits = Array.Empty<OSToolProgramUnit>();
    [SerializeField] OSToolInstallWindowController installer;
    [SerializeField, Min(0.1f)] float idleHintDelay = 5f;
    [SerializeField] bool useVisualLibrary;
    [SerializeField, Min(0.5f)] float firstUnpackDuration = 2.5f;

    GameDataManager boundData;
    OSWindowUnit window;
    float idleElapsed;
    bool isTextLibraryOpen;
    OSToolLineUnit textHeading;
    OSToolLineUnit textFooter;
    OSToolLibraryFrame textFrame;
    OSToolArchiveCard archiveCard;
    float unpackElapsed;
    readonly List<OSToolLineUnit> textProgramLines = new List<OSToolLineUnit>();

    public bool IsOpen => isTextLibraryOpen || (libraryRoot != null && libraryRoot.gameObject.activeSelf);
    public bool UsesVisualLibrary => useVisualLibrary;
    public bool HasUnpackedThisSession { get; private set; }
    public bool IsInstallationHintRequested { get; private set; }
    public bool IsInstalling => installer != null && installer.IsInstalling;
    public OSToolInstallWindowController Installer => installer;
    public bool IsUnpacking { get; private set; }
    public bool CanInteract => CanFillCommand && IsOpen;
    public bool CanFillCommand => terminal != null && terminal.IsReady && !IsInstalling && !IsUnpacking
        && !(terminal.Decoration != null && terminal.Decoration.IsAnimating)
        && isActiveAndEnabled && window != null && window.IsOpen && !window.IsMinimized
        && (OS_Handle.Instance == null || (OS_Handle.Instance.IsOSVisible && !OS_Handle.Instance.IsSystemInputLocked));
    public event Action LibraryOpened;
    public event Action InstallationHintRequested;

    void Awake() => window = GetComponent<OSWindowUnit>();

    void OnEnable()
    {
        window = GetComponent<OSWindowUnit>();
        boundData = GameDataManager.Instance;
        if (boundData != null)
            boundData.ToolProgramInstalled += HandleProgramInstalled;
        if (installer != null)
            installer.InstallationFinished += HandleInstallationFinished;
        GameEvents.LanguageChanged += HandleLanguageChanged;
        RefreshPrograms();
        if (archiveCard != null)
            archiveCard.RefreshLanguage();
    }

    void OnDisable()
    {
        if (boundData != null)
            boundData.ToolProgramInstalled -= HandleProgramInstalled;
        if (installer != null)
            installer.InstallationFinished -= HandleInstallationFinished;
        GameEvents.LanguageChanged -= HandleLanguageChanged;
        boundData = null;
    }

    void Update()
    {
        if (IsUnpacking)
        {
            UpdateUnpacking();
            return;
        }
        if (!CanInteract || IsInstallationHintRequested
            || (OS_Handle.Instance != null && !OS_Handle.Instance.IsWindowMouseActive(window)))
            return;

        Mouse mouse = Mouse.current;
        bool active = (Keyboard.current != null && Keyboard.current.anyKey.wasPressedThisFrame)
            || (mouse != null && (mouse.delta.ReadValue().sqrMagnitude > 0f
                || mouse.scroll.ReadValue().sqrMagnitude > 0f || mouse.leftButton.wasPressedThisFrame
                || mouse.rightButton.wasPressedThisFrame));
        idleElapsed = active ? 0f : idleElapsed + Time.unscaledDeltaTime;
        if (idleElapsed >= idleHintDelay)
            RequestInstallationHint();
    }

    public bool TryHandleCommand(string command)
    {
        if (IsUnpacking)
            return true;
        string[] parts = command.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0 || !string.Equals(parts[0], "tool", StringComparison.OrdinalIgnoreCase))
            return false;

        GameDataManager data = GameDataManager.Instance;
        if (parts.Length == 3 && string.Equals(parts[1], "unpack", StringComparison.OrdinalIgnoreCase)
            && string.Equals(parts[2], "--memory", StringComparison.OrdinalIgnoreCase))
        {
            if (data == null || !data.CanAccessToolPrograms || data.OSToolProgramData == null)
                terminal.AppendText(Localize("ui.os.tool.program.unavailable", "Memory packages unavailable."));
            else if (data.HasToolProgramsUnpacked)
                OpenLibrary();
            else
                BeginUnpacking();
        }
        else if (parts.Length == 3 && string.Equals(parts[1], "install", StringComparison.OrdinalIgnoreCase))
        {
            if (!HasUnpackedThisSession)
            {
                terminal.AppendText(Localize("ui.os.tool.program.unpack_first", "Run {0} first.").Replace("{0}", UnpackCommand));
            }
            else if (data == null || data.OSToolProgramData == null
                || !data.OSToolProgramData.TryGetProgram(parts[2], out OSToolProgramDefinition program))
            {
                terminal.AppendText(Localize("ui.os.tool.program.unknown", "Package not found: {0}").Replace("{0}", parts[2]), false);
            }
            else
            {
                OSToolInstallResult result = data.CheckToolProgramInstallation(program.Id);
                if (result != OSToolInstallResult.Success)
                    ReportResult(program, result);
                else if (installer == null || !installer.BeginInstallation(program))
                    ReportResult(program, OSToolInstallResult.Unavailable);
            }
        }
        else
        {
            return false;
        }
        idleElapsed = 0f;
        return true;
    }

    public void ShowInitialContents()
    {
        GameDataManager data = GameDataManager.Instance;
        if (data == null || !data.CanAccessToolPrograms || data.OSToolProgramData == null)
            return;
        if (data.HasToolProgramsUnpacked)
            OpenLibrary();
        else
            ShowPendingArchive();
    }

    public void ShowPendingArchive()
    {
        GameDataManager data = GameDataManager.Instance;
        if (data == null || !data.CanAccessToolPrograms || data.HasToolProgramsUnpacked)
            return;
        EnsureArchiveCard();
        archiveCard.Present(false, 0f);
        OSToolCommandLink.Attach(archiveCard, archiveCard.HitArea, this, UnpackCommand, archiveCard.Inks);
        terminal.RefreshContentLayout();
    }

    void EnsureArchiveCard()
    {
        if (archiveCard == null)
            archiveCard = OSToolArchiveCard.Create(terminal.OutputRoot, terminal.CommandInput.textComponent);
        archiveCard.gameObject.SetActive(true);
        archiveCard.transform.SetAsLastSibling();
    }

    void BeginUnpacking()
    {
        EnsureArchiveCard();
        unpackElapsed = 0f;
        IsUnpacking = true;
        OSToolCommandLink.Detach(archiveCard);
        archiveCard.Present(true, 0f);
        terminal.RefreshContentLayout();
    }

    void UpdateUnpacking()
    {
        if (window == null || !window.IsOpen || window.IsMinimized || GameTestConsole.BlocksGameplayInput
            || (OS_Handle.Instance != null && (!OS_Handle.Instance.IsOSVisible || OS_Handle.Instance.IsSystemInputLocked)))
            return;
        unpackElapsed += Time.unscaledDeltaTime;
        float progress = Mathf.Clamp01(unpackElapsed / Mathf.Max(0.25f, firstUnpackDuration - 0.25f));
        archiveCard.Present(true, progress);
        if (unpackElapsed < firstUnpackDuration)
            return;

        // Commit only after the presentation completes. A close before this point
        // returns to the pending ZIP; minimizing merely pauses this Update.
        GameDataManager.Instance.MarkToolProgramsUnpacked();
        OpenLibrary();
        terminal.RequestInputFocus();
    }

    void OpenLibrary()
    {
        terminal.Clear();
        HasUnpackedThisSession = true;
        if (useVisualLibrary && libraryRoot != null)
        {
            libraryRoot.gameObject.SetActive(true);
            libraryRoot.SetAsLastSibling();
        }
        else
        {
            isTextLibraryOpen = true;
            textHeading = terminal.AppendText(string.Empty, false);
            foreach (OSToolProgramDefinition program in GameDataManager.Instance.OSToolProgramData.Programs)
                textProgramLines.Add(terminal.AppendText(string.Empty, false));
        }
        RefreshPrograms();
        terminal.RefreshContentLayout();
        idleElapsed = 0f;
        LibraryOpened?.Invoke();
    }

    public void SelectProgram(OSToolProgramDefinition program)
    {
        if (!useVisualLibrary || !CanInteract || program == null)
            return;
        FillCommand(program.InstallCommand);
        RequestInstallationHint();
    }

    public bool TryGetProgramTarget(string programId, out RectTransform target)
    {
        target = null;
        var programs = GameDataManager.Instance?.OSToolProgramData?.Programs;
        if (!IsOpen || programs == null) return false;
        for (int i = 0; i < programs.Count; i++)
        {
            if (programs[i].Id != programId) continue;
            if (isTextLibraryOpen && i < textProgramLines.Count)
                target = textProgramLines[i]?.Label?.rectTransform;
            else if (i < programUnits.Length && programUnits[i] != null)
                target = programUnits[i].transform as RectTransform;
            return target != null && target.gameObject.activeInHierarchy;
        }
        return false;
    }

    // Clicking a library row or the waiting archive types its command for the player.
    public void FillCommand(string command)
    {
        if (!CanFillCommand || string.IsNullOrEmpty(command))
            return;
        OS_Handle.Instance?.NotifyWindowPointerPressed(window);
        terminal.FillCommand(command);
        idleElapsed = 0f;
    }

    public void RequestInstallationHint()
    {
        if (!CanInteract || IsInstallationHintRequested)
            return;
        IsInstallationHintRequested = true;
        InstallationHintRequested?.Invoke();
    }

    public void ClearOutput()
    {
        IsUnpacking = false;
        unpackElapsed = 0f;
        if (archiveCard != null)
            archiveCard.gameObject.SetActive(false);
        if (textFrame != null)
            textFrame.gameObject.SetActive(false);
        if (libraryRoot != null)
            libraryRoot.gameObject.SetActive(false);
        isTextLibraryOpen = false;
        textHeading = null;
        textFooter = null;
        textProgramLines.Clear();
        idleElapsed = 0f;
    }

    public void ResetSession()
    {
        // The chapter-four installer is detached from Tool and owned by the
        // entry director. Opening or closing Tool must not recreate it.
        if (installer != null && !installer.IsChapter04Installation)
            installer.CancelInstallation();
        HasUnpackedThisSession = false;
        IsInstallationHintRequested = false;
        ClearOutput();
    }

    void HandleInstallationFinished(OSToolProgramDefinition program, OSToolInstallResult result)
    {
        if (result == OSToolInstallResult.Success && program.BatteryCost > 0)
        {
            OSUIEnergySpendPresenter.Show(this,
                terminal != null && terminal.CommandInput != null
                    ? terminal.CommandInput.transform as RectTransform : null);
        }
        ReportResult(program, result);
        RefreshPrograms();
    }

    void ReportResult(OSToolProgramDefinition program, OSToolInstallResult result)
    {
        string key;
        string fallback;
        switch (result)
        {
            case OSToolInstallResult.Success:
                key = "ui.os.tool.install.success"; fallback = "Installed: {0}"; break;
            case OSToolInstallResult.AlreadyInstalled:
                key = "ui.os.tool.install.already"; fallback = "Already installed: {0}"; break;
            case OSToolInstallResult.InsufficientBattery:
                key = "ui.os.tool.install.battery_low"; fallback = "Insufficient energy. Required: {1}"; break;
            default:
                key = "ui.os.tool.program.unavailable"; fallback = "Memory packages unavailable."; break;
        }
        terminal.AppendText(Localize(key, fallback).Replace("{0}", program.DisplayName)
            .Replace("{1}", program.BatteryCost.ToString()));
    }

    void HandleProgramInstalled(string id) => RefreshPrograms();
    void HandleLanguageChanged(LanguageState language)
    {
        RefreshPrograms();
        if (archiveCard != null)
            archiveCard.RefreshLanguage();
    }

    public void RefreshPrograms()
    {
        OSToolProgramData_SO catalog = GameDataManager.Instance?.OSToolProgramData;
        if (catalog == null)
            return;
        if (isTextLibraryOpen)
        {
            bool decorated = terminal.Decoration != null && terminal.Decoration.HasDecoratedPresentation;
            string heading = "[ " + Localize("ui.os.tool.library.title", "Program Library") + " ]";
            BindTextLine(textHeading, decorated ? "<line-height=4>\n</line-height>\n<margin=12><align=center>. * ~ " + heading + " ~ * .</align></margin>\n" : heading, decorated);
            for (int i = 0; i < textProgramLines.Count && i < catalog.Programs.Count; i++)
            {
                OSToolProgramDefinition program = catalog.Programs[i];
                if (!GameDataManager.Instance.IsToolProgramAvailable(program.Id))
                {
                    OSToolCommandLink.Detach(textProgramLines[i]);
                    textProgramLines[i].gameObject.SetActive(false);
                    continue;
                }
                string cost = Localize("ui.os.tool.program.cost", "{0} energy")
                    .Replace("{0}", program.BatteryCost.ToString());
                bool installed = GameDataManager.Instance.IsToolProgramInstalled(program.Id);
                string status = installed
                    ? " [" + Localize("ui.os.tool.program.installed", "Installed") + "]" : string.Empty;
                OSToolLineUnit line = textProgramLines[i];
                line.gameObject.SetActive(true);
                OSLocalizedFontUtility.ApplyCurrent(line.Label);
                float tabIndent = line.Label.GetPreferredValues("\t|", Mathf.Infinity, Mathf.Infinity).x
                    - line.Label.GetPreferredValues("|", Mathf.Infinity, Mathf.Infinity).x;
                string indent = tabIndent.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
                string installLabel = Localize("ui.os.tool.program.install_command", "Install command: {0}")
                    .Replace("{0}", string.Empty).TrimEnd();
                string install = $"<nobr>{installLabel}</nobr> <nobr>{program.InstallCommand}</nobr>";
                string prefix = decorated ? "* " : string.Empty;
                // Keep labels and commands intact, with a one-tab indent on wrapped continuations.
                string text = $"{prefix}[{i + 1:00}] {program.DisplayName} | <nobr>{cost}</nobr>{status}\n<indent={indent}>{install}</indent>";
                BindTextLine(line, decorated ? "<margin=12>" + text + "</margin>" : text, true);
                if (!installed)
                    OSToolCommandLink.Attach(line, line.Label, this, program.InstallCommand, line.Label);
            }
            if (decorated && textFooter == null)
            {
                // Keep a measurable border row, with 4px of space above it.
                textFooter = terminal.AppendText("<line-height=4>\n</line-height><color=#FFFFFF00>|</color>");
                OSToolLineUnit lastProgram = textProgramLines.Count > 0 ? textProgramLines[textProgramLines.Count - 1] : textHeading;
                textFooter.transform.SetSiblingIndex(lastProgram.transform.GetSiblingIndex() + 1);
            }
            if (decorated)
            {
                if (textFrame == null)
                    textFrame = OSToolLibraryFrame.Create(textHeading.transform.parent);
                textFrame.Bind(textHeading, textFooter);
            }
            else if (textFrame != null)
            {
                textFrame.gameObject.SetActive(false);
            }
            terminal.RefreshContentLayout();
            return;
        }
        if (!useVisualLibrary)
            return;
        for (int i = 0; i < programUnits.Length; i++)
        {
            if (programUnits[i] == null)
                continue;
            bool available = i < catalog.Programs.Count
                && GameDataManager.Instance.IsToolProgramAvailable(catalog.Programs[i].Id);
            programUnits[i].gameObject.SetActive(available);
            if (available)
                programUnits[i].Bind(this, catalog.Programs[i], catalog.BatteryIcon);
        }
        terminal.RefreshContentLayout();
    }

    static void BindTextLine(OSToolLineUnit line, string text, bool richText = false)
    {
        if (line == null)
            return;
        line.BindText(text);
        if (line.Label != null)
            line.Label.richText = richText;
        OSLocalizedFontUtility.ApplyCurrent(line.Label);
    }

    public static string Localize(string key, string fallback)
    {
        return GameDataManager.Instance != null
            ? GameDataManager.Instance.GetLocalizedText(key, fallback) : fallback;
    }
}
