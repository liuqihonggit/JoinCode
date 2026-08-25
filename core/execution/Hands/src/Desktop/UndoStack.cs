namespace JoinCode.Hands.Desktop;

/// <summary>
/// 桌面操作撤销栈 — 线程安全地维护操作历史，支持回退最近 N 步（PRD U-03）
/// </summary>
[Register(typeof(IUndoStack), ServiceLifetime.Singleton)]
public sealed partial class UndoStack : ServiceEntity, IUndoStack
{
    private readonly Stack<DesktopOperation> _stack = new();
    private readonly object _lock = new();

    /// <summary>记录一个已执行的操作</summary>
    public void Push(DesktopOperation operation)
    {
        ArgumentNullException.ThrowIfNull(operation);
        lock (_lock)
        {
            _stack.Push(operation);
        }
    }

    /// <summary>弹出并返回栈顶操作（撤销一步），栈空返回 null</summary>
    public DesktopOperation? Pop()
    {
        lock (_lock)
        {
            return _stack.Count == 0 ? null : _stack.Pop();
        }
    }

    /// <summary>查看栈顶操作但不弹出，栈空返回 null</summary>
    public DesktopOperation? Peek()
    {
        lock (_lock)
        {
            return _stack.Count == 0 ? null : _stack.Peek();
        }
    }

    /// <summary>当前栈深度</summary>
    public int Count
    {
        get
        {
            lock (_lock)
            {
                return _stack.Count;
            }
        }
    }

    /// <summary>获取最近 N 步操作（不弹出），按时间倒序</summary>
    public IReadOnlyList<DesktopOperation> GetRecent(int count)
    {
        if (count <= 0)
            return [];

        lock (_lock)
        {
            return _stack.Take(count).ToArray();
        }
    }

    /// <summary>清空撤销栈</summary>
    public void Clear()
    {
        lock (_lock)
        {
            _stack.Clear();
        }
    }
}
