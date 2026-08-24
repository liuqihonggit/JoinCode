namespace Core.Context;

/// <summary>
/// 聊天流崩溃快照中间件 — 捕获 Chat Stream 管道异常，自动记录 CrashSnapshot
/// OnError=Propagate：记录快照后异常继续传播，由 ChatErrorHandlingMiddleware 统一分类
/// 零侵入：所有经过 Chat 管道的异常自动被记录，无需修改任何组件
/// </summary>
[Register(typeof(IChatMiddleware), ServiceLifetime.Singleton)]
public sealed partial class StreamCrashSnapshotMiddleware : ServiceEntity, IChatMiddleware
{
    private readonly ICrashSnapshotStore _store;

    public StreamCrashSnapshotMiddleware(ICrashSnapshotStore store)
    {
        _store = store;
    }

    public ErrorBehavior OnError => ErrorBehavior.Propagate;

    public async IAsyncEnumerable<ChatStreamEvent> InvokeAsync(
        ChatMiddlewareContext context,
        StreamMiddlewareDelegate<ChatMiddlewareContext, ChatStreamEvent> next,
        [EnumeratorCancellation] CancellationToken ct)
    {
        var enumerator = next(context, ct).GetAsyncEnumerator(ct);
        try
        {
            while (true)
            {
                ChatStreamEvent current;
                try
                {
                    if (!await enumerator.MoveNextAsync().ConfigureAwait(false))
                        yield break;
                    current = enumerator.Current;
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    _store.Add(new CrashSnapshot(
                        "ChatStream",
                        CrashSeverity.Error,
                        ex,
                        new CrashExecutionContext
                        {
                            OperationName = "ChatStreamPipeline",
                            TurnIndex = context.ConversationTurn,
                        }));
                    throw;
                }

                yield return current;
            }
        }
        finally
        {
            await enumerator.DisposeAsync().ConfigureAwait(false);
        }
    }
}
