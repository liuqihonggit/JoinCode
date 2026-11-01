namespace JoinCode.Cli.Output;

/// <summary>
/// ExitCode 扩展方法 — 细粒度退出码到逻辑分组的映射
/// </summary>
public static class ExitCodeExtensions {
    /// <summary>
    /// 将细粒度 ExitCode 映射到 CliErrorCategory 逻辑分组
    /// 对齐架构指南5类退出码语义，同时保留现有细粒度值
    /// </summary>
    public static CliErrorCategory ToCategory(this ExitCode exitCode) => exitCode switch {
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
    /// 将 ExitCode 映射为结构化错误 — 用于管道内错误（exitCode!=0 但管道完成）时统一 JSON 信封的 error 字段
    /// <para>仅映射非零退出码；Success 映射为 null（无错误）</para>
    /// </summary>
    public static CliStructuredError? ToStructuredError(this ExitCode exitCode) => exitCode switch {
        ExitCode.Success => null,
        ExitCode.GeneralError => new("RUNTIME_ERROR", "通用错误", "请检查错误日志获取详细信息", retryable: false),
        ExitCode.ConfigurationError => CliErrorCatalog.ConfigInvalidValue("configuration", "无效", "请检查配置文件或环境变量"),
        ExitCode.ArgumentParseError => CliErrorCatalog.ArgParseError("参数解析失败"),
        ExitCode.ApiKeyMissing => CliErrorCatalog.AuthApiKeyMissing(),
        ExitCode.SessionResumeFailed => new("RESOURCE_SESSION_NOT_FOUND", "会话恢复失败", "请使用 /history 查看可用会话列表", retryable: false),
        ExitCode.LlmCallFailed => new("LLM_CALL_FAILED", "LLM 调用失败", "请检查模型配置和网络连接", retryable: true),
        ExitCode.ToolExecutionFailed => CliErrorCatalog.ToolExecutionFailed("(unknown)"),
        ExitCode.McpConnectionFailed => CliErrorCatalog.ToolMcpConnectionFailed("(unknown)"),
        ExitCode.SubprocessCrashed => new("SUBPROCESS_CRASHED", "子进程异常退出", "请检查子进程配置", retryable: false),
        ExitCode.AwaitTimeout => CliErrorCatalog.TimeoutAwaitDiagnostic(0),
        ExitCode.LlmCallTimeout => CliErrorCatalog.NetTimeout("LLM 调用"),
        ExitCode.ToolExecutionTimeout => CliErrorCatalog.NetTimeout("工具执行"),
        ExitCode.McpConnectionTimeout => CliErrorCatalog.NetTimeout("MCP 连接"),
        ExitCode.SubprocessTimeout => CliErrorCatalog.NetTimeout("子进程"),
        ExitCode.StreamResponseTimeout => CliErrorCatalog.NetTimeout("流式响应"),
        ExitCode.Interrupted => new("INTERRUPTED", "用户中断", null, retryable: false),
        _ => new("UNKNOWN_ERROR", $"未知错误 ({(int)exitCode})", null, retryable: false),
    };

    /// <summary>
    /// 获取退出码的人类可读描述
    /// </summary>
    public static string ToFriendlyString(this ExitCode exitCode) => exitCode switch {
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