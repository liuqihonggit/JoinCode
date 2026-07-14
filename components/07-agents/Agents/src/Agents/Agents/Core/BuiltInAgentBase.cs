
namespace Core.Agents;

/// <summary>
/// 内置 Agent 基类 - 提供通用实现
/// </summary>
public abstract class BuiltInAgentBase : IBuiltInAgent, IAsyncDisposable
{
    protected readonly IChatClient Kernel;
    protected readonly ILogger? Logger;
    protected readonly AgentContext Context;
    protected readonly SemaphoreSlim ContextLock;
    protected readonly IClockService _clock;

    /// <summary>
    /// 上下文层级管理器 - 支持分层上下文压缩
    /// </summary>
    protected IContextHierarchy? ContextHierarchy { get; private set; }

    private IContextHierarchyFactory? _contextHierarchyFactory;

    /// <summary>
    /// 上下文压缩配置
    /// </summary>
    protected ContextHierarchyOptions CompressionConfig { get; private set; } = ContextHierarchyOptions.Default;

    public abstract string Name { get; }
    public abstract string Description { get; }
    public abstract BuiltInAgentType AgentType { get; }
    public abstract string SystemPrompt { get; }

    protected BuiltInAgentBase(
        IChatClient kernel,
        IClockService clock,
        ILogger? logger = null)
    {
        Kernel = kernel;
        _clock = clock;
        Logger = logger;
        Context = new AgentContext();
        ContextLock = new SemaphoreSlim(1, 1);

        // 在构造函数中同步初始化上下文
        InitializeContext();
    }

    /// <summary>
    /// 异步初始化 - 子类可在需要时调用
    /// </summary>
    protected async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await InitializeContextAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// 配置上下文压缩 - 子类可在构造函数中调用
    /// </summary>
    /// <param name="config">压缩配置</param>
    /// <param name="factory">上下文层级工厂（可选）</param>
    protected void ConfigureContextCompression(ContextHierarchyOptions config, IContextHierarchyFactory? factory = null)
    {
        CompressionConfig = config ?? ContextHierarchyOptions.Default;
        _contextHierarchyFactory = factory;

        if (CompressionConfig.AutoCompressionEnabled)
        {
            InitializeContextHierarchy();
        }
    }

    /// <summary>
    /// 初始化上下文层级管理器
    /// </summary>
    private void InitializeContextHierarchy()
    {
        if (_contextHierarchyFactory == null)
        {
            Logger?.LogWarning("[{AgentName}] IContextHierarchyFactory 未注册，跳过上下文层级管理器初始化", Name);
            return;
        }

        var options = CompressionConfig;
        ContextHierarchy = _contextHierarchyFactory.Create(options);
        Context.ContextHierarchy = ContextHierarchy;

        Logger?.LogInformation(
            "[{AgentName}] 上下文层级管理器已初始化，Token阈值: {Threshold}, 自动压缩: {AutoCompression}",
            Name,
            options.TokenThreshold,
            options.AutoCompressionEnabled);
    }

    /// <summary>
    /// 同步初始化上下文 - 在构造函数中调用
    /// </summary>
    protected virtual void InitializeContext()
    {
        Context.Messages.Clear();
        Context.Messages.Add(new ContractAgentMessage
        {
            Role = MessageRoleConstants.System,
            Content = SystemPrompt,
            Timestamp = _clock.GetUtcNow()
        });
    }

