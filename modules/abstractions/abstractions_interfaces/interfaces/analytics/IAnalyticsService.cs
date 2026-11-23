
namespace JoinCode.Abstractions.Interfaces;

public interface IAnalyticsService {
    /// <summary>记录分析事件。</summary>
    void TrackEvent(AnalyticsEventType type, string name, Dictionary<string, JsonElement>? data = null, string? agentName = null);
    /// <summary>记录工具调用。</summary>
    void TrackToolCall(string toolName, bool success, double durationMs, Dictionary<string, JsonElement>? data = null, string? agentName = null);
    /// <summary>记录工具错误。</summary>
    void TrackToolError(string toolName, string errorMessage, Dictionary<string, JsonElement>? data = null, string? agentName = null);
    /// <summary>记录代理启动。</summary>
    void TrackAgentStart(string agentName, string? sessionId = null);
    /// <summary>异步记录代理完成。</summary>
    Task TrackAgentCompleteAsync(string agentName, bool success, double durationMs, string? sessionId = null);
    /// <summary>获取工具使用统计。</summary>
    List<ToolUsageStatistics> GetToolUsageStatistics(int? days = null);
    /// <summary>获取使用量报告。</summary>
    UsageStatisticsReport GetUsageReport(int? days = null);
    /// <summary>获取事件历史。</summary>
    List<AnalyticsEvent> GetEventHistory(AnalyticsEventType? type = null, int limit = WorkflowConstants.Analytics.DefaultEventHistoryLimit);
    /// <summary>清除历史数据。</summary>
    void ClearHistory(int? olderThanDays = null);
    /// <summary>异步导出数据。</summary>
    Task<string> ExportDataAsync(DateTime? startDate = null, DateTime? endDate = null, CancellationToken cancellationToken = default);
}