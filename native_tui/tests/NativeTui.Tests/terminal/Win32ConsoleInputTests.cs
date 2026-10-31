namespace NativeTui.Tests.Terminal;

/// <summary>
/// Win32ConsoleInput 单元测试 — 验证 INPUT_RECORD 布局偏移（修复前 VirtualKeyCode@14 与
/// UnicodeChar@14 重叠、EventFlags@12 误读 ControlKeyState）+ MakeMouseEvent/MakeKeyInfo 转换逻辑。
/// TASK077 步骤 2 — 布局 bug TDD 复现→修复验证。
/// </summary>
public class Win32ConsoleInputTests
{
    /// <summary>KEY_EVENT VirtualKeyCode@10 与 UnicodeChar@14 不重叠（修复前同 @14 互相覆盖）。</summary>
    [Fact]
    public void InputRecord_KeyEvent_VirtualKeyCode_And_UnicodeChar_DoNot_Overlap()
    {
        Win32ConsoleInput.InputRecord record = default;
        record.EventType = 1;
        record.VirtualKeyCode = 0x41;
        record.UnicodeChar = 'B';
        Assert.Equal(0x41, record.VirtualKeyCode);
        Assert.Equal('B', record.UnicodeChar);
    }

    /// <summary>KEY_EVENT RepeatCount@8 与 VirtualKeyCode@10 不重叠（修复前 RepeatCount@10 与 VirtualKeyCode@10 重叠）。</summary>
    [Fact]
    public void InputRecord_KeyEvent_RepeatCount_And_VirtualKeyCode_Do_Not_Overlap()
    {
        Win32ConsoleInput.InputRecord record = default;
        record.EventType = 1;
        record.RepeatCount = 0x1234;
        record.VirtualKeyCode = 0x5678;
        Assert.Equal(0x1234, record.RepeatCount);
        Assert.Equal(0x5678, record.VirtualKeyCode);
    }

    /// <summary>MOUSE_EVENT EventFlags@16 与 MouseControlKeyState@12 不重叠（修复前 EventFlags@12 误读 ControlKeyState）。</summary>
    [Fact]
    public void InputRecord_MouseEvent_EventFlags_And_ControlKeyState_Do_Not_Overlap()
    {
        Win32ConsoleInput.InputRecord record = default;
        record.EventType = 2;
        record.MouseControlKeyState = 0x0010;
        record.EventFlags = 0x0008;
        Assert.Equal(0x0010u, record.MouseControlKeyState);
        Assert.Equal(0x0008u, record.EventFlags);
    }

    /// <summary>MOUSE_EVENT ButtonState@8 与 MouseControlKeyState@12 不重叠。</summary>
    [Fact]
    public void InputRecord_MouseEvent_ButtonState_And_ControlKeyState_Do_Not_Overlap()
    {
        Win32ConsoleInput.InputRecord record = default;
        record.EventType = 2;
        record.ButtonState = 0x0002;
        record.MouseControlKeyState = 0x0010;
        Assert.Equal(0x0002u, record.ButtonState);
        Assert.Equal(0x0010u, record.MouseControlKeyState);
    }

    /// <summary>左键 Press — ButtonState=FROM_LEFT_1ST_BUTTON_PRESSED(0x0002)。</summary>
    [Fact]
    public void MakeMouseEvent_Left_Press()
    {
        Win32ConsoleInput.InputRecord record = default;
        record.EventType = 2;
        record.ButtonState = 0x0002;
        record.EventFlags = 0;
        record.MouseX = 5;
        record.MouseY = 10;
        var mouse = Win32ConsoleInput.MakeMouseEvent(record);
        Assert.Equal(MouseKind.Press, mouse.Kind);
        Assert.Equal(MouseButton.Left, mouse.Button);
        Assert.Equal(5, mouse.Col);
        Assert.Equal(10, mouse.Row);
    }

    /// <summary>右键 Press — ButtonState=RIGHTMOST_BUTTON_PRESSED(0x0004)。</summary>
    [Fact]
    public void MakeMouseEvent_Right_Press()
    {
        Win32ConsoleInput.InputRecord record = default;
        record.EventType = 2;
        record.ButtonState = 0x0004;
        record.EventFlags = 0;
        var mouse = Win32ConsoleInput.MakeMouseEvent(record);
        Assert.Equal(MouseKind.Press, mouse.Kind);
        Assert.Equal(MouseButton.Right, mouse.Button);
    }

    /// <summary>Release — ButtonState=0。</summary>
    [Fact]
    public void MakeMouseEvent_Release()
    {
        Win32ConsoleInput.InputRecord record = default;
        record.EventType = 2;
        record.ButtonState = 0;
        record.EventFlags = 0;
        var mouse = Win32ConsoleInput.MakeMouseEvent(record);
        Assert.Equal(MouseKind.Release, mouse.Kind);
    }

