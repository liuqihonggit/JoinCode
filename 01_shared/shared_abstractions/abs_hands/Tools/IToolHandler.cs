
namespace JoinCode.Abstractions.Tools;

/// <summary>
/// 工具处理器委托 — 对齐 TS Tool.call 签名（含 onProgress）
/// </summary>
public delegate Task<ToolResult> ToolHandler(
    string toolName,
    Dictionary<string, JsonElement> arguments,
    CancellationToken cancellationToken,
    ToolProgressCallback? onProgress = null);

/// <summary>
/// 工具处理器接口
/// </summary>
public interface IToolHandler
{
    /// <summary>
    /// 工具名称
    /// </summary>
    string Name { get; }

    /// <summary>
    /// 工具描述
    /// </summary>
    string Description { get; }

    /// <summary>
    /// 输入参数模式
    /// </summary>
    ToolSchema InputSchema { get; }

    /// <summary>
    /// 工具类型 — 决定注入策略：System/Mcp/OnError
    /// </summary>
    ToolKind Kind { get; }

    /// <summary>
    /// 二级分组名 — 同组工具在系统提示词中合并展示
    /// </summary>
    string? GroupName { get; }

    /// <summary>
    /// 主分组名 — 来自 [McpToolDispatch(ToolCategory.Xxx)] 特性标记
    /// 用于两级分组导航: map[主分组][子分组][工具名]
    /// </summary>
    string? Category { get; }

    /// <summary>
    /// 超时策略 — 由源码生成器从 ToolHandlerGroupBase 继承链读取
    /// 决定绝对超时上限、是否 kill、是否支持续期
    /// </summary>
    ToolTimeoutPolicy TimeoutPolicy { get; }

    /// <summary>
    /// 执行工具 — 对齐 TS Tool.call(input, context, canUseTool, parentMsg, onProgress)
    /// </summary>
    Task<ToolResult> ExecuteAsync(
        Dictionary<string, JsonElement> arguments,
        CancellationToken cancellationToken = default,
        ToolProgressCallback? onProgress = null);
}
