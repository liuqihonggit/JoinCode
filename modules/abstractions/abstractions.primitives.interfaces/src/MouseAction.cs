namespace JoinCode.Abstractions.Interfaces;

/// <summary>
/// 鼠标动作类型 — 对应 Win32 SendInput 的 MOUSEEVENTF_* 标志
/// </summary>
public enum MouseAction {
    /// <summary>移动光标（不按下）</summary>
    [EnumValue("move")] Move,

    /// <summary>左键单击（按下后松开）</summary>
    [EnumValue("click")] Click,

    /// <summary>右键单击</summary>
    [EnumValue("right_click")] RightClick,

    /// <summary>左键双击（两次按下松开）</summary>
    [EnumValue("double_click")] DoubleClick,

    /// <summary>中键单击</summary>
    [EnumValue("middle_click")] MiddleClick,

    /// <summary>左键按下（拖拽起始）</summary>
    [EnumValue("left_down")] LeftDown,

    /// <summary>左键松开（拖拽结束）</summary>
    [EnumValue("left_up")] LeftUp,

    /// <summary>右键按下（上下文菜单唤起）</summary>
    [EnumValue("right_down")] RightDown,

    /// <summary>右键松开</summary>
    [EnumValue("right_up")] RightUp,
}