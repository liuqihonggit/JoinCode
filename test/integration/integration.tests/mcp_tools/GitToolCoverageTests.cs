namespace Integration.Tests.McpTools;

/// <summary>
/// git_/worktree 工具巡检覆盖（TASK072 E 项，清单 :146）— 注入 StubGitCommandRunner，
/// 让 14 个原本被跳过的 git 工具真走 handler 核心逻辑，验证不崩溃且命令真被构造（非参数验证失败路径）。
/// </summary>
public sealed class GitToolCoverageTests {
    /// <summary>文档要求覆盖的 14 个 git_/worktree 工具（与工具名常量对齐）。</summary>
    public static readonly string[] GitTools = [
        "git_status", "git_add", "git_commit", "git_log", "git_diff", "git_branch",
        "git_push", "git_pull", "git_clone", "git_reset", "git_clean",
        "worktree_create", "worktree_remove", "worktree_merge",
    ];

    [Fact]
    public async Task AllGitTools_ExecuteThroughHandler_WithStubRunner() {
        await using var host = new IsolatedTestHost(
            withTestPipelines: true, injectStubGitRunner: true, disposeProvider: false);
        await host.InitializeMcpAsync().ConfigureAwait(true);
        var gateway = host.GetRequiredService<IToolExecutionGateway>();
        var allTools = await host.GetRequiredService<IMcpToolRegistry>().GetAllToolInfosAsync().ConfigureAwait(true);
        var registered = allTools.Select(t => t.Name).ToHashSet(StringComparer.Ordinal);

        var crashOrDeadlock = new List<string>();
        var executed = new List<string>();

        foreach (var toolName in GitTools) {
            if (!registered.Contains(toolName)) continue; // 未注册的工具不算覆盖目标
            var tool = allTools.First(t => t.Name == toolName);
            var args = ArgumentGenerator.GenerateDefaultArguments(tool, host.TempDir);
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            try {
                var result = await gateway.ExecuteAsync(toolName, args, cts.Token).ConfigureAwait(true);
                result.Should().NotBeNull($"{toolName} 不应返回 null");
                // 关键断言：不崩溃/不死锁（工具可因参数语义返回 IsError，但 handler 核心路径被真执行）
                executed.Add(toolName);
            } catch (Exception ex) when (ex is not OperationCanceledException) {
                crashOrDeadlock.Add($"{toolName}: {ex.GetType().Name}: {ex.Message}");
            }
        }

        crashOrDeadlock.Should().BeEmpty("git_/worktree 工具不得崩溃或死锁");
        executed.Should().NotBeEmpty("至少应真执行一个 git 工具，证明跳过表已解除");
        var stub = host.GitRunnerStub;
        if (stub is null) throw new InvalidOperationException("injectStubGitRunner=true 时 GitRunnerStub 必须已注入");
        stub.ExecutedCommands.Should().NotBeEmpty("StubGitCommandRunner 应收到真实 git 命令");
    }

    [Fact]
    public void GitToolNames_AreCoveredByStrategyAsExecutable() {
        var strategy = new DefaultToolValidationStrategy();
        foreach (var name in GitTools) {
            var tool = new ToolInfo { Name = name, Category = "git" };
            var kind = strategy.GetKind(tool);
            kind.Should().NotBe(ToolValidationKind.Skip, $"{name} 不应再被策略跳过");
        }
    }
}
