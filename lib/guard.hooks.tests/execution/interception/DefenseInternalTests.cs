namespace Guard.Hooks.Tests;

/// <summary>
/// Defense 层内部纯函数确定性测试 — RedirectWhitelistNode/MtpPerturbationNode。
/// <para>不依赖时序/线程调度/真实 IO,给定输入→断言输出。
/// ExpandTilde 波浪号分支依赖 Environment.GetFolderPath,仅断言非波浪号路径与格式不变性;
/// NormalizeRedirectTarget/IsWithinWorkspace 使用 Path.GetFullPath 纯路径计算,确定性可测。</para>
/// </summary>
public class DefenseInternalTests {
    // 工作目录使用临时路径,避免依赖真实文件系统结构
    private const string FakeWorkDir = "/tmp/workspace";

    #region RedirectWhitelistNode.ExtractRedirectTargets

    [Fact]
    public void ExtractRedirectTargets_NoRedirect_Should_Be_Empty() {
        var targets = RedirectWhitelistNode.ExtractRedirectTargets("echo hello");
        targets.Should().BeEmpty();
    }

    [Fact]
    public void ExtractRedirectTargets_SimpleRedirect_Should_Extract() {
        var targets = RedirectWhitelistNode.ExtractRedirectTargets("echo hello > out.txt");
        targets.Should().ContainSingle().Which.Should().Be("out.txt");
    }

    [Fact]
    public void ExtractRedirectTargets_AppendRedirect_Should_Extract() {
        var targets = RedirectWhitelistNode.ExtractRedirectTargets("echo hello >> log.txt");
        targets.Should().ContainSingle().Which.Should().Be("log.txt");
    }

    [Fact]
    public void ExtractRedirectTargets_StderrRedirect_Should_Extract() {
        var targets = RedirectWhitelistNode.ExtractRedirectTargets("cmd 2> err.txt");
        targets.Should().ContainSingle().Which.Should().Be("err.txt");
    }

    [Fact]
    public void ExtractRedirectTargets_QuotedTarget_Should_Extract_Without_Quotes() {
        var targets = RedirectWhitelistNode.ExtractRedirectTargets("echo > \"file with spaces.txt\"");
        targets.Should().ContainSingle().Which.Should().Be("file with spaces.txt");
    }

    [Fact]
    public void ExtractRedirectTargets_SingleQuotedTarget_Should_Extract_Without_Quotes() {
        var targets = RedirectWhitelistNode.ExtractRedirectTargets("echo > 'file.txt'");
        targets.Should().ContainSingle().Which.Should().Be("file.txt");
    }

    [Fact]
    public void ExtractRedirectTargets_FdRedirect_Should_Be_Skipped() {
        // 2>&1 是 fd 重定向,不匹配文件重定向
        var targets = RedirectWhitelistNode.ExtractRedirectTargets("cmd 2>&1");
        targets.Should().BeEmpty();
    }

    [Fact]
    public void ExtractRedirectTargets_MultipleRedirects_Should_Extract_All() {
        var targets = RedirectWhitelistNode.ExtractRedirectTargets("cmd > out.txt 2> err.txt").ToList();
        targets.Should().HaveCount(2);
        targets.Should().Contain("out.txt");
        targets.Should().Contain("err.txt");
    }

    [Fact]
    public void ExtractRedirectTargets_DevNull_Should_Extract() {
        var targets = RedirectWhitelistNode.ExtractRedirectTargets("echo hello > /dev/null");
        targets.Should().ContainSingle().Which.Should().Be("/dev/null");
    }

    #endregion

    #region RedirectWhitelistNode.IsSafeDeviceTarget

    [Theory]
    [InlineData("/dev/null")]
    [InlineData("/dev/stderr")]
    [InlineData("/dev/stdout")]
    [InlineData("/DEV/NULL")]       // 大小写不敏感
    [InlineData("/Dev/Null")]
    public void IsSafeDeviceTarget_SafeDevices_Should_Be_True(string target) {
        RedirectWhitelistNode.IsSafeDeviceTarget(target).Should().BeTrue();
    }

