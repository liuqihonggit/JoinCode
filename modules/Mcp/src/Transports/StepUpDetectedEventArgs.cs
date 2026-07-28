namespace McpClient.Transports;

/// <summary>
/// Step-Up 认证检测事件参数 — 对齐 TS wrapFetchWithStepUpDetection
/// </summary>
public sealed class StepUpDetectedEventArgs : EventArgs
{
    /// <summary>
    /// 检测到的所需 scope
    /// </summary>
    public string Scope { get; init; } = string.Empty;
}
