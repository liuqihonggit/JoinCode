namespace Core.Query;

/// <summary>
/// 消息转储中间件 — 每轮 LLM 调用前，把即将发出的完整消息列表写盘（JCC_DUMP_MESSAGES=1）。
/// <para>存在理由：转储服务在 Brain.ContextServices，QueryEngine 在 Brain.Query，后者不能引用前者；
/// 而 QueryEngine 本来就提供 IQueryMiddleware 管道供跨层注入。此前转储只挂在 LLMInvocationHandler，
/// 打点证实 -p / 非交互链路根本不走它，导致整轮跑完一个 dump 文件都没有。</para>
/// <para>用 BeforeIterationHooks 而不是直接 await：转储只在开关打开时做一次小文件写入，
/// 且顺序上必然早于本轮 LLM 请求，进程即使随后失败也能看到当时发给模型的提示词。</para>
/// </summary>
[Register(typeof(IQueryMiddleware), ServiceLifetime.Singleton)]
public sealed partial class MessageDumpMiddleware : ServiceEntity, IQueryMiddleware {
    private readonly IChatFileContextService _dumper;

    /// <summary>注入转储服务 — 由 DI 提供，测试可替换为内存文件系统实现。</summary>
    public MessageDumpMiddleware(IChatFileContextService dumper) {
        _dumper = dumper;
    }

    /// <summary>转储失败不应中断 LLM 调用。</summary>
    public ErrorBehavior OnError => ErrorBehavior.Continue;

    /// <summary>注册每轮前的转储钩子后继续执行管道。</summary>
    public Task InvokeAsync(QueryMiddlewareContext context, MiddlewareDelegate<QueryMiddlewareContext> next, CancellationToken ct) {
        context.BeforeIterationHooks.Add(DumpAsync);
        return next(context, ct);
    }

    private async Task DumpAsync(QueryMiddlewareContext context, CancellationToken ct) {
        // 开关在服务内部已判一次；这里再判一次是为了关闭时连钩子调用栈都不进。
        if (Environment.GetEnvironmentVariable("JCC_DUMP_MESSAGES") != "1") return;

        var sessionId = context.Options?.SessionId ?? global::Core.Utils.SessionIdFactory.DefaultSessionId;
        Diag.WriteLine($"[DUMP] 转储 {context.ChatHistory.Count} 条消息, session={sessionId}");
        await _dumper.DumpMessageListAsync(context.ChatHistory, sessionId, 0, 0).ConfigureAwait(false);
    }
}
