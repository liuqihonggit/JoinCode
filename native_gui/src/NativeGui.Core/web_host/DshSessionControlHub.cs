namespace JoinCode.Gui.WebHost;

/// <summary>
/// 会话控制流中枢（TASK066 C7）：把宿主计算的会话投影变化（plan / permissions）实时推给
/// <c>session/control</c> 连接的订阅者。前端按 <c>seq</c> 丢弃过期帧，因此序号由发布方保证单调递增。
/// </summary>
internal sealed class DshSessionControlHub {
    private readonly Lock _gate = new();
    private readonly Dictionary<string, ControlListener> _listeners = new(StringComparer.Ordinal);

    /// <summary>登记一个控制帧订阅者（返回的句柄在流关闭时注销）</summary>
    /// <param name="send">推送一条投影帧的回调（由 WebSocket 层串行发送）</param>
    internal IDisposable Attach(Func<DshSessionControlProjection, Task> send) {
        var id = Guid.NewGuid().ToString("N");
        lock (_gate) {
            _listeners[id] = new ControlListener(id, send);
        }
        return new Detacher(this, id);
    }

    /// <summary>广播一条投影帧；单个订阅者异常只记日志，不中断其它订阅</summary>
    /// <param name="frame">投影帧（sessionId / key / value / seq）</param>
    internal async Task PublishAsync(DshSessionControlProjection frame) {
        var targets = Snapshot();
        foreach (var target in targets) {
            try {
                await target.Send(frame);
            } catch (Exception ex) {
                GuiDiag.Log($"[DSH-WEB] control frame push failed listener={target.Id}: {ex.Message}");
            }
        }
    }

    /// <summary>取订阅者快照（同步持锁，await 一律在锁外）</summary>
    private ControlListener[] Snapshot() {
        lock (_gate) {
            return [.. _listeners.Values];
        }
    }

    private void Detach(string id) {
        lock (_gate) {
            _listeners.Remove(id);
        }
    }

    /// <summary>一个控制流订阅者</summary>
    private sealed record ControlListener(string Id, Func<DshSessionControlProjection, Task> Send);

    /// <summary>订阅句柄（幂等注销）</summary>
    private sealed class Detacher(DshSessionControlHub hub, string id) : IDisposable {
        private int _disposed;

        /// <summary>注销订阅</summary>
        public void Dispose() {
            if (Interlocked.Exchange(ref _disposed, 1) == 0) {
                hub.Detach(id);
            }
        }
    }
}
