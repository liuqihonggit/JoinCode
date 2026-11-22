// JCC1018 豁免: 本测试以 Windows 路径字面量为被测数据（Windows 形态行为/守护判定/端点解析等），
// 被测对象即 Windows 路径语义，非跨平台误用 — 新增跨平台匹配测试请用 Path.Combine(GetTempPath()) 构造。
#pragma warning disable JCC1018
namespace Guard.Security.Tests;

/// <summary>
/// RedirectWhitelistNode 内部纯函数确定性测试 — 重定向目标提取、白名单判定、路径规范化。
/// <para>ExpandTilde/NormalizeRedirectTarget/IsWithinWorkspace 使用 Path.GetFullPath/Environment.GetFolderPath,
/// 属于确定性 IO(同机器同输入→同输出),可确定性测试。</para>
/// <para>跨平台: 用预处理符号定义平台路径常量</para>
/// </summary>
public class RedirectWhitelistNodeInternalTests {
#if WINDOWS
    private const string WorkDir = "D:\\project\\w2";
    private const string OutsidePath = "C:\\Windows\\System32\\drivers\\etc\\hosts";
#else
    private const string WorkDir = "/tmp/project";
    private const string OutsidePath = "/etc/hosts";
#endif

    #region ExtractRedirectTargets

    [Theory]
    [InlineData("echo hello > file.txt", "file.txt")]
    [InlineData("echo hello >> file.txt", "file.txt")]
    [InlineData("cmd 2> error.txt", "error.txt")]
    [InlineData("cmd &> all.txt", "all.txt")]
    public void ExtractRedirectTargets_SimpleRedirect_Should_Extract_Target(string command, string expected) {
        var targets = RedirectWhitelistNode.ExtractRedirectTargets(command).ToList();
        targets.Should().Contain(expected);
    }

    [Fact]
    public void ExtractRedirectTargets_QuotedTarget_Should_Extract_Without_Quotes() {
        var targets = RedirectWhitelistNode.ExtractRedirectTargets("echo hello > \"file with spaces.txt\"").ToList();
        targets.Should().ContainSingle().Which.Should().Be("file with spaces.txt");
    }

    [Fact]
    public void ExtractRedirectTargets_SingleQuotedTarget_Should_Extract_Without_Quotes() {
        var targets = RedirectWhitelistNode.ExtractRedirectTargets("echo hello > 'file.txt'").ToList();
        targets.Should().ContainSingle().Which.Should().Be("file.txt");
    }

    [Fact]
    public void ExtractRedirectTargets_NoRedirect_Should_Return_Empty() {
        var targets = RedirectWhitelistNode.ExtractRedirectTargets("echo hello").ToList();
        targets.Should().BeEmpty();
    }

    [Fact]
    public void ExtractRedirectTargets_FdRedirect_Should_Be_Skipped() {
        // 2>&1 是 fd 重定向,不应提取
        var targets = RedirectWhitelistNode.ExtractRedirectTargets("cmd 2>&1").ToList();
        targets.Should().BeEmpty();
    }

    [Fact]
    public void ExtractRedirectTargets_MultipleRedirects_Should_Extract_All() {
        var targets = RedirectWhitelistNode.ExtractRedirectTargets("cmd > out.txt 2> err.txt").ToList();
        targets.Should().HaveCount(2);
        targets.Should().Contain("out.txt");
        targets.Should().Contain("err.txt");
    }

    #endregion

    #region IsSafeDeviceTarget

    [Theory]
    [InlineData("/dev/null")]
    [InlineData("/dev/stderr")]
    [InlineData("/dev/stdout")]
    [InlineData("/DEV/NULL")]
    public void IsSafeDeviceTarget_SafeDevices_Should_Be_True(string target) {
        RedirectWhitelistNode.IsSafeDeviceTarget(target).Should().BeTrue();
    }

    [Theory]
    [InlineData("file.txt")]
    [InlineData("/tmp/output")]
    [InlineData("nul")]
    [InlineData("CON")]
    public void IsSafeDeviceTarget_NonSafeDevices_Should_Be_False(string target) {
        RedirectWhitelistNode.IsSafeDeviceTarget(target).Should().BeFalse();
    }

    #endregion

    #region EvaluateTarget

    [Fact]
    public void EvaluateTarget_SafeDevice_Should_Be_Whitelisted() {
        var result = RedirectWhitelistNode.EvaluateTarget("/dev/null", WorkDir);
        result.IsWhitelisted.Should().BeTrue();
        result.ViolatingTarget.Should().BeNull();
    }

    [Fact]
    public void EvaluateTarget_WorkspaceFile_Should_Be_Whitelisted() {
        var result = RedirectWhitelistNode.EvaluateTarget("output.txt", WorkDir);
        result.IsWhitelisted.Should().BeTrue();
    }

    [Fact]
    public void EvaluateTarget_OutsideWorkspace_Should_Not_Be_Whitelisted() {
        var result = RedirectWhitelistNode.EvaluateTarget(OutsidePath, WorkDir);
        result.IsWhitelisted.Should().BeFalse();
        result.ViolatingTarget.Should().NotBeNull();
    }

    #endregion

    #region NormalizeRedirectTarget

    [Fact]
    public void NormalizeRedirectTarget_RelativePath_Should_Resolve_Against_WorkDir() {
        var normalized = RedirectWhitelistNode.NormalizeRedirectTarget("file.txt", WorkDir);
        normalized.Should().Contain("file.txt");
        normalized.Should().StartWith(WorkDir);
    }

    [Fact]
    public void NormalizeRedirectTarget_AbsolutePath_Should_Keep_Absolute() {
        var absPath = Path.Combine(WorkDir, "file.txt");
        var normalized = RedirectWhitelistNode.NormalizeRedirectTarget(absPath, WorkDir);
        normalized.Should().Be(absPath);
    }

    #endregion

    #region ExpandTilde

    [Fact]
    public void ExpandTilde_NoTilde_Should_Return_As_Is() {
        RedirectWhitelistNode.ExpandTilde("file.txt").Should().Be("file.txt");
    }

    [Fact]
    public void ExpandTilde_OnlyTilde_Should_Return_Home() {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        RedirectWhitelistNode.ExpandTilde("~").Should().Be(home);
    }

    [Fact]
    public void ExpandTilde_TildeWithSubpath_Should_Expand() {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        RedirectWhitelistNode.ExpandTilde("~/docs").Should().Be(home + "/docs");
    }

    #endregion

    #region IsWithinWorkspace

    [Fact]
    public void IsWithinWorkspace_Inside_Should_Be_True() {
        var path = Path.GetFullPath(Path.Combine(WorkDir, "file.txt"));
        RedirectWhitelistNode.IsWithinWorkspace(path, WorkDir).Should().BeTrue();
    }

    [Fact]
    public void IsWithinWorkspace_Outside_Should_Be_False() {
        RedirectWhitelistNode.IsWithinWorkspace(OutsidePath, WorkDir).Should().BeFalse();
    }

    [Fact]
    public void IsWithinWorkspace_InvalidPath_Should_Be_False() {
        // 非法路径字符应返回 false 而非抛异常
        RedirectWhitelistNode.IsWithinWorkspace("|||invalid|||", WorkDir).Should().BeFalse();
    }

    #endregion
}
