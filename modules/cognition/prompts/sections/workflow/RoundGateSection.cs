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

            本会话启用了强制轮次闸门。每当你准备结束一轮工作时，必须调用 {RoundGatePolicy.ToolName} 工具声明下一步：
            1 直接继续 / 2 TODO 继续 / 3 规划任务 / 4 验收 / 5 结束。

            - 只有选择 5 结束才允许停止；未过闸门就收尾会被运行时拦截并强制续跑。
            - 任务未完成、未验收时不要选择 5，否则会被记为早退。
            - 选择 4 验收时必须启动真实 exe 验证（ADR 0080），禁止用单元测试代替验收；
              需要用户亲自验收的项用 ○ 标记并在回复末尾提示用户。
            - 该工具不等待用户输入，可在无人值守模式使用。
            """;
    }

    /// <summary>
    /// 创建轮次闸门 Section（内容缓存，进程内开关状态固定）。
    /// </summary>
    /// <returns>轮次闸门 Section 实例。</returns>
    public static SystemPromptSection Create() =>
        SystemPromptSection.Cached("round_gate", GetContent);
}
