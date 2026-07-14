namespace Core.Skills;

/// <summary>
/// 代码操作类型
/// </summary>
public enum CodeOperation
{
    /// <summary>
    /// 生成代码
    /// </summary>
    Generate,

    /// <summary>
    /// 分析代码
    /// </summary>
    Analyze,

    /// <summary>
    /// 执行代码
    /// </summary>
    Execute
}

/// <summary>
/// 代码服务中间件上下文
/// </summary>
public sealed class CodeContext : PipelineContextBase, IMetricsContext
{
    // === IMetricsContext ===

    public string MetricsPrefix => "code.operation";
    public bool IsMetricsSuccess => !IsSecurityFail && Result is not null;
    public long? MetricsDurationMs => null;
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
