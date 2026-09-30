namespace Abs.Tests.Constants;

/// <summary>
/// JccEnvVar 新增环境变量枚举值确定性测试 — 验证 ToValue() 与原硬编码字符串一致
/// <para>TASK031 阶段B4:常量委托统一(7 个新增枚举值)</para>
/// </summary>
[Trait("Category", "Deterministic")]
public class JccEnvVarTest {
    [Fact]
    public void DisableRetry_ShouldBeJccDisableRetry() {
        JccEnvVar.DisableRetry.ToValue().Should().Be("JCC_DISABLE_RETRY");
    }

    [Fact]
    public void ActorToolExecutor_ShouldBeJccActorToolExecutor() {
        JccEnvVar.ActorToolExecutor.ToValue().Should().Be("JCC_ACTOR_TOOL_EXECUTOR");
    }

    [Fact]
    public void GithubToken_ShouldBeJccGithubToken() {
        JccEnvVar.GithubToken.ToValue().Should().Be("JCC_GITHUB_TOKEN");
    }

    [Fact]
    public void GithubApiUrl_ShouldBeJccGithubApiUrl() {
        JccEnvVar.GithubApiUrl.ToValue().Should().Be("JCC_GITHUB_API_URL");
    }

    [Fact]
    public void AbsoluteTimeoutSeconds_ShouldBeJccAbsoluteTimeoutSeconds() {
        JccEnvVar.AbsoluteTimeoutSeconds.ToValue().Should().Be("JCC_ABSOLUTE_TIMEOUT_SECONDS");
    }

    [Fact]
    public void ResumeTimeoutSeconds_ShouldBeJccResumeTimeoutSeconds() {
        JccEnvVar.ResumeTimeoutSeconds.ToValue().Should().Be("JCC_RESUME_TIMEOUT_SECONDS");
    }

    [Fact]
    public void ClusterDecompositionOverride_ShouldBeJccClusterDecompositionOverride() {
        JccEnvVar.ClusterDecompositionOverride.ToValue().Should().Be("JCC_CLUSTER_DECOMPOSITION_OVERRIDE");
    }

    [Fact]
    public void GithubApiBase_ShouldBeJccGithubApiBase() {
        JccEnvVar.GithubApiBase.ToValue().Should().Be("JCC_GITHUB_API_BASE");
    }

    [Theory]
    [InlineData(JccEnvVar.DisableRetry, "JCC_DISABLE_RETRY")]
    [InlineData(JccEnvVar.ActorToolExecutor, "JCC_ACTOR_TOOL_EXECUTOR")]
    [InlineData(JccEnvVar.GithubToken, "JCC_GITHUB_TOKEN")]
    [InlineData(JccEnvVar.GithubApiUrl, "JCC_GITHUB_API_URL")]
    [InlineData(JccEnvVar.AbsoluteTimeoutSeconds, "JCC_ABSOLUTE_TIMEOUT_SECONDS")]
    [InlineData(JccEnvVar.ResumeTimeoutSeconds, "JCC_RESUME_TIMEOUT_SECONDS")]
    [InlineData(JccEnvVar.ClusterDecompositionOverride, "JCC_CLUSTER_DECOMPOSITION_OVERRIDE")]
    public void NewEnvVars_ShouldMatchExpectedStrings(JccEnvVar var, string expected) {
        var.ToValue().Should().Be(expected);
    }

    [Fact]
    public void NewEnvVars_ShouldBeDistinctFromExisting() {
        var newVars = new[] {
            JccEnvVar.DisableRetry, JccEnvVar.ActorToolExecutor, JccEnvVar.GithubToken,
            JccEnvVar.GithubApiUrl, JccEnvVar.AbsoluteTimeoutSeconds,
            JccEnvVar.ResumeTimeoutSeconds, JccEnvVar.ClusterDecompositionOverride
        };
        var values = newVars.Select(v => v.ToValue()).ToArray();
        values.Should().OnlyHaveUniqueItems();
    }
}
