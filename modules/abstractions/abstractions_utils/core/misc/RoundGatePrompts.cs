namespace JoinCode.Abstractions.Utils;

/// <summary>
/// 轮次闸门提示词目录 — tool 结果的注入文本与运行时阻断消息的唯一数据源。
/// <para>选项编号与 <see cref="RoundGateChoice"/> 一一对应；新增/调整选项只需改这里。</para>
/// </summary>
public static class RoundGatePrompts {
    /// <summary>每轮强制三问 — 防"假进展"的通用护栏（答不出 1 视为空转）。</summary>
    public const string EvidenceGuardrail = """
        【每轮必答三问（防假进展）】
        1) 本轮产出的可验证证据是什么（命令输出 / 文件片段 / 测试结果 / 真实运行输出）？
        2) 距离"原始需求逐条满足"还差什么？
        3) 下一轮的具体动作是什么？
        禁止把"思考过程、计划描述、承诺话术"当作证据；答不出第 1 问即为空转，闸门不放行。
        调用 round_gate 时用 evidence 参数提交第 1 问的证据摘要（可为空，但空则累计空转 2 轮触发强制重规划）。
        """;

    /// <summary>选项 1 直接继续 — 有明确下一步动作。</summary>
    public const string Continue = """
        【闸门 · 1 直接继续】继续执行当前任务，禁止总结、禁止重新规划、禁止等待用户确认。
        直接调用下一个工具或编辑文件推进；每完成一项再考虑是否收尾。
        """;

    /// <summary>选项 2 TODO 继续 — 有未关闭清单项。</summary>
    public const string NextTodo = """
        【闸门 · 2 TODO 继续】读取任务文档/TODO 列表，定位下一项未完成项：
        1) 将该项标记为进行中；
        2) 立即执行，不得跳过；
        3) 完成后更新任务文档（勾选 + 写入验证证据）。
        每一项都必须绑定验收标准；全部关闭后改选 3 自验，而不是继续留在此项。
        """;

    /// <summary>选项 3 自验 — 自我阶段性验收，逐条对照原始需求。</summary>
    public const string SelfAccept = """
        【闸门 · 3 自验】对已有产出做自我阶段性验收（不是"我觉得差不多"）：
        1) 逐条列出原始需求条目 → 是否满足 → 核验证据（命令/文件/测试/真实运行输出）；
        2) 必须项全满足才算通过，通过时下一次调用传 verdict=pass；
        3) 有不满足项必须传 verdict=fail，并列出不满足清单，下一轮选 8 回退修正；
        4) ⛔ 禁止用宽松自验替代真实运行验收；需要用户亲自确认的项留给 4 用户验收。
        """;

    /// <summary>选项 4 用户验收 — 高风险/歧义/主观项移交用户。</summary>
    public const string UserAccept = """
        【闸门 · 4 用户验收】把需要用户确认的项移交用户（本轮可收尾，等待用户回复）：
        1) 列出"待用户验收"清单，逐项写操作步骤 + 预期结果，前缀 ○；
        2) 高风险 / 破坏性 / 主观审美 / 歧义需求项必须在此移交，不得自行判定通过；
        3) 用户回复后再继续，通过则下一轮可选 5 完成。
        """;

    /// <summary>选项 5 完成 — 终验通过后的完成出口。</summary>
    public const string Done = """
        【闸门 · 5 完成】终验出口，只有全部满足才允许选本项：
        1) 第一层 自验报告：本会话已有 3 自验且 verdict=pass，需求逐条有产出与证据；
        2) 第二层 自动核查：无未关闭 TODO、无待返工缺陷、无触发中的熔断项；
        3) 第三层 用户终验：高风险/主观项必须已通过 4 用户验收移交（严格模式 JCC_ROUND_GATE_STRICT=1 强制）。
        "做完"= 原始需求逐条有产出且通过终验，不是"我觉得差不多"。
        收尾总结必须写：完成了什么、验收证据在哪、还有哪些 ○ 待用户验收项。
        """;

    /// <summary>选项 6 等候后台 — 有真实后台句柄。</summary>
    public const string AwaitBackground = """
        【闸门 · 6 等候后台】本轮挂起，等待真实后台信号：
        1) 写明句柄标识（后台任务 id / 回调名 / 等待的资源）与预期返回什么；
        2) 禁止把"没有句柄的空等"当作挂起——那属于 0 阻塞上报或 7 重新规划；
        3) 信号到达后的下一轮必须选 3 自验，把后台结果当作验收证据。
        """;

