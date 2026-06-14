using Lapassay.Core.Models;

namespace Lapassay.Core.Reporting;

/// <summary>
/// Loads two saved run files and diffs them. Centralizes JSON loading through
/// <see cref="JsonReport.Deserialize"/> — the canonical options (camelCase +
/// <c>AllowNamedFloatingPointLiterals</c>) — so every compare path (GUI history,
/// GUI file-picker, battery⇄AC, CLI <c>compare</c>) agrees and none of them silently
/// rejects a run that legitimately serialized a <c>NaN</c>/<c>Infinity</c> value.
/// </summary>
public static class DiffService
{
    /// <summary>Load two run JSON files and compute their diff. Throws on unreadable/invalid JSON.</summary>
    public static RunComparison BuildDiff(string pathA, string pathB, string labelA, string labelB)
    {
        var runA = JsonReport.Deserialize(File.ReadAllText(pathA));
        var runB = JsonReport.Deserialize(File.ReadAllText(pathB));
        return Compare.Diff(runA, runB, labelA, labelB);
    }

    /// <summary>Render a comparison to a self-contained HTML file; returns its full path.</summary>
    public static string WriteHtml(RunComparison cmp, string outPath)
    {
        HtmlReport.WriteToFile(cmp, outPath);
        return Path.GetFullPath(outPath);
    }
}
