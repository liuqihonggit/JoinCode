namespace NativeTui.Host.Tests;

/// <summary>
/// 斜杠命令串行集合 — SlashCommandRunner.RunAsync 内部重定向 Console.Out（进程全局），
/// 并发执行命令会互相踩输出捕获（Runner 契约：调用方保证不并发）；跑真实命令的测试类
/// 挂本集合强制类间串行（对齐 TUI/GUI 单命令队列语义）。
/// </summary>
[CollectionDefinition(CollectionName)]
public sealed class SlashCommandSerialCollection
{
    /// <summary>集合名（测试类挂靠用）。</summary>
    public const string CollectionName = "slash-command-serial";
}

/// <summary>
/// 斜杠命令交互测试共享 harness（TASK065 F2/F6）— fake DI（IChatService/ICodeService/
/// IPlanService/IToolRegistry stub）驱动真实命令注册链（ChatCommandRegistry 兜底生成注册），
/// 零引擎依赖纯回调管道验证；对齐 EngineSessionCommandCallbacksTests 的 fake 形态提取共享。
/// </summary>
internal static class CommandTestHarness
{
    /// <summary>构造 fake 服务容器（SlashCommandRunner 必需依赖齐备；chat 参数放宽 IChatService —
    /// TASK070 C6 CompactChatService 子类可注入）。</summary>
    public static ServiceProvider BuildFakeServices(IChatService chat) =>
        new ServiceCollection()
            .AddSingleton<IChatService>(chat)
            .AddSingleton<ICodeService>(new StubCodeService())
            .AddSingleton<IPlanService>(new StubPlanService())
            .AddSingleton<IToolRegistry>(new StubToolRegistry())
            .AddFakeCommandComposition()
            .BuildServiceProvider();

    /// <summary>注册斜杠命令组合桩（DSG051 起 Runner 经 INodeRegistry 取 CommandCategoryNode）：
    /// 命令注册表（未注入时用源码生成器全量注册）+ 按分类包装的节点注册表 + 固定 Profile。
    /// 探针命令测试传入自带 registry（已注册探针）复用同一节点投影。</summary>
    /// <param name="services">DI 集合。</param>
    /// <param name="commandRegistry">命令注册表（null = 生成器全量注册默认命令）。</param>
    /// <returns>链式返回服务集合。</returns>
    public static IServiceCollection AddFakeCommandComposition(
        this IServiceCollection services,
        ChatCommandRegistry? commandRegistry = null)
    {
        var registry = commandRegistry ?? new ChatCommandRegistry();
        if (commandRegistry is null)
        {
            GeneratedCommandRegistration.RegisterAllChatCommands(registry);
        }

        return services
            .AddSingleton(registry)
            .AddSingleton<INodeRegistry>(new FakeNodeRegistry(registry))
            .AddSingleton<IProfileManager>(new FakeProfileManager());
    }

    /// <summary>节点注册表桩 — 命令分类按 ChatCommandRegistry 实时投影（探针后注册也可见）。</summary>
    private sealed class FakeNodeRegistry(ChatCommandRegistry registry) : INodeRegistry
    {
        public bool TryGetNode(string nodeId, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out INode? node)
        {
            node = Project().FirstOrDefault(candidate => candidate.NodeId == nodeId);
            return node is not null;
        }

        /// <summary>桩不接受运行期节点注册 — 节点由命令注册表实时投影。</summary>
        public RegistrationHandle Register(string nodeId, Func<IServiceProvider, NodeMetadata?, INode> factory) =>
            new(dispose: static () => { }, replace: static _ => { });

        public Task<IReadOnlyList<INode>> GetNodesForConsumerAsync(
            string consumerId, ResolvedProfile profile, CancellationToken cancellationToken = default) =>
            Task.FromResult(Project());

        public Task<IReadOnlyList<T>> GetServicesForConsumerAsync<T>(
            string consumerId, ResolvedProfile profile, CancellationToken cancellationToken = default,
            bool failFastOnMissing = false)
            where T : class =>
            Task.FromResult<IReadOnlyList<T>>(Project().OfType<T>().ToList());

        private IReadOnlyList<INode> Project() =>
            Enum.GetValues<ChatCommandCategory>()
                .Select(category => (INode)new CommandCategoryNode(
                    $"{category.ToValue()}-command", category, registry.GetByCategory(category)))
                .ToList();
    }

    /// <summary>Profile 管理桩 — 固定测试 Profile，切换无操作。</summary>
    private sealed class FakeProfileManager : IProfileManager
    {
        public ResolvedProfile Current { get; } = new(
            "test", FrozenDictionary<string, ResolvedConsumerConfig>.Empty);

        public IReadOnlyList<string> AvailableProfiles => ["test"];

        public event Func<ResolvedProfile, ResolvedProfile, ValueTask>? ProfileSwitching
        {
            add { }
            remove { }
        }

