namespace JoinCode.Abstractions.Interfaces;

/// <summary>
/// 鼠标动作类型 — 对应 Win32 SendInput 的 MOUSEEVENTF_* 标志
/// </summary>
public enum MouseAction
{
    /// <summary>移动光标（不按下）</summary>
    Move,

    /// <summary>左键单击（按下后松开）</summary>
    Click,

    /// <summary>右键单击</summary>
    RightClick,

    /// <summary>左键双击（两次按下松开）</summary>
    DoubleClick,

    /// <summary>中键单击</summary>
    MiddleClick,

    /// <summary>左键按下（拖拽起始）</summary>
    LeftDown,

    /// <summary>左键松开（拖拽结束）</summary>
    LeftUp,

    /// <summary>右键按下（上下文菜单唤起）</summary>
    RightDown,

    /// <summary>右键松开</summary>
    RightUp,
}
