namespace JoinCode.Hands.Desktop.Tests;

/// <summary>
/// DesktopInputToolHandlers 纯方法单元测试 — 验证参数解析逻辑
/// </summary>
public sealed class DesktopInputToolHandlersTests
{
    [Theory]
    [InlineData("click", MouseAction.Click)]
    [InlineData("right_click", MouseAction.RightClick)]
    [InlineData("double_click", MouseAction.DoubleClick)]
    [InlineData("middle", MouseAction.MiddleClick)]
    [InlineData("middle_click", MouseAction.MiddleClick)]
    [InlineData("left_down", MouseAction.LeftDown)]
    [InlineData("left_up", MouseAction.LeftUp)]
    public void ParseMouseAction_KnownActions_ReturnCorrectEnum(string input, MouseAction expected)
    {
        DesktopInputToolHandlers.ParseMouseAction(input).Should().Be(expected);
    }

    [Fact]
    public void ParseMouseAction_CaseInsensitive_ReturnsClick()
    {
        DesktopInputToolHandlers.ParseMouseAction("CLICK").Should().Be(MouseAction.Click);
        DesktopInputToolHandlers.ParseMouseAction("RIGHT_CLICK").Should().Be(MouseAction.RightClick);
    }

    [Fact]
    public void ParseMouseAction_UnknownAction_DefaultsToClick()
    {
        DesktopInputToolHandlers.ParseMouseAction("foobar").Should().Be(MouseAction.Click);
    }

    [Fact]
    public void ParseKeyModifier_None_ReturnsNone()
    {
        DesktopInputToolHandlers.ParseKeyModifier("none").Should().Be(KeyModifier.None);
    }

    [Theory]
    [InlineData("shift", KeyModifier.Shift)]
    [InlineData("control", KeyModifier.Control)]
    [InlineData("ctrl", KeyModifier.Control)]
    [InlineData("alt", KeyModifier.Alt)]
    [InlineData("win", KeyModifier.Win)]
    [InlineData("windows", KeyModifier.Win)]
    public void ParseKeyModifier_SingleModifiers_ReturnCorrectEnum(string input, KeyModifier expected)
    {
        DesktopInputToolHandlers.ParseKeyModifier(input).Should().Be(expected);
    }

    [Fact]
    public void ParseKeyModifier_CombinedModifiers_ReturnBitwiseOr()
    {
        DesktopInputToolHandlers.ParseKeyModifier("shift|control").Should().Be(KeyModifier.Shift | KeyModifier.Control);
        DesktopInputToolHandlers.ParseKeyModifier("ctrl|alt|shift").Should().Be(KeyModifier.Control | KeyModifier.Alt | KeyModifier.Shift);
    }

    [Fact]
    public void ParseKeyModifier_CaseInsensitive()
    {
        DesktopInputToolHandlers.ParseKeyModifier("SHIFT").Should().Be(KeyModifier.Shift);
        DesktopInputToolHandlers.ParseKeyModifier("Ctrl").Should().Be(KeyModifier.Control);
    }

    [Fact]
    public void ParseKeyModifier_UnknownPart_Ignored()
    {
        DesktopInputToolHandlers.ParseKeyModifier("shift|foobar").Should().Be(KeyModifier.Shift);
    }
}
