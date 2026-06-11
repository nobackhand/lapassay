namespace Lapassay.Core;

/// <summary>
/// The one place the tool version and schema versions live. Previously the version
/// literal was duplicated in five files and had already drifted (sustained runs
/// stamped 0.4.0 while everything else said 0.6.0).
/// </summary>
public static class LapassayVersion
{
    public const string Value = "0.6.0";

    /// <summary>Single-run JSON schema. 1.1: additive `repeats`. 1.2: fp16 → fp16alu id,
    /// additive `adapter` on GPU results. Additive changes only — old readers keep working.</summary>
    public const string SingleRunSchema = "1.2";

    /// <summary>Sustained-run JSON schema. Unchanged since introduction.</summary>
    public const string SustainedSchema = "1.0";
}
