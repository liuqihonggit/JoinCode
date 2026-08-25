namespace JoinCode.Hands.Desktop.Tests;

/// <summary>
/// Win32WindowManagementService 纯方法单元测试 — 验证窗口匹配逻辑
/// </summary>
public sealed class Win32WindowManagementServiceTests
{
    private static WindowInfo MakeInfo(string title, string? processName = null) =>
        new(IntPtr.Zero, title, processName, new WindowRect(0, 0, 100, 100), IsVisible: true);

    [Fact]
    public void MatchWindow_TitleContainsQuery_ReturnsTrue()
    {
        var info = MakeInfo("记事本 - 无标题", "notepad");

        Win32WindowManagementService.MatchWindow(info, "记事本").Should().BeTrue();
    }

    [Fact]
    public void MatchWindow_ProcessNameContainsQuery_ReturnsTrue()
    {
        var info = MakeInfo("Untitled", "notepad");

        Win32WindowManagementService.MatchWindow(info, "notepad").Should().BeTrue();
    }

    [Fact]
    public void MatchWindow_NoMatch_ReturnsFalse()
    {
        var info = MakeInfo("Untitled", "notepad");

        Win32WindowManagementService.MatchWindow(info, "calculator").Should().BeFalse();
    }

    [Fact]
    public void MatchWindow_CaseInsensitive_ReturnsTrue()
    {
        var info = MakeInfo("Untitled", "notepad");

        Win32WindowManagementService.MatchWindow(info, "NOTEPAD").Should().BeTrue();
    }

    [Fact]
    public void MatchWindow_EmptyQuery_ReturnsFalse()
    {
        var info = MakeInfo("Untitled", "notepad");

        Win32WindowManagementService.MatchWindow(info, "").Should().BeFalse();
    }

    [Fact]
    public void MatchWindow_NullProcessName_OnlyMatchesTitle()
    {
        var info = MakeInfo("MyWindow", processName: null);

        Win32WindowManagementService.MatchWindow(info, "MyWindow").Should().BeTrue();
        Win32WindowManagementService.MatchWindow(info, "nope").Should().BeFalse();
    }

    [Fact]
    public void MatchWindow_PartialTitleMatch_ReturnsTrue()
    {
        var info = MakeInfo("项目 - Visual Studio Code", "Code");

        Win32WindowManagementService.MatchWindow(info, "Visual Studio").Should().BeTrue();
    }
}
