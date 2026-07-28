namespace JoinCode.Abstractions.Interfaces;

/// <summary>
/// Turn Diff 提供者接口 — 对齐 TS useTurnDiffs
/// 从对话历史中提取每个 AI 对话轮次的文件编辑 diff
/// </summary>
public interface ITurnDiffProvider
{
    /// <summary>
    /// 记录用户提示（标记新 Turn 的开始）
    /// </summary>
    void RecordUserPrompt(string prompt);

    /// <summary>
    /// 记录文件编辑工具调用
    /// </summary>
    /// <param name="filePath">文件路径</param>
    /// <param name="result">工具结果（diff 输出）</param>
    /// <param name="isNewFile">是否为新文件</param>
    void RecordFileEdit(string filePath, string? result, bool isNewFile = false);

    /// <summary>
    /// 获取所有 Turn Diff 列表（按 TurnIndex 倒序）
    /// </summary>
    IReadOnlyList<TurnDiffSnapshot> GetTurnDiffs();

    /// <summary>
    /// 清除所有记录
    /// </summary>
    void Clear();
}

/// <summary>
/// Turn Diff 快照 — 用于跨层传递（不依赖 Host 层类型）
/// </summary>
public sealed record TurnDiffSnapshot
{
    public required int TurnIndex { get; init; }
    public required string? UserPromptPreview { get; init; }
    public required DateTimeOffset Timestamp { get; init; }
    public required int FilesCount { get; init; }
    public required int LinesAdded { get; init; }
    public required int LinesRemoved { get; init; }
}
