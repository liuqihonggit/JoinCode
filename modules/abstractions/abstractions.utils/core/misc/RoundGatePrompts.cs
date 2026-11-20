namespace JoinCode.Abstractions.Utils;

/// <summary>
/// 轮次闸门提示词目录 — tool 结果的注入文本与运行时阻断消息的唯一数据源。
/// <para>选项编号与 <see cref="RoundGateChoice"/> 一一对应；新增/调整选项只需改这里。</para>
/// </summary>
public static class RoundGatePrompts {
    /// <summary>选项 1 直接继续 — 要求不回退到总结/规划。</summary>
    public const string Continue = """
        【闸门 · 1 直接继续】继续执行当前任务，禁止总结、禁止重新规划、禁止等待用户确认。
        直接调用下一个工具或编辑文件推进；每完成一项再考虑是否收尾。
        """;

    /// <summary>选项 2 TODO 继续 — 从任务文档取下一项执行。</summary>
    public const string NextTodo = """
        【闸门 · 2 TODO 继续】读取任务文档/TODO 列表，定位下一项未完成项：
        1) 将该项标记为进行中；
        2) 立即执行，不得跳过；
        3) 完成后更新任务文档（勾选 + 写入验证证据）。
        若当前没有任务文档，先按 3 规划任务。
        """;

    /// <summary>选项 3 规划任务 — 先补任务清单与四列验收表。</summary>
    public const string Plan = """
        【闸门 · 3 规划任务】暂停执行，先补齐计划再动手：
        1) 在任务文档中列出剩余任务清单；
        2) 每项写四列验收表（基建实现 / 消费点 / 已实现 / 已验收）；
        3) 标注依赖顺序；
        4) 从第一项开始执行。
        """;

    /// <summary>选项 4 验收 — 真实 exe 验收，禁止单元测试挡箭牌，○ 标记用户亲自验收项。</summary>
    public const string Acceptance = """
        【闸门 · 4 验收】进入验收阶段，必须走真实运行验收（ADR 0080）：
        1) 编译本次改动；
        2) 启动真实 exe（如 artifacts/bin/JoinCode/Debug/net10.0/jcc.exe）或真实入口，按用户路径实际操作；
        3) 记录实际输出、日志或截图作为验收证据；
        4) 对照任务文档的验收表逐项勾选（基建实现 / 消费点 / 已实现 / 已验收）；
        5) ⛔ 禁止用"单元测试通过"作为验收依据——单元测试只能作为补充；
        6) 需要用户亲自验收的项（交互体验、视觉效果、真实账号/环境操作）必须用 ○ 标记，
           并在回复末尾单列"待用户验收"清单，写明每项操作步骤和预期结果，提示用户逐项确认。
        """;

    /// <summary>选项 5 结束 — 放行收尾，但要求先满足完成/验收前提。</summary>
    public const string End = """
        【闸门 · 5 结束】结束闸门已放行，本轮允许停止。选择本项的前提：
        - 任务全部完成；
        - 验收已完成，或剩余项已用 ○ 标记并在回复中提示用户。
        请输出收尾总结：完成了什么、验收证据在哪、还有哪些 ○ 待用户验收项。
        """;

    /// <summary>取某个选项对应的注入文本。</summary>
    /// <param name="choice">闸门选项。</param>
    /// <returns>该选项的注入提示词。</returns>
    public static string ForChoice(RoundGateChoice choice) => choice switch {
        RoundGateChoice.Continue => Continue,
        RoundGateChoice.NextTodo => NextTodo,
        RoundGateChoice.Plan => Plan,
        RoundGateChoice.Acceptance => Acceptance,
        RoundGateChoice.End => End,
        _ => throw new ArgumentOutOfRangeException(nameof(choice), choice, "未知的轮次闸门选项")
    };

    /// <summary>
    /// 构建"未过闸门就收尾"的阻断消息 — 由运行时注入上下文并继续循环。
    /// </summary>
    /// <param name="rejection">当前是第几次拦截（从 1 开始）。</param>
    /// <param name="maxRejections">拦截上限；≤0 表示不限制。</param>
    /// <returns>阻断消息文本。</returns>
    public static string BuildStopBlockedMessage(int rejection, int maxRejections) {
        var limitText = maxRejections <= 0
            ? "本会话不限制拦截次数"
            : $"最多 {maxRejections} 次";

        return $"""
            ⛔ 收尾被拦截（第 {rejection} 次，{limitText}）：你还没有调用 round_gate 选择下一步。
            必须调用 round_gate：
            1 直接继续 / 2 TODO 继续 / 3 规划任务 / 4 验收 / 5 结束
            - 只有选择 5 结束才允许本轮停止；
            - 任务未完成或未验收时选择 5 会被记为早退；
            - 不确定是否完成时，先选 4 验收，用真实 exe 验证后再决定是否选 5。
            """;
    }

    /// <summary>
    /// 构建非法选项的报错 — 解释原因、允许值与正确写法（诱导式报错）。
    /// </summary>
    /// <param name="choice">收到的非法值。</param>
    /// <returns>报错文本。</returns>
    public static string BuildInvalidChoiceMessage(int choice) => $"""
        round_gate 参数错误: choice={choice} 不在允许范围。
        原因: 必须选择 1~5 之间的下一步。
        允许值: 1 直接继续 / 2 TODO 继续 / 3 规划任务 / 4 验收 / 5 结束。
        正确写法: round_gate(choice=1) 表示直接继续；round_gate(choice=5) 表示结束本轮。
        """;
}
