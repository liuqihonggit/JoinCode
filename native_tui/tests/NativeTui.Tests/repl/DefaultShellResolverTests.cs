namespace NativeTui.Tests.Repl;

/// <summary>
/// defaultShell 键解析器测试 — settings.current.defaultShell（"bash"/"powershell"）覆盖平台默认；
/// 对齐 TS resolveDefaultShell（默认 bash，不自动切 Windows PowerShell）。
/// C# 平台适配差异：未配置时 Windows 用 cmd.exe（C# 既有决策，登记差异）。
/// </summary>
public sealed class DefaultShellResolverTests
{
    [Fact]
    public void Resolve_Bash_Explicitly_ReturnsBash()
    {
        var (fileName, flag) = DefaultShellResolver.Resolve("bash", isWindows: false);
        Assert.Equal("bash", fileName);
        Assert.Equal("-c", flag);
    }

    [Fact]
    public void Resolve_PowerShell_Explicitly_ReturnsPwsh()
    {
        var (fileName, flag) = DefaultShellResolver.Resolve("powershell", isWindows: false);
        Assert.Equal("pwsh", fileName);
        Assert.Equal("-Command", flag);
    }

    [Fact]
    public void Resolve_NullOnWindows_FallsBackToCmdExe()
    {
        var (fileName, flag) = DefaultShellResolver.Resolve(null, isWindows: true);
        Assert.Equal("cmd.exe", fileName);
        Assert.Equal("/c", flag);
    }

    [Fact]
    public void Resolve_NullOnNonWindows_FallsBackToBash()
    {
        var (fileName, flag) = DefaultShellResolver.Resolve(null, isWindows: false);
        Assert.Equal("bash", fileName);
        Assert.Equal("-c", flag);
    }

    [Fact]
    public void Resolve_BashOnWindows_OverridesCmdExe()
    {
        var (fileName, flag) = DefaultShellResolver.Resolve("bash", isWindows: true);
        Assert.Equal("bash", fileName);
        Assert.Equal("-c", flag);
    }

    [Fact]
    public void Resolve_UnknownValue_FallsBackToPlatformDefault()
    {
        var (fileName, _) = DefaultShellResolver.Resolve("zsh", isWindows: false);
        Assert.Equal("bash", fileName);
    }
}
