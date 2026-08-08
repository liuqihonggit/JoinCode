using JoinCode.Abstractions.Configuration.Llm;
using JoinCode.Abstractions.Interfaces;
using JoinCode.Abstractions.LLM.Chat;

namespace JoinCode.Gui.Hosting;

/// <summary>
/// 引擎会话实现 — 进程内组装真实 AI 工作流（AddAiWorkflowServices + 共享管道），
/// 替代骨架阶段的占位回显。UI 与引擎解耦的唯一边界仍为 <c>IJccChatSession</c>。
/// 引擎具体组装收敛在本类，不蔓延到 Views/ViewModels。
/// </summary>
internal sealed class JccChatSession : IJccChatSession
{
    private readonly Microsoft.Extensions.DependencyInjection.ServiceProvider _services;
    private readonly IChatService _chat;
    private readonly JoinCode.Abstractions.Configuration.WorkflowConfig _config;

    internal JccChatSession(
        Microsoft.Extensions.DependencyInjection.ServiceProvider services,
        IChatService chat,
        JoinCode.Abstractions.Configuration.WorkflowConfig config)
    {
        _services = services;
        _chat = chat;
        _config = config;
    }

    /// <summary>
    /// 创建引擎会话：加载配置 → 组装 DI（Composition + 共享管道）→ 解析 IChatService。
    /// provider 为 null 时回退到环境变量 / 默认 deepseek 配置。
    /// </summary>
    public static async Task<IJccChatSession> CreateAsync(
        CancellationToken cancellationToken = default)
    {
        var config = await LoadConfigFallbackAsync(cancellationToken).ConfigureAwait(false);

        var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection();
        services.AddSingleton<Microsoft.Extensions.Configuration.IConfiguration>(
            new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build());
        services.AddLogging(b => b.AddConsole());
        services.AddAiWorkflowServices(config);
        services.AddAllPipelines();

        var sp = services.BuildServiceProvider();
        var chat = sp.GetRequiredService<IChatService>();
        return new JccChatSession(sp, chat, config);
    }

    public bool IsReady => true;

    /// <summary>当前 Provider 名称（deepseek/openai/azure/anthropic/agnes）</summary>
    public string CurrentProvider => _config.Provider.Provider;

    /// <summary>当前启用的模型 ID</summary>
    public string CurrentModelId => _config.Provider.ModelId;

    /// <summary>当前 Provider 可选真实模型 ID 列表（绑定共享 ModelConfigLoader）</summary>
    public IReadOnlyList<string> AvailableModels =>
        ModelConfigLoader.GetModels(_config.Provider.Provider)
            .Select(m => m.Id)
            .ToArray();

    public Task InitializeAsync(CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    public IAsyncEnumerable<ChatStreamEvent> StreamAsync(
        string message,
        CancellationToken cancellationToken = default)
        => _chat.StreamWithEventsAsync(message, cancellationToken);

    public Task<IReadOnlyList<ApiMessageRecord>> GetMessagesAsync(CancellationToken cancellationToken = default)
        => _chat.GetMessageListAsync(cancellationToken);

    public Task ClearHistoryAsync(CancellationToken cancellationToken = default)
        => _chat.ClearHistoryAsync(cancellationToken);

    public Task<RewindResult> RewindLastTurnAsync(CancellationToken cancellationToken = default)
        => _chat.RewindLastTurnAsync(cancellationToken);

    public async ValueTask DisposeAsync()
    {
        if (_chat is IAsyncDisposable chatDisposable)
        {
            await chatDisposable.DisposeAsync().ConfigureAwait(false);
        }
        await _services.DisposeAsync().ConfigureAwait(false);
    }

    private static async Task<JoinCode.Abstractions.Configuration.WorkflowConfig> LoadConfigFallbackAsync(
        CancellationToken cancellationToken)
    {
        var config = await new Core.Configuration.ConfigLoader().LoadAsync(
                new IO.FileSystem.PhysicalFileSystem(), cancellationToken)
            .ConfigureAwait(false);

        // 进程内 GUI：不连接命名管道服务，标准 HTTP QueryService（PipeEndpoint 置 null）
        config.PipeEndpoint = null;
        return config;
    }
}