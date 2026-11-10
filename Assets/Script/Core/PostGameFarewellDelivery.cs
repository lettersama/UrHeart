using System;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

/// <summary>
/// Keeps one durable request per save. A short-lived launcher asks Windows WMI
/// to start the delivery worker outside Steam's tracked game process tree.
/// Reopening may resume an interrupted request; the worker owns deduplication.
/// </summary>
public static class PostGameFarewellDelivery
{
    public const int DeliveryDelaySeconds = 300;
    const string HelperResource = "PostGameFarewell/WindowsDelivery.ps1";
    static readonly UTF8Encoding Utf8 = new UTF8Encoding(true);

    public static string StateDirectory { get { return Path.Combine(Application.persistentDataPath, "PostGameFarewell"); } }

    public static string GetSaveStateDirectory(string saveId)
        => GetSaveStateDirectory(StateDirectory, saveId);

    public static string GetSaveStateDirectory(string root, string saveId)
    {
        // Existing saves retain the original ledger, including pending attempts.
        // Never generate an ID while loading, or a restart could resend the gift.
        if (string.IsNullOrEmpty(saveId)) return Path.GetFullPath(root);
        Guid parsed;
        if (saveId.Length != 32 || !Guid.TryParseExact(saveId, "N", out parsed))
            throw new ArgumentException("Invalid farewell save identity.", "saveId");
        return Path.GetFullPath(Path.Combine(root, "saves", parsed.ToString("N")));
    }

    [Serializable]
    sealed class Notification
    {
        public string title;
        public string body;
    }

    [Serializable]
    sealed class Request
    {
        public int schemaVersion = 2;
        public string installationToken;
        public string requestId;
        public int parentPid;
        // A string preserves all 64 bits across JSON readers, including PowerShell.
        public string parentStartTimeUtcTicks;
        public string createdUtc;
        public int delaySeconds = DeliveryDelaySeconds;
        public string language;
        public string suggestedFileName;
        public string letterText;
        public Notification notification;
        public string mode = "production";
        public bool disableToast;
        public string testDesktopDirectory = "";
        public string testIsolationToken = "";
    }

    [Serializable]
    sealed class LaunchFailure
    {
        public string status = "launch_failed";
        public string utc;
        public string detail;
    }

    /// <summary>
    /// Call once per run after a committed completion reaches the menu. True means
    /// delivery finished previously or the short launcher started, not that the
    /// desktop file/notification is delivered. Pending requests keep their identity.
    /// </summary>
    public static bool TrySchedule(string saveId, string language, string suggestedFileName, string letterText,
        string notificationTitle, string notificationBody, out string detail)
    {
#if !UNITY_STANDALONE_WIN || UNITY_EDITOR
        detail = "External farewell delivery is disabled outside a Windows player build.";
        return false;
#else
        if (Application.isEditor || Application.platform != RuntimePlatform.WindowsPlayer)
        {
            detail = "External farewell delivery is disabled in the Editor.";
            return false;
        }
        try
        {
            return TryScheduleWindows(GetSaveStateDirectory(saveId),
                CreateRequest(language, suggestedFileName, letterText, notificationTitle, notificationBody), out detail);
        }
        catch (Exception error) { detail = error.Message; return false; }
#endif
    }

    static Request CreateRequest(string language, string fileName, string letter, string title, string body)
    {
        using (var parent = Process.GetCurrentProcess())
            return new Request {
                requestId = Guid.NewGuid().ToString("N"), parentPid = parent.Id,
                parentStartTimeUtcTicks = parent.StartTime.ToUniversalTime().Ticks.ToString(System.Globalization.CultureInfo.InvariantCulture),
                createdUtc = DateTime.UtcNow.ToString("O"), language = language ?? "",
                suggestedFileName = SafeFileName(fileName), letterText = letter,
                notification = new Notification { title = title ?? "", body = body ?? "" }
            };
    }

