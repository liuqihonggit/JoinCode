
namespace Core.Summary;

/// <summary>
/// 离开摘要配置选项
/// </summary>
public sealed class AwaySummaryOptions {
    /// <summary>
    /// 获取或设置事件最大保留时长；超过此时长的事件将被丢弃。默认 24 小时。
    /// </summary>
    public TimeSpan MaxEventAge { get; init; } = TimeSpan.FromHours(24);

    /// <summary>
    /// 获取或设置跟踪事件的最大数量。默认 1000。
    /// </summary>
    public int MaxEventsToTrack { get; init; } = 1000;

    /// <summary>
    /// 获取或设置摘要模板字符串。默认为 <see cref="DefaultTemplate"/>。
    /// </summary>
    public string SummaryTemplate { get; init; } = DefaultTemplate;

    /// <summary>
    /// 获取或设置一个值，指示摘要是否包含工具调用。默认 <c>true</c>。
    /// </summary>
    public bool IncludeToolCalls { get; init; } = true;

    /// <summary>
    /// 获取或设置一个值，指示摘要是否包含消息。默认 <c>true</c>。
    /// </summary>
    public bool IncludeMessages { get; init; } = true;

    /// <summary>
    /// 获取或设置一个值，指示摘要是否包含错误。默认 <c>true</c>。
    /// </summary>
    public bool IncludeErrors { get; init; } = true;

    /// <summary>
    /// 获取或设置摘要文本最大长度；超出时截断并追加截断标记。默认 2000。
    /// </summary>
    public int MaxSummaryLength { get; init; } = 2000;

    /// <summary>
    /// 获取或设置自动保存间隔。默认 5 分钟。
    /// </summary>
    public TimeSpan AutoSaveInterval { get; init; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// 默认摘要模板，包含离开时间、返回时间、时长、活动概要、关键事件、错误详情与待处理事项占位符。
    /// </summary>
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