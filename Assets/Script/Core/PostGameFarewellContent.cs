using System;
using System.Linq;

public static class PostGameFarewellContent
{
    public const string Prefix = "postgame.farewell.";
    public const string Cat = " /\\_/\\\n( o.- )\n / > <3";
    public const string WindowTitle = "Live";
    public static string Text(string suffix) => GameDataManager.Instance?.GetLocalizedText(Prefix + suffix) ?? Prefix + suffix;
    public static string FileName => Text("filename");
    public static string Letter => Text("letter");
    public static string ReturnMessage => string.Join("\n\n", Enumerable.Range(1, 7).Select(i => Text("return.line_" + i.ToString("00"))));

    public static bool IsReady()
    {
        var data = GameDataManager.Instance;
        if (data?.LocalizationData == null) return false;
        string[] suffixes = { "filename", "letter", "ui.close", "ui.attachment", "notification.body" };
        return suffixes.Concat(Enumerable.Range(1, 7).Select(i => "return.line_" + i.ToString("00")))
            .All(suffix => data.LocalizationData.Entries.Any(entry => entry.Key == Prefix + suffix
                && entry.Values.Any(value => value.Language == data.CurrentLanguageState && !string.IsNullOrWhiteSpace(value.Text))))
            && Letter.Contains(Cat);
    }
}