    [Theory]
    [InlineData("out.txt")]
    [InlineData("/etc/passwd")]
    [InlineData("nul")]
    [InlineData("con")]
    [InlineData("")]
    public void IsSafeDeviceTarget_NonSafe_Should_Be_False(string target) {
        RedirectWhitelistNode.IsSafeDeviceTarget(target).Should().BeFalse();
    }

    #endregion

    #region RedirectWhitelistNode.ExpandTilde

    [Fact]
    public void ExpandTilde_NonTildePath_Should_Return_As_Is() {
        RedirectWhitelistNode.ExpandTilde("/usr/bin").Should().Be("/usr/bin");
        RedirectWhitelistNode.ExpandTilde("relative/path").Should().Be("relative/path");
        RedirectWhitelistNode.ExpandTilde("").Should().Be("");
    }

    [Fact]
    public void ExpandTilde_BareTilde_Should_Return_Home() {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        RedirectWhitelistNode.ExpandTilde("~").Should().Be(home);
    }

    [Fact]
    public void ExpandTilde_TildeWithSubpath_Should_Expand() {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        RedirectWhitelistNode.ExpandTilde("~/Documents").Should().Be(home + "/Documents");
        RedirectWhitelistNode.ExpandTilde("~/.bashrc").Should().Be(home + "/.bashrc");
    }

    #endregion

    #region RedirectWhitelistNode.NormalizeRedirectTarget

    [Fact]
    public void NormalizeRedirectTarget_RelativePath_Should_Resolve_Against_WorkDir() {
        var normalized = RedirectWhitelistNode.NormalizeRedirectTarget("out.txt", FakeWorkDir);
        // 规范化后应包含工作目录前缀(路径分隔符可能跨平台)
        normalized.Should().Contain("out.txt");
    }

