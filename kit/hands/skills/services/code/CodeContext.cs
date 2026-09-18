namespace Core.Skills;

/// <summary>
/// 代码操作类型
/// </summary>
public enum CodeOperation
{
    /// <summary>
    /// 生成代码
    /// </summary>
    [EnumValue("generate")]
    Generate,

    /// <summary>
    /// 分析代码
    /// </summary>
    [EnumValue("analyze")]
    Analyze,

    /// <summary>
    /// 执行代码
    /// </summary>
    [EnumValue("execute")]
    Execute,
}

/// <summary>
/// 代码服务中间件上下文
/// </summary>
public sealed class CodeContext : PipelineContextBase, IMetricsContext
{
    // === IMetricsContext ===

    /// <summary>
    /// 指标前缀
    /// </summary>
    public string MetricsPrefix => "code.operation";
    /// <summary>
    /// 指标是否成功 — 安全验证未失败且结果非 null
    /// </summary>
    public bool IsMetricsSuccess => !IsSecurityFail && Result is not null;
    /// <summary>
    /// 指标持续时间毫秒 — 不采集，返回 null
    /// </summary>
    public long? MetricsDurationMs => null;
    /// <summary>
    /// 构建指标标签字典
    /// </summary>
    /// <returns>包含操作类型、缓存命中、安全失败等标签的字典</returns>
    public Dictionary<string, string> BuildMetricsTags()
    {
        var tags = new Dictionary<string, string>
        {
            ["operation"] = Operation.ToString().ToLowerInvariant(),
            ["cached"] = IsCached.ToString()
        };
        if (IsSecurityFail) tags["security_fail"] = "true";
        return tags;
    }

    /// <summary>
    /// 操作类型
    /// </summary>
    public required CodeOperation Operation { get; init; }

    /// <summary>
    /// 输入内容（prompt 或 code）
    /// </summary>
    public required string Input { get; init; }

    /// <summary>
    /// 输出结果
    /// </summary>
    public string? Result { get; set; }

    /// <summary>
    /// 是否命中缓存
    /// </summary>
    public bool IsCached { get; set; }

    /// <summary>
    /// 是否安全验证失败
    /// </summary>
    public bool IsSecurityFail { get; set; }
}
