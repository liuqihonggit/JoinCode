namespace JoinCode.Abstractions.Interfaces;

public interface IProgressTracker {
    /// <summary>记录工具使用。</summary>
    void RecordToolUse(string toolName, string? activityDescription = null, Dictionary<string, string>? input = null);
    /// <summary>记录 Token 用量。</summary>
    void RecordTokenUsage(int tokenCount);
    /// <summary>更新进度摘要。</summary>
    void UpdateSummary(string summary);
    /// <summary>转换为代理进度信息。</summary>
    AgentProgress ToProgress();
}
