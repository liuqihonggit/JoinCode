namespace Core.Skills;

/// <summary>
/// 技能执行中间件共享上下文 — 在管道各阶段间传递状态
/// </summary>
public sealed class SkillContext : PipelineContextBase, IMetricsContext {
    // === IMetricsContext ===

    /// <summary>
    /// 指标前缀
    /// </summary>
    public string MetricsPrefix => "skill.execute";
    /// <summary>
    /// 指标是否成功 — 结果成功标志，结果为 null 视为失败
    /// </summary>
    public bool IsMetricsSuccess => Result?.Success ?? false;
    /// <summary>
    /// 指标持续时间毫秒 — 来自计时器
    /// </summary>
    public long? MetricsDurationMs => Stopwatch?.ElapsedMilliseconds;
    /// <summary>
    /// 构建指标标签字典
    /// </summary>
    /// <returns>包含技能名称标签的字典</returns>
    public Dictionary<string, string> BuildMetricsTags() => new() { ["skill"] = SkillName };

    // === 输入 ===

    /// <summary>
    /// 技能名称
    /// </summary>
    public required string SkillName { get; init; }

    /// <summary>
    /// 技能参数
    /// </summary>
    public Dictionary<string, JsonElement> Parameters { get; init; } = [];

    /// <summary>
    /// 解析到的技能定义 — 由 SkillService 在创建 context 前设置
    /// </summary>
    public required SkillDefinition Skill { get; init; }

    /// <summary>
    /// 技能执行上下文
    /// </summary>
    public required ExecutionContext ExecutionContext { get; init; }

    /// <summary>
    /// 取消令牌
    /// </summary>
    public CancellationToken CancellationToken { get; init; }

    // === SkillValidationMiddleware 填充 ===

    /// <summary>
    /// 验证错误信息 — 由 SkillValidationMiddleware 设置，非 null 表示验证失败
    /// </summary>
    public string? ValidationError { get; set; }

    // === SkillTelemetryMiddleware 填充 ===

    /// <summary>
    /// 遥测 Span — 由 SkillTelemetryMiddleware 创建和释放
    /// </summary>
    public ITelemetrySpan? Span { get; set; }

    /// <summary>
    /// 计时器 — 由 SkillTelemetryMiddleware 创建
    /// </summary>
    public System.Diagnostics.Stopwatch? Stopwatch { get; set; }

    // === SkillExecutionMiddleware 填充 ===

    /// <summary>
    /// 执行结果 — 由 SkillExecutionMiddleware 设置
    /// </summary>
    public SkillResult? Result { get; set; }
}