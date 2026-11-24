namespace Core.Hooks;

/// <summary>
/// Hook <c>systemMessage</c> 的回合内旁白通道（TASK135）：
/// 任一 hook 事件产出的 systemMessage 都先落到这里，再由当前回合的捕获回调上屏。
/// <para>
/// 为什么用 <see cref="AsyncLocal{T}"/>：旁白必须跟着发起 hook 的那条异步调用链走，而不是按进程/会话
/// 全局广播。工具链、Stop/Notification 等 hook 与回合在同一执行流内，自然拿得到当前捕获；
/// 后台任务若在回合结束后才执行 hook，其执行上下文里没有捕获，<see cref="Publish"/> 返回 false 静默丢弃
/// （fail-soft，不抛异常、不阻塞 hook）。
/// </para>
/// <para>
/// <c>UserPromptSubmit</c> 被显式拒绝：该事件已有两条既有出口（主代理路径直发、回落管道
/// <c>ChatStreamEvent.SystemNotice</c>），若再经本通道会重复上屏。保持单一出口，避免双投递。
/// </para>
/// </summary>
public static class HookSystemMessageChannel {
    /// <summary>当前异步执行流的捕获作用域（嵌套时指向最内层，Dispose 恢复上一层）。</summary>
    private static readonly AsyncLocal<CaptureScope?> CurrentCapture = new();

    /// <summary>
    /// 开启当前异步上下文的旁白捕获。
    /// </summary>
    /// <param name="publish">上屏回调（由回合驱动器提供 — 通常转调事件消费者的 OnSystemNotice）。</param>
    /// <returns>作用域；Dispose 时停止投递并恢复上一层捕获。</returns>
    public static IDisposable BeginCapture(Action<string> publish) {
        ArgumentNullException.ThrowIfNull(publish);
        var scope = new CaptureScope(publish, CurrentCapture.Value);
        CurrentCapture.Value = scope;
        return scope;
    }

    /// <summary>
    /// 发布一条 hook 旁白；仅当当前异步上下文存在捕获且事件允许时投递。
    /// </summary>
    /// <param name="hookEvent">产出该旁白的 hook 事件（UserPromptSubmit 由既有出口处理，这里跳过）。</param>
    /// <param name="message">旁白文本；null/空白不投递。</param>
    /// <returns>是否真正投递（false = 无捕获、事件跳过或空白）。</returns>
    public static bool Publish(HookEvent hookEvent, string? message) {
        if (hookEvent == HookEvent.UserPromptSubmit || string.IsNullOrWhiteSpace(message)) {
            return false;
        }

        var scope = CurrentCapture.Value;
        if (scope is null) {
            return false;
        }

        scope.Deliver(message);
        return true;
    }

    /// <summary>捕获作用域 — 持有上屏回调与上一层作用域；Dispose 幂等。</summary>
    private sealed class CaptureScope(Action<string> publish, CaptureScope? previous) : IDisposable {
        private bool _disposed;

        /// <summary>投递一条旁白；作用域已释放后不再投递（防 fire-and-forget 回调打到已结束回合）。</summary>
        internal void Deliver(string message) {
            if (!_disposed) {
                publish(message);
            }
        }

        /// <summary>释放作用域并恢复上一层（仅当当前上下文仍指向本作用域时，避免跨上下文误写）。</summary>
        public void Dispose() {
            if (_disposed) {
                return;
            }

            _disposed = true;
            if (ReferenceEquals(CurrentCapture.Value, this)) {
                CurrentCapture.Value = previous;
            }
        }
    }
}
