
namespace JoinCode.Abstractions.Interfaces;

public interface IAnalyticsService
{
    void TrackEvent(AnalyticsEventType type, string name, Dictionary<string, JsonElement>? data = null, string? agentName = null);
    void TrackToolCall(string toolName, bool success, double durationMs, Dictionary<string, JsonElement>? data = null, string? agentName = null);
    void TrackToolError(string toolName, string errorMessage, Dictionary<string, JsonElement>? data = null, string? agentName = null);
    void TrackAgentStart(string agentName, string? sessionId = null);
    void TrackAgentComplete(string agentName, bool success, double durationMs, string? sessionId = null);
    List<ToolUsageStatistics> GetToolUsageStatistics(int? days = null);
    UsageStatisticsReport GetUsageReport(int? days = null);
    List<AnalyticsEvent> GetEventHistory(AnalyticsEventType? type = null, int limit = WorkflowConstants.Analytics.DefaultEventHistoryLimit);
    void ClearHistory(int? olderThanDays = null);
    Task<string> ExportDataAsync(DateTime? startDate = null, DateTime? endDate = null, CancellationToken cancellationToken = default);
}
