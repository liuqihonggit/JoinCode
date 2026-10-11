namespace Core.Utils;

/// <summary>
/// 统一异步 stderr 写入器 — 所有诊断日志通过此管道输出,消除 Console.Error 同步阻塞导致的管道死锁。
/// <para>ConcurrentQueue 缓存 + 后台 Timer 消费,队列满时丢弃旧消息(非阻塞)。</para>
/// <para>锁操作/插件加载/DI 初始化 永不阻塞在日志输出上。</para>
/// </summary>
public static class AsyncStderrWriter {
    private static readonly ConcurrentQueue<string> _queue = new();
    private static Timer? _drainTimer;
    private static int _drainStarted;
    private const int MaxQueueSize = 16384;

    /// <summary>
    /// 非阻塞入队 — 消息入队后立即返回,后台线程异步写入 stderr。
    /// 队列满时丢弃旧消息,保证调用方永不阻塞。
    /// </summary>
    public static void Enqueue(string message) {
        _queue.Enqueue(message);
        while (_queue.Count > MaxQueueSize && _queue.TryDequeue(out _)) { }
        EnsureDrainStarted();
    }

    /// <summary>
    /// 刷新 — 同步等待队列中所有消息写入 stderr。
    /// 用于进程退出前确保日志不丢失。
    /// </summary>
    public static void Flush() {
        while (_queue.TryDequeue(out var msg)) {
            try { Console.Error.WriteLine(msg); } catch (IOException) { break; }
        }
    }

    private static void EnsureDrainStarted() {
        if (Interlocked.CompareExchange(ref _drainStarted, 1, 0) != 0) return;
        _drainTimer = new Timer(static _ => DrainQueue(), null, TimeSpan.Zero, TimeSpan.FromMilliseconds(50));
    }

    private static void DrainQueue() {
        while (_queue.TryDequeue(out var msg)) {
            try { Console.Error.WriteLine(msg); } catch (IOException) { break; }
        }
    }
}
