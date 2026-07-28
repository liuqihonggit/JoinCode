
namespace Core.Context.Compact;

/// <summary>
/// SessionMemory 提取回调 — 在每轮 LLM 采样后判断是否需要提取会话记忆
/// 对齐 TS sessionMemory.ts::extractSessionMemory
/// 核心消费链路：ISessionMemoryExtractionService.BuildExtractionPromptAsync() → IForkSubAgentManager.ForkAsync()
/// </summary>
[Register]
public sealed partial class SessionMemoryExtractionCallback : IPostSamplingCallback
{
    private const string EditToolName = "Edit";

    private readonly ISessionMemoryExtractionService _extractionService;
    private readonly IForkSubAgentManager? _forkManager;
    private readonly ILogger<SessionMemoryExtractionCallback>? _logger;

    public SessionMemoryExtractionCallback(
        ISessionMemoryExtractionService extractionService,
        IForkSubAgentManager? forkManager = null,
        ILogger<SessionMemoryExtractionCallback>? logger = null)
    {
        _extractionService = extractionService ?? throw new ArgumentNullException(nameof(extractionService));
        _forkManager = forkManager;
        _logger = logger;
    }

    public async Task OnPostSamplingAsync(PostSamplingContext context)
    {
        if (context.QuerySource != "repl_main_thread") return;

        if (!_extractionService.ShouldExtract(context.EstimatedTokenCount, context.ToolCallsSinceLastExtraction))
        {
            return;
        }

        try
        {
            await _extractionService.InitializeSessionMemoryFileAsync(context.CancellationToken).ConfigureAwait(false);

            var prompt = await _extractionService.BuildExtractionPromptAsync(context.CancellationToken).ConfigureAwait(false);

            _logger?.LogDebug("SessionMemory 提取提示词已构建，长度={Length}", prompt.Length);

            if (_forkManager is not null && context.SessionId is not null)
            {
                var memoryPath = _extractionService.GetMemoryFilePath();

                var forkOptions = new ForkOptions
                {
                    ParentSessionId = context.SessionId,
                    TaskDescription = "session_memory",
                    AllowedTools = [EditToolName],
                    UseExactTools = true,
                    RunInBackground = true,
                    ShareCache = false,
                    ShareContext = false,
                    MaxIterations = 3,
                    SystemPrompt = "你是一个会话记忆更新助手。你的唯一任务是使用 Edit 工具更新会话记忆文件，然后停止。不要调用任何其他工具。"
                };

                var result = await _forkManager.ForkAsync(forkOptions, context.CancellationToken).ConfigureAwait(false);

                _logger?.LogDebug("SessionMemory forked agent 完成: ForkId={ForkId}, State={State}",
                    result.ForkId, result.State);
            }
            else
            {
                _logger?.LogDebug("SessionMemory forked agent 不可用（IForkSubAgentManager 或 SessionId 缺失），跳过执行");
            }

            _extractionService.RecordExtractionCompleted(context.EstimatedTokenCount);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "SessionMemory 提取回调执行失败");
        }
    }
}