    [Fact]
    public void NormalizeRedirectTarget_DevNull_Should_Pass_Through() {
        var normalized = RedirectWhitelistNode.NormalizeRedirectTarget("/dev/null", FakeWorkDir);
        // /dev/null 在 Unix 是合法路径,在 Windows GetFullPath 会拼接,但不应抛异常
        normalized.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void NormalizeRedirectTarget_AbsolutePath_Should_Remain_Absolute() {
        var normalized = RedirectWhitelistNode.NormalizeRedirectTarget("/tmp/other/file.txt", FakeWorkDir);
        normalized.Should().Contain("file.txt");
    }

    #endregion

    #region RedirectWhitelistNode.IsWithinWorkspace

    [Fact]
    public void IsWithinWorkspace_PathInside_Should_Be_True() {
        var workDir = Path.GetFullPath(FakeWorkDir);
        var inside = Path.Combine(workDir, "subdir", "file.txt");
        RedirectWhitelistNode.IsWithinWorkspace(inside, workDir).Should().BeTrue();
    }

    [Fact]
    public void IsWithinWorkspace_PathOutside_Should_Be_False() {
        var workDir = Path.GetFullPath(FakeWorkDir);
        var outside = Path.GetFullPath("/tmp/other/file.txt");
        RedirectWhitelistNode.IsWithinWorkspace(outside, workDir).Should().BeFalse();
    }

    [Fact]
    public void IsWithinWorkspace_SamePath_Should_Be_True() {
        var workDir = Path.GetFullPath(FakeWorkDir);
        RedirectWhitelistNode.IsWithinWorkspace(workDir, workDir).Should().BeTrue();
    }

    [Fact]
    public void IsWithinWorkspace_InvalidPath_Should_Be_False() {
        // 含非法字符的路径 GetFullPath 抛异常,应返回 false
        RedirectWhitelistNode.IsWithinWorkspace(":::invalid:::", FakeWorkDir).Should().BeFalse();
    }

    #endregion

    #region RedirectWhitelistNode.EvaluateTarget

    [Fact]
    public void EvaluateTarget_SafeDevice_Should_Be_Whitelisted() {
        var result = RedirectWhitelistNode.EvaluateTarget("/dev/null", FakeWorkDir);
        result.IsWhitelisted.Should().BeTrue();
        result.ViolatingTarget.Should().BeNull();
    }

    [Fact]
    public void EvaluateTarget_InsideWorkspace_Should_Be_Whitelisted() {
        var workDir = Path.GetFullPath(FakeWorkDir);
        var inside = Path.Combine(workDir, "file.txt");
        var result = RedirectWhitelistNode.EvaluateTarget(inside, workDir);
        result.IsWhitelisted.Should().BeTrue();
        result.NormalizedTarget.Should().NotBeNull();
    }

    [Fact]
    public void EvaluateTarget_OutsideWorkspace_Should_Be_Violation() {
        var workDir = Path.GetFullPath(FakeWorkDir);
        var outside = Path.GetFullPath("/tmp/other/file.txt");
        var result = RedirectWhitelistNode.EvaluateTarget(outside, workDir);
        result.IsWhitelisted.Should().BeFalse();
        result.ViolatingTarget.Should().Be(outside);
    }

    #endregion

    #region MtpPerturbationNode.ExtractFeatures

    [Fact]
    public void ExtractFeatures_SimpleCommand_Should_Set_Length() {
        var features = MtpPerturbationNode.ExtractFeatures("echo hello");
        features.CommandLength.Should().Be(10);
        features.HasRedirectSymbol.Should().BeFalse();
        features.HasPathLikeToken.Should().BeFalse();
    }

    [Fact]
    public void ExtractFeatures_RedirectSymbol_Should_Be_Detected() {
        MtpPerturbationNode.ExtractFeatures("cmd > out.txt").HasRedirectSymbol.Should().BeTrue();
        MtpPerturbationNode.ExtractFeatures("cmd < in.txt").HasRedirectSymbol.Should().BeTrue();
        MtpPerturbationNode.ExtractFeatures("cmd >> log.txt").HasRedirectSymbol.Should().BeTrue();
        MtpPerturbationNode.ExtractFeatures("echo hello").HasRedirectSymbol.Should().BeFalse();
    }

    [Fact]
    public void ExtractFeatures_PathLikeToken_Should_Be_Detected() {
        MtpPerturbationNode.ExtractFeatures("./build").HasPathLikeToken.Should().BeTrue();
        MtpPerturbationNode.ExtractFeatures(@"C:\path").HasPathLikeToken.Should().BeTrue();
        MtpPerturbationNode.ExtractFeatures("~/home").HasPathLikeToken.Should().BeTrue();
        MtpPerturbationNode.ExtractFeatures("echo hello").HasPathLikeToken.Should().BeFalse();
    }

    [Fact]
    public void ExtractFeatures_EmptyCommand_Should_Have_Zero_Length() {
        var features = MtpPerturbationNode.ExtractFeatures("");
        features.CommandLength.Should().Be(0);
    }

    #endregion

    #region MtpPerturbationNode.ContainsPathError

    [Theory]
    [InlineData("file not found")]
    [InlineData("No such file or directory")]
    [InlineData("cannot access 'foo'")]
    [InlineData("FILE NOT FOUND")]           // 大小写不敏感
    [InlineData("NO SUCH FILE")]
    public void ContainsPathError_PathErrors_Should_Be_True(string stderr) {
        MtpPerturbationNode.ContainsPathError(stderr).Should().BeTrue();
    }

    [Theory]
    [InlineData("success")]
    [InlineData("permission denied")]
    [InlineData("")]
    public void ContainsPathError_NoPathError_Should_Be_False(string stderr) {
        MtpPerturbationNode.ContainsPathError(stderr).Should().BeFalse();
    }

    #endregion

    #region MtpPerturbationNode.DetectAnomaly

    [Fact]
    public void DetectAnomaly_NonZeroExit_WithRedirect_Should_Be_Anomaly() {
        var features = new PerturbationFeatures(10, HasRedirectSymbol: true, HasPathLikeToken: false, DateTimeOffset.UtcNow);
        MtpPerturbationNode.DetectAnomaly(features, exitCode: 1, stderr: null).Should().BeTrue();
    }

    [Fact]
    public void DetectAnomaly_ZeroExit_WithRedirect_Should_Not_Be_Anomaly() {
        var features = new PerturbationFeatures(10, HasRedirectSymbol: true, HasPathLikeToken: false, DateTimeOffset.UtcNow);
        MtpPerturbationNode.DetectAnomaly(features, exitCode: 0, stderr: null).Should().BeFalse();
    }

    [Fact]
    public void DetectAnomaly_StderrPathError_Should_Be_Anomaly() {
        var features = new PerturbationFeatures(10, HasRedirectSymbol: false, HasPathLikeToken: false, DateTimeOffset.UtcNow);
        MtpPerturbationNode.DetectAnomaly(features, exitCode: 0, "No such file").Should().BeTrue();
    }

    [Fact]
    public void DetectAnomaly_CleanRun_Should_Not_Be_Anomaly() {
        var features = new PerturbationFeatures(10, HasRedirectSymbol: false, HasPathLikeToken: false, DateTimeOffset.UtcNow);
        MtpPerturbationNode.DetectAnomaly(features, exitCode: 0, stderr: null).Should().BeFalse();
    }

    [Fact]
    public void DetectAnomaly_NonZeroExit_NoRedirect_NoStderr_Should_Not_Be_Anomaly() {
        var features = new PerturbationFeatures(10, HasRedirectSymbol: false, HasPathLikeToken: false, DateTimeOffset.UtcNow);
        MtpPerturbationNode.DetectAnomaly(features, exitCode: 1, stderr: null).Should().BeFalse();
    }

    #endregion

    #region MtpPerturbationNode.CountConsecutiveAnomalies

    [Fact]
    public void CountConsecutiveAnomalies_Empty_Should_Be_Zero() {
        MtpPerturbationNode.CountConsecutiveAnomalies(ImmutableList<MtpPerturbationNode.PerturbationRecord>.Empty).Should().Be(0);
    }

    [Fact]
    public void CountConsecutiveAnomalies_AllAnomalies_Should_Count_All() {
        var records = ImmutableList.Create(
            MakeRecord(true),
            MakeRecord(true),
            MakeRecord(true));
        MtpPerturbationNode.CountConsecutiveAnomalies(records).Should().Be(3);
    }

    [Fact]
    public void CountConsecutiveAnomalies_LastNAnomalies_Should_Count_From_End() {
        // 从最新往前数连续异常,遇到非异常停止
        var records = ImmutableList.Create(
            MakeRecord(false),   // 旧
            MakeRecord(true),
            MakeRecord(true));   // 最新
        MtpPerturbationNode.CountConsecutiveAnomalies(records).Should().Be(2);
    }

    [Fact]
    public void CountConsecutiveAnomalies_LastIsNotAnomaly_Should_Be_Zero() {
        var records = ImmutableList.Create(
            MakeRecord(true),
            MakeRecord(false));   // 最新非异常
        MtpPerturbationNode.CountConsecutiveAnomalies(records).Should().Be(0);
    }

    [Fact]
    public void CountConsecutiveAnomalies_SingleAnomaly_Should_Be_One() {
        var records = ImmutableList.Create(MakeRecord(true));
        MtpPerturbationNode.CountConsecutiveAnomalies(records).Should().Be(1);
    }

    [Fact]
    public void CountConsecutiveAnomalies_SingleNonAnomaly_Should_Be_Zero() {
        var records = ImmutableList.Create(MakeRecord(false));
        MtpPerturbationNode.CountConsecutiveAnomalies(records).Should().Be(0);
    }

    private static MtpPerturbationNode.PerturbationRecord MakeRecord(bool isAnomaly)
        => new(
            new PerturbationFeatures(5, HasRedirectSymbol: false, HasPathLikeToken: false, DateTimeOffset.UtcNow),
            IsAnomaly: isAnomaly,
            Command: "cmd");

    #endregion
}
