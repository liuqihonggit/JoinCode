namespace NativeTui.Tests.Repl.Input;

/// <summary>
/// Win32ConsoleInputPump 单元测试 — ConvertEvent 事件转换逻辑 + Drain/Dispose 冒烟。
/// TASK077 步骤 4 — ReadConsoleInput 阻塞读由 spike 手动验收（ADR 0080），转换逻辑单元覆盖。
/// </summary>
public class Win32ConsoleInputPumpTests
{
    /// <summary>键盘事件 → ConsoleKeyAdapter.ToKeyPress 归一 KeyPress。</summary>
    [Fact]
    public void ConvertEvent_Key_ReturnsKeyPress()
    {
        var keyInfo = new ConsoleKeyInfo('a', ConsoleKey.A, false, false, false);
        var evt = new Win32ConsoleInput.InputEvent(keyInfo, null);
        var key = Win32ConsoleInputPump.ConvertEvent(evt);
        Assert.NotNull(key);
        Assert.Equal(KeyKind.Char, key!.Kind);
        Assert.Equal("a", key.Input);
    }

    /// <summary>鼠标事件 → KeyPress(Mouse) 包装，修饰位透传。</summary>
    [Fact]
    public void ConvertEvent_Mouse_ReturnsMouseKeyPress()
    {
        var mouse = new MouseEvent(MouseKind.Press, MouseButton.Left, 5, 10, Shift: true, Meta: false, Ctrl: true);
        var evt = new Win32ConsoleInput.InputEvent(null, mouse);
        var key = Win32ConsoleInputPump.ConvertEvent(evt);
        Assert.NotNull(key);
        Assert.Equal(KeyKind.Mouse, key!.Kind);
        Assert.NotNull(key.Mouse);
        Assert.Equal(MouseKind.Press, key.Mouse!.Value.Kind);
        Assert.Equal(MouseButton.Left, key.Mouse!.Value.Button);
        Assert.Equal(5, key.Mouse!.Value.Col);
        Assert.Equal(10, key.Mouse!.Value.Row);
        Assert.True(key.Mouse!.Value.Shift);
        Assert.False(key.Mouse!.Value.Meta);
        Assert.True(key.Mouse!.Value.Ctrl);
        Assert.True(key.Shift);
        Assert.False(key.Meta);
        Assert.True(key.Ctrl);
    }

    /// <summary>空事件（Key/Mouse 均 null）→ null。</summary>
    [Fact]
    public void ConvertEvent_Empty_ReturnsNull()
    {
        var evt = new Win32ConsoleInput.InputEvent(null, null);
        Assert.Null(Win32ConsoleInputPump.ConvertEvent(evt));
    }

    /// <summary>Drain 空通道返回 false（无键就绪）。</summary>
    [Fact]
    public void Drain_EmptyChannel_ReturnsFalse()
    {
        using var pump = new Win32ConsoleInputPump();
        var dispatched = pump.Drain(_ => { });
        Assert.False(dispatched);
    }

    /// <summary>Dispose 不崩溃（幂等 — 多次 Dispose 安全）。</summary>
    [Fact]
    public void Dispose_Idempotent()
    {
        var pump = new Win32ConsoleInputPump();
        pump.Dispose();
        pump.Dispose();
    }
}
