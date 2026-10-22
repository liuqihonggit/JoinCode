namespace MockServer.E2E.Tests;

/// <summary>
/// P0 bug 回归锁定 E2E 覆盖测试 — TASK065 第六波
/// 锁定第三/五波修复的生产 bug 不复发: /vendor catalog 崩溃降级、/mcp add 子命令识别、/model 别名透传直切、/effort 设置
/// 真实 jcc 进程 + MockServer 模拟 LLM，NonInteractive 单轮形态
/// </summary>
public sealed class ChatCommandP0RegressionCoverageTests : CoverageTestBase {
    public ChatCommandP0RegressionCoverageTests(ITestOutputHelper output) : base(output) { }

    // ============================================================
    // Phase 6: P0 bug 回归锁定 — 2026-10-23 新增
    // ============================================================

    /// <summary>/vendor 无参 — VendorCommand catalog 崩溃修复回归（无参列出供应商）</summary>
    [Fact]
    public async Task VendorCommand_ShouldListVendors() {
        await RunScriptAsync(ChatCommandConversationScripts.VendorCommand).ConfigureAwait(true);
    }

    /// <summary>/vendor deepseek — VendorCommand 切换降级修复回归（catalog 可用时正常切换）</summary>
    [Fact]
    public async Task VendorSwitchDeepSeekCommand_ShouldSwitchVendor() {
        await RunScriptAsync(ChatCommandConversationScripts.VendorSwitchDeepSeekCommand).ConfigureAwait(true);
    }

    /// <summary>/mcp add testsvr — MCP add 子命令识别修复回归（输出用法提示而非未知操作）</summary>
    [Fact]
    public async Task McpAddUsageCommand_ShouldShowAddUsage() {
        await RunScriptAsync(ChatCommandConversationScripts.McpAddUsageCommand).ConfigureAwait(true);
    }

    /// <summary>/model gpt-4o — ModelCommand catalog 崩溃修复回归（别名透传直切）</summary>
    [Fact]
    public async Task ModelSwitchCommand_ShouldSwitchModelDirectly() {
        await RunScriptAsync(ChatCommandConversationScripts.ModelSwitchCommand).ConfigureAwait(true);
    }

    /// <summary>/effort high — EffortCommand 设置形态回归（输出力度设置成功）</summary>
    [Fact]
    public async Task EffortSetHighCommand_ShouldSetEffortLevel() {
        await RunScriptAsync(ChatCommandConversationScripts.EffortSetHighCommand).ConfigureAwait(true);
    }
}
