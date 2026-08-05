namespace Hands.Tests.Shell;

/// <summary>
/// PathConverter + EnvironmentProbeService.GatePath 单元测试
/// 验证路径门控核心逻辑在不同平台和 Shell 类型下的行为
/// </summary>
public class EnvironmentProbeServicePathGateTests
{
    #region PathConverter.WindowsPathToPosixPath — 纯静态逻辑，不依赖平台

    [Theory]
    [InlineData("C:\\Users\\test", "/c/Users/test")]
    [InlineData("D:\\project\\w3", "/d/project/w3")]
    [InlineData("c:\\Users\\test", "/c/Users/test")]
    [InlineData("Z:\\foo\\bar", "/z/foo/bar")]
    [InlineData("C:/Users/test", "/c/Users/test")]
    [InlineData("D:/project/w3", "/d/project/w3")]
    public void WindowsPathToPosixPath_DriveLetter_ConvertsCorrectly(string input, string expected)
    {
        var result = PathConverter.WindowsPathToPosixPath(input);
        result.Should().Be(expected);
    }

    [Theory]
    [InlineData("\\\\server\\share\\path", "//server/share/path")]
    [InlineData("\\\\192.168.1.1\\c$\\Windows", "//192.168.1.1/c$/Windows")]
    public void WindowsPathToPosixPath_UncPath_ConvertsCorrectly(string input, string expected)
    {
        var result = PathConverter.WindowsPathToPosixPath(input);
        result.Should().Be(expected);
    }

    [Theory]
    [InlineData("relative\\path", "relative/path")]
    [InlineData("path", "path")]
    [InlineData("", "")]
    public void WindowsPathToPosixPath_RelativeOrEmpty_ReturnsAsIs(string input, string expected)
    {
        var result = PathConverter.WindowsPathToPosixPath(input);
        result.Should().Be(expected);
    }

    [Fact]
    public void WindowsPathToPosixPath_NullInput_ReturnsNull()
    {
        var result = PathConverter.WindowsPathToPosixPath(null!);
        result.Should().BeNull();
    }

    #endregion

    #region PathConverter.PosixPathToWindowsPath

    [Theory]
    [InlineData("/c/Users/test", "C:\\Users\\test")]
    [InlineData("/d/project/w3", "D:\\project\\w3")]
    [InlineData("C:/Users/test", "C:\\Users\\test")]
    [InlineData("D:/project/w3", "D:\\project\\w3")]
    [InlineData("//server/share/path", "\\\\server\\share\\path")]
    [InlineData("//192.168.1.1/c$/Windows", "\\\\192.168.1.1\\c$\\Windows")]
    public void PosixPathToWindowsPath_PosixDriveLetter_ConvertsCorrectly(string input, string expected)
    {
        var result = PathConverter.PosixPathToWindowsPath(input);
        result.Should().Be(expected);
    }

    [Theory]
    [InlineData("C:\\Users\\test", "C:\\Users\\test")]
    [InlineData("C:/Users/test", "C:\\Users\\test")]
    [InlineData("/home/user", "/home/user")]
    [InlineData("", "")]
    public void PosixPathToWindowsPath_NonPosixDriveLetter_ReturnsAsIs(string input, string expected)
    {
        var result = PathConverter.PosixPathToWindowsPath(input);
        result.Should().Be(expected);
    }

    [Fact]
    public void PosixPathToWindowsPath_NullInput_ReturnsNull()
    {
        var result = PathConverter.PosixPathToWindowsPath(null!);
        result.Should().BeNull();
    }

    #endregion

    #region PathConverter.LooksLikeWindowsPath

    [Theory]
    [InlineData("C:\\Users\\test", true)]
    [InlineData("c:\\path", true)]
    [InlineData("\\\\server\\share", true)]
    [InlineData("//server/share", true)]
    [InlineData("/home/user", false)]
    [InlineData("relative/path", false)]
    [InlineData("", false)]
    public void LooksLikeWindowsPath_DetectsCorrectly(string input, bool expected)
    {
        PathConverter.LooksLikeWindowsPath(input).Should().Be(expected);
    }

    #endregion

    #region PathConverter.GateCommandPaths

    [Theory]
    [InlineData("cat C:\\Users\\test\\file.txt", true, "cat /c/Users/test/file.txt")]
    [InlineData("cat C:\\Users\\test\\file.txt", false, "cat C:\\Users\\test\\file.txt")]
    [InlineData("cd /c/Users/test; npm run build", false, "cd C:\\Users\\test; npm run build")]
    [InlineData("cd /c/Users/test; npm run build", true, "cd /c/Users/test; npm run build")]
    [InlineData("echo hello", true, "echo hello")]
    [InlineData("echo hello", false, "echo hello")]
    [InlineData("cat D:\\project\\w3\\src\\file.cs", true, "cat /d/project/w3/src/file.cs")]
    [InlineData("python D:/project/script.py", true, "python /d/project/script.py")]
    [InlineData("python D:/project/script.py", false, "python D:\\project\\script.py")]
    [InlineData("cat C:/Users/test/file.txt", false, "cat C:\\Users\\test\\file.txt")]
    public void GateCommandPaths_ConvertsPathsInCommand(string input, bool toPosix, string expected)
    {
        var result = PathConverter.GateCommandPaths(input, toPosix);
        result.Should().Be(expected);
    }

