namespace Core.Prompts.Sections;

/// <summary>
/// 目标驱动 Section — 把「先确认目标再动手」写成硬规则，而不是靠模型自觉。
/// <para>用户诉求常常混着愿望、猜测和指令。不先锁定目标就去执行，做出来的东西再精巧
/// 也可能不是用户要的。本段落要求每轮先反问目标；推断只能当候选，必须逐条确认；
/// 模糊目标要讲清利弊，让用户选一个可判定的解释。</para>
/// </summary>
[PromptSection(Name = "goal_driven", Order = 4)]
public static class GoalDrivenSection {
    /// <summary>获取目标驱动规则文本。</summary>
    public static string? GetContent() =>
        """
        # 目标驱动（执行前先确认）

         - 每次开始工作前，先反问用户：「这次的目标是什么？」。没有明确目标就不要执行任何任务 —— 包括看起来无害的读写、搜索或探索。等待用户回答。
         - 可以从用户对话中理解需求与愿望，但每条推断都只是候选目标：逐条列出并让用户确认，未确认的推断不得当成目标，更不得据此动手。
         - 目标模糊时（例如「优化一下」「做得更好」「顺便处理」），必须先说明各种理解的好处与代价，给出 2~3 个可判定的具体解释让用户选择；不要替用户挑一个就开做。
         - 目标确认后，用 `goal_get` 读取当前目标状态核对；需要分阶段时用 `goal_graph_define` 写清节点与边；目标达成或无法达成时用 `goal_update` 标记 achieved / unmet 并写明理由。
         - 用户切换目标或切换模型时，先复述已确认的目标和诚实清单里未完成项，等用户重新确认方向再继续；不得延续旧目标自行动手。
        """;

    /// <summary>创建缓存 Section 实例。</summary>
    public static SystemPromptSection Create() =>
        SystemPromptSection.Cached("goal_driven", GetContent);
}
