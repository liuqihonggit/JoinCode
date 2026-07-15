
namespace Core.Prompts.Templates.System;

/// <summary>
/// 提示词建议回调 — 在查询循环结束后生成用户输入建议
/// 对齐 TS promptSuggestion.ts::executePromptSuggestion
/// 核心消费链路：PromptSuggestionFilter.SuggestionPrompt → IForkSubAgentManager.ForkAsync() → PromptSuggestionFilter.ShouldFilterSuggestion()
/// </summary>
[Register]
public sealed partial class PromptSuggestionCallback : IPostSamplingCallback
{
    private readonly IForkSubAgentManager? _forkManager;
    private readonly ILogger<PromptSuggestionCallback>? _logger;

    public PromptSuggestionCallback(
        IForkSubAgentManager? forkManager = null,
        ILogger<PromptSuggestionCallback>? logger = null)
    {
        _forkManager = forkManager;
        _logger = logger;
    }

    public async Task OnPostSamplingAsync(PostSamplingContext context)
    {
        if (context.QuerySource != "repl_main_thread") return;

        if (_forkManager is null || context.SessionId is null)
        {
            _logger?.LogDebug("PromptSuggestion forked agent 不可用（IForkSubAgentManager 或 SessionId 缺失），跳过执行");
            return;
        }

        try
        {
            var suggestion = await GenerateSuggestionAsync(context).ConfigureAwait(false);

            if (suggestion is null || PromptSuggestionFilter.ShouldFilterSuggestion(suggestion))
            {
                _logger?.LogDebug("PromptSuggestion 建议被过滤或为空");
                return;
            }

            _logger?.LogDebug("PromptSuggestion 生成建议: {Suggestion}", suggestion);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "PromptSuggestion 回调执行失败");
        }
    }

    private async Task<string?> GenerateSuggestionAsync(PostSamplingContext context)
    {
        var forkOptions = new ForkOptions
        {
            ParentSessionId = context.SessionId!,
            TaskDescription = "prompt_suggestion",
            AllowedTools = [],
            UseExactTools = true,
            RunInBackground = true,
            ShareCache = false,
            ShareContext = false,
            MaxIterations = 1,
            SystemPrompt = PromptSuggestionFilter.SuggestionPrompt
        };

        var result = await _forkManager!.ForkAsync(forkOptions, context.CancellationToken).ConfigureAwait(false);

        return result.State == ForkState.Completed
            ? result.Result?.Trim()
            : null;
    }
}
