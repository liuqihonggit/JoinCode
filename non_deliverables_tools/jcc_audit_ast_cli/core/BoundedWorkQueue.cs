namespace JccAuditCli;

/// <summary>
/// 有界并发工作队列 — 固定数量的 worker 从无锁并发队列取任务，
/// 以 Task.WhenAll 汇合，替代 SemaphoreSlim 限流与 Parallel.ForEachAsync。
/// </summary>
public static class BoundedWorkQueue {
    /// <summary>
    /// 以指定并发度处理异步工作项。
    /// </summary>
    /// <typeparam name="T">工作项类型</typeparam>
    /// <param name="items">待处理工作项</param>
    /// <param name="degree">最大并发 worker 数</param>
    /// <param name="work">单个工作项处理函数</param>
    /// <param name="ct">取消令牌</param>
    public static async Task RunAsync<T>(
        IReadOnlyList<T> items,
        int degree,
        Func<T, CancellationToken, Task> work,
        CancellationToken ct) {
        if (items.Count == 0) return;

        var queue = new ConcurrentQueue<T>(items);
        var workerCount = Math.Max(1, Math.Min(degree, items.Count));

        async Task WorkerAsync() {
            while (!ct.IsCancellationRequested && queue.TryDequeue(out var item)) {
                await work(item, ct).ConfigureAwait(false);
            }
        }

        var workers = Enumerable.Range(0, workerCount)
            .Select(_ => Task.Run(WorkerAsync, CancellationToken.None))
            .ToArray();

        await Task.WhenAll(workers).ConfigureAwait(false);
    }
}
