namespace Core.Prompts.Sections;

/// <summary>
/// 轮次闸门 Section — 告知 AI 收尾前必须调用 round_gate 选择下一步。
/// <para>闸门关闭（JCC_ROUND_GATE=off 或 auto 下的无人值守会话）时返回 null，不注入任何内容。</para>
/// </summary>
[PromptSection(Name = "round_gate", InjectOn = PromptSectionInject.AgentMode)]
public static class RoundGateSection {
    /// <summary>
    /// 获取轮次闸门协议说明；闸门未启用时返回 null。
    /// </summary>
    /// <returns>协议说明文本；未启用时返回 null。</returns>
    public static string? GetContent() {
        if (!RoundGatePolicy.IsEnabledForMainAgent())
            return null;

        return $"""
            # 轮次闸门（{RoundGatePolicy.ToolName}）

            本会话启用了强制轮次闸门。每当你要结束一轮工作时，必须调用 {RoundGatePolicy.ToolName} 声明下一步：

            {RoundGatePrompts.AllOptions}

            ## 状态机规则
            - 执行（1/2）→ 验证（3 自验，verdict=pass/fail）→ 完成候选（5）。**产出必须过 3 才能碰 5**，未过验证的收尾会被运行时拦截。
            - 元操作：7 重新规划（路线错/卡死）、8 回退修正（上轮有缺陷，用 subject 标注缺陷）。
            - 收敛出口：9 降级交付、10 求助升级；缺用户输入才用 0 阻塞上报（隐式出口）。
            - 挂起：6 等候后台（必须有真实句柄）。4 用户验收把 ○ 清单移交用户，本轮可收尾等回复。
            - 只有 4 / 5 / 6 / 9 / 10 / 0 才可能收尾，且 5 需先有 3 自验通过；严格模式（JCC_ROUND_GATE_STRICT=1）下 5 与 9 还需已移交 4 用户验收。
            - 熔断：重规划 >2 次→强制 10；同一缺陷返工 >2 次→强制 7；自验连续 3 轮不通过→强制 10；连续 2 轮空转→强制 7；用户验收连续 2 次驳回→强制 7。
            - 闸门会给"1 个推荐 + 2 个备选"；偏离推荐需在 note 写理由，连续偏离 2 次后锁定推荐项。
            - 该工具不等待用户输入，可在无人值守模式使用。

            ## 全局宪法
            把整件事做完再交卷；完成前中途停下极少合理。除非缺少继续所需的用户输入，否则继续做完。
            这不免除高风险/破坏性操作前的确认。"做完"= 原始需求逐条有产出且通过终验。
            空转（无证据推进）和摆烂（可解却求助）同样不可接受。
            """;
    }

    /// <summary>
    /// 创建轮次闸门 Section（内容缓存，进程内开关状态固定）。
    /// </summary>
    /// <returns>轮次闸门 Section 实例。</returns>
    public static SystemPromptSection Create() =>
        SystemPromptSection.Cached("round_gate", GetContent);
}
