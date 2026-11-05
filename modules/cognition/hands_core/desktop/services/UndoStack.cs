namespace JoinCode.Hands.Desktop;

/// <summary>
/// 桌面操作撤销栈 — 线程安全地维护操作历史，支持回退最近 N 步（PRD U-03）
/// </summary>
[Register(typeof(IUndoStack), ServiceLifetime.Singleton)]
public sealed partial class UndoStack : ServiceEntity, IUndoStack {
    private ImmutableList<DesktopOperation> _stack = ImmutableList<DesktopOperation>.Empty;

    /// <summary>记录一个已执行的操作</summary>
    public void Push(DesktopOperation operation) {
        ArgumentNullException.ThrowIfNull(operation);
        ImmutableInterlocked.Update(ref _stack, list => list.Add(operation));
    }

    /// <summary>弹出并返回栈顶操作（撤销一步），栈空返回 null</summary>
    public DesktopOperation? Pop() {
        DesktopOperation? result = null;
        ImmutableInterlocked.Update(ref _stack, list => {
            if (list.Count == 0)
                return list;
            result = list[list.Count - 1];
            return list.RemoveAt(list.Count - 1);
        });
        return result;
    }

    /// <summary>查看栈顶操作但不弹出，栈空返回 null</summary>
    public DesktopOperation? Peek() {
        var current = Volatile.Read(ref _stack);
        return current.Count == 0 ? null : current[current.Count - 1];
    }

    /// <summary>当前栈深度</summary>
    public int Count => Volatile.Read(ref _stack).Count;

    /// <summary>获取最近 N 步操作（不弹出），按时间倒序</summary>
    public IReadOnlyList<DesktopOperation> GetRecent(int count) {
        if (count <= 0)
            return [];

        var current = Volatile.Read(ref _stack);
        var take = Math.Min(count, current.Count);
        var result = new DesktopOperation[take];
        for (var i = 0; i < take; i++) {
            result[i] = current[current.Count - 1 - i];
        }
        return result;
    }

    /// <summary>清空撤销栈</summary>
    public void Clear() {
        Interlocked.Exchange(ref _stack, ImmutableList<DesktopOperation>.Empty);
    }
}
