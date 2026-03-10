using System.Text;
using FlaUI.Core.AutomationElements;

namespace WinAppMCP.Models;

/// <summary>
/// Result of ranked element resolution. Contains the best-matching element
/// and, when multiple candidates matched, a list of all matches for diagnostics.
/// </summary>
public sealed class ResolveResult
{
    public AutomationElement? Element { get; init; }
    public IReadOnlyList<ScoredMatch>? AmbiguousMatches { get; init; }

    public bool IsAmbiguous => AmbiguousMatches is { Count: > 1 };

    public static ResolveResult Single(AutomationElement? element) =>
        new() { Element = element };

    public static ResolveResult Ranked(AutomationElement? best, IReadOnlyList<ScoredMatch> allMatches) =>
        new() { Element = best, AmbiguousMatches = allMatches };

    /// <summary>
    /// Formats a diagnostic warning listing all matched elements with scores and structural context.
    /// </summary>
    public string FormatAmbiguityWarning()
    {
        if (!IsAmbiguous) return string.Empty;

        var sb = new StringBuilder();
        sb.Append($"\n⚠ Ambiguity: {AmbiguousMatches!.Count} elements matched.");

        var selected = AmbiguousMatches[0];
        sb.Append($" Selected: {selected.Info.ToCompactString()} (score {selected.Score}, depth {selected.Depth}).");

        sb.Append(" Other matches:");
        for (int i = 1; i < AmbiguousMatches.Count; i++)
        {
            var m = AmbiguousMatches[i];
            var parentDesc = string.IsNullOrEmpty(m.ParentDescription) ? "" : $", parent: {m.ParentDescription}";
            sb.Append($"\n  - {m.Info.ToCompactString()} (score {m.Score}, depth {m.Depth}{parentDesc})");
        }
        return sb.ToString();
    }
}

/// <summary>
/// An element match with its computed ranking score and structural context.
/// </summary>
public sealed class ScoredMatch
{
    public required AutomationElement Element { get; init; }
    public required ElementInfo Info { get; init; }
    public required int Score { get; init; }
    public required int Depth { get; init; }
    public string ParentDescription { get; init; } = string.Empty;
}