    /// <summary>选项 7 重新规划 — 路线错/卡死/原计划失效。</summary>
    public const string Replan = """
        【闸门 · 7 重新规划】暂停执行，进入重规划。输出：
        ① 原计划哪一步失效了、证据是什么；
        ② 卡点是信息缺失 / 工具缺失 / 方案错误中的哪一类；
        ③ 新计划（≤5 步），每步标明验收方式；
        ④ 新旧计划差异一句话。
        重规划后本轮不执行，下一轮按新计划走"1 直接继续"。
        约束：同一任务重规划超过 2 次 → 强制转 10 求助/升级。
        """;

    /// <summary>选项 8 回退/修正 — 上轮产出有缺陷需返工。</summary>
    public const string Rework = """
        【闸门 · 8 回退/修正】指出需返工的具体产出与缺陷（引用文件/片段/测试失败输出），用 subject 标注缺陷标识。
        给出：缺陷根因、修正范围（只改这些）、回归验证方式。
        修正后必须重跑 3 自验，不得直接转 5 完成。
        约束：同一缺陷返工超过 2 次 → 强制转 7 重新规划（怀疑方案本身有问题）。
        """;

    /// <summary>选项 9 降级交付 — 无法全做完但可交付可用子集。</summary>
    public const string Degrade = """
        【闸门 · 9 降级交付】声明无法完整交付，必须给出：
        ① 已完成且可用的部分（附证据）；
        ② 未完成部分及原因（能力 / 信息 / 时间 / 风险）；
        ③ 取舍类型：临时降级（后续可补齐）还是永久裁剪（需求变更），并给补齐成本；
        ④ 降级方案对用户的实际影响；
        ⑤ 补齐所需的下一步。
        禁止把"没做"包装成"降级"——降级必须是可用子集，且必须先经 4 用户验收移交。
        """;

    /// <summary>选项 10 求助/升级 — 缺工具/权限/信息。</summary>
    public const string Escalate = """
        【闸门 · 10 求助/升级】列清：已尝试的方案及各自失败证据、当前精确卡点、
        需要的人/工具/权限/信息（具体到可执行）、拿到后立即执行的下一步。
        一次只提一个最关键请求。
        约束：不得用此出口逃避可自行解决的困难（可解却求助 = 摆烂）。
        """;

    /// <summary>选项 0 阻塞上报 — 缺用户输入才能继续（隐式出口）。</summary>
    public const string BlockedReport = """
        【闸门 · 0 阻塞上报（隐式出口）】缺少只有用户能提供的输入，本轮直接停下并上报：
        1) 明确缺什么（参数 / 需求选择 / 用户决策 / 凭据）；
        2) 说明已做到哪一步、拿到输入后的下一步动作；
        3) 卡点信息必须只在用户手里——若模型/工具/权限能解决，应选 10 求助/升级而非本项。
        """;

    /// <summary>全部选项一览（阻断消息与系统提示共用）。</summary>
    public const string AllOptions = """
        0 阻塞上报（隐式出口） / 1 直接继续 / 2 TODO 继续 / 3 自验 / 4 用户验收 / 5 完成 /
        6 等候后台 / 7 重新规划 / 8 回退修正 / 9 降级交付 / 10 求助升级
        """;

    /// <summary>取某个选项对应的注入文本。</summary>
    /// <param name="choice">闸门选项。</param>
    /// <returns>该选项的注入提示词。</returns>
    public static string ForChoice(RoundGateChoice choice) => choice switch {
        RoundGateChoice.BlockedReport => BlockedReport,
        RoundGateChoice.Continue => Continue,
        RoundGateChoice.NextTodo => NextTodo,
        RoundGateChoice.SelfAccept => SelfAccept,
        RoundGateChoice.UserAccept => UserAccept,
        RoundGateChoice.Done => Done,
        RoundGateChoice.AwaitBackground => AwaitBackground,
        RoundGateChoice.Replan => Replan,
        RoundGateChoice.Rework => Rework,
        RoundGateChoice.Degrade => Degrade,
        RoundGateChoice.Escalate => Escalate,
        _ => throw new ArgumentOutOfRangeException(nameof(choice), choice, "未知的轮次闸门选项")
    };

