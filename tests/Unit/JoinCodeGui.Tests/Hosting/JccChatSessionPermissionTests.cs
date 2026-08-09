using FluentAssertions;

using JoinCode.Abstractions.Interfaces;
using JoinCode.Abstractions.LLM.Chat;
using JoinCode.Abstractions.Configuration;
using JoinCode.Abstractions.Configuration.Providers;
using JoinCode.Abstractions.Security;
using JoinCode.Abstractions.Security.Permission;
using JoinCode.Gui.Hosting;

using Microsoft.Extensions.DependencyInjection;

namespace JoinCode.Gui.Tests.Hosting;

/// <summary>
/// 引擎会话权限确认闭环测试 — 验证网关在引擎抛出
/// <see cref="PermissionPendingConfirmationException"/> 时：询问 UI → 批准工具 → 撤回本轮 → 重发同消息。
/// 不依赖真实引擎，用可控的假 ChatService 模拟"首次抛出权限异常、重试后成功"。
/// </summary>
public class JccChatSessionPermissionTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    [Fact]
    public async Task Stream_WhenPermissionPendingAndAllowed_RetriesAndSucceeds()
    {
        var fakeChat = new FakeChatService(throwOnFirstStream: true);
        var fakePermission = new FakePermissionManager();
        var services = new ServiceCollection();
        services.AddSingleton<IToolPermissionManager>(fakePermission);
        var sp = services.BuildServiceProvider();
        var session = new JccChatSession(sp, fakeChat, CreateConfig());
        var handlerCalls = 0;

        session.PermissionConfirmationHandler = request =>
        {
            handlerCalls++;
            request.ToolName.Should().Be("bash");
            request.ConfirmationPrompt.Should().NotBeNullOrWhiteSpace();
            return Task.FromResult(PermissionConfirmationDecision.Allow);
        };

        var events = new List<ChatStreamEvent>();
        await foreach (var evt in session.StreamAsync("请执行命令").WithCancellation(new CancellationTokenSource(Timeout).Token))
        {
            events.Add(evt);
        }

        handlerCalls.Should().Be(1);
        fakeChat.StreamInvokeCount.Should().Be(2, "首次抛出权限异常后应重发同一条消息");
        fakeChat.RewindInvokeCount.Should().Be(1, "重发前应先撤回上一轮避免重复");
        fakePermission.ApprovedTool.Should().Be("bash");
        events.Any(e => e.Type == ChatStreamEventType.Complete).Should().BeTrue();
        events.Should().NotContain(e => e.Type == ChatStreamEventType.ToolCallEnd && e.IsToolError);
    }

    [Fact]
    public async Task Stream_WhenPermissionDenied_EmitsToolErrorAndStops()
    {
        var fakeChat = new FakeChatService(throwOnFirstStream: true);
        var fakePermission = new FakePermissionManager();
        var services = new ServiceCollection();
        services.AddSingleton<IToolPermissionManager>(fakePermission);
        var sp = services.BuildServiceProvider();
        var session = new JccChatSession(sp, fakeChat, CreateConfig());
        var handlerCalls = 0;

        session.PermissionConfirmationHandler = request =>
        {
            handlerCalls++;
            return Task.FromResult(PermissionConfirmationDecision.Deny);
        };

        var events = new List<ChatStreamEvent>();
        await foreach (var evt in session.StreamAsync("请执行命令").WithCancellation(new CancellationTokenSource(Timeout).Token))
        {
            events.Add(evt);
        }

        handlerCalls.Should().Be(1);
        fakeChat.StreamInvokeCount.Should().Be(1, "拒绝后不应重试");
        fakeChat.RewindInvokeCount.Should().Be(0);
        fakePermission.ApprovedTool.Should().BeNull();
        events.Should().Contain(e => e.Type == ChatStreamEventType.ToolCallEnd && e.IsToolError);
    }

    [Fact]
    public async Task Stream_WhenNoHandler_DefaultsToDeny()
    {
        var fakeChat = new FakeChatService(throwOnFirstStream: true);
        var services = new ServiceCollection();
        services.AddSingleton<IToolPermissionManager>(new FakePermissionManager());
        var sp = services.BuildServiceProvider();
        var session = new JccChatSession(sp, fakeChat, CreateConfig());

        var events = new List<ChatStreamEvent>();
        await foreach (var evt in session.StreamAsync("请执行命令").WithCancellation(new CancellationTokenSource(Timeout).Token))
        {
            events.Add(evt);
        }

        fakeChat.StreamInvokeCount.Should().Be(1);
        events.Should().Contain(e => e.Type == ChatStreamEventType.ToolCallEnd && e.IsToolError);
    }

    private static WorkflowConfig CreateConfig() => new()
    {
        Provider = new ProviderConfig
        {
            Provider = "openai",
            ApiKey = "sk-test",
            ModelId = "gpt-4o"
        },
        PipeEndpoint = null
    };

    /// <summary>
    /// 可控假 ChatService：首次流式抛权限异常（并预产出一条事件），后续正常完成。
    /// </summary>
    private sealed class FakeChatService : IChatService
    {
        private readonly bool _throwOnFirstStream;

        public FakeChatService(bool throwOnFirstStream)
        {
            _throwOnFirstStream = throwOnFirstStream;
        }

        public int StreamInvokeCount { get; private set; }
        public int RewindInvokeCount { get; private set; }
        public string? LastSystemPrompt { get; private set; }

        public async IAsyncEnumerable<ChatStreamEvent> StreamWithEventsAsync(
            string message,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            StreamInvokeCount++;
            if (_throwOnFirstStream && StreamInvokeCount == 1)
            {
                yield return ChatStreamEvent.ToolStart("bash", "call_1", "{}");
                throw new PermissionPendingConfirmationException("bash", "是否允许执行 bash 命令？", "req-1");
            }

            yield return ChatStreamEvent.Text("执行完成");
            yield return ChatStreamEvent.Done();
            await Task.CompletedTask;
        }

        public Task<string> SendMessageAsync(string message, CancellationToken cancellationToken = default)
            => Task.FromResult(string.Empty);

        public async IAsyncEnumerable<string> SendMessageStreamAsync(string message, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.CompletedTask;
            yield break;
        }

        public Task ClearHistoryAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<IReadOnlyList<ApiMessageRecord>> GetMessageListAsync(CancellationToken cancellationToken = default)
            => Task.FromResult((IReadOnlyList<ApiMessageRecord>)[]);

        public Task SetSystemPromptAsync(string systemPrompt, CancellationToken cancellationToken = default)
        {
            LastSystemPrompt = systemPrompt;
            return Task.CompletedTask;
        }

        public Task<RewindResult> RewindLastTurnAsync(CancellationToken cancellationToken = default)
        {
            RewindInvokeCount++;
            return Task.FromResult(RewindResult.Ok(RewindKind.TrimLastTurn, 2, 5));
        }

        public Task<RewindResult> RewindToMessageIndexAsync(int messageIndex, CancellationToken cancellationToken = default)
            => Task.FromResult(RewindResult.Ok(RewindKind.TruncateToIndex, 1, 0));

        public Task<RewindResult> RewindToStartAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(RewindResult.Ok(RewindKind.ClearHistory, 0, 0));

        public Task LoadSessionMessagesAsync(IReadOnlyList<ApiMessageRecord> messages, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task CompactHistoryAsync(string summary, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    /// <summary>
    /// 记录批准调用的假权限管理器（仅需 ApproveToolTemporarily）。
    /// </summary>
    private sealed class FakePermissionManager : IToolPermissionManager
    {
        public string? ApprovedTool { get; private set; }

        public Task<PermissionResult> CheckPermissionAsync(PermissionRequest request, CancellationToken cancellationToken = default)
            => Task.FromResult(PermissionResult.Granted());

        public Task SetPermissionModeAsync(PermissionMode mode, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task<PermissionMode> GetCurrentModeAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(PermissionMode.Default);

        public Task AddAllowedPromptAsync(string prompt, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task<int> StripDangerousRulesAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(0);

        public Task RestoreDangerousRulesAsync(int ruleCount, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public void ApproveToolTemporarily(string toolName, TimeSpan duration)
            => ApprovedTool = toolName;

        public void RemoveTemporaryApproval(string toolName)
        {
        }

        public void ClearCache()
        {
        }
    }
}