    [Fact]
    public void GateCommandPaths_ExcludesUrls()
    {
        var cmd = "curl https://api.example.com/data C:\\Users\\test\\output.json";
        var result = PathConverter.GateCommandPaths(cmd, toPosix: true);
        result.Should().Contain("https://api.example.com/data");
        result.Should().Contain("/c/Users/test/output.json");
    }

    [Fact]
    public void GateCommandPaths_MultiplePaths()
    {
        var cmd = "copy C:\\Users\\test\\a.txt C:\\Users\\test\\b.txt";
        var result = PathConverter.GateCommandPaths(cmd, toPosix: true);
        result.Should().Be("copy /c/Users/test/a.txt /c/Users/test/b.txt");
    }

    [Fact]
    public void GateCommandPaths_NullOrEmpty_ReturnsAsIs()
    {
        PathConverter.GateCommandPaths(null!, true).Should().BeNull();
        PathConverter.GateCommandPaths("", true).Should().Be("");
    }

    #endregion

    #region GatePath — 集成测试（依赖平台）

    /// <summary>
    /// GatePath 在 Windows + Bash 下应将 Windows 路径转为 POSIX
    /// 注意：此测试在 Windows 上运行时才验证 Windows 行为
    /// </summary>
    [Fact]
    public void GatePath_WindowsBash_ConvertsToPosix()
    {
        if (!OperatingSystem.IsWindows()) return;

        var sut = CreateSut();
        var result = sut.GatePath("C:\\Users\\test", MockProvider(SystemActuatorKind.Bash));
        result.Should().Be("/c/Users/test");
    }

    [Fact]
    public void GatePath_WindowsPowerShell_KeepsWindowsFormat()
    {
        if (!OperatingSystem.IsWindows()) return;

        var sut = CreateSut();
        var result = sut.GatePath("C:\\Users\\test", MockProvider(SystemActuatorKind.PowerShell));
        result.Should().Be("C:\\Users\\test");
    }

    [Fact]
    public void GatePath_WindowsCmd_KeepsWindowsFormat()
    {
        if (!OperatingSystem.IsWindows()) return;

        var sut = CreateSut();
        var result = sut.GatePath("C:\\Users\\test", MockProvider(SystemActuatorKind.Cmd));
        result.Should().Be("C:\\Users\\test");
    }

    [Fact]
    public void GatePath_WindowsPowerShell_PosixInput_ConvertsToWindows()
    {
        if (!OperatingSystem.IsWindows()) return;

        var sut = CreateSut();
        var result = sut.GatePath("/c/Users/test", MockProvider(SystemActuatorKind.PowerShell));
        result.Should().Be("C:\\Users\\test");
    }

    [Fact]
    public void GatePath_EmptyOrNull_ReturnsAsIs()
    {
        var sut = CreateSut();
        sut.GatePath("", MockProvider(SystemActuatorKind.Bash)).Should().Be("");
        sut.GatePath(null!, MockProvider(SystemActuatorKind.Bash)).Should().BeNull();
    }

    [Fact]
    public void GatePath_WindowsBash_PosixInput_NoChange()
    {
        if (!OperatingSystem.IsWindows()) return;

        var sut = CreateSut();
        var result = sut.GatePath("/c/Users/test", MockProvider(SystemActuatorKind.Bash));
        result.Should().Be("/c/Users/test");
    }

    [Fact]
    public void GatePath_WindowsPowerShell_ForwardSlashPath_ConvertsToBackslash()
    {
        if (!OperatingSystem.IsWindows()) return;

        var sut = CreateSut();
        var result = sut.GatePath("C:/Users/test", MockProvider(SystemActuatorKind.PowerShell));
        result.Should().Be("C:\\Users\\test");
    }

    [Fact]
    public void GatePath_WindowsCmd_ForwardSlashPath_ConvertsToBackslash()
    {
        if (!OperatingSystem.IsWindows()) return;

        var sut = CreateSut();
        var result = sut.GatePath("C:/Users/test", MockProvider(SystemActuatorKind.Cmd));
        result.Should().Be("C:\\Users\\test");
    }

    [Fact]
    public void GatePath_WindowsPython_ForwardSlashPath_ConvertsToBackslash()
    {
        if (!OperatingSystem.IsWindows()) return;

        var sut = CreateSut();
        var result = sut.GatePath("C:/Users/test", MockProvider(SystemActuatorKind.Python));
        result.Should().Be("C:\\Users\\test");
    }

    [Fact]
    public void GatePath_WindowsPython_PosixInput_ConvertsToWindows()
    {
        if (!OperatingSystem.IsWindows()) return;

        var sut = CreateSut();
        var result = sut.GatePath("/c/Users/test", MockProvider(SystemActuatorKind.Python));
        result.Should().Be("C:\\Users\\test");
    }

    #endregion

    private static ISystemActuator MockProvider(SystemActuatorKind kind)
    {
        var mock = Mock.Of<ISystemActuator>(p => p.Kind == kind);
        return mock;
    }

    private static EnvironmentProbeService CreateSut()
        => new(Mock.Of<IToolHealthMonitor>(), NullLogger<EnvironmentProbeService>.Instance);
}
