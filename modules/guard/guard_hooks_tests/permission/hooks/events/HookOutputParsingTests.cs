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
        public HookResult TestParseHookOutput(string stdout, HookEvent? sourceEvent = null) =>
            ParseHookOutput(stdout, stderr: "", exitCode: 0, command: "test", isAsync: false, sourceEvent: sourceEvent);

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

    // TASK130-A6：additionalContext / initialUserMessage 此前在解析层就被丢掉，
    // 下游 SessionStart / UserPromptSubmit 的注入链等于死代码。
    [Fact]
    public void ParseHookOutput_AdditionalContext_SetsAdditionalContext() {
        var executor = new TestHookExecutor();
        var stdout = """{"hookSpecificOutput":{"hookEventName":"SessionStart","additionalContext":"本仓库禁止直接提交到 main"}}""";

        var result = executor.TestParseHookOutput(stdout);

        result.AdditionalContext.Should().Be("本仓库禁止直接提交到 main");
        result.Message.Should().BeNull("上下文不进 Message，避免被当用户可见提示重复展示");
    }

    [Fact]
    public void ParseHookOutput_InitialUserMessage_SetsInitialUserMessage() {
        var executor = new TestHookExecutor();
        var stdout = """{"hookSpecificOutput":{"initialUserMessage":"继续上次的重构"}}""";

        var result = executor.TestParseHookOutput(stdout);

        result.InitialUserMessage.Should().Be("继续上次的重构");
    }

    [Fact]
    public void ParseHookOutput_NonStringAdditionalContext_DoesNotThrow() {
        var executor = new TestHookExecutor();
        var stdout = """{"hookSpecificOutput":{"additionalContext":{"nested":"object"}}}""";

        var result = executor.TestParseHookOutput(stdout);

        result.AdditionalContext.Should().BeNull("非字符串输出按缺失处理，不许让整条 hook 解析失败");
    }

    // 纯文本 stdout：只有 SessionStart / UserPromptSubmit 是「给模型的上下文」。
    [Theory]
    [InlineData(HookEvent.SessionStart)]
    [InlineData(HookEvent.UserPromptSubmit)]
    public void ParseHookOutput_PlainStdout_ContextEvent_MapsToAdditionalContext(HookEvent sourceEvent) {
        var executor = new TestHookExecutor();

        var result = executor.TestParseHookOutput("记住：本会话用中文回答\n", sourceEvent);

        result.AdditionalContext.Should().Be("记住：本会话用中文回答");
        result.Message.Should().BeNull();
    }

    [Theory]
    [InlineData(HookEvent.PreToolUse)]
    [InlineData(HookEvent.PostToolUse)]
    [InlineData(null)]
    public void ParseHookOutput_PlainStdout_OtherEvent_StaysMessage(HookEvent? sourceEvent) {
        var executor = new TestHookExecutor();

        var result = executor.TestParseHookOutput("hook said something", sourceEvent);

        result.AdditionalContext.Should().BeNull("其余事件不许把 stdout 变成模型上下文");
        result.Message.Should().Be("hook said something");
    }
}
