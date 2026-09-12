namespace JoinCode.Abstractions.Interfaces;

/// <summary>
/// 撤销栈 — 维护桌面操作历史，支持回退最近 N 步（PRD U-03）
/// </summary>
public interface IUndoStack
{
    /// <summary>记录一个已执行的操作</summary>
    void Push(DesktopOperation operation);

    /// <summary>弹出并返回栈顶操作（撤销一步），栈空返回 null</summary>
    DesktopOperation? Pop();

    /// <summary>查看栈顶操作但不弹出，栈空返回 null</summary>
    DesktopOperation? Peek();

    /// <summary>当前栈深度</summary>
    int Count { get; }

    /// <summary>获取最近 N 步操作（不弹出），按时间倒序</summary>
    IReadOnlyList<DesktopOperation> GetRecent(int count);

    /// <summary>清空撤销栈</summary>
    void Clear();
}
