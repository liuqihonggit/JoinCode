// JCC1018 豁免: 本测试以 Windows 路径字面量为被测数据（Windows 形态行为/守护判定/端点解析等），
// 被测对象即 Windows 路径语义，非跨平台误用 — 新增跨平台匹配测试请用 Path.Combine(GetTempPath()) 构造。
#pragma warning disable JCC1018
// JCC11003 抑制: 存量代码可空抑制, 后续逐步修复
namespace Hands.Tests.Shell;

/// <summary>
/// PathConverter 单元测试 — 验证路径转换核心逻辑
/// </summary>
public class EnvironmentProbeServicePathGateTests {
    #region PathConverter.WindowsPathToPosixPath — 纯静态逻辑，不依赖平台

    [Theory]
    [InlineData("C:\\Users\\test", "/c/Users/test")]
    [InlineData("D:\\project\\w3", "/d/project/w3")]
    [InlineData("c:\\Users\\test", "/c/Users/test")]
    [InlineData("Z:\\foo\\bar", "/z/foo/bar")]
    [InlineData("C:/Users/test", "/c/Users/test")]
    [InlineData("D:/project/w3", "/d/project/w3")]
    public void WindowsPathToPosixPath_DriveLetter_ConvertsCorrectly(string input, string expected) {
        var result = PathConverter.WindowsPathToPosixPath(input);
        result.Should().Be(expected);
    }

    [Theory]
    [InlineData("\\\\server\\share\\path", "//server/share/path")]
    [InlineData("\\\\192.168.1.1\\c$\\Windows", "//192.168.1.1/c$/Windows")]
    public void WindowsPathToPosixPath_UncPath_ConvertsCorrectly(string input, string expected) {
        var result = PathConverter.WindowsPathToPosixPath(input);
        result.Should().Be(expected);
    }

    [Theory]
    [InlineData("relative\\path", "relative/path")]
    [InlineData("path", "path")]
    [InlineData("", "")]
    public void WindowsPathToPosixPath_RelativeOrEmpty_ReturnsAsIs(string input, string expected) {
        var result = PathConverter.WindowsPathToPosixPath(input);
        result.Should().Be(expected);
    }

    [SuppressMessage("AotSafety", "JCC11003", Justification = "边界可空抑制: 反射/延迟初始化/解析保证非空")]
    [Fact]
    public void WindowsPathToPosixPath_NullInput_ReturnsNull() {
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
    public void PosixPathToWindowsPath_PosixDriveLetter_ConvertsCorrectly(string input, string expected) {
        var result = PathConverter.PosixPathToWindowsPath(input);
        result.Should().Be(expected);
    }

    [Theory]
    [InlineData("C:\\Users\\test", "C:\\Users\\test")]
    [InlineData("C:/Users/test", "C:\\Users\\test")]
    [InlineData("/home/user", "/home/user")]
    [InlineData("", "")]
    public void PosixPathToWindowsPath_NonPosixDriveLetter_ReturnsAsIs(string input, string expected) {
        var result = PathConverter.PosixPathToWindowsPath(input);
        result.Should().Be(expected);
    }

    [SuppressMessage("AotSafety", "JCC11003", Justification = "边界可空抑制: 反射/延迟初始化/解析保证非空")]
    [Fact]
    public void PosixPathToWindowsPath_NullInput_ReturnsNull() {
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
    public void LooksLikeWindowsPath_DetectsCorrectly(string input, bool expected) {
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
    public void GateCommandPaths_ConvertsPathsInCommand(string input, bool toPosix, string expected) {
        var result = PathConverter.GateCommandPaths(input, toPosix);
        result.Should().Be(expected);
    }

    [Fact]
    public void GateCommandPaths_ExcludesUrls() {
        var cmd = "curl https://api.example.com/data C:\\Users\\test\\output.json";
        var result = PathConverter.GateCommandPaths(cmd, toPosix: true);
        result.Should().Contain("https://api.example.com/data");
        result.Should().Contain("/c/Users/test/output.json");
    }

    [Fact]
    public void GateCommandPaths_MultiplePaths() {
        var cmd = "copy C:\\Users\\test\\a.txt C:\\Users\\test\\b.txt";
        var result = PathConverter.GateCommandPaths(cmd, toPosix: true);
        result.Should().Be("copy /c/Users/test/a.txt /c/Users/test/b.txt");
    }

    [SuppressMessage("AotSafety", "JCC11003", Justification = "边界可空抑制: 反射/延迟初始化/解析保证非空")]
    [Fact]
    public void GateCommandPaths_NullOrEmpty_ReturnsAsIs() {
        PathConverter.GateCommandPaths(null!, true).Should().BeNull();
        PathConverter.GateCommandPaths("", true).Should().Be("");
    }

    #endregion
}