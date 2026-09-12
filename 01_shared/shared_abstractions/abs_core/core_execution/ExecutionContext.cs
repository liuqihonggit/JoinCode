
namespace JoinCode.Abstractions.Execution;

/// <summary>
/// 执行上下文 - 聚合横切关注点（CancellationToken + Logger）
/// </summary>
public sealed record ExecutionContext(
    CancellationToken CancellationToken = default,
    ILogger? Logger = null);
