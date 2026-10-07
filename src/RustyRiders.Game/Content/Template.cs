using System.Globalization;
using System.Text.RegularExpressions;

namespace RustyRiders.Game.Content;

/// <summary>Authored text with <c>{name}</c> placeholders, filled from definition values.</summary>
internal static partial class Template
{
    internal static string Fill(string text, params (string Name, object Value)[] values)
    {
        foreach (var (name, value) in values)
            text = text.Replace("{" + name + "}", Format(value), StringComparison.Ordinal);
        return text;
    }

    /// <summary>Rejects a template that names a placeholder its caller never fills.</summary>
    internal static void Check(string path, string field, string text, params string[] allowed)
    {
        foreach (Match match in Placeholder().Matches(text))
            Authored.Require(allowed.Contains(match.Groups[1].Value), path, field,
                $"unknown placeholder '{match.Value}'; this text can use {(allowed.Length == 0 ? "none" : string.Join(", ", allowed.Select(a => "{" + a + "}")))}.");
    }

    /// <summary>Rejects any placeholder in fields that are shown exactly as written.</summary>
    internal static void Plain(string path, params (string Field, string Text)[] fields)
    {
        foreach (var (field, text) in fields) Check(path, field, text);
    }

    private static string Format(object value) => value switch
    {
        float number => number.ToString("0.##", CultureInfo.InvariantCulture),
        double number => number.ToString("0.##", CultureInfo.InvariantCulture),
        IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString() ?? ""
    };

    [GeneratedRegex(@"\{([A-Za-z]+)\}")]
    private static partial Regex Placeholder();
}
