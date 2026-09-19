
namespace Core.Prompts.Templates.System;

/// <summary>
/// 提示词建议回调 — 在查询循环结束后生成用户输入建议
/// 对齐 TS promptSuggestion.ts::executePromptSuggestion
/// 核心消费链路：PromptSuggestionFilter.SuggestionPrompt → IForkSubAgentManager.ForkAsync() → PromptSuggestionFilter.ShouldFilterSuggestion()
/// </summary>
[Register(typeof(IPostSamplingCallback), ServiceLifetime.Singleton)]
public sealed partial class PromptSuggestionCallback : ServiceEntity, IPostSamplingCallback {
    private readonly IForkSubAgentManager? _forkManager;
    private readonly ILogger<PromptSuggestionCallback>? _logger;

    /// <summary>
    /// 构造提示词建议回调。
    /// </summary>
    /// <param name="forkManager">子智能体分叉管理器，可选。</param>
    /// <param name="logger">日志记录器，可选。</param>
    public PromptSuggestionCallback(
        IForkSubAgentManager? forkManager = null,
        ILogger<PromptSuggestionCallback>? logger = null) {
        _forkManager = forkManager;
        _logger = logger;
    }

    /// <summary>
    /// 采样后回调入口 — 在主线程查询结束后生成提示词建议。
    /// </summary>
    /// <param name="context">采样后上下文。</param>
    /// <returns>表示异步操作的任务。</returns>
    public async Task OnPostSamplingAsync(PostSamplingContext context) {
        if (context.QuerySource != "repl_main_thread") return;

        if (_forkManager is null || context.SessionId is null) {
            _logger?.LogDebug("PromptSuggestion forked agent 不可用（IForkSubAgentManager 或 SessionId 缺失），跳过执行");
            return;
        }

        try {
            var suggestion = await GenerateSuggestionAsync(context).ConfigureAwait(false);

            if (suggestion is null || PromptSuggestionFilter.ShouldFilterSuggestion(suggestion)) {
                _logger?.LogDebug("PromptSuggestion 建议被过滤或为空");
                return;
            }

            _logger?.LogDebug("PromptSuggestion 生成建议: {Suggestion}", suggestion);
        } catch (Exception ex) {
            _logger?.LogWarning(ex, "PromptSuggestion 回调执行失败");
        }
    }

    private async Task<string?> GenerateSuggestionAsync(PostSamplingContext context) {
        var forkOptions = new ForkOptions {
            ParentSessionId = context.SessionId ?? string.Empty,
            TaskDescription = "prompt_suggestion",
            AllowedTools = [],
            UseExactTools = true,
            RunInBackground = true,
            ShareCache = false,
            ShareContext = false,
            MaxIterations = 1,
            SystemPrompt = PromptSuggestionFilter.SuggestionPrompt
        };

        var forkManager = _forkManager ?? throw new InvalidOperationException("Fork manager not available.");
        var result = await forkManager.ForkAsync(forkOptions, context.CancellationToken).ConfigureAwait(false);

        return result.State == ForkState.Completed
            ? result.Result?.Trim()
            : null;
    }
}