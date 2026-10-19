namespace NativeTui.Tests.Repl;

/// <summary>
/// ConsoleKeyAdapter 测试 — ReadKey 路径的修饰符归一：
/// Shift+Tab → BackTab（Windows Terminal 在 VT 模式下发 ESC[Z，ReadKey 退化路径同样归一）。
/// </summary>
public sealed class ConsoleKeyAdapterTests
{
    [Fact]
    public void PlainTab_MapsToTab()
    {
        var key = ConsoleKeyAdapter.ToKeyPress(new ConsoleKeyInfo('\t', ConsoleKey.Tab, shift: false, alt: false, control: false));
        Assert.NotNull(key);
        Assert.Equal(KeyKind.Tab, key!.Kind);
    }

    [Fact]
    public void ShiftTab_MapsToBackTab()
    {
        var key = ConsoleKeyAdapter.ToKeyPress(new ConsoleKeyInfo('\t', ConsoleKey.Tab, shift: true, alt: false, control: false));
        Assert.NotNull(key);
        Assert.Equal(KeyKind.BackTab, key!.Kind);
        Assert.True(key.Shift);
    }
}
