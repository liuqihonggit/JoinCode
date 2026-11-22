namespace Pipelines.Tests;

/// <summary>
/// 测试辅助 — 构造上下文与异步枚举桩，消除 IO/时序依赖。
/// 所有异步枚举桩均用 await Task.CompletedTask 立即完成，不引入真实时序。
/// </summary>
internal static class TestHelpers {
    /// <summary>构造最小可用 ChatMiddlewareContext（无 IO 依赖）。</summary>
    internal static ChatMiddlewareContext NewContext(string message = "test", int turn = 1, bool dryRun = false) {
        return new ChatMiddlewareContext {
            Message = message,
            ConversationTurn = turn,
            IsDryRun = dryRun,
            ToolUseContext = new ToolUseContext()
        };
    }

    /// <summary>同步收集异步枚举的所有事件到列表。</summary>
    internal static async Task<List<ChatStreamEvent>> CollectAsync(
        IAsyncEnumerable<ChatStreamEvent> source, CancellationToken ct = default) {
        var list = new List<ChatStreamEvent>();
        await foreach (var e in source.WithCancellation(ct)) {
            list.Add(e);
        }
        return list;
    }

    /// <summary>返回确定性事件序列（无时序、无 IO）。</summary>
    internal static async IAsyncEnumerable<ChatStreamEvent> EventsAsync(
        ChatStreamEvent[] events, [EnumeratorCancellation] CancellationToken ct = default) {
        await Task.CompletedTask;
        foreach (var e in events) {
            ct.ThrowIfCancellationRequested();
            yield return e;
        }
    }

    /// <summary>枚举首次 MoveNextAsync 即抛指定异常（无前置事件、无时序）。</summary>
    internal static IAsyncEnumerable<ChatStreamEvent> ThrowAsync(Exception ex, CancellationToken ct = default)
        => new ThrowingAsyncEnumerable(ex);

    /// <summary>先返回前置事件，再抛指定异常（验证透传+异常分类组合）。</summary>
    internal static async IAsyncEnumerable<ChatStreamEvent> EventsThenThrowAsync(
        ChatStreamEvent[] events, Exception ex, [EnumeratorCancellation] CancellationToken ct = default) {
        await Task.CompletedTask;
        foreach (var e in events) {
            ct.ThrowIfCancellationRequested();
            yield return e;
        }
        ct.ThrowIfCancellationRequested();
        throw ex;
    }

    /// <summary>
    /// 首次 MoveNextAsync 即抛异常的 IAsyncEnumerable 实现 — 避免 async-iterator 无 yield 的 CS8419。
    /// </summary>
    private sealed class ThrowingAsyncEnumerable : IAsyncEnumerable<ChatStreamEvent> {
        private readonly Exception _ex;
        public ThrowingAsyncEnumerable(Exception ex) => _ex = ex;
        public IAsyncEnumerator<ChatStreamEvent> GetAsyncEnumerator(CancellationToken ct = default)
            => new Enumerator(_ex, ct);

        private sealed class Enumerator : IAsyncEnumerator<ChatStreamEvent> {
            private readonly Exception _ex;
            private readonly CancellationToken _ct;
            public Enumerator(Exception ex, CancellationToken ct) { _ex = ex; _ct = ct; }
            public ChatStreamEvent Current => throw new InvalidOperationException();
            public ValueTask<bool> MoveNextAsync() {
                _ct.ThrowIfCancellationRequested();
                throw _ex;
            }
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }
}
