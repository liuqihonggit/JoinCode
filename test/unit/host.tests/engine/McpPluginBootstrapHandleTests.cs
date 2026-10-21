namespace JoinCode.Host.Tests.Engine;

/// <summary>
/// MCP 插件后台加载生命周期句柄测试（启动开销优化 — TASK037 后续）：
/// 后台链与宿主 Dispose 的退出竞态防护 — Run 记录链 Task + 持取消源；
/// DisposeAsync 先取消（加速断链 — OCE 静默收尾）再无超时等待链完成
/// （对齐 AGENTS 资源规范：释放函数禁止超时，直接 await）。
/// 场景：真实交互会话链早完成（Dispose 瞬回）；启动即退出（selftest）等链收尾（日志干净无 ObjectDisposed 竞态）。
/// </summary>
public sealed class McpPluginBootstrapHandleTests
{
    /// <summary>DisposeAsync — 等待后台链完成（链未完成时阻塞，完成后返回）。</summary>
    [Fact]
    public async Task ShutdownWaitsForBackgroundChain()
    {
        await using var handle = new McpPluginBootstrapHandle();
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        handle.Run(ct => gate.Task);

        var disposeTask = handle.DisposeAsync().AsTask();
        await Task.Delay(50);
        Assert.False(disposeTask.IsCompleted);

        gate.SetResult();
        await disposeTask.WaitAsync(TimeSpan.FromSeconds(5));
    }

    /// <summary>DisposeAsync — 先取消再等（门闩监听取消：Dispose 触发 ct 取消 → 链收尾 → Dispose 返回）。</summary>
    [Fact]
    public async Task ShutdownCancelsTokenThenWaits()
    {
        await using var handle = new McpPluginBootstrapHandle();
        var cancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        handle.Run(async ct =>
        {
            await using var registration = ct.Register(() => cancelled.TrySetResult());
            await cancelled.Task;
        });

        var disposeTask = handle.DisposeAsync().AsTask();
        await disposeTask.WaitAsync(TimeSpan.FromSeconds(5));
    }

    /// <summary>DisposeAsync — 后台链异常已被链内吞（Run 的链恒成功语义）；Dispose 不抛。</summary>
    [Fact]
    public async Task ShutdownSwallowsBackgroundFault()
    {
        await using var handle = new McpPluginBootstrapHandle();
        handle.Run(_ => Task.FromException(new InvalidOperationException("链失败")));

        var disposeTask = handle.DisposeAsync().AsTask();
        await disposeTask.WaitAsync(TimeSpan.FromSeconds(5));
    }

    /// <summary>幂等/顺序 — 未 Run 直接 Dispose no-op；二次 Dispose no-op。</summary>
    [Fact]
    public async Task ShutdownIdempotentAndNoRunNoop()
    {
        await using var handle = new McpPluginBootstrapHandle();
        await handle.DisposeAsync();
        await handle.DisposeAsync();

        handle.Run(_ => Task.CompletedTask);
        await handle.DisposeAsync();
        await handle.DisposeAsync();
    }

    /// <summary>Run 多次 — 仅首次生效（单链语义，后到 no-op）。</summary>
    [Fact]
    public async Task RunSecondCallIgnored()
    {
        await using var handle = new McpPluginBootstrapHandle();
        var firstGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondObserved = false;
        handle.Run(_ => firstGate.Task);
        handle.Run(_ =>
        {
            secondObserved = true;
            return Task.CompletedTask;
        });

        firstGate.SetResult();
        await handle.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(secondObserved);
    }

    /// <summary>Completion — 插件就绪完成信号（邮箱/通知语义，替代时间性等待）：Run 前已完成
    /// （无可等之物）；Run 后反映后台链 — 门闩放行前挂起、放行后完成。消费方（E2E/宿主）等
    /// 此信号确认工具注册就绪，禁止 Task.Delay 固定延时（CI 快慢机均不可靠）。</summary>
    [Fact]
    public async Task Completion_ReflectsBackgroundChain_NotElapsed()
    {
        await using var handle = new McpPluginBootstrapHandle();
        Assert.True(handle.Completion.IsCompleted);

        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        handle.Run(_ => gate.Task);
        var completion = handle.Completion;
        Assert.False(completion.IsCompleted);

        gate.SetResult();
        await completion.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(handle.Completion.IsCompleted);
    }

    /// <summary>Completion 占位语义契约锁（TASK063 P8）— Run 前占位是"无可等之物"设计决定而非就绪信号：
    /// ①Run 前 await 立即通过（占位完成 ≠ 插件就绪 — 误用后果见 xml 契约）；
    /// ②Run 后 Completion 必须切换为后台链 Task 实例（引用变更守卫 — 防实现退化为恒返回占位，
    /// 该退化会令就绪信号永远假完成 → 消费方全部拿到空工具表且零异常暴露）。</summary>
    [Fact]
    public async Task Completion_BeforeRun_Placeholder_NotReadinessSignal()
    {
        await using var handle = new McpPluginBootstrapHandle();
        var placeholder = handle.Completion;
        Assert.True(placeholder.IsCompleted);
        await placeholder.WaitAsync(TimeSpan.FromSeconds(1));

        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        handle.Run(_ => gate.Task);
        var chainTask = handle.Completion;
        Assert.NotSame(placeholder, chainTask);
        Assert.False(chainTask.IsCompleted);

        gate.SetResult();
        await chainTask.WaitAsync(TimeSpan.FromSeconds(5));
    }
}
