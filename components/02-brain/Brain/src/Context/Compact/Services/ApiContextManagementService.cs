namespace Core.Context.Compact;

/// <summary>
/// API 端上下文管理服务 — 对齐 TS apiMicrocompact.ts getAPIContextManagement
/// 通过 context_management 请求参数让 Anthropic API 在服务端自动清理工具结果
/// 不破坏 prompt cache，优于客户端 microcompact
/// </summary>
[Register]
public sealed partial class ApiContextManagementService : IApiContextManagementService
{
    // 对齐 TS: DEFAULT_MAX_INPUT_TOKENS / DEFAULT_TARGET_INPUT_TOKENS
    private const int DefaultMaxInputTokens = 180_000;
    private const int DefaultTargetInputTokens = 40_000;

    /// <summary>
    /// 可清除工具结果的工具名 — 对齐 TS TOOLS_CLEARABLE_RESULTS
    /// 使用枚举常量，避免硬编码字符串
    /// </summary>
    private static readonly string[] ToolsClearableResults =
    [
        ShellToolNameConstants.ShellExecute,  // "Bash"
        ShellToolNameConstants.Powershell,    // "PowerShell"
        SearchToolNameConstants.Glob,         // "Glob"
        SearchToolNameConstants.Grep,         // "Grep"
        FileToolNameConstants.FileRead,       // "Read"
        WebToolNameConstants.WebFetch,        // "WebFetch"
        WebToolNameConstants.WebSearch,       // "WebSearch"
    ];

    /// <summary>
    /// 可清除工具使用记录的工具名 — 对齐 TS TOOLS_CLEARABLE_USES
    /// 这些工具的输入不需要保留（写入类操作）
    /// </summary>
    private static readonly string[] ToolsClearableUses =
    [
        FileToolNameConstants.FileEdit,       // "Edit"
        FileToolNameConstants.FileWrite,      // "Write"
        NotebookToolNameConstants.NotebookEdit, // "NotebookEdit"
    ];

    /// <summary>
    /// 获取 API 端上下文管理配置 — 对齐 TS getAPIContextManagement
    /// </summary>
    public ContextManagementConfig? GetConfig(
        bool hasThinking = false,
        bool isRedactThinkingActive = false,
        bool clearAllThinking = false)
    {
        var strategies = new List<ContextEditStrategy>();

        // 对齐 TS: thinking 策略 — 保留 thinking 块
        // redact-thinking 激活时跳过（已编辑的块无模型可见内容）
        // clearAllThinking 时仅保留最近 1 个 thinking turn
        if (hasThinking && !isRedactThinkingActive)
        {
            strategies.Add(new ClearThinkingStrategy
            {
                Keep = clearAllThinking
                    ? new ContextKeep { Type = "thinking_turns", Value = 1 }
                    : "all"
            });
        }

        // 对齐 TS: 工具清除策略 — 通过环境变量控制
        var useClearToolResults = IsEnvTruthy("JCC_USE_API_CLEAR_TOOL_RESULTS");
        var useClearToolUses = IsEnvTruthy("JCC_USE_API_CLEAR_TOOL_USES");

        if (!useClearToolResults && !useClearToolUses)
        {
            return strategies.Count > 0 ? new ContextManagementConfig { Edits = strategies } : null;
        }

        if (useClearToolResults)
        {
            var triggerThreshold = GetEnvInt("JCC_API_MAX_INPUT_TOKENS", DefaultMaxInputTokens);
            var keepTarget = GetEnvInt("JCC_API_TARGET_INPUT_TOKENS", DefaultTargetInputTokens);

            strategies.Add(new ClearToolUsesStrategy
            {
                Trigger = new ContextTrigger { Type = "input_tokens", Value = triggerThreshold },
                ClearAtLeast = new ContextTokenThreshold
                {
                    Type = "input_tokens",
                    Value = triggerThreshold - keepTarget
                },
                ClearToolInputs = ToolsClearableResults,
            });
        }

        if (useClearToolUses)
        {
            var triggerThreshold = GetEnvInt("JCC_API_MAX_INPUT_TOKENS", DefaultMaxInputTokens);
            var keepTarget = GetEnvInt("JCC_API_TARGET_INPUT_TOKENS", DefaultTargetInputTokens);

            strategies.Add(new ClearToolUsesStrategy
            {
                Trigger = new ContextTrigger { Type = "input_tokens", Value = triggerThreshold },
                ClearAtLeast = new ContextTokenThreshold
                {
                    Type = "input_tokens",
                    Value = triggerThreshold - keepTarget
                },
                ExcludeTools = ToolsClearableUses,
            });
        }

        return strategies.Count > 0 ? new ContextManagementConfig { Edits = strategies } : null;
    }

    private static bool IsEnvTruthy(string envVar)
    {
        var value = Environment.GetEnvironmentVariable(envVar);
        return string.Equals(value, "true", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(value, "1", StringComparison.OrdinalIgnoreCase);
    }

    private static int GetEnvInt(string envVar, int defaultValue)
    {
        var value = Environment.GetEnvironmentVariable(envVar);
        return int.TryParse(value, out var result) ? result : defaultValue;
    }
}
