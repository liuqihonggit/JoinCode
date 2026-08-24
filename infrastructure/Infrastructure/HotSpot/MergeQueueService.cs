namespace Infrastructure.HotSpot;

/// <summary>
/// 合并队列服务实现 — 队长串行处理Worker产出
/// 编译校验和合并通过回调注入（实际接入时绑定 BuildQueueService + WorktreeMergeService）
/// </summary>
[Register(typeof(IMergeQueueService), ServiceLifetime.Singleton)]
public sealed class MergeQueueService : IMergeQueueService
{
    private readonly ConcurrentQueue<MergeQueueItem> _queue = new();
    private readonly Func<string, CancellationToken, Task<bool>> _compileValidator;
    private readonly Func<string, CancellationToken, Task<bool>> _mergeExecutor;

    public MergeQueueService(
        Func<string, CancellationToken, Task<bool>> compileValidator,
        Func<string, CancellationToken, Task<bool>> mergeExecutor)
    {
        _compileValidator = compileValidator ?? throw new ArgumentNullException(nameof(compileValidator));
        _mergeExecutor = mergeExecutor ?? throw new ArgumentNullException(nameof(mergeExecutor));
    }

    public Task EnqueueAsync(MergeQueueItem item, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(item);
        cancellationToken.ThrowIfCancellationRequested();
        _queue.Enqueue(item);
        return Task.CompletedTask;
    }

    public async Task<MergeResult> ProcessNextAsync(CancellationToken cancellationToken = default)
    {
        if (!_queue.TryDequeue(out var item))
            return MergeResult.Empty();

        cancellationToken.ThrowIfCancellationRequested();

        var compileOk = await _compileValidator(item.WorktreeBranch, cancellationToken).ConfigureAwait(false);
        if (!compileOk)
            return MergeResult.CompileFailed(item.WorkerId, $"分支 {item.WorktreeBranch} 编译未通过");

        var mergeOk = await _mergeExecutor(item.WorktreeBranch, cancellationToken).ConfigureAwait(false);
        if (!mergeOk)
            return MergeResult.MergeFailed(item.WorkerId, $"分支 {item.WorktreeBranch} 合并失败");

        return MergeResult.Ok(item.WorktreeBranch, item.WorkerId);
    }

    public IReadOnlyList<MergeQueueItem> GetPending() => [.. _queue];

    public int PendingCount => _queue.Count;
}
