using Lapassay.Core.Models;

namespace Lapassay.Core;

/// <summary>
/// Turns a <see cref="RunContext"/> into a human-readable confidence verdict, shown next
/// to the score in the GUI, the HTML hero, and the CLI summary. One implementation so all
/// three surfaces agree. The level is driven by the two factors that actually move score
/// variance: GPU clock locking (Developer Mode) and repeat count. Admin and power source
/// are disclosed as context but don't change the level (admin only affects telemetry;
/// battery changes the *operating point*, not the measurement quality).
/// </summary>
public static class RunConfidence
{
    public static (string Level, string Detail) Assess(RunContext? context)
    {
        if (context is null)
            return ("UNKNOWN", "run conditions not recorded");

        var level = context switch
        {
            { DeveloperMode: true, RepeatCount: >= 3 } => "HIGH",
            { DeveloperMode: true } or { RepeatCount: >= 3 } => "MEDIUM",
            _ => "LOW",
        };

        var factors = new List<string>
        {
            context.DeveloperMode ? "GPU clocks locked" : "GPU clocks unlocked",
            $"N={context.RepeatCount}",
            context.OnBattery ? "on battery" : "on AC",
        };
        if (!context.IsAdmin)
            factors.Add("no power telemetry (not admin)");

        return (level, string.Join(" · ", factors));
    }
}
