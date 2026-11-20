namespace McpToolDispatch;

/// <summary>
/// 轮次闸门工具处理器 — 强制 AI 在收尾前选择下一步并注入对应工作流提示词。
/// <para>
/// 本工具不等待用户输入，只返回选项对应的注入文本；是否允许结束回合由运行时
/// （QueryEngine / QueryLoopMiddleware）按 <see cref="RoundGatePolicy"/> 统一裁决。
/// </para>
/// </summary>
[McpToolDispatch(ToolCategory.Agent, Kind = ToolKind.System)]
public class RoundGateToolHandlers {
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
        "Each choice injects the corresponding workflow prompt into your context.",
        MessageRoleEnumConstants.System)]
    public Task<ToolResult> RoundGateAsync(
        [McpToolParameter("Next step: 1=continue, 2=next TODO, 3=plan tasks, 4=acceptance, 5=end (only 5 stops)")] int choice,
        [McpToolParameter("One-line note about the next action (optional)", Required = false)] string? note = null,
        CancellationToken cancellationToken = default) {
        cancellationToken.ThrowIfCancellationRequested();

        if (!RoundGatePolicy.TryFromNumber(choice, out var parsed)) {
            return Task.FromResult(ToolResultBuilder.Error()
                .WithText(RoundGatePrompts.BuildInvalidChoiceMessage(choice))
                .Build());
        }

        var text = RoundGatePrompts.ForChoice(parsed);
        if (!string.IsNullOrWhiteSpace(note))
            text = $"{text}\n\n（你的下一步说明：{note}）";

        return Task.FromResult(ToolResultBuilder.Success().WithText(text).Build());
    }
}
