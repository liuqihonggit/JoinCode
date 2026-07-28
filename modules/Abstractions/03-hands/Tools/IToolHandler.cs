
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
    /// 执行工具 — 对齐 TS Tool.call(input, context, canUseTool, parentMsg, onProgress)
    /// </summary>
    /// <param name="arguments">工具参数</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <param name="onProgress">进度回调 — 对齐 TS ToolCallProgress，工具执行过程中可报告中间进度</param>
    Task<ToolResult> ExecuteAsync(
        Dictionary<string, JsonElement> arguments,
        CancellationToken cancellationToken = default,
        ToolProgressCallback? onProgress = null);
}
