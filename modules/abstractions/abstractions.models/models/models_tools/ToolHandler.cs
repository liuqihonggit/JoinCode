namespace JoinCode.Abstractions.Tools;

/// <summary>
/// 工具处理器委托 — 对齐 TS Tool.call 签名（含 onProgress）
/// </summary>
public delegate Task<ToolResult> ToolHandler(
    string toolName,
    Dictionary<string, JsonElement> arguments,
    CancellationToken cancellationToken,
    ToolProgressCallback? onProgress = null);