    static bool TryScheduleWindows(string stateDirectory, Request proposed, out string detail)
    {
        if (string.IsNullOrWhiteSpace(proposed.letterText))
        {
            detail = "The farewell letter is empty.";
            return false;
        }
        try
        {
            string requestPath = Path.Combine(stateDirectory, "request.json");
            string receiptPath = Path.Combine(stateDirectory, "receipt.json");
            Directory.CreateDirectory(stateDirectory);
            using (var scheduleLock = new FileStream(Path.Combine(stateDirectory, "schedule.lock"),
                FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
            {
                if (File.Exists(receiptPath))
                {
                    detail = "Farewell delivery already has a final receipt; no helper was started.";
                    return true;
                }

                string tokenPath = Path.Combine(stateDirectory, "installation-token.txt");
                string token;
                if (File.Exists(tokenPath))
                {
                    token = File.ReadAllText(tokenPath, Utf8).Trim();
                    Guid parsed;
                    if (!Guid.TryParseExact(token, "N", out parsed))
                        throw new InvalidDataException("Invalid persistent farewell installation token.");
                }
                else
                {
                    token = Guid.NewGuid().ToString("N");
                    WriteNewAtomic(tokenPath, token);
                }

                Request request = proposed;
                if (File.Exists(requestPath))
                {
                    request = JsonUtility.FromJson<Request>(File.ReadAllText(requestPath, Utf8));
                    Guid requestId;
                    if (request == null || (request.schemaVersion != 1 && request.schemaVersion != 2)
                        || request.installationToken != token || request.mode != proposed.mode
                        || !Guid.TryParseExact(request.requestId, "N", out requestId))
                        throw new InvalidDataException("Invalid persistent farewell request; it was not replaced.");
                }
                else
                {
                    request.installationToken = token;
                    WriteNewAtomic(requestPath, JsonUtility.ToJson(request, true));
                }

                var resource = Resources.Load<TextAsset>(HelperResource);
                if (resource == null || string.IsNullOrWhiteSpace(resource.text))
                    throw new FileNotFoundException("Bundled Windows farewell helper is missing.");
                // Do not replace a script that an older running worker might still
                // be reading. A content-addressed file also permits safe upgrades.
                string helperHash;
                using (var sha = SHA256.Create())
                    helperHash = BitConverter.ToString(sha.ComputeHash(Utf8.GetBytes(resource.text))).Replace("-", "").ToLowerInvariant();
                string helperPath = Path.Combine(stateDirectory, "WindowsDelivery-" + helperHash + ".ps1");
                if (!File.Exists(helperPath)) WriteNewAtomic(helperPath, resource.text);
                else if (!string.Equals(File.ReadAllText(helperPath, Utf8), resource.text, StringComparison.Ordinal))
                    throw new InvalidDataException("Stored farewell helper differs from the bundled helper.");
                string powershellPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),
                    @"WindowsPowerShell\v1.0\powershell.exe");
                if (!File.Exists(powershellPath)) throw new FileNotFoundException("Windows PowerShell is unavailable.", powershellPath);
                var start = new ProcessStartInfo {
                    FileName = powershellPath,
                    // Only file paths are arguments; all letter/notification text is
                    // UTF-8 JSON data and is never evaluated as a shell command.
                    Arguments = "-NoLogo -NoProfile -NonInteractive -Sta -ExecutionPolicy RemoteSigned -WindowStyle Hidden -File "
                        + QuoteArgument(helperPath) + " -RequestPath " + QuoteArgument(requestPath)
                        + " -LaunchDetached" + (request.mode == "test" ? " -TestMode" : ""),
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WindowStyle = ProcessWindowStyle.Hidden,
                    WorkingDirectory = stateDirectory
                };
                // The WMI worker also gets a sanitized environment. Clear these
                // here so the short launcher cannot register another Steam session.
                foreach (string name in new System.Collections.Generic.List<string>(start.Environment.Keys))
                    if (name.StartsWith("Steam", StringComparison.OrdinalIgnoreCase)
                        || name.StartsWith("GAMEOVERLAY", StringComparison.OrdinalIgnoreCase))
                        start.Environment.Remove(name);
                using (var child = Process.Start(start))
                {
                    if (child == null) throw new InvalidOperationException("Windows did not start the farewell helper.");
                    detail = "Farewell launcher started; Windows will own the detached five-minute delivery worker.";
                    return true;
                }
            }
        }
        catch (Exception error)
        {
            detail = error.GetType().Name + ": " + error.Message;
            try
            {
                if (stateDirectory == null) return false;
                Directory.CreateDirectory(stateDirectory);
                WriteNewAtomic(Path.Combine(stateDirectory, "launch-failure-" + Guid.NewGuid().ToString("N") + ".json"),
                    JsonUtility.ToJson(new LaunchFailure { utc = DateTime.UtcNow.ToString("O"), detail = detail }, true));
            }
            catch (Exception) { /* The caller still receives the launch error if storage itself is unavailable. */ }
            return false;
        }
    }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
    // Explicitly isolated entry for process/Steam-exit QA. Never targets Desktop.
    public static bool TryScheduleIsolatedForTests(string directory, int delaySeconds, out string detail, int parentPid = 0)
    {
        string testRoot = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Temp", "PostGameFarewellQA"))
            + Path.DirectorySeparatorChar;
        directory = Path.GetFullPath(directory);
        if (!directory.StartsWith(testRoot, StringComparison.OrdinalIgnoreCase) || delaySeconds < 0 || delaySeconds > 10)
            throw new ArgumentException("An isolated PostGameFarewellQA directory and 0..10 second delay are required.");
        Directory.CreateDirectory(directory);
        string marker = Path.Combine(directory, ".postgame-farewell-test");
        if (!File.Exists(marker)) WriteNewAtomic(marker, Guid.NewGuid().ToString("N"));
        var request = CreateRequest("en", "QA Letter.txt", "Isolated farewell delivery test.", "QA", "QA");
        if (parentPid > 0)
            using (var parent = Process.GetProcessById(parentPid))
            {
                request.parentPid = parent.Id;
                request.parentStartTimeUtcTicks = parent.StartTime.ToUniversalTime().Ticks.ToString(System.Globalization.CultureInfo.InvariantCulture);
            }
        request.mode = "test"; request.disableToast = true; request.delaySeconds = delaySeconds;
        request.testDesktopDirectory = Path.Combine(directory, "desktop");
        request.testIsolationToken = File.ReadAllText(marker, Utf8).Trim();
        return TryScheduleWindows(directory, request, out detail);
    }
#endif

