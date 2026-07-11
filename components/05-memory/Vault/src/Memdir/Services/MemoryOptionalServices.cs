using JoinCode.Abstractions.Attributes;

namespace Core.Memdir;

/// <summary>
/// MemoryManagementService 的可选服务聚合 — 减少构造函数参数注入
/// </summary>
[Register]
public sealed record MemoryOptionalServices(
    IMemorySearchHistoryService? SearchHistoryService = null,
    IAssistantDailyLogService? DailyLogService = null,
    global::Memdir.Sync.ITeamMemorySyncService? TeamMemorySyncService = null,
    IMemoryScanner? MemoryScanner = null,
    IMemoryTruncator? MemoryTruncator = null,
    IMemoryRelevanceSelector? RelevanceSelector = null,
    IMemoryAgeCalculator? AgeCalculator = null,
    ITelemetryService? TelemetryService = null)
{
    /// <summary>
    /// 从 DI 容器解析所有可选服务 — 保持向后兼容
    /// </summary>
    public static MemoryOptionalServices FromServiceProvider(IServiceProvider sp) => new(
        SearchHistoryService: sp.GetService<IMemorySearchHistoryService>(),
        DailyLogService: sp.GetService<IAssistantDailyLogService>(),
        TeamMemorySyncService: sp.GetService<global::Memdir.Sync.ITeamMemorySyncService>(),
        MemoryScanner: sp.GetService<IMemoryScanner>(),
        MemoryTruncator: sp.GetService<IMemoryTruncator>(),
        RelevanceSelector: sp.GetService<IMemoryRelevanceSelector>(),
        AgeCalculator: sp.GetService<IMemoryAgeCalculator>(),
        TelemetryService: sp.GetService<ITelemetryService>());
}
