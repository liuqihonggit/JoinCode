namespace JoinCode.Abstractions.Tools;

/// <summary>
/// 工具模板 — 描述LLM可动态创建的工具定义
/// 模板存储在 ~/.jcc/tool-templates/ 目录下，每个模板一个 JSON 文件
/// </summary>
public sealed class ToolTemplate
{
    /// <summary>
    /// 模板标识（文件名，不含扩展名）
    /// </summary>
    public required string Id { get; init; }

    /// <summary>
    /// 工具名称
    /// </summary>
    public required string ToolName { get; init; }

    /// <summary>
    /// 工具描述 — LLM 看到的说明
    /// </summary>
    public required string Description { get; init; }

    /// <summary>
    /// 工具类型 — System/Mcp/OnError
    /// </summary>
    public ToolKind Kind { get; init; } = ToolKind.Mcp;

    /// <summary>
    /// 二级分组名
    /// </summary>
    public string? GroupName { get; init; }

    /// <summary>
    /// 工具参数定义 — JSON Schema 格式
    /// </summary>
    public required ToolTemplateParameter[] Parameters { get; init; }

    /// <summary>
    /// 执行类型 — shell/script/mcp_call
    /// </summary>
    public required ToolTemplateExecution Execution { get; init; }
}

/// <summary>
/// 工具模板参数定义
/// </summary>
public sealed class ToolTemplateParameter
{
    /// <summary>
    /// 参数名称
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// 参数描述
    /// </summary>
    public required string Description { get; init; }

    /// <summary>
    /// 参数类型 — string/number/boolean/array/object
    /// </summary>
    public string Type { get; init; } = "string";

    /// <summary>
    /// 是否必填
    /// </summary>
    public bool Required { get; init; } = true;

    /// <summary>
    /// 默认值（JSON 字符串）
    /// </summary>
    public string? DefaultValue { get; init; }

    /// <summary>
    /// 枚举值列表（可选）
    /// </summary>
    public string[]? EnumValues { get; init; }
}

/// <summary>
/// 工具模板执行定义
/// </summary>
public sealed class ToolTemplateExecution
{
    /// <summary>
    /// 执行类型: shell（命令行）、script（C# 脚本）、mcp_call（调用 MCP 服务器）
    /// </summary>
    public required string Type { get; init; }

    /// <summary>
    /// Shell 命令模板 — 支持 {{param}} 占位符替换
    /// 例如: "python {{script_path}} --input {{input_file}}"
    /// </summary>
    public string? Command { get; init; }

    /// <summary>
    /// Shell 参数列表 — 支持 {{param}} 占位符替换
    /// </summary>
    public string[]? Args { get; init; }

    /// <summary>
    /// MCP 调用目标 — 服务器名.方法名
    /// 例如: "filesystem.read_file"
    /// </summary>
    public string? McpTarget { get; init; }

    /// <summary>
    /// 超时时间（秒）
    /// </summary>
    public int TimeoutSeconds { get; init; } = 30;
}

/// <summary>
/// 工具模板服务接口 — 加载、创建、注册动态工具
/// </summary>
public interface IToolTemplateService
{
    /// <summary>
    /// 加载所有模板
    /// </summary>
    Task<IReadOnlyList<ToolTemplate>> LoadTemplatesAsync(CancellationToken ct = default);

    /// <summary>
    /// 根据模板创建并注册工具到注册表
    /// </summary>
    Task<IToolHandler> CreateAndRegisterAsync(ToolTemplate template, IToolRegistry registry, CancellationToken ct = default);

    /// <summary>
    /// 保存模板到磁盘
    /// </summary>
    Task SaveTemplateAsync(ToolTemplate template, CancellationToken ct = default);

    /// <summary>
    /// 列出可用模板
    /// </summary>
    Task<IReadOnlyList<ToolTemplate>> ListTemplatesAsync(CancellationToken ct = default);
}
