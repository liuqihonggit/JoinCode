
namespace Core.Context.Compact;

[RegisterOptions]
public sealed partial class CompactThresholds
{
    public int AutoCompactBufferTokens { get; init; } = 13_000;
    public int WarningBufferTokens { get; init; } = 20_000;
    public int ErrorBufferTokens { get; init; } = 20_000;
    public int ManualCompactBufferTokens { get; init; } = 3_000;
    public int MaxConsecutiveAutoCompactFailures { get; init; } = 3;
    public int MaxOutputTokensForSummary { get; init; } = 20_000;
    public int PostCompactMaxFilesToRestore { get; init; } = 5;
    public int PostCompactTokenBudget { get; init; } = 50_000;
    public int PostCompactMaxTokensPerFile { get; init; } = 5_000;
    public double SoftCompactRatio { get; init; } = 0.5;

    public static CompactThresholds Default { get; } = new();
}
