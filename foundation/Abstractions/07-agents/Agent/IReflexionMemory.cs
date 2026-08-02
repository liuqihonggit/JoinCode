namespace JoinCode.Abstractions.Interfaces.Doctor;

/// <summary>
/// 反思记忆存储 — 持久化修复经验
/// </summary>
public interface IReflexionMemory
{
    /// <summary>
    /// 存储修复经验
    /// </summary>
    Task StoreAsync(
        CodePatch patch,
        DiagnosticReport diagnostic,
        bool wasSuccessful,
        CancellationToken ct = default);

    /// <summary>
    /// 检索与当前诊断相似的历史修复
    /// </summary>
    Task<IReadOnlyList<CodePatch>> RetrieveSimilarPatchesAsync(
        DiagnosticReport diagnostic,
        int maxResults = 3,
        CancellationToken ct = default);

    /// <summary>
    /// 获取反思记忆统计 — 各规则的成功修复次数/总次数
    /// </summary>
    Task<IReadOnlyList<ReflexionRuleStats>> GetStatisticsAsync(CancellationToken ct = default);
}

/// <summary>
/// 反思记忆规则统计
/// </summary>
public sealed record ReflexionRuleStats
{
    public required string RuleId { get; init; }
    public required int TotalAttempts { get; init; }
    public required int SuccessfulPatches { get; init; }
    public required int FailedPatches { get; init; }
    public required DateTimeOffset LastAttemptAt { get; init; }
}
