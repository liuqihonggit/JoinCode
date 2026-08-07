namespace Core.Agents;

/// <summary>
/// 子智能体输出截断结果 — L3 落盘指针兜底
/// </summary>
public sealed record SubAgentOutputTruncationResult(
    string FinalText,
    string? ArchivedPath,
    bool WasTruncated);
