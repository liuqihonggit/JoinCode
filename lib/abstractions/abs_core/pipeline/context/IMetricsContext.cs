namespace JoinCode.Abstractions.Pipeline;

/// <summary>
/// 指标记录上下文 — 支持通用 MetricsMiddleware 复用
/// </summary>
public interface IMetricsContext : IPipelineContext
{
    /// <summary>指标名称前缀（如 "web.operation"、"skill.execute"、"code.operation"）</summary>
    string MetricsPrefix { get; }

    /// <summary>操作是否成功</summary>
    bool IsMetricsSuccess { get; }

    /// <summary>构建指标标签 — 各 Context 自行决定标签内容</summary>
    Dictionary<string, string> BuildMetricsTags();

    /// <summary>可选：耗时（毫秒），非 null 时自动记录 duration 直方图</summary>
    long? MetricsDurationMs { get; }
}
