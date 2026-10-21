namespace NativeTui.Core.Repl;

/// <summary>
/// 通知优先级 — 对齐基准 context/notifications.tsx PRIORITIES 映射：数值越小出队权重越高
/// （immediate=0 &lt; high=1 &lt; medium=2 &lt; low=3）。
/// </summary>
public enum NotificationPriority : byte
{
    /// <summary>立即显示（抢占当前通知 — 原通知回队等待再显示）。</summary>
    Immediate = 0,

    /// <summary>高优先级（出队权重次高）。</summary>
    High = 1,

    /// <summary>中优先级（默认）。</summary>
    Medium = 2,

    /// <summary>低优先级（出队权重最低）。</summary>
    Low = 3,
}

/// <summary>
/// 通知队列条目 — 不可变 record（fold 替换式合并）。key 契约 = 文本本身：
/// <see cref="ReplNotifications.Enqueue"/> 签名无独立 key 参数，text 兼任去重/fold 身份键
/// （基准 BaseNotification.key 的等价简化 — 通知文本天然唯一标识调用方语义）。
/// </summary>
/// <param name="Key">身份键（= 文本）。</param>
/// <param name="Text">显示文本（不含 fold 计数后缀）。</param>
/// <param name="Priority">出队权重（小值优先）。</param>
/// <param name="TimeoutMs">显示时长毫秒（到期由帧拍 Tick 清除）。</param>
/// <param name="Count">fold 合并计数（同 key 重复入队累加；&gt;1 时显示 xN 后缀）。</param>
internal sealed record NotificationEntry(
    string Key,
    string Text,
    NotificationPriority Priority,
    long TimeoutMs,
    int Count)
{
    /// <summary>显示文本（计数 &gt; 1 追加 " xN" — 基准 fold 合并的计数形态）。</summary>
    public string DisplayText => Count > 1 ? $"{Text} x{Count}" : Text;

    /// <summary>
    /// fold 合并 — 对齐基准 fold(accumulator, incoming)：计数累加，
    /// 参数字段（Priority/TimeoutMs）取 incoming（新通知生效 — 重新计时的显示时长重置语义）。
    /// </summary>
    /// <param name="incoming">新到达的同 key 通知。</param>
    /// <returns>合并后的新条目（调用方替换式写入）。</returns>
    public NotificationEntry Fold(NotificationEntry incoming) => incoming with { Count = Count + incoming.Count };
}

/// <summary>
/// 通知队列状态机 — 对齐基准 context/notifications.tsx（useNotifications 38-229 行）的
/// 队列/优先级/invalidates/fold 语义，时间模型从 React setTimeout 重构为帧拍驱动
/// （禁 Task.Delay/线程）：渲染帧调用 <see cref="Tick"/> 注入时间戳推进
/// （到期清除 → 优先级出队），<see cref="Enqueue"/> 的计时基准为最近帧时间戳
/// （事件与渲染同帧对齐，误差 ≤ 一拍 — 帧拍模型的确定性代价）。
/// invalidates 语义按 Enqueue 契约简化为同 key 替换：同 key 新通知 fold 合并旧的
/// （计数 xN + 新参数生效 + 计时重置）。汉化文案通道保留：通知文本由调用方传入。
/// 纯函数化可测：全部时间输入为注入参数，无墙钟/线程依赖。
/// </summary>
public sealed class ReplNotifications
{
    /// <summary>默认显示时长（基准 DEFAULT_TIMEOUT_MS = 8000ms）。</summary>
    public const long DefaultTimeoutMs = 8000;

    private readonly List<NotificationEntry> _queue = [];
    private NotificationEntry? _current;
    private long _currentStartMs;
    private long _lastTickMs;

    /// <summary>当前显示文本（含 xN 计数后缀；null = 无显示 — FooterBar 行 2 右侧消费）。</summary>
    public string? Current => _current?.DisplayText;

    /// <summary>当前显示条目身份键（null = 无显示）。</summary>
    public string? CurrentKey => _current?.Key;

    /// <summary>待出队条数。</summary>
    public int QueuedCount => _queue.Count;

    /// <summary>帧拍推进（渲染帧调用）：当前通知到期清除 → 队列非空按优先级出队下一条。</summary>
    /// <param name="nowMs">帧时间戳（毫秒）。</param>
    public void Tick(long nowMs)
    {
        _lastTickMs = nowMs;
        if (_current is not null && nowMs - _currentStartMs >= _current.TimeoutMs)
        {
            _current = null;
        }

        if (_current is null)
        {
            DequeueNext(nowMs);
        }
    }

