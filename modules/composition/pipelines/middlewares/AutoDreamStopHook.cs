namespace JoinCode.Pipelines.Middlewares;

/// <summary>
/// 回合结束钩子 — 把「查询完成」这件事转成一次自动记忆整合的投递（TASK126 / TASK103 §2.3 #40）。
/// <para>
/// 基准是 <c>query/stopHooks.ts:155</c> 的 <c>void executeAutoDream(...)</c>：**fire-and-forget**，
/// 在 stop hook 之前投递、不等待结果。本类照抄这个语义 —— <see cref="OnStopAsync"/> 只投递不等待，
/// 因此 dream 跑多久都不影响回合返回时机。
/// </para>
/// <para>
/// <b>为什么落在 <c>IQueryStopHook</c> 而不是 <c>query-pipeline</c> 的一个中间件节点</b>：生产链的
/// query 中间件来自 DI 集合（<c>QueryEngine.BuildPipeline</c> 取 <c>IEnumerable&lt;IQueryMiddleware&gt;</c>），
/// 不是 profile 组装的那条 <c>MiddlewarePipeline&lt;QueryMiddlewareContext&gt;</c>；后者在生产里没有
/// 消费方。加节点会变成「dump 里有、门禁绿、运行期不执行」。而 <c>query_stop_hooks</c> 的消费者
/// （<c>StopHookMiddleware</c>，本身是 DI 集合成员）确实在跑，只是列表一直是空的。详见 TASK126 §2。
/// </para>
/// <para>
/// <b>为什么返回 Continue 而不是 Stop</b>：管理器遇到 <c>ShouldStop=true</c> 会立即短路返回
/// （<c>QueryStopHookManager.ExecuteStopHooksAsync</c>），那等于本 hook 把「我不阻止停止」
/// 误表达成「到此为止，别问后面的」。返回 Continue 时若无人要求停止，管理器末尾同样返回 Stop，
/// 结果一致但不抢占别的 hook。
/// </para>
/// <para>
/// <b>trigger 为 null 时是「永不放行」而不是「报错」</b>：宿主没加载 Dream 插件（因此没有
/// <c>AutoDreamConfig</c>）或没装 dream 子系统时，正确的行为是不触发，而不是让
/// <c>QueryStopHookManager</c> 构造失败把整个查询链拖下水。
/// </para>
/// <para>
/// <b>为什么收 <c>Func&lt;AutoDreamTrigger?&gt;</c> 而不是实例</b>：插件由 <c>McpInitModule</c>
/// 后台加载，本 hook 却在首次查询构建管道时就被构造 —— 构造期抓依赖实测稳定拿到 null
/// （真机 <c>jcc -p hi</c>：<c>[DREAM-AUTO] hook disabled</c> 与 <c>[CAPS-PLUGIN] dream=load</c> 并存）。
/// 因此解析推迟到每次 <see cref="OnStopAsync"/>，**成功即缓存** ——
/// <see cref="JoinCode.Dream.AutoDreamTrigger"/> 持有扫描节流状态，每回合新建等于废掉节流。
/// </para>
/// <para>
/// 类型一律写全限定名：<c>Core.Query.StopHooks</c> 的 <c>StopHookContext</c>/<c>StopHookResult</c>
/// 与 <c>Core.Hooks.Lifecycle</c> 的同名类型冲突，加 global using 会波及本程序集其它文件
/// （JCC1005 又禁止文件内 using）。
/// </para>
/// </summary>
internal sealed class AutoDreamStopHook : Core.Query.StopHooks.IQueryStopHook {
    private readonly Func<JoinCode.Dream.AutoDreamTrigger?>? _resolveTrigger;
    private readonly ILogger<AutoDreamStopHook>? _logger;

    private JoinCode.Dream.AutoDreamTrigger? _trigger;
    private int _resolvedOnce;

    /// <summary>构造回合结束钩子。</summary>
    /// <param name="resolveTrigger">触发器解析委托；每次停止时调用，返回 null 表示 dream 尚未就绪。</param>
    /// <param name="logger">日志。</param>
    public AutoDreamStopHook(Func<JoinCode.Dream.AutoDreamTrigger?>? resolveTrigger, ILogger<AutoDreamStopHook>? logger = null) {
        _resolveTrigger = resolveTrigger;
        _logger = logger;
    }

    /// <summary>注册名 — 必须与 resources/orders.yaml 的 query_stop_hooks 列表一致。</summary>
    public string Name => nameof(AutoDreamStopHook);

    /// <summary>执行位次 — 由 orders.yaml 的位置派生（列表内越小越先执行），实现类不写数字。</summary>
    public int Priority => OrderIndex.Default.OrderOf("query_stop_hooks", Name);

    /// <inheritdoc />
    public Task<Core.Query.StopHooks.StopHookResult> OnStopAsync(
        Core.Query.StopHooks.StopHookContext context,
        CancellationToken ct = default) {
        var trigger = ResolveTrigger();
        if (trigger is null) {
            return Task.FromResult(Core.Query.StopHooks.StopHookResult.Continue());
        }

        try {
            // 用 CancellationToken.None 而不是 ct：ct 属于本回合，回合返回后就会被取消，
            // 传下去会让整合刚开始就被打断（然后回滚锁、永远整合不成）。
            _ = trigger.FireAsync(context.SessionId, CancellationToken.None);
        } catch (Exception ex) {
            // FireAsync 内部已全捕获；这里只兜「投递本身」抛出的极端情况（如线程池拒绝）。
            _logger?.LogWarning(ex, "[AutoDreamStopHook] 自动整合投递失败（忽略，不影响回合）");
        }

        return Task.FromResult(Core.Query.StopHooks.StopHookResult.Continue());
    }

    /// <summary>解析并缓存触发器；dream 尚未就绪时只在首次打点，避免每回合刷同一行。</summary>
    private JoinCode.Dream.AutoDreamTrigger? ResolveTrigger() {
        var cached = Volatile.Read(ref _trigger);
        if (cached is not null || _resolveTrigger is null) {
            return cached;
        }

        var resolved = _resolveTrigger();
        if (resolved is null) {
            if (Interlocked.Exchange(ref _resolvedOnce, 1) == 0) {
                Diag.WriteLine("[DREAM-AUTO] hook waiting: dream subsystem not registered yet（每回合重试）");
            }

            return null;
        }

        Interlocked.Exchange(ref _trigger, resolved);
        return resolved;
    }
}
