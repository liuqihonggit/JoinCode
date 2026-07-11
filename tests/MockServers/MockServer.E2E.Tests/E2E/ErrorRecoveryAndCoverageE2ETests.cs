namespace MockServer.E2E.Tests;

[Trait("Category", "Integration")]
public sealed class ApiErrorRecoveryE2ETests : CoverageTestBase
{
    public ApiErrorRecoveryE2ETests(ITestOutputHelper output) : base(output) { }

    [Fact]
    public async Task RateLimit429_ShouldRecoverOnRetry()
    {
        await RunScriptAsync(ApiErrorRecoveryScripts.RateLimit429ThenRecover);
    }

    [Fact]
    public async Task ServerError500_ShouldRecoverOnRetry()
    {
        await RunScriptAsync(ApiErrorRecoveryScripts.ServerError500ThenRecover);
    }

    [Fact]
    public async Task ServiceUnavailable503_ShouldRecoverOnRetry()
    {
        await RunScriptAsync(ApiErrorRecoveryScripts.ServiceUnavailable503ThenRecover);
    }

    [Fact]
    public async Task ErrorThenToolCall_ShouldRecoverWithToolUse()
    {
        await RunScriptAsync(ApiErrorRecoveryScripts.ErrorThenToolCallThenRecover);
    }

    [Fact]
    public async Task AuthError401_ShouldNotRetry()
    {
        await RunScriptAsync(ApiErrorRecoveryScripts.AuthError401NoRetry);
    }
}

[Trait("Category", "Integration")]
public sealed class StreamInterruptionE2ETests : CoverageTestBase
{
    public StreamInterruptionE2ETests(ITestOutputHelper output) : base(output) { }

    [Fact]
    public async Task ToolCallFailure_ShouldRecoverOnNextTurn()
    {
        await RunScriptAsync(StreamInterruptionScripts.ToolCallFailureThenRecover);
    }

    [Fact]
    public async Task UnknownTool_ShouldFallbackToTextResponse()
    {
        await RunScriptAsync(StreamInterruptionScripts.UnknownToolThenFallback);
    }

    [Fact]
    public async Task MultiToolPartialFailure_ShouldContinueWithPartialResults()
    {
        await RunScriptAsync(StreamInterruptionScripts.MultiToolPartialFailure);
    }
}

[Trait("Category", "Integration")]
public sealed class PermissionDenialE2ETests : CoverageTestBase
{
    public PermissionDenialE2ETests(ITestOutputHelper output) : base(output) { }

    [Fact]
    public async Task AskPermissionMode_TextOnly_ShouldGetResponse()
    {
        await RunScriptAsync(PermissionDenialScripts.AskPermissionMode);
    }

    [Fact]
    public async Task DenyPermissionMode_TextOnly_ShouldGetResponse()
    {
        await RunScriptAsync(PermissionDenialScripts.DenyPermissionMode);
    }

    [Fact]
    public async Task DenyPermissionMode_ToolCall_ShouldBeBlocked()
    {
        await RunScriptAsync(PermissionDenialScripts.DenyModeToolCallBlocked);
    }

    [Fact]
    public async Task AutoPermissionMode_ToolCall_ShouldProceed()
    {
        await RunScriptAsync(PermissionDenialScripts.AutoPermissionModeToolCall);
    }
}

[Trait("Category", "Integration")]
public sealed class AnthropicDeepCoverageE2ETests : IAsyncLifetime
{
    private readonly ITestOutputHelper _output;
    private readonly ILoggerFactory _loggerFactory;

    public AnthropicDeepCoverageE2ETests(ITestOutputHelper output)
    {
        _output = output;
        _loggerFactory = LoggerFactory.Create(builder =>
        {
            builder.AddConsole();
            builder.SetMinimumLevel(LogLevel.Information);
        });
    }

