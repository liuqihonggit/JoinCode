namespace JoinCode.Cli.Output;

/// <summary>
/// CLI 错误码注册表 — 集中定义所有结构化错误码
/// 命名规范: CATEGORY_SPECIFIC_ERROR（大写+下划线）
/// </summary>
public static class CliErrorCatalog
{
    // ── 认证类 (AUTH_) ──

    /// <summary>API Key 缺失</summary>
    public static CliStructuredError AuthApiKeyMissing(string? provider = null) =>
        new("AUTH_API_KEY_MISSING",
            $"API Key 缺失{(provider is not null ? $"（供应商: {provider}）" : "")}",
            "请设置环境变量 JCC_API_KEY 或在 ~/.jcc/auth.json 中配置",
            retryable: false);

    /// <summary>API Key 无效或过期</summary>
    public static CliStructuredError AuthApiKeyInvalid(string provider) =>
        new("AUTH_API_KEY_INVALID",
            $"API Key 无效或已过期（供应商: {provider}）",
            "请检查 API Key 是否正确，或重新生成",
            retryable: false);

    /// <summary>OAuth Token 过期</summary>
    public static CliStructuredError AuthTokenExpired() =>
        new("AUTH_TOKEN_EXPIRED",
            "OAuth Token 已过期",
            "请运行 /login 重新认证",
            retryable: false);

    // ── 配置类 (CONFIG_) ──

    /// <summary>配置文件缺失</summary>
    public static CliStructuredError ConfigFileMissing(string path) =>
        new("CONFIG_FILE_MISSING",
            $"配置文件不存在: {path}",
            "请运行 jcc 首次启动自动生成，或手动创建配置文件",
            retryable: false);

    /// <summary>配置项无效</summary>
    public static CliStructuredError ConfigInvalidValue(string key, string value, string? expected = null) =>
        new("CONFIG_INVALID_VALUE",
            $"配置项 '{key}' 的值无效: {value}",
            expected is not null ? $"期望值: {expected}" : "请检查配置文件或环境变量",
            retryable: false);

    /// <summary>模型不可用</summary>
    public static CliStructuredError ConfigModelUnavailable(string modelId) =>
        new("CONFIG_MODEL_UNAVAILABLE",
            $"模型 '{modelId}' 不可用",
            "请检查模型 ID 是否正确，或在 settings.json 中配置可用模型",
            retryable: false);

    // ── 网络类 (NET_) ──

    /// <summary>API 端点不可达</summary>
    public static CliStructuredError NetEndpointUnreachable(string endpoint) =>
        new("NET_ENDPOINT_UNREACHABLE",
            $"API 端点不可达: {endpoint}",
            "请检查网络连接和端点 URL 是否正确",
            retryable: true);

    /// <summary>请求超时</summary>
    public static CliStructuredError NetTimeout(string? detail = null) =>
        new("NET_TIMEOUT",
            $"请求超时{(detail is not null ? $": {detail}" : "")}",
            "请检查网络连接，或增加 JCC_API_TIMEOUT_MS 环境变量的值",
            retryable: true);

    /// <summary>速率限制</summary>
    public static CliStructuredError NetRateLimited(int? retryAfterSeconds = null) =>
        new("NET_RATE_LIMITED",
            "API 请求被速率限制",
            retryAfterSeconds is not null
                ? $"请等待 {retryAfterSeconds} 秒后重试"
                : "请稍后重试，或降低请求频率",
            retryable: true);

    // ── 资源类 (RESOURCE_) ──

    /// <summary>资源未找到</summary>
    public static CliStructuredError ResourceNotFound(string resourceType, string identifier) =>
        new("RESOURCE_NOT_FOUND",
            $"{resourceType} '{identifier}' 未找到",
            "请检查标识符是否正确",
            retryable: false);

    /// <summary>会话不存在</summary>
    public static CliStructuredError ResourceSessionNotFound(string sessionId) =>
        new("RESOURCE_SESSION_NOT_FOUND",
            $"会话 '{sessionId}' 不存在",
            "请使用 /history 查看可用会话列表",
            retryable: false);

    // ── 冲突类 (CONFLICT_) ──

    /// <summary>工作目录未信任</summary>
    public static CliStructuredError ConflictWorkspaceNotTrusted(string path) =>
        new("CONFLICT_WORKSPACE_NOT_TRUSTED",
            $"工作目录未被信任: {path}",
            "请使用 --trust 参数自动信任，或在交互模式下确认信任",
            retryable: false);

    /// <summary>操作被权限拒绝</summary>
    public static CliStructuredError ConflictPermissionDenied(string operation) =>
        new("CONFLICT_PERMISSION_DENIED",
            $"操作被权限拒绝: {operation}",
            "请使用 --permission-mode 调整权限模式，或使用 --allowed-tools 添加工具白名单",
            retryable: false);

    // ── 参数类 (ARG_) ──

    /// <summary>参数解析错误</summary>
    public static CliStructuredError ArgParseError(string detail) =>
        new("ARG_PARSE_ERROR",
            detail,
            "使用 --help 查看可用选项",
            retryable: false);

    /// <summary>缺少必需参数</summary>
    public static CliStructuredError ArgMissingRequired(string paramName) =>
        new("ARG_MISSING_REQUIRED",
            $"缺少必需参数: {paramName}",
            "使用 --help 查看参数说明",
            retryable: false);

    // ── 工具类 (TOOL_) ──

    /// <summary>工具执行失败</summary>
    public static CliStructuredError ToolExecutionFailed(string toolName, string? detail = null) =>
        new("TOOL_EXECUTION_FAILED",
            $"工具 '{toolName}' 执行失败{(detail is not null ? $": {detail}" : "")}",
            "请检查工具参数是否正确，或在交互模式下查看详细错误",
            retryable: false);

    /// <summary>MCP 连接失败</summary>
    public static CliStructuredError ToolMcpConnectionFailed(string serverName, string? detail = null) =>
        new("TOOL_MCP_CONNECTION_FAILED",
            $"MCP 服务器 '{serverName}' 连接失败{(detail is not null ? $": {detail}" : "")}",
            "请检查 MCP 服务器配置和运行状态",
            retryable: true);
}