    /// <summary>
    /// 构建本轮状态机提示 — 追加到闸门工具结果，告知推荐项、备选、申诉与熔断状态。
    /// </summary>
    /// <param name="step">本轮闸门状态快照。</param>
    /// <returns>状态提示文本。</returns>
    public static string BuildStateAdvisory(RoundGateStep step) {
        var alternatives = step.Recommendation.Alternatives.Count == 0
            ? "（无）"
            : string.Join(" / ", step.Recommendation.Alternatives.Select(RoundGatePolicy.Describe));

        var lines = new List<string> {
            $"【闸门 · 状态机】推荐下一轮：{RoundGatePolicy.Describe(step.Recommendation.Choice)} —— {step.Recommendation.Reason}",
            $"备选：{alternatives}",
        };

        if (step.IsAppeal)
            lines.Add($"你的实选（{RoundGatePolicy.Describe(step.Choice)}）偏离推荐：已记申诉 1 次（上限 {RoundGateSession.MaxAppealStreak} 次，可在 note 写明理由）。");

        if (step.AppealLocked)
            lines.Add("⛔ 申诉已达上限：后续必须按推荐路径执行，闸门不再放行偏离选择。");

        if (step.Fuse is { } fuse)
            lines.Add($"⛔ 熔断生效：{fuse.Reason} —— 强制转 {RoundGatePolicy.Describe(fuse.ForcedChoice)}，不得自行绕过。");

        return string.Join("\n", lines);
    }

    /// <summary>
    /// 构建"未过闸门/未过终验就收尾"的阻断消息 — 由运行时注入上下文并继续循环。
    /// </summary>
    /// <param name="rejection">当前是第几次拦截（从 1 开始）。</param>
    /// <param name="maxRejections">拦截上限；≤0 表示不限制。</param>
    /// <param name="reason">本次拦截原因（来自 <see cref="RoundGateSession.EvaluateStop"/>）。</param>
    /// <param name="recommendation">当前推荐选项与备选。</param>
    /// <returns>阻断消息文本。</returns>
    public static string BuildStopBlockedMessage(
        int rejection, int maxRejections, string reason, RoundGateRecommendation recommendation) {
        var limitText = maxRejections <= 0 ? "本会话不限制拦截次数" : $"最多 {maxRejections} 次";
        var alternatives = recommendation.Alternatives.Count == 0
            ? "（无）"
            : string.Join(" / ", recommendation.Alternatives.Select(RoundGatePolicy.Describe));

        return $"""
            ⛔ 收尾被拦截（第 {rejection} 次，{limitText}）：{reason}
            必须调用 {RoundGatePolicy.ToolName} 选择下一步，允许值：{AllOptions}
            - 产出必须过 3 自验（verdict=pass）才能选 5 完成；
            - 4 用户验收 / 9 降级交付 / 10 求助 / 0 阻塞上报 才允许把本轮交给用户；
            - 闸门推荐：{RoundGatePolicy.Describe(recommendation.Choice)} —— {recommendation.Reason}（备选：{alternatives}）
            {EvidenceGuardrail}
            """;
    }

    /// <summary>
    /// 构建非法选项的报错 — 解释原因、允许值与正确写法（诱导式报错）。
    /// </summary>
    /// <param name="choice">收到的非法值。</param>
    /// <returns>报错文本。</returns>
    public static string BuildInvalidChoiceMessage(int choice) => $"""
        {RoundGatePolicy.ToolName} 参数错误: choice={choice} 不在允许范围。
        原因: 只接受 0~10 的终点选项（0 为隐式出口"阻塞上报"）。
        允许值: {AllOptions}
        正确写法: {RoundGatePolicy.ToolName}(choice=1) 直接继续；choice=3 verdict="pass" 自验通过；choice=5 完成（需先自验通过）。
        """;

    /// <summary>
    /// 构建自验结论缺失/非法的报错 — choice=3 必须声明 verdict。
    /// </summary>
    /// <param name="rawVerdict">收到的非法值。</param>
    /// <returns>报错文本。</returns>
    public static string BuildInvalidVerdictMessage(string? rawVerdict) {
        var shown = string.IsNullOrWhiteSpace(rawVerdict) ? "未提供" : rawVerdict;

        return $"""
            {RoundGatePolicy.ToolName} 参数错误: verdict={shown} 不是合法的自验结论。
            原因: choice=3 自验必须声明结论，闸门据此决定冲突是"8 回退"还是"5 完成"。
            允许值: pass（通过）/ fail（不通过）。
            正确写法: {RoundGatePolicy.ToolName}(choice=3, verdict="pass", evidence="dotnet test 全绿")。
            """;
    }
}
