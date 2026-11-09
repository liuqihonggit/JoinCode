namespace Core.Context;

/// <summary>
/// 聊天流崩溃快照中间件 — 捕获 Chat Stream 管道异常，自动记录 CrashSnapshot
/// OnError=Propagate：记录快照后异常继续传播，由 ChatErrorHandlingMiddleware 统一分类
/// 零侵入：所有经过 Chat 管道的异常自动被记录，无需修改任何组件
/// </summary>
[Register(typeof(IChatMiddleware), ServiceLifetime.Singleton)]
public sealed partial class StreamCrashSnapshotMiddleware : ServiceEntity, IChatMiddleware {
    private readonly ICrashSnapshotStore _store;

    /// <summary>
    /// 初始化聊天流崩溃快照中间件
    /// </summary>
    /// <param name="store">崩溃快照存储</param>
    public StreamCrashSnapshotMiddleware(ICrashSnapshotStore store) {
        _store = store;
    }

    /// <summary>错误行为策略：记录快照后异常继续传播</summary>
    public ErrorBehavior OnError => ErrorBehavior.Propagate;

    /// <summary>
    /// 捕获管道异常并记录 CrashSnapshot，然后重新抛出异常
    /// </summary>
    /// <param name="context">中间件共享上下文</param>
    /// <param name="next">下游中间件委托</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>聊天流事件异步枚举</returns>
    public async IAsyncEnumerable<ChatStreamEvent> InvokeAsync(
        ChatMiddlewareContext context,
        StreamMiddlewareDelegate<ChatMiddlewareContext, ChatStreamEvent> next,
        [EnumeratorCancellation] CancellationToken ct) {
        await using var enumerator = next(context, ct).GetAsyncEnumerator(ct);
        while (true) {
            ChatStreamEvent current;
            try {
                if (!await enumerator.MoveNextAsync().ConfigureAwait(false))
                    yield break;
                current = enumerator.Current;
            } catch (OperationCanceledException) { throw; } catch (Exception ex) {
                _store.Add(new CrashSnapshot(
                    "ChatStream",
                    CrashSeverity.Error,
                    ex,
                    new CrashExecutionContext {
                        OperationName = "ChatStreamPipeline",
                        TurnIndex = context.ConversationTurn,
                    }));
                throw;
            }

            yield return current;
        }
    }
}