namespace Composition.Tests.Commands;

/// <summary>
/// #29a — /install-github-app 在非交互（TUI）下的回退行为。
/// <para>一手依据：基准 commands/install-github-app/install-github-app.tsx:42
/// INITIAL_STATE.selectedWorkflows = ['claude','claude-review']（两个都装）；
/// C# SelectWorkflowsAsync 的 selected 初值也是这两个 —— 非交互回退即取该默认值。</para>
/// <para>症状取证：原三处 Selector 无 !IsNonInteractive 守卫，TUI 下 ShowAsync 写 stdout
/// 被 ConsoleSuppressionGate 收进环形缓冲（用户不可见）→ 立刻返回 Cancelled → 静默"取消"。</para>
/// </summary>
public sealed class InstallGitHubAppCommandNonInteractiveTests {
    /// <summary>有当前仓库 → 非交互直接用它（不再弹 Selector）。</summary>
    [Fact]
    public async Task ChooseRepo_NonInteractive_WithCurrentRepo_UsesIt() {
        var result = await InstallGitHubAppCommand.ChooseRepoAsync(
            "owner/repo", nonInteractive: true, CancellationToken.None);

        result.Should().Be("owner/repo");
    }

    /// <summary>无当前仓库 → 非交互返回 null（走 ExecuteAsync 既有的"安装已取消"路径）。</summary>
    [Fact]
    public async Task ChooseRepo_NonInteractive_NoCurrentRepo_ReturnsNull() {
        var result = await InstallGitHubAppCommand.ChooseRepoAsync(
            string.Empty, nonInteractive: true, CancellationToken.None);

        result.Should().BeNull();
    }

    /// <summary>非交互选工作流 → 基准默认两个都装。</summary>
    [Fact]
    public async Task SelectWorkflows_NonInteractive_ReturnsBothDefaults() {
        var result = await InstallGitHubAppCommand.SelectWorkflowsAsync(
            nonInteractive: true, CancellationToken.None);

        result.Should().BeEquivalentTo(["claude", "claude-review"]);
    }

    /// <summary>非交互 + 有本地 API Key → 直接用本地 Key，不读掩码输入。</summary>
    [Fact]
    public async Task ChooseApiKey_NonInteractive_WithLocalKey_UsesIt() {
        var keyVar = ProviderEnvVar.AnthropicApiKey.ToValue();
        var prior = Environment.GetEnvironmentVariable(keyVar);
        Environment.SetEnvironmentVariable(keyVar, "sk-local-test-key");
        try {
            var result = await InstallGitHubAppCommand.ChooseApiKeyMethodAsync(
                nonInteractive: true, CancellationToken.None);

            result.SecretName.Should().Be(ProviderEnvVarEnumConstants.AnthropicApiKey);
            result.SecretValue.Should().Be("sk-local-test-key");
            result.AuthType.Should().Be("api_key");
        } finally {
            Environment.SetEnvironmentVariable(keyVar, prior);
        }
    }

    /// <summary>非交互 + 无本地 Key → 返回 default（ExecuteAsync 报"安装已取消"），不读掩码输入挂死。</summary>
    [Fact]
    public async Task ChooseApiKey_NonInteractive_NoLocalKey_ReturnsDefault() {
        var keyVar = ProviderEnvVar.AnthropicApiKey.ToValue();
        var prior = Environment.GetEnvironmentVariable(keyVar);
        Environment.SetEnvironmentVariable(keyVar, null);
        try {
            var result = await InstallGitHubAppCommand.ChooseApiKeyMethodAsync(
                nonInteractive: true, CancellationToken.None);

            result.Should().Be(default);
        } finally {
            Environment.SetEnvironmentVariable(keyVar, prior);
        }
    }

}
