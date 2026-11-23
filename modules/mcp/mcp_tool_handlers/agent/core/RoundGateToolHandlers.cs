namespace McpToolDispatch;

/// <summary>
/// 轮次闸门工具处理器 — 强制 AI 在收尾前选择下一步并注入对应工作流提示词。
/// <para>
/// 本工具不等待用户输入，只返回选项对应的注入文本；是否允许结束回合由运行时
/// （QueryEngine 持有的 <see cref="RoundGateSession"/>）按状态机统一裁决。
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
    /// 通过闸门并声明下一步 — 收尾前必须调用；只有出口选项可能停止本轮，且 5 完成需先有 3 自验通过。
    /// </summary>
    /// <param name="choice">下一步选项：0 阻塞上报 / 1 直接继续 / 2 TODO 继续 / 3 自验 / 4 用户验收 / 5 完成 / 6 等候后台 / 7 重新规划 / 8 回退修正 / 9 降级交付 / 10 求助升级。</param>
    /// <param name="note">一句话说明下一步将做什么；偏离推荐时写申诉理由（可选）。</param>
    /// <param name="evidence">本轮可验证证据摘要（命令输出/文件/测试结果）；为空会累计空转（可选）。</param>
    /// <param name="subject">缺陷/子项标识，用于同一缺陷返工计数（可选）。</param>
    /// <param name="verdict">choice=3 的自验结论：pass 通过 / fail 不通过（选择 3 时必填）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>对应选项的工作流提示词；非法选项/非法结论返回带允许值与正确写法的报错。</returns>
    [McpTool(SystemToolNameEnumConstants.RoundGate,
        "Mandatory round gate. Before ending your turn you MUST call this and pick the next step: " +
        "1=continue directly, 2=next TODO, 3=self-acceptance (verdict=pass|fail), 4=hand off to user acceptance, " +
        "5=done (requires a passed self-acceptance), 6=await background handle, 7=replan, 8=rework a defect, " +
        "9=degraded delivery (needs user acceptance handoff), 10=escalate/ask for help, 0=blocked on user input. " +
        "Only 4/5/6/9/10/0 can stop the turn; stopping otherwise is blocked by the runtime state machine. " +
        "Each choice injects the corresponding workflow prompt plus the recommended next option. " +
        "Pass evidence to prove real progress (empty evidence accumulates idle rounds and forces a replan).",
        MessageRoleEnumConstants.System)]
    public async Task<ToolResult> RoundGateAsync(
        [McpToolParameter("Next step: 0=blocked, 1=continue, 2=next TODO, 3=self-accept, 4=user accept, 5=done, 6=await background, 7=replan, 8=rework, 9=degrade, 10=escalate")] int choice,
        [McpToolParameter("One-line note about the next action or the reason for deviating from the recommendation (optional)", Required = false)] string? note = null,
        [McpToolParameter("Verifiable evidence summary for this round (optional; empty counts as idle)", Required = false)] string? evidence = null,
        [McpToolParameter("Defect/subject id used for rework counting (optional)", Required = false)] string? subject = null,
        [McpToolParameter("Self-acceptance verdict for choice=3: pass|fail (required when choice=3)", Required = false)] string? verdict = null,
        CancellationToken cancellationToken = default) {
        cancellationToken.ThrowIfCancellationRequested();

        if (!RoundGatePolicy.TryFromNumber(choice, out var parsed)) {
            return ToolResultBuilder.Error()
                .WithText(RoundGatePrompts.BuildInvalidChoiceMessage(choice))
                .Build();
        }

        if (!RoundGatePolicy.TryParseVerdict(verdict, out var parsedVerdict)) {
            return ToolResultBuilder.Error()
                .WithText(RoundGatePrompts.BuildInvalidVerdictMessage(verdict))
                .Build();
        }

        if (parsed == RoundGateChoice.SelfAccept && parsedVerdict == RoundGateVerdict.None) {
            return ToolResultBuilder.Error()
                .WithText(RoundGatePrompts.BuildInvalidVerdictMessage(null))
                .Build();
        }

        var text = RoundGatePrompts.ForChoice(parsed);
        if (parsed == RoundGateChoice.NextTodo)
            text = await AppendReadyTodosAsync(text, cancellationToken).ConfigureAwait(false);

        text = $"{text}\n\n{RoundGatePrompts.EvidenceGuardrail}";
        text = AppendInvocationEcho(text, note, evidence, subject, parsedVerdict);

        _logger?.LogInformation("[RoundGate] choice={Choice} verdict={Verdict} subject={Subject} evidence={HasEvidence}",
            choice, parsedVerdict.ToValue(), subject ?? "-", string.IsNullOrWhiteSpace(evidence) ? "no" : "yes");
        return ToolResultBuilder.Success().WithText(text).Build();
    }

    private static string AppendInvocationEcho(
        string text, string? note, string? evidence, string? subject, RoundGateVerdict verdict) {
        var lines = new List<string>();
        if (!string.IsNullOrWhiteSpace(note))
            lines.Add($"（你的下一步说明：{note}）");
        if (!string.IsNullOrWhiteSpace(evidence))
            lines.Add($"（本轮证据：{evidence}）");
        if (!string.IsNullOrWhiteSpace(subject))
            lines.Add($"（缺陷标识：{subject}）");
        if (verdict != RoundGateVerdict.None)
            lines.Add($"（自验结论：{verdict.ToValue()}）");

        return lines.Count == 0 ? text : $"{text}\n\n{string.Join("\n", lines)}";
    }

    private async Task<string> AppendReadyTodosAsync(string text, CancellationToken cancellationToken) {
        if (_todoService is null)
            return text;

        try {
            var ready = await _todoService.GetReadyTodosAsync(cancellationToken).ConfigureAwait(false);
            if (ready.Count == 0)
                return $"{text}\n\n当前没有就绪 TODO：请改选 7 重新规划，或 1 直接继续。";

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
