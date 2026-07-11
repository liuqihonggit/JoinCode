
namespace Core.Utils;

/// <summary>
/// 执行上下文 - 聚合横切关注点
/// </summary>
public sealed record ExecutionContext(
    CancellationToken CancellationToken = default,
    ILogger? Logger = null
);
