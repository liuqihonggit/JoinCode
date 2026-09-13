
namespace Core.Context.Compact;

/// <summary>
/// 压缩阈值配置 — 控制自动压缩、警告和兜底的各类阈值
/// </summary>
[RegisterOptions]
public sealed partial class CompactThresholds : ServiceEntity
{
    /// <summary>自动压缩缓冲 token 数</summary>
    public int AutoCompactBufferTokens { get; init; } = 13_000;
    /// <summary>警告缓冲 token 数</summary>
    public int WarningBufferTokens { get; init; } = 20_000;
    /// <summary>错误缓冲 token 数</summary>
    public int ErrorBufferTokens { get; init; } = 20_000;
    /// <summary>手动压缩缓冲 token 数</summary>
    public int ManualCompactBufferTokens { get; init; } = 3_000;
    /// <summary>连续自动压缩失败上限</summary>
    public int MaxConsecutiveAutoCompactFailures { get; init; } = 3;
    /// <summary>摘要输出的最大 token 数</summary>
    public int MaxOutputTokensForSummary { get; init; } = 20_000;
    /// <summary>压缩后恢复的最大文件数</summary>
    public int PostCompactMaxFilesToRestore { get; init; } = 5;
    /// <summary>压缩后 token 预算</summary>
    public int PostCompactTokenBudget { get; init; } = 50_000;
    /// <summary>压缩后单文件最大 token 数</summary>
    public int PostCompactMaxTokensPerFile { get; init; } = 5_000;
    /// <summary>软压缩占比阈值</summary>
    public double SoftCompactRatio { get; init; } = 0.5;

    /// <summary>默认配置实例</summary>
    public static CompactThresholds Default { get; } = new();
}
