namespace Core.Tests.Hooks.Events;

#pragma warning disable JCC11003 // 测试代码中 NotBeNull 后访问属性需要 null-forgiving

/// <summary>
/// Hook 协议层输出解析测试（TASK071-D7+D8）— 验证 hookSpecificOutput 节点中
/// permissionDecision/updatedInput/systemMessage 的解析（对齐基准 processHookJSONOutput）。
/// 通过测试子类暴露 protected ParseHookOutput。
/// </summary>
public sealed class HookOutputParsingTests {
    /// <summary>测试用执行器 — 暴露 ParseHookOutput 为 public。</summary>
    private sealed class TestHookExecutor : HookExecutorBase<BashCommandHook> {
        public HookResult TestParseHookOutput(string stdout) =>
            ParseHookOutput(stdout, stderr: "", exitCode: 0, command: "test", isAsync: false);

        public override string SupportedType => "test";
        public override Task<HookResult> ExecuteTypedAsync(BashCommandHook hook, HookInput input, CancellationToken cancellationToken = default) =>
            throw new NotImplementedException();
    }

    [Fact]
    public void ParseHookOutput_PermissionDecisionAllow_SetsPermissionAllow() {
        var executor = new TestHookExecutor();
        var stdout = """{"hookSpecificOutput":{"permissionDecision":"allow"}}""";

        var result = executor.TestParseHookOutput(stdout);

        result.PermissionRequestResult.Should().BeOfType<PermissionAllowResult>();
    }

    [Fact]
    public void ParseHookOutput_PermissionDecisionDeny_SetsPermissionDeny() {
        var executor = new TestHookExecutor();
        var stdout = """{"hookSpecificOutput":{"permissionDecision":"deny","permissionDecisionReason":"blocked by rule"}}""";

        var result = executor.TestParseHookOutput(stdout);

        var deny = result.PermissionRequestResult.Should().BeOfType<PermissionDenyResult>().Subject;
        deny.Message.Should().Be("blocked by rule");
    }

    [Fact]
    public void ParseHookOutput_SystemMessage_SetsSystemMessage() {
        var executor = new TestHookExecutor();
        var stdout = """{"hookSpecificOutput":{"systemMessage":"warning from hook"}}""";

        var result = executor.TestParseHookOutput(stdout);

        result.SystemMessage.Should().Be("warning from hook");
    }

    [Fact]
    public void ParseHookOutput_UpdatedInput_SetsUpdatedInput() {
        var executor = new TestHookExecutor();
        var stdout = """{"hookSpecificOutput":{"updatedInput":{"key":"value"}}}""";

        var result = executor.TestParseHookOutput(stdout);

        result.UpdatedInput.Should().NotBeNull();
        result.UpdatedInput!["key"].GetString().Should().Be("value");
    }
}
