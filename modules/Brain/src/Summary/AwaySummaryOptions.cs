
namespace Core.Summary;

public sealed class AwaySummaryOptions
{
    public TimeSpan MaxEventAge { get; init; } = TimeSpan.FromHours(24);
    public int MaxEventsToTrack { get; init; } = 1000;
    public string SummaryTemplate { get; init; } = DefaultTemplate;
    public bool IncludeToolCalls { get; init; } = true;
    public bool IncludeMessages { get; init; } = true;
    public bool IncludeErrors { get; init; } = true;
    public int MaxSummaryLength { get; init; } = 2000;
    public TimeSpan AutoSaveInterval { get; init; } = TimeSpan.FromMinutes(5);

    public const string DefaultTemplate = """
        # 离开摘要
        离开时间: {AwayTime}
        返回时间: {ReturnTime}
        离开时长: {Duration}

        ## 活动概要
        - 工具调用: {ToolCallCount} 次
        - 消息数: {MessageCount} 条
        - 错误数: {ErrorCount} 个

        ## 关键事件
        {KeyEvents}

        ## 错误详情
        {ErrorDetails}

        ## 待处理事项
        {PendingItems}
        """;
}