    protected virtual async Task InitializeContextAsync(CancellationToken cancellationToken = default)
    {
        await ContextLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            InitializeContext();
        }
        finally
        {
            ContextLock.Release();
        }
    }


    public virtual async Task<AgentResponse> ProcessAsync(
        string userInput,
        bool useTools = false,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        var response = new AgentResponse();

        try
        {
            Logger?.LogInformation("[{AgentName}] 处理用户输入: {Input}, 使用工具: {UseTools}", Name, userInput, useTools);

            await ContextLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                Context.Messages.Add(new ContractAgentMessage
                {
                    Role = MessageRoleConstants.User,
                    Content = userInput,
                    Timestamp = _clock.GetUtcNow()
                });
            }
            finally
            {
                ContextLock.Release();
            }

            // 检查并触发上下文自动压缩（基于配置）
            await CheckAndCompressContextAsync(cancellationToken).ConfigureAwait(false);

            var chatCompletionService = Kernel.GetChatCompletionService();
            var chatHistory = await BuildMessageListAsync(cancellationToken).ConfigureAwait(false);

            var executionSettings = new ChatOptions
            {
                Temperature = GetTemperature(),
                MaxTokens = GetMaxTokens(),
                TopP = 0.95f,
                ToolChoice = useTools ? ToolChoice.AutoInvoke : ToolChoice.None
            };

            var results = await chatCompletionService.GetApiMessageContentsAsync(
                chatHistory,
                executionSettings,
                Kernel,
                cancellationToken).ConfigureAwait(false);
            var result = results[0];

            stopwatch.Stop();

            response.Content = result.Content ?? "抱歉，我无法生成回复。";
            response.ExecutionTimeMs = stopwatch.ElapsedMilliseconds;

            if (result.TokenUsage != null)
            {
                response.TokenUsage = new TokenUsage
                {
                    PromptTokens = result.TokenUsage.PromptTokens,
                    CompletionTokens = result.TokenUsage.CompletionTokens,
                    CacheCreationInputTokens = result.TokenUsage.CacheCreationInputTokens,
                    CacheReadInputTokens = result.TokenUsage.CacheReadInputTokens
                };
            }

            await ContextLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                Context.Messages.Add(new ContractAgentMessage
                {
                    Role = MessageRoleConstants.Assistant,
                    Content = response.Content,
                    Timestamp = _clock.GetUtcNow()
                });
                Context.TotalTokenUsage.PromptTokens += response.TokenUsage.PromptTokens;
                Context.TotalTokenUsage.CompletionTokens += response.TokenUsage.CompletionTokens;
                Context.TotalTokenUsage.CacheCreationInputTokens += response.TokenUsage.CacheCreationInputTokens;
                Context.TotalTokenUsage.CacheReadInputTokens += response.TokenUsage.CacheReadInputTokens;
            }
            finally
            {
                ContextLock.Release();
            }

            Logger?.LogInformation("[{AgentName}] 响应生成完成，耗时 {ElapsedMs}ms", Name, response.ExecutionTimeMs);

            return response;
        }
        catch (OperationCanceledException)
        {
            Logger?.LogWarning("[{AgentName}] 处理已取消", Name);
            throw;
        }
        catch (Exception ex)
        {
            Logger?.LogError(ex, "[{AgentName}] 处理时出错", Name);
            response.Content = $"处理时出错: {ex.Message}";
            return response;
        }
    }

    public virtual async Task ClearContextAsync(CancellationToken cancellationToken = default)
    {
        await ContextLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            Context.Messages.Clear();
            Context.Messages.Add(new ContractAgentMessage
            {
                Role = MessageRoleConstants.System,
                Content = SystemPrompt,
                Timestamp = _clock.GetUtcNow()
            });
            Context.TotalToolCalls = 0;
            Context.TotalTokenUsage = new TokenUsage();
        }
        finally
        {
            ContextLock.Release();
        }

        Logger?.LogInformation("[{AgentName}] 上下文已清空", Name);
    }

    public virtual async Task<AgentContext> GetContextAsync(CancellationToken cancellationToken = default)
    {
        await ContextLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var context = new AgentContext
            {
                Messages = new List<ContractAgentMessage>(Context.Messages),
                StartedAt = Context.StartedAt,
                TotalToolCalls = Context.TotalToolCalls,
                TotalTokenUsage = new TokenUsage
                {
                    PromptTokens = Context.TotalTokenUsage.PromptTokens,
                    CompletionTokens = Context.TotalTokenUsage.CompletionTokens,
                    CacheCreationInputTokens = Context.TotalTokenUsage.CacheCreationInputTokens,
                    CacheReadInputTokens = Context.TotalTokenUsage.CacheReadInputTokens
                },
                ContextHierarchy = Context.ContextHierarchy
            };
            return context;
        }
        finally
        {
            ContextLock.Release();
        }
    }

    /// <summary>
    /// 获取压缩后的上下文 - 将当前上下文压缩到指定层级
    /// </summary>
    /// <param name="targetLayer">目标层级类型</param>
    /// <param name="compressionFunc">压缩函数，如果为 null 则使用默认压缩</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>压缩后的上下文内容</returns>
    protected virtual async Task<string?> GetCompressedContextAsync(
        ContextLayerType targetLayer,
        Func<string, ContextLayerType, string>? compressionFunc = null,
        CancellationToken cancellationToken = default)
    {
        if (ContextHierarchy == null)
        {
            Logger?.LogWarning("[{AgentName}] 上下文层级管理器未初始化，无法获取压缩上下文", Name);
            return null;
        }

        return await GetCompressedContextCoreAsync(targetLayer, compressionFunc, cancellationToken).ConfigureAwait(false);
    }

    private async Task<string?> GetCompressedContextCoreAsync(
        ContextLayerType targetLayer,
        Func<string, ContextLayerType, string>? compressionFunc = null,
        CancellationToken cancellationToken = default)
    {
        if (ContextHierarchy == null)
        {
            return null;
        }

        var currentLayer = await ContextHierarchy.GetCurrentLayerAsync(cancellationToken).ConfigureAwait(false);
        if (currentLayer == null)
        {
            Logger?.LogWarning("[{AgentName}] 当前层级为空，无法压缩", Name);
            return null;
        }

        compressionFunc ??= DefaultCompressionFunc;
        var promotedLayer = await ContextHierarchy.PromoteToLayerAsync(targetLayer, compressionFunc, cancellationToken).ConfigureAwait(false);

        if (promotedLayer == null)
        {
            Logger?.LogWarning("[{AgentName}] 压缩层级返回空", Name);
            return null;
        }

        Logger?.LogInformation(
            "[{AgentName}] 上下文已压缩到 {TargetLayer} 层级，Token数: {TokenCount}",
            Name,
            targetLayer,
            promotedLayer.TokenCount);

        return promotedLayer.Content;
    }

    /// <summary>
    /// 恢复上下文层 - 将指定层级解压为详细层级
    /// </summary>
    /// <param name="sourceLayer">源层级类型</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>是否成功恢复</returns>
    protected virtual async Task<bool> RestoreContextLayerAsync(
        ContextLayerType sourceLayer,
        CancellationToken cancellationToken = default)
    {
        if (ContextHierarchy == null)
        {
            Logger?.LogWarning("[{AgentName}] 上下文层级管理器未初始化，无法恢复上下文层", Name);
            return false;
        }

        var result = await ContextHierarchy.DemoteToLayerAsync(sourceLayer, cancellationToken).ConfigureAwait(false);

        if (result)
        {
            Logger?.LogInformation(
                "[{AgentName}] 上下文层 {SourceLayer} 已恢复",
                Name,
                sourceLayer);
        }
        else
        {
            Logger?.LogWarning(
                "[{AgentName}] 无法恢复上下文层 {SourceLayer}",
                Name,
                sourceLayer);
        }

        return result;
    }

    /// <summary>
    /// 检查并触发上下文自动压缩
    /// 在 ProcessAsync 中自动调用
    /// </summary>
    /// <param name="cancellationToken">取消令牌</param>
    protected virtual async Task CheckAndCompressContextAsync(CancellationToken cancellationToken = default)
    {
        if (ContextHierarchy == null || !CompressionConfig.AutoCompressionEnabled)
        {
            return;
        }

        var totalTokens = await ContextHierarchy.GetTotalTokenCountAsync(cancellationToken).ConfigureAwait(false);

        if (totalTokens <= CompressionConfig.TokenThreshold)
        {
            return;
        }

        Logger?.LogInformation(
            "[{AgentName}] Token 总数 ({TotalTokens}) 超过阈值 ({Threshold})，触发自动压缩",
            Name,
            totalTokens,
            CompressionConfig.TokenThreshold);

        var detailedLayer = await ContextHierarchy.GetLayerAsync(ContextLayerType.Detailed, cancellationToken).ConfigureAwait(false);
        if (detailedLayer != null && !detailedLayer.IsCompressed)
        {
            try
            {
                await GetCompressedContextCoreAsync(ContextLayerType.Summary, cancellationToken: cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Logger?.LogError(ex, "[{AgentName}] 自动压缩失败", Name);
            }
        }
    }

    /// <summary>
    /// 默认压缩函数
    /// </summary>
    private static string DefaultCompressionFunc(string content, ContextLayerType targetLayer)
    {
        if (string.IsNullOrEmpty(content))
        {
            return string.Empty;
        }

        var targetLength = targetLayer switch
        {
            ContextLayerType.Summary => Math.Max(200, content.Length / 4),
            ContextLayerType.Index => Math.Max(50, content.Length / 10),
            _ => content.Length
        };

        if (content.Length <= targetLength)
        {
            return content;
        }

        var prefix = content[..(targetLength / 2)];
        var suffix = content[^(targetLength / 2)..];

        return $"{prefix}...[压缩内容 {content.Length - targetLength} 字符]...{suffix}";
    }

    protected virtual async Task<MessageList> BuildMessageListAsync(CancellationToken cancellationToken = default)
    {
        var chatHistory = new MessageList();

        await ContextLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            chatHistory.AddRange(
                Context.Messages.Select(message => new ApiMessage(
                    MessageRoleExtensions.FromValue(message.Role.ToLower()) ?? MessageRole.User,
                    message.Content)));
        }
        finally
        {
            ContextLock.Release();
        }

        return chatHistory;
    }

  
    protected virtual float GetTemperature() => LlmParameters.Default.Temperature;
    protected virtual int GetMaxTokens() => LlmParameters.Default.MaxTokens;

    public virtual async ValueTask DisposeAsync()
    {
        if (ContextHierarchy is IDisposable disposableHierarchy)
            disposableHierarchy.Dispose();
        ContextLock.Dispose();
        GC.SuppressFinalize(this);
    }
}
