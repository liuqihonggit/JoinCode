namespace Core.Context;

/// <summary>
/// 工具调用执行结果 — ChatToolOrchestrator.ExecuteToolCallAsync 的返回值
/// </summary>
public sealed record ToolCallResult
{
    /// <summary>工具结果文本</summary>
    public required string ResultText { get; init; }

    /// <summary>是否为错误结果</summary>
    public required bool IsError { get; init; }

    /// <summary>结构化 Patch 数据</summary>
    public StructuredPatchHunk[]? StructuredPatch { get; init; }

    /// <summary>图片输出的 ContentBlocks</summary>
    public IReadOnlyList<ToolContent>? ContentBlocks { get; init; }

    /// <summary>
    /// 上下文修改器 — 对齐 TS ToolResult.contextModifier
    /// 由 ChatService 在处理结果时应用到 ToolUseContext
    /// </summary>
    public Action<ToolUseContext>? ContextModifier { get; init; }

    /// <summary>
    /// 注入消息 — 对齐 TS SkillTool newMessages
    /// 由 ChatService 在处理结果时追加到对话历史
    /// </summary>
    public IReadOnlyList<JoinCode.Abstractions.LLM.Chat.ApiMessage>? InjectedMessages { get; init; }
}

/// <summary>
/// 工具调用编排器 — 从 ChatService.StreamWithEventsAsync 提取
/// 负责权限检查、Hook 编排、工具执行
/// </summary>
[Register]
public sealed partial class ChatToolOrchestrator : IChatToolOrchestrator
{
    private readonly IToolRegistry? _toolRegistry;
    private readonly IPermissionChecker? _permissionChecker;
    private readonly IHookOrchestrator? _hookOrchestrator;
    [Inject] private readonly ILogger<ChatToolOrchestrator>? _logger;

    /// <summary>
    /// 初始化工具编排器
    /// </summary>
    public ChatToolOrchestrator(
        IToolRegistry? toolRegistry = null,
        IPermissionChecker? permissionChecker = null,
        IHookOrchestrator? hookOrchestrator = null,
        ILogger<ChatToolOrchestrator>? logger = null)
    {
        _toolRegistry = toolRegistry;
        _permissionChecker = permissionChecker;
        _hookOrchestrator = hookOrchestrator;
        _logger = logger;
    }

