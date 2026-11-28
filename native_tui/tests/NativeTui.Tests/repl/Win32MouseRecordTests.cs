using System.Runtime.InteropServices;

namespace NativeTui.Tests.Repl;

/// <summary>
/// TASK109 P19 — Win32 鼠标记录解码回归。
/// 真机现象：/help 面板与主屏滚轮完全无反应（点击/展开正常）。
/// 根因两层：① <c>InputRecord</c> 把 <c>dwEventFlags</c> 声明在 offset 12（实为 dwControlKeyState），
/// 鼠标记录读到的 flags 恒 0 → 滚轮落进「按钮状态」分支被当成 Release；
/// ② 滚轮方向用 <c>ButtonState &amp; 0xFF000000</c> 判断，正负颠倒。
/// 本门禁锁：字段偏移必须与 MOUSE_EVENT_RECORD 文档一致 + 纯函数解码逐个方向正确。
/// </summary>
public sealed class Win32MouseRecordTests
{
    private const uint EventFlagsNone = 0x0000;
    private const uint EventFlagsMoved = 0x0001;
    private const uint EventFlagsDoubleClick = 0x0002;
    private const uint EventFlagsHorizontalWheel = 0x0004;
    private const uint EventFlagsWheel = 0x0008;

    private const uint ButtonLeftDown = 0x0002;
    private const uint ButtonRightDown = 0x0004;

    private const uint ShiftPressed = 0x0010;
    private const uint LeftCtrlPressed = 0x0008;
    private const uint LeftAltPressed = 0x0002;

    private const int WheelDelta = 120;

    /// <summary>wheel 增量写入 dwButtonState 高 16 位（有符号）。</summary>
    private static uint WheelState(int delta) => unchecked((uint)(delta << 16));

    [Fact]
    public void InputRecord_MouseFields_MatchWin32Offsets()
    {
        Assert.Equal(20, Marshal.SizeOf<Win32InputPump.InputRecord>());
        Assert.Equal(4, (int)Marshal.OffsetOf<Win32InputPump.InputRecord>(nameof(Win32InputPump.InputRecord.MouseX)));
        Assert.Equal(6, (int)Marshal.OffsetOf<Win32InputPump.InputRecord>(nameof(Win32InputPump.InputRecord.MouseY)));
        Assert.Equal(8, (int)Marshal.OffsetOf<Win32InputPump.InputRecord>(nameof(Win32InputPump.InputRecord.ButtonState)));
        // 关键：dwControlKeyState@12、dwEventFlags@16（修复前 EventFlags 误落 12）
        Assert.Equal(12, (int)Marshal.OffsetOf<Win32InputPump.InputRecord>(nameof(Win32InputPump.InputRecord.MouseControlKeyState)));
        Assert.Equal(16, (int)Marshal.OffsetOf<Win32InputPump.InputRecord>(nameof(Win32InputPump.InputRecord.EventFlags)));
    }

    [Fact]
    public void WheelUp_PositiveDelta()
    {
        var evt = Win32InputPump.DecodeMouse(EventFlagsWheel, 10, 20, WheelState(WheelDelta), 0);

        Assert.Equal(MouseKind.WheelUp, evt.Kind);
        Assert.Equal(10, evt.Col);
        Assert.Equal(20, evt.Row);
    }

    [Fact]
    public void WheelDown_NegativeDelta()
    {
        var evt = Win32InputPump.DecodeMouse(EventFlagsWheel, 0, 0, WheelState(-WheelDelta), 0);

        Assert.Equal(MouseKind.WheelDown, evt.Kind);
    }

    [Fact]
    public void HorizontalWheel_RightAndLeft()
    {
        var right = Win32InputPump.DecodeMouse(EventFlagsHorizontalWheel, 0, 0, WheelState(WheelDelta), 0);
        var left = Win32InputPump.DecodeMouse(EventFlagsHorizontalWheel, 0, 0, WheelState(-WheelDelta), 0);

        Assert.Equal(MouseKind.WheelRight, right.Kind);
        Assert.Equal(MouseKind.WheelLeft, left.Kind);
    }

    [Fact]
    public void LeftPress_And_Release()
    {
        var press = Win32InputPump.DecodeMouse(EventFlagsNone, 3, 4, ButtonLeftDown, 0);
        var release = Win32InputPump.DecodeMouse(EventFlagsNone, 3, 4, 0, 0);

        Assert.Equal(MouseKind.Press, press.Kind);
        Assert.Equal(MouseButton.Left, press.Button);
        Assert.Equal(MouseKind.Release, release.Kind);
    }

    [Fact]
    public void RightPress_MapsRightButton()
    {
        var evt = Win32InputPump.DecodeMouse(EventFlagsNone, 0, 0, ButtonRightDown, 0);

        Assert.Equal(MouseKind.Press, evt.Kind);
        Assert.Equal(MouseButton.Right, evt.Button);
    }

    [Fact]
    public void DoubleClick_IsPress()
    {
        var evt = Win32InputPump.DecodeMouse(EventFlagsDoubleClick, 1, 2, ButtonLeftDown, 0);

        Assert.Equal(MouseKind.Press, evt.Kind);
    }

    [Fact]
    public void Modifiers_ComeFromControlKeyState_NotEventFlags()
    {
        var evt = Win32InputPump.DecodeMouse(
            EventFlagsNone,
            0,
            0,
            ButtonLeftDown,
            ShiftPressed | LeftCtrlPressed | LeftAltPressed);

        Assert.True(evt.Shift);
        Assert.True(evt.Ctrl);
        Assert.True(evt.Meta);
    }

    /// <summary>回归：MOUSE_MOVED + 左键按下必须是 Drag（修复前一律落 Press 分支 —
    /// 拖拽每步重锚，选区永远只有 anchor 没有 focus，真机拖拽无高亮不复制）。</summary>
    [Fact]
    public void MouseMoved_WithLeftButtonDown_IsDrag()
    {
        var evt = Win32InputPump.DecodeMouse(EventFlagsMoved, 5, 6, ButtonLeftDown, 0);

        Assert.Equal(MouseKind.Drag, evt.Kind);
        Assert.Equal(MouseButton.Left, evt.Button);
        Assert.Equal(5, evt.Col);
        Assert.Equal(6, evt.Row);
    }

    /// <summary>回归：MOUSE_MOVED 无按键必须是 Move（修复前落 Release 分支 —
    /// hover 高亮依赖 Move 事件，真机 hover 永远不触发）。</summary>
    [Fact]
    public void MouseMoved_NoButton_IsMove()
    {
        var evt = Win32InputPump.DecodeMouse(EventFlagsMoved, 7, 8, 0, 0);

        Assert.Equal(MouseKind.Move, evt.Kind);
    }

    /// <summary>回归：滚轮记录的 dwEventFlags=WHEELED 不得被当成修饰键（修复前 ControlKeyState 读的是该字段，
    /// 0x0008 会被解释成 LEFT_CTRL_PRESSED）。</summary>
    [Fact]
    public void WheelRecord_DoesNotReportPhantomCtrl()
    {
        var evt = Win32InputPump.DecodeMouse(EventFlagsWheel, 0, 0, WheelState(WheelDelta), 0);

        Assert.False(evt.Ctrl);
        Assert.False(evt.Shift);
        Assert.False(evt.Meta);
    }
}
