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
public sealed partial class ChatToolOrchestrator : ServiceEntity, IChatToolOrchestrator
{
    private readonly IToolRegistry? _toolRegistry;
    private readonly IToolExecutionGateway? _toolExecutionGateway;
    private readonly ICmdMap? _cmdMap;
    private readonly IServiceProvider? _serviceProvider;
    [Inject] private readonly ILogger<ChatToolOrchestrator>? _logger;

    /// <summary>
    /// 初始化工具编排器
    /// </summary>
    public ChatToolOrchestrator(
        IToolRegistry? toolRegistry = null,
        IToolExecutionGateway? toolExecutionGateway = null,
        ICmdMap? cmdMap = null,
        IServiceProvider? serviceProvider = null,
        ILogger<ChatToolOrchestrator>? logger = null)
    {
        _toolRegistry = toolRegistry;
        _toolExecutionGateway = toolExecutionGateway;
        _cmdMap = cmdMap;
        _serviceProvider = serviceProvider;
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
        if (_toolExecutionGateway is null && _cmdMap is null)
        {
            return new ToolCallResult
            {
                ResultText = FormatToolError($"工具执行网关不可用: {toolCallName}"),
                IsError = true
            };
        }

        try
        {
            var arguments = toolCallArguments ?? new Dictionary<string, JsonElement>();

            // 先检查是否是斜杠命令（通过 CmdMap）— AI 调 ExposeToMcp=true 的斜杠命令时走此路径
            if (_cmdMap is not null && _serviceProvider is not null)
            {
                var descriptor = await _cmdMap.ResolveAsync(toolCallName, ct).ConfigureAwait(false);
                if (descriptor is { Source: CmdSource.Slash })
                {
                    var cmdCtx = new CmdContext
                    {
                        CancellationToken = ct,
                        TriggerSource = CmdSource.Mcp,
                        JsonArgs = arguments,
                        Services = _serviceProvider,
                    };
                    var cmdResult = await descriptor.ExecuteAsync(cmdCtx).ConfigureAwait(false);

                    var sb = new StringBuilder();
                    foreach (var c in cmdResult.Content)
                    {
                        if (string.IsNullOrEmpty(c.Text)) continue;
                        if (sb.Length > 0) sb.Append('\n');
                        sb.Append(c.Text);
                    }

                    _logger?.LogInformation("[ChatToolOrchestrator] 斜杠命令调用: {ToolName} → {Result}",
                        toolCallName, cmdResult.IsError ? "ERROR" : "OK");

                    return new ToolCallResult
                    {
                        ResultText = sb.ToString(),
                        IsError = cmdResult.IsError,
                    };
                }
            }

            string? argumentRepairHint = null;
            if (arguments.Count > 0 && _toolRegistry is not null)
            {
                var handler = await _toolRegistry.GetToolAsync(toolCallName, ct).ConfigureAwait(false);
                if (handler is not null)
                {
                    var argRepair = LlmJsonHelper.RepairArguments(toolCallName, arguments, handler.InputSchema, _logger);
                    if (argRepair.RepairHint is not null)
                    {
                        arguments = argRepair.RepairedArguments;
                        argumentRepairHint = argRepair.RepairHint;
                    }
                }
            }

            var combinedRepairHint = argumentRepairHint;

            if (_toolExecutionGateway is null)
            {
                return new ToolCallResult
                {
                    ResultText = FormatToolError($"工具执行网关不可用: {toolCallName}"),
                    IsError = true
                };
            }

            var toolResult = await _toolExecutionGateway.ExecuteAsync(toolCallName, arguments ?? new Dictionary<string, JsonElement>(), ct).ConfigureAwait(false);

            // 构建结果文本
            string resultText;
            if (toolResult.IsImage)
            {
                resultText = "[Image data detected and sent to model]";
            }
            else
            {
                if (toolResult.IsError)
                {
                    var sb = new StringBuilder();
                    foreach (var c in toolResult.Content)
                    {
                        if (string.IsNullOrEmpty(c.Text)) continue;
                        if (sb.Length > 0) sb.Append('\n');
                        sb.Append("Error: ").Append(c.Text);
                    }
                    resultText = sb.ToString();
                }
                else
                {
                    var sb = new StringBuilder();
                    foreach (var c in toolResult.Content)
                    {
                        if (string.IsNullOrEmpty(c.Text)) continue;
                        if (sb.Length > 0) sb.Append('\n');
                        sb.Append(c.Text);
                    }
                    resultText = sb.ToString();
                }
            }

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
            // 多级报错分类：可重试的基础设施故障（限流/超时/5xx）在错误文本中标注，
            // 让 LLM 知道可以重试；致命/逻辑错误保持原样。不 rethrow — 保持工具循环契约，
            // 由模型自行决定修复或放弃（对齐 TS tool_use_error 行为）。
            if (ex is WorkflowException { IsRetryable: true } retryableEx)
            {
                _logger?.LogWarning(retryableEx, "[ChatToolOrchestrator] 工具调用可重试失败: {ToolName}, Code={Code}, Retry={Retry}",
                    toolCallName, retryableEx.ErrorCode, retryableEx.SuggestedRetryCount);
                return new ToolCallResult { ResultText = FormatToolError($"工具调用失败（可重试）: {ex.Message}"), IsError = true };
            }

            _logger?.LogError(ex, "[ChatToolOrchestrator] 工具调用失败: {ToolName}", toolCallName);
            Diag.WriteError($"[ChatToolOrchestrator] Tool={toolCallName}", ex);
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

}