    /// <summary>
    /// 执行工具调用：权限检查 → PreHook → 执行 → PostHook
    /// </summary>
    /// <param name="toolCallName">工具名称</param>
    /// <param name="toolCallId">工具调用 ID</param>
    /// <param name="toolCallArguments">工具调用参数（已解析）</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>工具调用结果</returns>
    /// <exception cref="PermissionPendingConfirmationException">
    /// 交互模式下权限待确认时向上传播，由 Host 层弹出确认框
    /// </exception>
    public async Task<ToolCallResult> ExecuteToolCallAsync(
        string toolCallName,
        string? toolCallId,
        Dictionary<string, JsonElement>? toolCallArguments,
        CancellationToken ct)
    {
        if (_toolRegistry is null)
        {
            return new ToolCallResult
            {
                ResultText = FormatToolError($"工具注册表不可用: {toolCallName}"),
                IsError = true
            };
        }

        try
        {
            var arguments = toolCallArguments ?? new Dictionary<string, JsonElement>();

            string? argumentRepairHint = null;
            if (arguments.Count > 0 && _toolRegistry is not null)
            {
                var handler = await _toolRegistry.GetToolAsync(toolCallName, ct).ConfigureAwait(false);
                if (handler is not null)
                {
                    var argRepair = ToolCallRepairService.RepairArguments(toolCallName, arguments, handler.InputSchema);
                    if (argRepair.RepairHint is not null)
                    {
                        arguments = argRepair.RepairedArguments;
                        argumentRepairHint = argRepair.RepairHint;
                    }
                }
            }

            var combinedRepairHint = argumentRepairHint;

            // 权限检查 — 对齐 TS 版 hasPermissionsToUseTool
            await CheckPermissionAsync(toolCallName, arguments, ct).ConfigureAwait(false);

            // Hook: PreToolUse — 工具执行前触发 Hook 编排
            await ExecutePreHooksAsync(toolCallName, arguments, ct).ConfigureAwait(false);

            var toolResult = await _toolRegistry!.ExecuteToolAsync(toolCallName, arguments!, ct).ConfigureAwait(false);

            // 构建结果文本
            string resultText;
            if (toolResult.IsImage)
            {
                resultText = "[Image data detected and sent to model]";
            }
            else
            {
                resultText = string.Join("\n",
                    toolResult.Content
                        .Select(c => toolResult.IsError ? $"Error: {c.Text}" : c.Text)
                        .Where(t => !string.IsNullOrEmpty(t)));
            }

            // Hook: PostToolUse — 工具执行后触发 Hook 编排
            await ExecutePostHooksAsync(toolCallName, resultText, ct).ConfigureAwait(false);

            if (combinedRepairHint is not null)
            {
                resultText = $"[ToolCallRepair] {combinedRepairHint}\n{resultText}";
            }

            _logger?.LogInformation("[ChatToolOrchestrator] 工具调用: {ToolName} → {Result}",
                toolCallName, toolResult.IsError ? "ERROR" : "OK");

            return new ToolCallResult
            {
                ResultText = resultText,
                IsError = toolResult.IsError,
                StructuredPatch = toolResult.StructuredPatch,
                ContentBlocks = toolResult.IsImage ? toolResult.Content : null,
                ContextModifier = toolResult.ContextModifier,
                InjectedMessages = toolResult.InjectedMessages
            };
        }
        catch (PermissionPendingConfirmationException ex)
        {
            if (Core.Utils.TestEnvironmentDetector.IsNonInteractive)
            {
                _logger?.LogWarning("[ChatToolOrchestrator] 非交互模式下权限确认自动拒绝: {ToolName}", toolCallName);
                return new ToolCallResult { ResultText = FormatToolError(ex.Message), IsError = true };
            }

            throw;
        }
        catch (PermissionDeniedException ex)
        {
            _logger?.LogWarning("[ChatToolOrchestrator] 工具权限被拒绝: {ToolName}, Reason={Reason}", toolCallName, ex.Message);
            return new ToolCallResult { ResultText = FormatToolError(ex.Message), IsError = true };
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "[ChatToolOrchestrator] 工具调用失败: {ToolName}", toolCallName);
            return new ToolCallResult { ResultText = FormatToolError($"工具调用失败: {ex.Message}"), IsError = true };
        }
    }

    /// <summary>
    /// 格式化工具错误消息 — 对齐 TS formatError + tool_use_error 标签
    /// 超过 10000 字符时截断：保留前 5000 + 后 5000
    /// </summary>
    private static string FormatToolError(string message)
    {
        if (message.Length > 10000)
        {
            var halfLength = 5000;
            var truncated = message.Length - 10000;
            message = $"{message[..halfLength]}\n\n... [{truncated} characters truncated] ...\n\n{message[^halfLength..]}";
        }

        return $"<tool_use_error>{message}</tool_use_error>";
    }

    /// <summary>
    /// 权限检查 — 对齐 TS 版 hasPermissionsToUseTool
    /// </summary>
    private async Task CheckPermissionAsync(string toolCallName, Dictionary<string, JsonElement>? arguments, CancellationToken ct)
    {
        if (_permissionChecker is null) return;

        var permResult = await _permissionChecker.CheckPermissionAsync(toolCallName, arguments).ConfigureAwait(false);
        if (permResult.ConfirmationRequired)
        {
            // 提取 RuleContent — 对齐 TS 版 ruleContent
            // WebFetch 使用 "domain:hostname" 格式
            string? ruleContent = null;
            if (string.Equals(toolCallName, WebToolNameConstants.WebFetch, StringComparison.OrdinalIgnoreCase)
                && arguments != null
                && arguments.TryGetValue("url", out var urlEl)
                && urlEl.ValueKind == JsonValueKind.String)
            {
                try
                {
                    if (Uri.TryCreate(urlEl.GetString(), UriKind.Absolute, out var parsed))
                        ruleContent = $"domain:{parsed.Host}";
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Trace.WriteLine($"URL parsing failed for permission rule: {ex.Message}");
                }
            }

            throw new PermissionPendingConfirmationException(
                toolCallName, permResult.Reason ?? $"工具 '{toolCallName}' 需要确认",
                ruleContent: ruleContent);
        }

        if (!permResult.IsApproved)
        {
            throw PermissionDeniedException.ToolDenied(
                toolCallName,
                permResult.Reason ?? "权限被拒绝");
        }
    }

    /// <summary>
    /// Hook: PreToolUse — 工具执行前触发 Hook 编排
    /// </summary>
    private async Task ExecutePreHooksAsync(string toolCallName, Dictionary<string, JsonElement>? arguments, CancellationToken ct)
    {
        if (_hookOrchestrator is null) return;

        var prePayload = new Dictionary<string, JsonElement>
        {
            ["tool_name"] = JsonSerializer.SerializeToElement(toolCallName, ChatServiceJsonContext.Default.String),
            ["tool_input"] = arguments != null
                ? JsonSerializer.SerializeToElement(arguments, ChatServiceJsonContext.Default.DictionaryStringJsonElement)
                : JsonSerializer.SerializeToElement((string?)null, ChatServiceJsonContext.Default.String)
        };

        await foreach (var hookResult in _hookOrchestrator.ExecuteHooksAsync(
            HookEvent.PreToolUse, prePayload,
            matcher: toolCallName, cancellationToken: ct).ConfigureAwait(false))
        {
            if (hookResult.Outcome == HookOutcome.Blocking)
            {
                throw PermissionDeniedException.ToolDenied(toolCallName,
                    hookResult.Message ?? "Hook 阻止了工具执行");
            }
        }
    }

    /// <summary>
    /// Hook: PostToolUse — 工具执行后触发 Hook 编排
    /// </summary>
    private async Task ExecutePostHooksAsync(string toolCallName, string toolResultText, CancellationToken ct)
    {
        if (_hookOrchestrator is null) return;

        var postPayload = new Dictionary<string, JsonElement>
        {
            ["tool_name"] = JsonSerializer.SerializeToElement(toolCallName, ChatServiceJsonContext.Default.String),
            ["tool_result"] = JsonSerializer.SerializeToElement(toolResultText ?? "", ChatServiceJsonContext.Default.String)
        };

        await foreach (var _ in _hookOrchestrator.ExecuteHooksAsync(
            HookEvent.PostToolUse, postPayload,
            matcher: toolCallName, cancellationToken: ct).ConfigureAwait(false))
        {
            // 消费所有结果，PostToolUse 通常不阻塞
        }
    }
}
