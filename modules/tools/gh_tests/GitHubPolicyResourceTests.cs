namespace Gh.Tests;

/// <summary>jcc-gh 内嵌策略契约 — 必填位置参数与公共选项必须进入硬白名单。</summary>
public sealed class GitHubPolicyResourceTests {
    /// <summary>策略生成器遗漏 WellKnownParam 时，真实 jcc gh 会被 Policy.Parameter 拒绝。</summary>
    [Fact]
    public void EmbeddedPolicy_DeclaresPrNumberAndCommonOptions() {
        const string resourceName = "JoinCode.CliNodes.Groups.github.policy.yaml";
        using var stream = typeof(GitHubToolHandlers).Assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"jcc-gh 缺少内嵌策略资源 '{resourceName}'");
        using var reader = new StreamReader(stream);
        var manifest = CliToolPolicyLoader.Parse(reader.ReadToEnd(), resourceName);
        var checks = Assert.Single(manifest.Tools, tool => tool.ToolId == "gh_pr_checks");

        Assert.Contains(checks.Parameters, parameter => parameter.Name == "pr_number");
        Assert.Contains(checks.Parameters, parameter => parameter.Name == "repo");
        Assert.Contains(checks.Parameters, parameter => parameter.Name == "working_dir");
        Assert.Contains(checks.Parameters, parameter => parameter.Name == "limit");
    }
}
