namespace Guard.Security.Tests;

/// <summary>
/// MtpPerturbationNode 内部纯函数确定性测试 — 特征提取、异常检测、路径错误检测、连续异常计数。
/// </summary>
public class MtpPerturbationNodeInternalTests {

    #region ExtractFeatures

    [Fact]
    public void ExtractFeatures_Should_Set_CommandLength() {
        var features = MtpPerturbationNode.ExtractFeatures("ls -la");
        features.CommandLength.Should().Be(6);
    }

    [Fact]
    public void ExtractFeatures_Should_Detect_RedirectSymbol() {
        MtpPerturbationNode.ExtractFeatures("cmd > out").HasRedirectSymbol.Should().BeTrue();
        MtpPerturbationNode.ExtractFeatures("cmd < in").HasRedirectSymbol.Should().BeTrue();
        MtpPerturbationNode.ExtractFeatures("cmd").HasRedirectSymbol.Should().BeFalse();
    }

    [Fact]
    public void ExtractFeatures_Should_Detect_PathLikeToken() {
        MtpPerturbationNode.ExtractFeatures("cat /etc/passwd").HasPathLikeToken.Should().BeTrue();
        MtpPerturbationNode.ExtractFeatures("echo C:\\file").HasPathLikeToken.Should().BeTrue();
        MtpPerturbationNode.ExtractFeatures("echo ~").HasPathLikeToken.Should().BeTrue();
        MtpPerturbationNode.ExtractFeatures("echo hello").HasPathLikeToken.Should().BeFalse();
    }

    [Fact]
    public void ExtractFeatures_EmptyCommand_Should_Have_Zero_Length() {
        var features = MtpPerturbationNode.ExtractFeatures("");
        features.CommandLength.Should().Be(0);
    }

    #endregion

    #region DetectAnomaly

    [Fact]
    public void DetectAnomaly_NonZeroExit_WithRedirect_Should_Be_Anomaly() {
        var features = new PerturbationFeatures(10, HasRedirectSymbol: true, HasPathLikeToken: false, DateTimeOffset.UtcNow);
        MtpPerturbationNode.DetectAnomaly(features, exitCode: 1, stderr: null).Should().BeTrue();
    }

    [Fact]
    public void DetectAnomaly_ZeroExit_NoError_Should_Not_Be_Anomaly() {
        var features = new PerturbationFeatures(10, HasRedirectSymbol: false, HasPathLikeToken: false, DateTimeOffset.UtcNow);
        MtpPerturbationNode.DetectAnomaly(features, exitCode: 0, stderr: null).Should().BeFalse();
    }

    [Fact]
    public void DetectAnomaly_Stderr_WithPathError_Should_Be_Anomaly() {
        var features = new PerturbationFeatures(10, HasRedirectSymbol: false, HasPathLikeToken: false, DateTimeOffset.UtcNow);
        MtpPerturbationNode.DetectAnomaly(features, exitCode: 0, stderr: "file not found").Should().BeTrue();
    }

    [Fact]
    public void DetectAnomaly_Stderr_WithoutPathError_Should_Not_Be_Anomaly() {
        var features = new PerturbationFeatures(10, HasRedirectSymbol: false, HasPathLikeToken: false, DateTimeOffset.UtcNow);
        MtpPerturbationNode.DetectAnomaly(features, exitCode: 0, stderr: "some other error").Should().BeFalse();
    }

    [Fact]
    public void DetectAnomaly_NonZeroExit_NoRedirect_NoPathError_Should_Not_Be_Anomaly() {
        var features = new PerturbationFeatures(10, HasRedirectSymbol: false, HasPathLikeToken: false, DateTimeOffset.UtcNow);
        MtpPerturbationNode.DetectAnomaly(features, exitCode: 1, stderr: "generic error").Should().BeFalse();
    }

    #endregion

    #region ContainsPathError

    [Theory]
    [InlineData("file not found")]
    [InlineData("No such file or directory")]
    [InlineData("cannot access 'file'")]
    [InlineData("FILE NOT FOUND")]
    public void ContainsPathError_WithErrors_Should_Be_True(string stderr) {
        MtpPerturbationNode.ContainsPathError(stderr).Should().BeTrue();
    }

    [Theory]
    [InlineData("success")]
    [InlineData("permission denied")]
    [InlineData("")]
    public void ContainsPathError_WithoutErrors_Should_Be_False(string stderr) {
        MtpPerturbationNode.ContainsPathError(stderr).Should().BeFalse();
    }

    #endregion

    #region CountConsecutiveAnomalies

    private static MtpPerturbationNode.PerturbationRecord MakeRecord(bool isAnomaly) {
        var features = new PerturbationFeatures(5, false, false, DateTimeOffset.UtcNow);
        return new MtpPerturbationNode.PerturbationRecord(features, isAnomaly, "cmd");
    }

    [Fact]
    public void CountConsecutiveAnomalies_Empty_Should_Be_Zero() {
        var records = ImmutableList.Create<MtpPerturbationNode.PerturbationRecord>();
        MtpPerturbationNode.CountConsecutiveAnomalies(records).Should().Be(0);
    }

    [Fact]
    public void CountConsecutiveAnomalies_AllAnomaly_Should_Return_Count() {
        var records = ImmutableList.Create(
            MakeRecord(true),
            MakeRecord(true),
            MakeRecord(true));
        MtpPerturbationNode.CountConsecutiveAnomalies(records).Should().Be(3);
    }

    [Fact]
    public void CountConsecutiveAnomalies_LastNotAnomaly_Should_Return_Zero() {
        var records = ImmutableList.Create(
            MakeRecord(true),
            MakeRecord(false));
        MtpPerturbationNode.CountConsecutiveAnomalies(records).Should().Be(0);
    }

    [Fact]
    public void CountConsecutiveAnomalies_Mixed_Should_Count_From_End() {
        var records = ImmutableList.Create(
            MakeRecord(false),
            MakeRecord(true),
            MakeRecord(true));
        MtpPerturbationNode.CountConsecutiveAnomalies(records).Should().Be(2);
    }

    #endregion

    #region Null 守卫

    [Fact]
    public void ContainsPathError_NullStderr_ThrowsArgumentNullException() {
        var act = () => MtpPerturbationNode.ContainsPathError(null!);
        act.Should().Throw<ArgumentNullException>()
           .WithParameterName("stderr");
    }

    [Fact]
    public void ExtractFeatures_NullCommand_ThrowsArgumentNullException() {
        var act = () => MtpPerturbationNode.ExtractFeatures(null!);
        act.Should().Throw<ArgumentNullException>()
           .WithParameterName("command");
    }

    [Fact]
    public void DetectAnomaly_NullFeatures_ThrowsArgumentNullException() {
        var act = () => MtpPerturbationNode.DetectAnomaly(null!, exitCode: 0, stderr: null);
        act.Should().Throw<ArgumentNullException>()
           .WithParameterName("features");
    }

    [Fact]
    public void CountConsecutiveAnomalies_NullRecords_ThrowsArgumentNullException() {
        var act = () => MtpPerturbationNode.CountConsecutiveAnomalies(null!);
        act.Should().Throw<ArgumentNullException>()
           .WithParameterName("records");
    }

    #endregion
}
