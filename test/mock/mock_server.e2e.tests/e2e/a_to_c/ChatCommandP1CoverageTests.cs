namespace MockServer.E2E.Tests;

/// <summary>
/// P1 高频命令 E2E 覆盖测试 — TASK065 第六波补覆盖
/// 包含: /debuglog -e（错误段查询）、/??（需求澄清）、/sampling（无参查询）、/switch（无参显示模式）、/diag recent（崩溃快照查询）
/// </summary>
public sealed class ChatCommandP1CoverageTests : CoverageTestBase {
    public ChatCommandP1CoverageTests(ITestOutputHelper output) : base(output) { }

    /// <summary>/debuglog -e — 断言错误段标题输出且无错误</summary>
    [Fact]
    public async Task DebugLogErrorCommand_ShouldShowErrorSection() {
        await RunScriptAsync(ChatCommandConversationScripts.DebugLogErrorCommand).ConfigureAwait(true);
    }

    /// <summary>/?? 需求描述 — mock 响应含 ClarifyDoneMarker，断言澄清模式横幅输出且无错误</summary>
    [Fact]
    public async Task AskClarifyCommand_ShouldCompleteClarifyLoop() {
        await RunScriptAsync(ChatCommandConversationScripts.AskClarifyCommand).ConfigureAwait(true);
    }

    /// <summary>/sampling 无参 — 断言采样参数查询输出且无错误</summary>
    [Fact]
    public async Task SamplingCommand_ShouldShowSamplingParams() {
        await RunScriptAsync(ChatCommandConversationScripts.SamplingCommand).ConfigureAwait(true);
    }

    /// <summary>/switch 无参 — 断言当前模式输出且无错误</summary>
    [Fact]
    public async Task SwitchCommand_ShouldShowCurrentMode() {
        await RunScriptAsync(ChatCommandConversationScripts.SwitchCommand).ConfigureAwait(true);
    }

    /// <summary>/diag recent — 断言崩溃快照查询输出且无错误</summary>
    [Fact]
    public async Task DiagRecentCommand_ShouldShowCrashSnapshotReport() {
        await RunScriptAsync(ChatCommandConversationScripts.DiagRecentCommand).ConfigureAwait(true);
    }
}
