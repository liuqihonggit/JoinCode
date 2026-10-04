namespace Core.Utils;

/// <summary>
/// in-flight 任务注册表 — RegisterInFlight/CleanupCompleted 管理(DSG033 方案B)。
/// <para>封装 fire-and-forget 任务注册+水位线清理,ActorBase 持有此实例。</para>
/// <para>DisposeAsync 在 Consumer 退出后等待所有注册的 in-flight 任务完成,确保资源真正释放。</para>
/// <para>水位线清理:超过 CleanupThreshold(256) 时清理已完成 Task,保留最近 CleanupRetain(64) 个供诊断。</para>
/// <para>线程安全:ConcurrentQueue 线程安全,偶发并发清理无副作用(多清一次仅少保留几个已完成项)。</para>
/// <para>正确性:未完成 Task(IsCompleted==false)一定被保留,DisposeAsync 的 WhenAll 仍等待全部未完成项。</para>
/// </summary>
internal sealed class ActorInFlightRegistry {
    private readonly ConcurrentQueue<Task> _tasks = new();
    private const int CleanupThreshold = 256;
    private const int CleanupRetain = 64;

    /// <summary>当前注册任务数 — 供测试验证清理有效性</summary>
    public int Count => _tasks.Count;

    /// <summary>
    /// 注册 in-flight 任务 — 子类 Handle 里 fire-and-forget 启动的任务应通过此方法注册。
    /// <para>DisposeAsync 在 Consumer 退出后自动等待所有注册的 in-flight 任务完成,确保资源真正释放。</para>
    /// <para>DSG033 方案B: 水位线清理已完成 Task,防止长生命周期 Actor ConcurrentQueue 无限增长。</para>
    /// </summary>
    /// <param name="task">fire-and-forget 启动的任务引用</param>
    public void Register(Task task) {
        _tasks.Enqueue(task);
        if (_tasks.Count > CleanupThreshold) {
            CleanupCompleted();
        }
    }

    /// <summary>
    /// 清理已完成的 in-flight 任务 — 从队列头部 Dequeue 已完成项,保留最近 CleanupRetain 个供诊断。
    /// </summary>
    private void CleanupCompleted() {
        var retained = 0;
        var toRequeue = new List<Task>();
        while (_tasks.TryDequeue(out var t)) {
            if (!t.IsCompleted && retained < CleanupRetain) {
                toRequeue.Add(t);
                retained++;
            }
        }
        foreach (var t in toRequeue) {
            _tasks.Enqueue(t);
        }
    }

    /// <summary>快照所有任务 — DisposeAsync 中等待全部完成</summary>
    public Task[] ToArray() => _tasks.ToArray();

    /// <summary>清空队列 — DisposeAsync 中等待完成后调用</summary>
    public void Clear() => _tasks.Clear();
}
