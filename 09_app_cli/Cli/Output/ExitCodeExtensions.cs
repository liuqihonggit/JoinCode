namespace JoinCode.Cli.Output;

/// <summary>
/// ExitCode 扩展方法 — 细粒度退出码到逻辑分组的映射
/// </summary>
public static class ExitCodeExtensions
{
    /// <summary>
    /// 将细粒度 ExitCode 映射到 CliErrorCategory 逻辑分组
    /// 对齐架构指南5类退出码语义，同时保留现有细粒度值
    /// </summary>
    public static CliErrorCategory ToCategory(this ExitCode exitCode) => exitCode switch
    {
        ExitCode.Success => CliErrorCategory.Success,

        // 1: 参数错误
        ExitCode.ArgumentParseError => CliErrorCategory.ArgumentError,

        // 2: 认证失败
        ExitCode.ConfigurationError => CliErrorCategory.AuthError,
        ExitCode.ApiKeyMissing => CliErrorCategory.AuthError,

        // 3: 资源未找到
        ExitCode.SessionResumeFailed => CliErrorCategory.NotFound,

        // 4: 临时失败（可重试）
        ExitCode.LlmCallFailed => CliErrorCategory.Transient,
        ExitCode.LlmCallTimeout => CliErrorCategory.Transient,
        ExitCode.McpConnectionFailed => CliErrorCategory.Transient,
        ExitCode.McpConnectionTimeout => CliErrorCategory.Transient,
        ExitCode.ToolExecutionTimeout => CliErrorCategory.Transient,
        ExitCode.SubprocessTimeout => CliErrorCategory.Transient,
        ExitCode.StreamResponseTimeout => CliErrorCategory.Transient,

        // 5: 冲突（不可重试）
        ExitCode.GeneralError => CliErrorCategory.Conflict,
        ExitCode.ToolExecutionFailed => CliErrorCategory.Conflict,
        ExitCode.SubprocessCrashed => CliErrorCategory.Conflict,
        ExitCode.AwaitTimeout => CliErrorCategory.Conflict,

        // 信号终止
        ExitCode.Interrupted => CliErrorCategory.Success,

        _ => CliErrorCategory.Conflict,
    };

    /// <summary>
    /// 获取退出码的人类可读描述
    /// </summary>
    public static string ToFriendlyString(this ExitCode exitCode) => exitCode switch
    {
        ExitCode.Success => "成功",
        ExitCode.GeneralError => "通用错误",
        ExitCode.ConfigurationError => "配置错误",
        ExitCode.ArgumentParseError => "参数错误",
        ExitCode.ApiKeyMissing => "API Key 缺失",
        ExitCode.SessionResumeFailed => "会话恢复失败",
        ExitCode.LlmCallFailed => "LLM 调用失败",
        ExitCode.ToolExecutionFailed => "工具执行失败",
        ExitCode.McpConnectionFailed => "MCP 连接失败",
        ExitCode.SubprocessCrashed => "子进程崩溃",
        ExitCode.AwaitTimeout => "超时强制退出",
        ExitCode.LlmCallTimeout => "LLM 调用超时",
        ExitCode.ToolExecutionTimeout => "工具执行超时",
        ExitCode.McpConnectionTimeout => "MCP 连接超时",
        ExitCode.SubprocessTimeout => "子进程超时",
        ExitCode.StreamResponseTimeout => "流式响应超时",
        ExitCode.Interrupted => "用户中断",
        _ => $"未知错误 ({(int)exitCode})",
    };
}