    /// <summary>滚轮上 — EventFlags=MOUSE_WHEELED(0x0008)，ButtonState 高字节非零。</summary>
    [Fact]
    public void MakeMouseEvent_WheelUp()
    {
        Win32ConsoleInput.InputRecord record = default;
        record.EventType = 2;
        record.ButtonState = 0xFF000000;
        record.EventFlags = 0x0008;
        var mouse = Win32ConsoleInput.MakeMouseEvent(record);
        Assert.Equal(MouseKind.WheelUp, mouse.Kind);
    }

    /// <summary>滚轮下 — EventFlags=MOUSE_WHEELED，ButtonState 高字节为零。</summary>
    [Fact]
    public void MakeMouseEvent_WheelDown()
    {
        Win32ConsoleInput.InputRecord record = default;
        record.EventType = 2;
        record.ButtonState = 0x00FF0000;
        record.EventFlags = 0x0008;
        var mouse = Win32ConsoleInput.MakeMouseEvent(record);
        Assert.Equal(MouseKind.WheelDown, mouse.Kind);
    }

    /// <summary>修饰位 Shift — MouseControlKeyState=SHIFT_PRESSED(0x0010)。</summary>
    [Fact]
    public void MakeMouseEvent_Shift_Modifier()
    {
        Win32ConsoleInput.InputRecord record = default;
        record.EventType = 2;
        record.ButtonState = 0x0002;
        record.MouseControlKeyState = 0x0010;
        var mouse = Win32ConsoleInput.MakeMouseEvent(record);
        Assert.True(mouse.Shift);
        Assert.False(mouse.Ctrl);
        Assert.False(mouse.Meta);
    }

    /// <summary>修饰位 Ctrl — MouseControlKeyState=LEFT_CTRL_PRESSED(0x0004)|RIGHT_CTRL_PRESSED(0x0008)=0x000C。</summary>
    [Fact]
    public void MakeMouseEvent_Ctrl_Modifier()
    {
        Win32ConsoleInput.InputRecord record = default;
        record.EventType = 2;
        record.ButtonState = 0x0002;
        record.MouseControlKeyState = 0x000C;
        var mouse = Win32ConsoleInput.MakeMouseEvent(record);
        Assert.False(mouse.Shift);
        Assert.True(mouse.Ctrl);
        Assert.False(mouse.Meta);
    }

    /// <summary>修饰位 Alt — MouseControlKeyState=LEFT_ALT_PRESSED(0x0002)|RIGHT_ALT_PRESSED(0x0001)=0x0003。</summary>
    [Fact]
    public void MakeMouseEvent_Alt_Modifier()
    {
        Win32ConsoleInput.InputRecord record = default;
        record.EventType = 2;
        record.ButtonState = 0x0002;
        record.MouseControlKeyState = 0x0003;
        var mouse = Win32ConsoleInput.MakeMouseEvent(record);
        Assert.False(mouse.Shift);
        Assert.False(mouse.Ctrl);
        Assert.True(mouse.Meta);
    }

    /// <summary>无修饰位 — MouseControlKeyState=0 全 false。</summary>
    [Fact]
    public void MakeMouseEvent_No_Modifiers()
    {
        Win32ConsoleInput.InputRecord record = default;
        record.EventType = 2;
        record.ButtonState = 0x0002;
        record.MouseControlKeyState = 0;
        var mouse = Win32ConsoleInput.MakeMouseEvent(record);
        Assert.False(mouse.Shift);
        Assert.False(mouse.Ctrl);
        Assert.False(mouse.Meta);
    }

    /// <summary>MakeKeyInfo 修饰位 — ControlKeyState 含 Shift/Ctrl/Alt。</summary>
    [Fact]
    public void MakeKeyInfo_Modifiers()
    {
        Win32ConsoleInput.InputRecord record = default;
        record.EventType = 1;
        record.VirtualKeyCode = 0x41;
        record.UnicodeChar = 'A';
        record.ControlKeyState = 0x0010 | 0x0006 | 0x0003;
        var keyInfo = Win32ConsoleInput.MakeKeyInfo(record);
        Assert.True(keyInfo.Modifiers.HasFlag(ConsoleModifiers.Shift));
        Assert.True(keyInfo.Modifiers.HasFlag(ConsoleModifiers.Control));
        Assert.True(keyInfo.Modifiers.HasFlag(ConsoleModifiers.Alt));
    }

    /// <summary>MakeKeyInfo VK 码映射 — VirtualKeyCode=0x41 → ConsoleKey.A。</summary>
    [Fact]
    public void MakeKeyInfo_VkCode_Mapping()
    {
        Win32ConsoleInput.InputRecord record = default;
        record.EventType = 1;
        record.VirtualKeyCode = 0x41;
        record.UnicodeChar = 'a';
        record.ControlKeyState = 0;
        var keyInfo = Win32ConsoleInput.MakeKeyInfo(record);
        Assert.Equal(ConsoleKey.A, keyInfo.Key);
        Assert.Equal('a', keyInfo.KeyChar);
    }
}
