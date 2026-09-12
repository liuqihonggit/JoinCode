namespace Core.Context;

/// <summary>
/// 工具调用编排器接口 — 权限检查 + Hook 执行 + 工具调用
/// </summary>
public interface IChatToolOrchestrator
{
    /// <summary>
    /// 执行工具调用
    /// </summary>
    Task<ToolCallResult> ExecuteToolCallAsync(string toolCallName, string? toolCallId, Dictionary<string, JsonElement>? toolCallArguments, CancellationToken ct);
}