    /// <summary>
    /// 入队通知（key = text）：同 key fold 合并（计数 xN + 新参数生效，当前显示命中则计时重置 —
    /// 基准 fold 分支）；Immediate 直接抢占显示（原当前通知非 Immediate 时回队、
    /// 队内 Immediate 条目互斥清除 — 基准 immediate 分支）；其余入队尾等待出队；
    /// 当前无显示时立即出队生效。空文本忽略（无意义通知）。
    /// </summary>
    /// <param name="text">通知文本（汉化文案由调用方给出；兼任去重/fold 身份键）。</param>
    /// <param name="priority">优先级（默认 Medium）。</param>
    /// <param name="timeout">显示时长（null = 默认 8s）。</param>
    public void Enqueue(string text, NotificationPriority priority = NotificationPriority.Medium, TimeSpan? timeout = null)
    {
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        var now = _lastTickMs;
        var entry = new NotificationEntry(text, text, priority, TimeoutOf(timeout), 1);
        if (priority == NotificationPriority.Immediate)
        {
            EnqueueImmediate(entry, now);
            return;
        }

        // 同 key fold：当前显示合并（invalidates 替换语义 — 基准 fold current 分支重置计时）
        if (_current is { } current && current.Key == entry.Key)
        {
            _current = current.Fold(entry);
            _currentStartMs = now;
            return;
        }

        // 同 key fold：队列内合并（替换原条目）
        var index = _queue.FindIndex(queued => queued.Key == entry.Key);
        if (index >= 0)
        {
            _queue[index] = _queue[index].Fold(entry);
            return;
        }

        _queue.Add(entry);
        if (_current is null)
        {
            DequeueNext(now);
        }
    }

    /// <summary>
    /// 按键移除（当前显示同键立即清除并尝试出队；队内同键移除）— 基准 removeNotification。
    /// 幂等：不存在的键 no-op。
    /// </summary>
    /// <param name="key">身份键（= 通知文本）。</param>
    public void Remove(string key)
    {
        if (_current is { } current && current.Key == key)
        {
            _current = null;
        }

        _queue.RemoveAll(queued => queued.Key == key);
        if (_current is null)
        {
            DequeueNext(_lastTickMs);
        }
    }

    /// <summary>清空（当前显示 + 队列 — 会话重置用）。</summary>
    public void Clear()
    {
        _current = null;
        _queue.Clear();
    }

    /// <summary>
    /// Immediate 入队 — 抢占当前显示：同 key 条目 fold 吸收（当前 + 队列），
    /// 原当前通知（非 Immediate、不同 key）回队尾，队内其余 Immediate 条目互斥清除
    /// （基准 immediate 分支 queue.filter(priority !== 'immediate')）。
    /// </summary>
    /// <param name="entry">新通知（Priority = Immediate）。</param>
    /// <param name="now">最近帧时间戳。</param>
    private void EnqueueImmediate(NotificationEntry entry, long now)
    {
        if (_current is { } current)
        {
            if (current.Key == entry.Key)
            {
                entry = current.Fold(entry);
            }
            else if (current.Priority != NotificationPriority.Immediate)
            {
                _queue.Add(current);
            }
        }

        for (var i = _queue.Count - 1; i >= 0; i--)
        {
            if (_queue[i].Key == entry.Key)
            {
                entry = _queue[i].Fold(entry);
                _queue.RemoveAt(i);
            }
            else if (_queue[i].Priority == NotificationPriority.Immediate)
            {
                _queue.RemoveAt(i);
            }
        }

        _current = entry;
        _currentStartMs = now;
    }

    /// <summary>出队下一条：优先级小值优先、同级 FIFO（先入队优先）；空队列 no-op。</summary>
    /// <param name="nowMs">帧时间戳（新当前通知的计时起点）。</param>
    private void DequeueNext(long nowMs)
    {
        var best = BestIndex();
        if (best < 0)
        {
            return;
        }

        _current = _queue[best];
        _queue.RemoveAt(best);
        _currentStartMs = nowMs;
    }

    /// <summary>队列内最高出队权重索引（小值优先，同级取先入队 — FIFO；空队列 -1）。</summary>
    private int BestIndex()
    {
        var best = -1;
        for (var i = 0; i < _queue.Count; i++)
        {
            if (best < 0 || _queue[i].Priority < _queue[best].Priority)
            {
                best = i;
            }
        }

        return best;
    }

    /// <summary>显示时长换算（null/非正 = 默认 8s）。</summary>
    /// <param name="timeout">调用方指定时长。</param>
    private static long TimeoutOf(TimeSpan? timeout) =>
        timeout is { } span && span.TotalMilliseconds > 0 ? (long)span.TotalMilliseconds : DefaultTimeoutMs;
}