        public Task SwitchAsync(string profileName, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    /// <summary>IChatService 记录桩 — ClearHistoryAsync 计数，其余成员不支持。</summary>
    internal sealed class FakeChatService : IChatService
    {
        public int ClearHistoryCalls;

        public Task ClearHistoryAsync(CancellationToken cancellationToken = default)
        {
            ClearHistoryCalls++;
            return Task.CompletedTask;
        }

        /// <summary>TASK135 B1 — 会话翻页入口；测试桩不做真实翻页。</summary>
        public Task<string> StartNewConversationAsync(string? projectPath = null, CancellationToken cancellationToken = default) => Task.FromResult("new-session");
        public Task<string> SendMessageAsync(string message, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public IAsyncEnumerable<string> SendMessageStreamAsync(string message, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public IAsyncEnumerable<ChatStreamEvent> StreamWithEventsAsync(string message, CancellationToken cancellationToken = default, IEventConsumer? eventConsumer = null) => throw new NotSupportedException();
        public Task<IReadOnlyList<ApiMessageRecord>> GetMessageListAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task SetSystemPromptAsync(string systemPrompt, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<RewindResult> RewindLastTurnAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<RewindResult> RewindToMessageIndexAsync(int messageIndex, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<RewindResult> RewindToStartAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task LoadSessionMessagesAsync(IReadOnlyList<ApiMessageRecord> messages, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task CompactHistoryAsync(string summary, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask DisposeAsync() => default;
    }

    /// <summary>可压缩 ChatService 桩（TASK070 C6 — /compact 投影测试）：历史/摘要/压实三链路可编程。</summary>
    internal sealed class CompactChatService : IChatService
    {
        public int CompactHistoryCalls;

        public Task ClearHistoryAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        /// <summary>TASK135 B1 — 会话翻页入口；测试桩不做真实翻页。</summary>
        public Task<string> StartNewConversationAsync(string? projectPath = null, CancellationToken cancellationToken = default) => Task.FromResult("new-session");
        public Task<string> SendMessageAsync(string message, CancellationToken cancellationToken = default) => Task.FromResult("稳定摘要文本");
        public IAsyncEnumerable<string> SendMessageStreamAsync(string message, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public IAsyncEnumerable<ChatStreamEvent> StreamWithEventsAsync(string message, CancellationToken cancellationToken = default, IEventConsumer? eventConsumer = null) => throw new NotSupportedException();
        public Task<IReadOnlyList<ApiMessageRecord>> GetMessageListAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ApiMessageRecord>>([
                new ApiMessageRecord { Role = "user", Content = "第一问" },
                new ApiMessageRecord { Role = "assistant", Content = "第一答" },
            ]);
        public Task SetSystemPromptAsync(string systemPrompt, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<RewindResult> RewindLastTurnAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<RewindResult> RewindToMessageIndexAsync(int messageIndex, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<RewindResult> RewindToStartAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task LoadSessionMessagesAsync(IReadOnlyList<ApiMessageRecord> messages, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task CompactHistoryAsync(string summary, CancellationToken cancellationToken = default) {
            CompactHistoryCalls++;
            return Task.CompletedTask;
        }
        public ValueTask DisposeAsync() => default;
    }

    /// <summary>ICodeService 签名占位桩 — BuildCommandServiceProvider 必需依赖，命令执行不触达。</summary>
    private sealed class StubCodeService : ICodeService
    {
        public Task<string> GenerateCodeAsync(string prompt, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<string> AnalyzeCodeAsync(string code, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<string> ExecuteCodeAsync(string code, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    /// <summary>IPlanService 签名占位桩 — BuildCommandServiceProvider 必需依赖，命令执行不触达。</summary>
    private sealed class StubPlanService : IPlanService
    {
        public Task<string> ExecutePlanAsync(string userPrompt, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<PlanExecutionResult> ExecutePlanWithResultAsync(string userPrompt, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    /// <summary>IToolRegistry 签名占位桩 — 使 CmdMap 构造（非 null 走斜杠优先路由）。</summary>
    private sealed class StubToolRegistry : IToolRegistry
    {
        public ValueTask DisposeAsync() => default;
        public Task RegisterToolAsync(IToolHandler handler, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task RegisterToolsBatchAsync(IReadOnlyList<IToolHandler> handlers, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task RegisterToolAsync(string name, string description, ToolSchema inputSchema, ToolHandler handler, CancellationToken cancellationToken = default, ToolKind kind = ToolKind.System, string? groupName = null, ToolTimeoutPolicy? timeoutPolicy = null, string? category = null) => throw new NotSupportedException();
        public Task<bool> UnregisterToolAsync(string toolName, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IToolHandler?> GetToolAsync(string toolName, CancellationToken cancellationToken = default) => Task.FromResult<IToolHandler?>(null);
        public Task<IReadOnlyDictionary<string, IToolHandler>> GetAllToolsAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ToolResult> ExecuteToolAsync(string toolName, Dictionary<string, JsonElement> arguments, CancellationToken cancellationToken = default, ToolProgressCallback? onProgress = null) => throw new NotSupportedException();
        public Task<ToolInfo?> GetToolInfoAsync(string toolName, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<ToolInfo>> GetAllToolInfosAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> ContainsToolAsync(string toolName, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<int> GetCountAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task ClearAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<FrozenSet<string>> GetGroupNamesAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyDictionary<string, IToolHandler>> GetToolsByKindAsync(ToolKind kind, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyDictionary<string, IToolHandler>> GetToolsByGroupAsync(string groupName, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
