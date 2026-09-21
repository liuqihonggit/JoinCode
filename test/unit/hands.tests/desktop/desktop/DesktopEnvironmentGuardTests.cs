namespace JoinCode.Hands.Desktop.Tests;

/// <summary>
/// DesktopEnvironmentGuard 单元测试 — AC-11 环境守卫（CI/无头环境明确报错）
/// </summary>
public sealed class DesktopEnvironmentGuardTests {
    /// <summary>AC-11: CI=true 环境返回非交互式 + 诊断含 CI</summary>
    [Fact]
    public void CheckInteractiveDesktop_CIEnvironment_ReturnsNonInteractive() {
        var prevCI = Environment.GetEnvironmentVariable("CI");
        try {
            Environment.SetEnvironmentVariable("CI", "true");
            var result = DesktopEnvironmentGuard.CheckInteractiveDesktop();
            result.IsInteractive.Should().BeFalse();
            result.Diagnostic.Should().Contain("CI");
        } finally {
            Environment.SetEnvironmentVariable("CI", prevCI);
        }
    }

    /// <summary>AC-11: GITHUB_ACTIONS=true 环境返回非交互式</summary>
    [Fact]
    public void CheckInteractiveDesktop_GitHubActions_ReturnsNonInteractive() {
        var prev = Environment.GetEnvironmentVariable("GITHUB_ACTIONS");
        try {
            Environment.SetEnvironmentVariable("GITHUB_ACTIONS", "true");
            var result = DesktopEnvironmentGuard.CheckInteractiveDesktop();
            result.IsInteractive.Should().BeFalse();
            result.Reasons.Should().NotBeEmpty();
        } finally {
            Environment.SetEnvironmentVariable("GITHUB_ACTIONS", prev);
        }
    }
}