namespace MockServer.Core;

/// <summary>
/// MockServer 配置文件模型 — 通过 JSON 文件配置端口和预设响应脚本
/// </summary>
public sealed class MockServerConfig : MockServerConfigBase<MockServerConfig>
{
    /// <summary>
    /// 预设响应脚本序列 — 按请求顺序返回，支持工具调用
    /// </summary>
    public List<ScriptedTurn> ScriptedTurns { get; set; } = [];

    /// <summary>
    /// 默认文本响应（脚本耗尽时使用）
    /// </summary>
    public string DefaultResponse { get; set; } = "Mock response (script exhausted).";

    protected override JsonTypeInfo<MockServerConfig> JsonTypeInfo => MockServerJsonContext.Default.MockServerConfig;
    protected override string LogPrefix => "[MockServer]";
    protected override string ConfigNotFoundMessage => "MockServer 配置文件不存在: {0}";

    /// <summary>从 JSON 文件加载配置</summary>
    public static MockServerConfig LoadFromFile(string path)
        => LoadFromFile(path, MockServerJsonContext.Default.MockServerConfig, "MockServer 配置文件不存在: {0}");

    /// <summary>从 JSON 文件加载配置 — 文件不存在时返回默认配置</summary>
    public static MockServerConfig LoadFromFileOrDefault(string path)
        => LoadFromFileOrDefault(path, MockServerJsonContext.Default.MockServerConfig, "[MockServer]", "MockServer 配置文件不存在: {0}");
}

/// <summary>
/// 预设响应轮次 — 对应一次 LLM 请求的响应
/// </summary>
public sealed class ScriptedTurn
{
    /// <summary>
    /// 文本响应（与 ToolCalls 互斥，ToolCalls 优先）
    /// </summary>
    public string? TextResponse { get; set; }

    /// <summary>
    /// 工具调用列表（如 read 工具）— 非空时优先返回工具调用
    /// </summary>
    public List<ToolCallConfig>? ToolCalls { get; set; }

    /// <summary>
    /// 思考内容（DeepSeek reasoning_content / Anthropic thinking）
    /// </summary>
    public string? ThinkingContent { get; set; }

    /// <summary>
    /// 工具调用后的跟进文本（工具调用返回后，下一轮的文本响应）
    /// </summary>
    public string? FollowUpText { get; set; }

    /// <summary>
    /// HTTP 状态码覆盖 — 非空时返回指定错误码（如 429/500/503），用于测试错误恢复
    /// </summary>
    public int? HttpStatusCode { get; set; }
}

/// <summary>
/// 工具调用配置 — 模拟 LLM 返回的工具调用请求
/// </summary>
public sealed class ToolCallConfig
{
    /// <summary>
    /// 工具名称（如 "read"、"write"）
    /// </summary>
    public string ToolName { get; set; } = "";

    /// <summary>
    /// 工具参数（JSON 字符串，如 {"file_path":"README.md"}）
    /// </summary>
    public string Arguments { get; set; } = "{}";

    /// <summary>
    /// 工具调用 ID（可选，不指定则自动生成）
    /// </summary>
    public string? ToolCallId { get; set; }
}
