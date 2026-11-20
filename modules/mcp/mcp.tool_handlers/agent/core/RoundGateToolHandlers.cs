namespace McpToolDispatch;

/// <summary>
/// 轮次闸门工具处理器 — 强制 AI 在收尾前选择下一步并注入对应工作流提示词。
/// <para>
/// 本工具不等待用户输入，只返回选项对应的注入文本；是否允许结束回合由运行时
/// （QueryEngine / QueryLoopMiddleware）按 <see cref="RoundGatePolicy"/> 统一裁决。
/// </para>
/// <para>
/// 选择"2 TODO 继续"时，若 <see cref="ITodoService"/> 可用，会把真实就绪 TODO 追加到注入文本，
/// 让 AI 直接拿下一项执行，而不是只收到泛化提示。
/// </para>
/// </summary>
[McpToolDispatch(ToolCategory.Agent, Kind = ToolKind.System)]
public class RoundGateToolHandlers {
    private readonly ITodoService? _todoService;
    private readonly ILogger<RoundGateToolHandlers>? _logger;

    /// <summary>
    /// 初始化轮次闸门工具处理器。
    /// </summary>
    /// <param name="todoService">待办服务（可选；能力关闭时为 null，回退为通用提示）。</param>
    /// <param name="logger">日志记录器（可选）。</param>
    public RoundGateToolHandlers(ITodoService? todoService = null, ILogger<RoundGateToolHandlers>? logger = null) {
        _todoService = todoService;
        _logger = logger;
    }

    /// <summary>
    /// 通过闸门并声明下一步 — 收尾前必须调用，choice=5 是唯一允许停止的选项。
    /// </summary>
    /// <param name="choice">下一步选项：1 直接继续 / 2 TODO 继续 / 3 规划任务 / 4 验收 / 5 结束。</param>
    /// <param name="note">一句话说明下一步将做什么（可选）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>对应选项的工作流提示词；非法选项返回带允许值与正确写法的报错。</returns>
    [McpTool(SystemToolNameEnumConstants.RoundGate,
        "Mandatory round gate. Before ending your turn you MUST call this and pick the next step: " +
        "1=continue directly, 2=continue with next TODO, 3=plan remaining tasks, 4=acceptance with real exe run, 5=end. " +
        "Only choice 5 allows the turn to stop; stopping without it is blocked by the runtime. " +
        "Each choice injects the corresponding workflow prompt into your context. " +
        "You can call it in parallel with your last action in the same round to avoid an extra round trip.",
        MessageRoleEnumConstants.System)]
    public async Task<ToolResult> RoundGateAsync(
        [McpToolParameter("Next step: 1=continue, 2=next TODO, 3=plan tasks, 4=acceptance, 5=end (only 5 stops)")] int choice,
        [McpToolParameter("One-line note about the next action (optional)", Required = false)] string? note = null,
        CancellationToken cancellationToken = default) {
        cancellationToken.ThrowIfCancellationRequested();

        if (!RoundGatePolicy.TryFromNumber(choice, out var parsed)) {
            return ToolResultBuilder.Error()
                .WithText(RoundGatePrompts.BuildInvalidChoiceMessage(choice))
                .Build();
        }

        var text = RoundGatePrompts.ForChoice(parsed);
        if (parsed == RoundGateChoice.NextTodo)
            text = await AppendReadyTodosAsync(text, cancellationToken).ConfigureAwait(false);

        if (!string.IsNullOrWhiteSpace(note))
            text = $"{text}\n\n（你的下一步说明：{note}）";

        _logger?.LogInformation("[RoundGate] choice={Choice} note={Note}", choice, note ?? "-");
        return ToolResultBuilder.Success().WithText(text).Build();
    }

    private async Task<string> AppendReadyTodosAsync(string text, CancellationToken cancellationToken) {
        if (_todoService is null)
            return text;

        try {
            var ready = await _todoService.GetReadyTodosAsync(cancellationToken).ConfigureAwait(false);
            if (ready.Count == 0)
                return $"{text}\n\n当前没有就绪 TODO：请改用 3 规划任务，或 1 直接继续。";

            var lines = ready
                .Take(5)
                .Select((todo, index) => $"{index + 1}) {todo.Content}（id={todo.Id}，priority={todo.Priority}）");
            return $"{text}\n\n当前就绪 TODO：\n{string.Join("\n", lines)}";
        } catch (OperationCanceledException) {
            throw;
        } catch (Exception ex) {
            _logger?.LogWarning(ex, "[RoundGate] 读取就绪 TODO 失败，回退为通用提示");
            return text;
        }
    }
}
