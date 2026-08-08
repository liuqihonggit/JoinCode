
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
    /// 默认超时时间（秒，默认 120）
    /// </summary>
    [Range(1, 3600, ErrorMessage = "DefaultTimeoutSeconds 必须在 1 秒到 1 小时之间")]
    public int DefaultTimeoutSeconds { get; set; } = 120;

    /// <summary>
    /// 搜索类命令默认超时时间（秒，默认 30）
    /// 适用于 rg/grep/find/ag 等搜索命令，防止搜索范围过大时长时间卡顿
    /// </summary>
    [Range(5, 300, ErrorMessage = "SearchCommandTimeoutSeconds 必须在 5 秒到 5 分钟之间")]
    public int SearchCommandTimeoutSeconds { get; set; } = 30;

    /// <summary>
    /// 绝对超时上限（秒，默认 120）— OneShotCommandGroup 工具的硬性超时上限
    /// 0 = 禁用绝对超时（回退到原有行为）
    /// 可通过环境变量 JCC_ABSOLUTE_TIMEOUT_SECONDS 覆盖
    /// </summary>
    [Range(0, 3600, ErrorMessage = "AbsoluteTimeoutSeconds 必须在 0 秒到 1 小时之间")]
    public int AbsoluteTimeoutSeconds { get; set; } = 120;

    /// <summary>
    /// 续期默认超时（秒，默认 600=10分钟）— resume_timed_out_task 的默认超时
    /// 可通过环境变量 JCC_RESUME_TIMEOUT_SECONDS 覆盖
    /// </summary>
    [Range(60, 3600, ErrorMessage = "ResumeTimeoutSeconds 必须在 1 分钟到 1 小时之间")]
    public int ResumeTimeoutSeconds { get; set; } = 600;

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
