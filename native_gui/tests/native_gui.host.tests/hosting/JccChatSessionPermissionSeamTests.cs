namespace JoinCode.Gui.Tests.Hosting;

/// <summary>
/// 引擎会话权限接缝测试 — 宿主注入的确认委托转发到引擎侧 <see cref="GuiPermissionConfirmationHandler"/>；
/// 流式发送直通引擎（确认在同轮内由引擎确认点解决，网关不再重发/撤回）。
/// </summary>
public class JccChatSessionPermissionSeamTests {
    [Fact]
    public async Task PermissionConfirmationHandler_ForwardsToEngineHandler() {
        var services = new ServiceCollection();
        services.AddSingleton<IToolPermissionManager>(new FakePermissionManager());
        services.AddSingleton<GuiPermissionConfirmationHandler>();
        services.AddSingleton<IPermissionConfirmationHandler>(sp => sp.GetRequiredService<GuiPermissionConfirmationHandler>());
        var sp = services.BuildServiceProvider();
        await using var session = new JccChatSession(sp, new FakeChatService(), CreateConfig());
        Func<PermissionConfirmationRequest, Task<PermissionConfirmationDecision>> callback =
            _ => Task.FromResult(PermissionConfirmationDecision.Allow);

        session.PermissionConfirmationHandler = callback;

        var engineHandler = sp.GetService<IPermissionConfirmationHandler>().Should().BeOfType<GuiPermissionConfirmationHandler>().Subject;
        engineHandler.Handler.Should().BeSameAs(callback, "宿主委托必须落到引擎确认器上，否则引擎确认点无处理者");
        session.PermissionConfirmationHandler.Should().BeSameAs(callback);
    }

    [Fact]
    public async Task StreamAsync_PassesThroughEngineStreamWithoutRewind() {
        await using var session = new JccChatSession(new ServiceCollection().BuildServiceProvider(), new FakeChatService(), CreateConfig());

        var events = new List<ChatStreamEvent>();
        await foreach (var evt in session.StreamAsync("你好").WithCancellation(new CancellationTokenSource(TimeSpan.FromSeconds(5)).Token)) {
            events.Add(evt);
        }

        events.Should().Contain(e => e.Type == ChatStreamEventType.Complete);
    }

    private static WorkflowConfig CreateConfig() => new() {
        Provider = new ProviderConfig {
            Vendor = "openai",
            ApiKey = "sk-test",
            ModelId = "gpt-4o"
        },
        PipeEndpoint = null
    };

    /// <summary>最小假 ChatService — 正常产出一条文本事件与完成事件。</summary>
    private sealed class FakeChatService : IChatService {
        public async IAsyncEnumerable<ChatStreamEvent> StreamWithEventsAsync(
            string message,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default) {
            yield return ChatStreamEvent.Text("执行完成");
            yield return ChatStreamEvent.Done();
            await Task.CompletedTask;
        }

        public Task<string> SendMessageAsync(string message, CancellationToken cancellationToken = default)
            => Task.FromResult(string.Empty);

        public async IAsyncEnumerable<string> SendMessageStreamAsync(string message, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default) {
            await Task.CompletedTask;
            yield break;
        }

        public Task ClearHistoryAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<IReadOnlyList<ApiMessageRecord>> GetMessageListAsync(CancellationToken cancellationToken = default)
            => Task.FromResult((IReadOnlyList<ApiMessageRecord>)[]);

        public Task SetSystemPromptAsync(string systemPrompt, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<RewindResult> RewindLastTurnAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(RewindResult.Ok(RewindKind.TrimLastTurn, 0, 0));

        public Task<RewindResult> RewindToMessageIndexAsync(int messageIndex, CancellationToken cancellationToken = default)
            => Task.FromResult(RewindResult.Ok(RewindKind.TruncateToIndex, 0, 0));

        public Task<RewindResult> RewindToStartAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(RewindResult.Ok(RewindKind.ClearHistory, 0, 0));

        public Task LoadSessionMessagesAsync(IReadOnlyList<ApiMessageRecord> messages, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task CompactHistoryAsync(string summary, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    /// <summary>最小假权限管理器。</summary>
    private sealed class FakePermissionManager : IToolPermissionManager {
        public Task<PermissionResult> CheckPermissionAsync(PermissionRequest request, CancellationToken cancellationToken = default)
            => Task.FromResult(PermissionResult.Granted());

        public Task SetPermissionModeAsync(PermissionMode mode, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<PermissionMode> GetCurrentModeAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(PermissionMode.Auto);

        public Task AddAllowedPromptAsync(string prompt, CancellationToken cancellationToken = default) => Task.CompletedTask;

        /// <summary>TASK070 批次3 接口扩展 — 永久批准（seam 测试不消费，no-op 满足接口）。</summary>
        public Task ApproveToolPermanentlyAsync(string toolName, string? ruleContent, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task<int> StripDangerousRulesAsync(CancellationToken cancellationToken = default) => Task.FromResult(0);

        public Task RestoreDangerousRulesAsync(int ruleCount, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public void ApproveToolTemporarily(string toolName, TimeSpan duration) {
        }

        public void ApproveLevelTemporarily(CommandDangerLevel level) {
        }

        public void RemoveTemporaryApproval(string toolName) {
        }

        public Task ClearCacheAsync() => Task.CompletedTask;
    }
}
