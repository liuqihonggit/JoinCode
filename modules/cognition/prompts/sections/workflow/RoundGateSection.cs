namespace Core.Prompts.Sections;

/// <summary>
/// 轮次闸门 Section — 只注入协议骨架与硬约束；逐选项提示词由 round_gate 工具结果按需注入。
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

            收尾前必须调用 {RoundGatePolicy.ToolName} 声明下一步；不调用会被运行时拦截并强制续跑。
            工具结果只注入你所选那一项的提示词与下一轮推荐，不预载其它选项。

            - 只有 4 用户验收 / 5 完成 / 6 等候后台 / 9 降级交付 / 10 求助升级 / 0 阻塞上报 能收尾。
            - 5 完成必须先有 3 自验（verdict=pass）；9 降级需已移交 4；严格模式（JCC_ROUND_GATE_STRICT=1）下 5 也需先 4。
            - 熔断：重规划>2 次 → 10；同一缺陷返工>2 次 → 7；自验连续 3 轮不通过 → 10；连续 2 轮无证据 → 7。
            - 偏离推荐需在 note 写理由，连续 2 次后锁定推荐项。
            - 全局原则：把整件事做完再交卷；缺用户输入才停下；这不免除高风险/破坏性操作前的确认。
            """;
    }

    /// <summary>
    /// 创建轮次闸门 Section（内容缓存，进程内开关状态固定）。
    /// </summary>
    /// <returns>轮次闸门 Section 实例。</returns>
    public static SystemPromptSection Create() =>
        SystemPromptSection.Cached("round_gate", GetContent);
}
