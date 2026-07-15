
namespace JoinCode.Abstractions.Configuration.Execution;

/// <summary>
/// Shell 执行配置
/// </summary>
/// <remarks>
/// 手动注册（有自定义验证逻辑），不使用 [RegisterOptions]
/// </remarks>
public sealed class ShellExecutionConfig
{
    /// <summary>
    /// 最大输出字节数（默认 30KB）— 对齐 TS BASH_MAX_OUTPUT_DEFAULT (30000)
    /// </summary>
    [Range(1024, 1024 * 1024, ErrorMessage = "MaxOutputBytes 必须在 1KB 到 1MB 之间")]
    public int MaxOutputBytes { get; set; } = 30_000;

    /// <summary>
    /// 默认超时时间（秒，默认 30）
    /// </summary>
    [Range(1, 3600, ErrorMessage = "DefaultTimeoutSeconds 必须在 1 秒到 1 小时之间")]
    public int DefaultTimeoutSeconds { get; set; } = 120;

    /// <summary>
    /// 是否启用命令执行日志
    /// </summary>
    public bool EnableExecutionLogging { get; set; } = true;

    /// <summary>
    /// 危险命令列表
    /// </summary>
    public IReadOnlyList<string> DangerousCommands { get; set; } = new[]
    {
        "rm -rf /",
        "format",
        "dd if=",
        "mkfs",
        "fdisk"
    };
}