    public Task InitializeAsync() => Task.CompletedTask;
    public Task DisposeAsync()
    {
        _loggerFactory.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task Anthropic_MultiToolCalls_ShouldExecuteBoth()
    {
        await RunScriptWithProviderAsync(AnthropicDeepCoverageScripts.AnthropicMultiToolCalls, ProviderKind.Anthropic);
    }

    [Fact]
    public async Task Anthropic_ThinkingThenToolCall_ShouldShowBoth()
    {
        await RunScriptWithProviderAsync(AnthropicDeepCoverageScripts.AnthropicThinkingThenToolCall, ProviderKind.Anthropic);
    }

    [Fact]
    public async Task Anthropic_FiveRoundMemory_ShouldMaintainContext()
    {
        await RunScriptWithProviderAsync(AnthropicDeepCoverageScripts.AnthropicFiveRoundMemory, ProviderKind.Anthropic);
    }

    [Fact]
    public async Task Anthropic_Error429ThenRecover_ShouldRecover()
    {
        await RunScriptWithProviderAsync(AnthropicDeepCoverageScripts.AnthropicError429ThenRecover, ProviderKind.Anthropic);
    }

    [Fact]
    public async Task DeepSeek_ReasoningThenToolCall_ShouldShowBoth()
    {
        await RunScriptWithProviderAsync(AnthropicDeepCoverageScripts.DeepSeekReasoningThenToolCall, ProviderKind.DeepSeek);
    }

    [Fact]
    public async Task DeepSeek_MultiToolCalls_ShouldExecuteBoth()
    {
        await RunScriptWithProviderAsync(AnthropicDeepCoverageScripts.DeepSeekMultiToolCalls, ProviderKind.DeepSeek);
    }

    [Fact]
    public async Task DeepSeek_FiveRoundMemory_ShouldMaintainContext()
    {
        await RunScriptWithProviderAsync(AnthropicDeepCoverageScripts.DeepSeekFiveRoundMemory, ProviderKind.DeepSeek);
    }

    [Fact]
    public async Task DeepSeek_Error500ThenRecover_ShouldRecover()
    {
        await RunScriptWithProviderAsync(AnthropicDeepCoverageScripts.DeepSeekError500ThenRecover, ProviderKind.DeepSeek);
    }

    private async Task RunScriptWithProviderAsync(ConversationScript script, ProviderKind provider)
    {
        var sw = Stopwatch.StartNew();
        var runner = new DualRoleConversationRunner(
            _loggerFactory.CreateLogger<DualRoleConversationRunner>());
        try
        {
            var result = await runner.RunAsync(script, provider).ConfigureAwait(true);
            sw.Stop();

            _output.WriteLine($"[{provider}] 脚本: {result.ScriptName}, 耗时: {sw.Elapsed.TotalMilliseconds:F1}ms");
            _output.WriteLine($"[{provider}] 断言: {result.AssertResults.Count(a => a.IsPassed)} 通过 / {result.AssertResults.Count(a => !a.IsPassed)} 失败");

            result.AllPassed.Should().BeTrue(
                $"所有断言应通过。失败: {FormatFailures(result)}");
        }
        finally
        {
            await runner.DisposeAsync().ConfigureAwait(true);
        }
    }

    private static string FormatFailures(ConversationResult result)
    {
        var failures = result.AssertResults.Where(a => !a.IsPassed).ToList();
        if (failures.Count == 0) return "(无)";
        return string.Join("; ", failures.Select(f =>
            $"{f.Type}: Expected=\"{f.Expected}\" Desc=\"{f.Description}\""));
    }
}

[Trait("Category", "Integration")]
public sealed class McpProtocolE2ETests : CoverageTestBase
{
    public McpProtocolE2ETests(ITestOutputHelper output) : base(output) { }

    [Fact]
    public async Task McpToolList_ShouldReturnClientList()
    {
        await RunScriptAsync(McpProtocolScripts.McpToolListThenCall);
    }

    [Fact]
    public async Task McpClientCall_ShouldReturnClientStatus()
    {
        await RunScriptAsync(McpProtocolScripts.McpClientCall);
    }

    [Fact]
    public async Task McpToolCallThenFollowUp_ShouldMaintainContext()
    {
        await RunScriptAsync(McpProtocolScripts.McpToolCallThenFollowUp);
    }
}

[Trait("Category", "Integration")]
public sealed class ConcurrentRequestE2ETests : CoverageTestBase
{
    public ConcurrentRequestE2ETests(ITestOutputHelper output) : base(output) { }

    [Fact]
    public async Task RapidSequentialRequests_ShouldHandleAll()
    {
        await RunScriptAsync(ConcurrentRequestScripts.RapidSequentialRequests);
    }

    [Fact]
    public async Task InterleavedToolCallsAndText_ShouldHandleBoth()
    {
        await RunScriptAsync(ConcurrentRequestScripts.InterleavedToolCallsAndText);
    }
}
