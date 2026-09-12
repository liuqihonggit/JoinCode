namespace JoinCode.Hands.Desktop.Tests;

/// <summary>
/// Win32DesktopInputService 纯方法单元测试 — 验证标志映射/结构构造逻辑
/// </summary>
public sealed class Win32DesktopInputServiceTests
{
    [Fact]
    public void MouseActionToFlags_Click_ReturnsLeftDownUp()
    {
        var (down, up) = Win32DesktopInputService.MouseActionToFlags(MouseAction.Click);

        down.Should().Be(NativeConstants.MOUSEEVENTF_LEFTDOWN);
        up.Should().Be(NativeConstants.MOUSEEVENTF_LEFTUP);
    }

    [Fact]
    public void MouseActionToFlags_RightClick_ReturnsRightDownUp()
    {
        var (down, up) = Win32DesktopInputService.MouseActionToFlags(MouseAction.RightClick);

        down.Should().Be(NativeConstants.MOUSEEVENTF_RIGHTDOWN);
        up.Should().Be(NativeConstants.MOUSEEVENTF_RIGHTUP);
    }

    [Fact]
    public void MouseActionToFlags_DoubleClick_ReturnsLeftDownUp()
    {
        var (down, up) = Win32DesktopInputService.MouseActionToFlags(MouseAction.DoubleClick);

        down.Should().Be(NativeConstants.MOUSEEVENTF_LEFTDOWN);
        up.Should().Be(NativeConstants.MOUSEEVENTF_LEFTUP);
    }

    [Fact]
    public void MouseActionToFlags_MiddleClick_ReturnsMiddleDownUp()
    {
        var (down, up) = Win32DesktopInputService.MouseActionToFlags(MouseAction.MiddleClick);

        down.Should().Be(NativeConstants.MOUSEEVENTF_MIDDLEDOWN);
        up.Should().Be(NativeConstants.MOUSEEVENTF_MIDDLEUP);
    }

    [Fact]
    public void MouseActionToFlags_LeftDown_ReturnsDownOnly()
    {
        var (down, up) = Win32DesktopInputService.MouseActionToFlags(MouseAction.LeftDown);

        down.Should().Be(NativeConstants.MOUSEEVENTF_LEFTDOWN);
        up.Should().Be(0u);
    }

    [Fact]
    public void MouseActionToFlags_LeftUp_ReturnsUpOnly()
    {
        var (down, up) = Win32DesktopInputService.MouseActionToFlags(MouseAction.LeftUp);

        down.Should().Be(0u);
        up.Should().Be(NativeConstants.MOUSEEVENTF_LEFTUP);
    }

    [Fact]
    public void MouseActionToFlags_Move_ReturnsZeroFlags()
    {
        var (down, up) = Win32DesktopInputService.MouseActionToFlags(MouseAction.Move);

        down.Should().Be(0u);
        up.Should().Be(0u);
    }

    [Fact]
    public void KeyModifierToVirtualKeys_None_ReturnsEmpty()
    {
        var keys = Win32DesktopInputService.KeyModifierToVirtualKeys(KeyModifier.None);

        keys.Should().BeEmpty();
    }

    [Fact]
    public void KeyModifierToVirtualKeys_SingleModifiers_ReturnCorrectVk()
    {
        Win32DesktopInputService.KeyModifierToVirtualKeys(KeyModifier.Shift).Should().Equal((ushort)0x10);
        Win32DesktopInputService.KeyModifierToVirtualKeys(KeyModifier.Control).Should().Equal((ushort)0x11);
        Win32DesktopInputService.KeyModifierToVirtualKeys(KeyModifier.Alt).Should().Equal((ushort)0x12);
        Win32DesktopInputService.KeyModifierToVirtualKeys(KeyModifier.Win).Should().Equal((ushort)0x5B);
    }

    [Fact]
    public void KeyModifierToVirtualKeys_CombinedModifiers_ReturnInShiftCtrlAltWinOrder()
    {
        var keys = Win32DesktopInputService.KeyModifierToVirtualKeys(KeyModifier.Control | KeyModifier.Shift | KeyModifier.Alt | KeyModifier.Win);

        keys.Should().Equal((ushort)0x10, (ushort)0x11, (ushort)0x12, (ushort)0x5B);
    }

    [Fact]
    public void BuildMouseInput_SetsTypeAndFlags()
    {
        var input = Win32DesktopInputService.BuildMouseInput(NativeConstants.MOUSEEVENTF_LEFTDOWN);

        input.type.Should().Be(NativeConstants.INPUT_MOUSE);
        input.u.mi.dwFlags.Should().Be(NativeConstants.MOUSEEVENTF_LEFTDOWN);
    }

    [Fact]
    public void BuildKeyInput_Down_SetsVkAndNoKeyUpFlag()
    {
        var input = Win32DesktopInputService.BuildKeyInput(0x0D, down: true);

        input.type.Should().Be(NativeConstants.INPUT_KEYBOARD);
        input.u.ki.wVk.Should().Be((ushort)0x0D);
        input.u.ki.dwFlags.Should().Be(0u);
    }

    [Fact]
    public void BuildKeyInput_Up_SetsVkAndKeyUpFlag()
    {
        var input = Win32DesktopInputService.BuildKeyInput(0x0D, down: false);

        input.u.ki.wVk.Should().Be((ushort)0x0D);
        input.u.ki.dwFlags.Should().Be(NativeConstants.KEYEVENTF_KEYUP);
    }

    [Fact]
    public void BuildUnicodeInput_SetsScanAndUnicodeFlag()
    {
        var input = Win32DesktopInputService.BuildUnicodeInput((ushort)'A', down: true);

        input.type.Should().Be(NativeConstants.INPUT_KEYBOARD);
        input.u.ki.wVk.Should().Be((ushort)0);
        input.u.ki.wScan.Should().Be((ushort)'A');
        input.u.ki.dwFlags.Should().Be(NativeConstants.KEYEVENTF_UNICODE);
    }

    [Fact]
    public void BuildUnicodeInput_Up_SetsScanAndUnicodeKeyUpFlag()
    {
        var input = Win32DesktopInputService.BuildUnicodeInput((ushort)'中', down: false);

        input.u.ki.wScan.Should().Be((ushort)'中');
        input.u.ki.dwFlags.Should().Be(NativeConstants.KEYEVENTF_UNICODE | NativeConstants.KEYEVENTF_KEYUP);
    }
}