    static string SafeFileName(string requested)
    {
        string name = string.IsNullOrWhiteSpace(requested) ? "Live - Letter" : requested.Trim();
        foreach (char bad in Path.GetInvalidFileNameChars()) name = name.Replace(bad, '_');
        name = name.TrimEnd(' ', '.');
        if (name.EndsWith(".txt", StringComparison.OrdinalIgnoreCase)) name = name.Substring(0, name.Length - 4);
        if (name.Length > 80)
        {
            name = name.Substring(0, 80);
            if (char.IsHighSurrogate(name[name.Length - 1])) name = name.Substring(0, name.Length - 1);
        }
        name = name.TrimEnd(' ', '.');
        if (name.Length == 0) name = "Live - Letter";
        string first = name.Split('.')[0].ToUpperInvariant();
        if (first == "CON" || first == "PRN" || first == "AUX" || first == "NUL"
            || (first.Length == 4 && (first.StartsWith("COM", StringComparison.Ordinal) || first.StartsWith("LPT", StringComparison.Ordinal))
                && first[3] >= '0' && first[3] <= '9')) name = "Live - " + name;
        return name + ".txt";
    }

    // Windows command-line quoting for ProcessStartInfo.Arguments, not PowerShell code quoting.
    static string QuoteArgument(string value)
    {
        var quoted = new StringBuilder("\"");
        int slashes = 0;
        foreach (char c in value)
        {
            if (c == '\\') { slashes++; continue; }
            if (c == '"') { quoted.Append('\\', slashes * 2 + 1); quoted.Append(c); }
            else { quoted.Append('\\', slashes); quoted.Append(c); }
            slashes = 0;
        }
        quoted.Append('\\', slashes * 2); quoted.Append('"');
        return quoted.ToString();
    }

    static void WriteNewAtomic(string path, string text)
    {
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            using (var writer = new StreamWriter(file, Utf8))
            {
                writer.Write(text); writer.Flush(); file.Flush(true);
            }
            File.Move(temporary, path); // Never replaces an existing request, token, helper or receipt.
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
