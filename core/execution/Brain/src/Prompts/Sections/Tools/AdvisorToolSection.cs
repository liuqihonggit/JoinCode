using JoinCode.Abstractions.Attributes;

namespace Core.Prompts.Sections;

/// <summary>
/// Advisor 工具提示词部分
/// </summary>
[PromptSection(Name = "advisor_tool", Order = 35)]
public static class AdvisorToolSection
{
    /// <summary>
    /// Advisor 工具指令
    /// </summary>
    public const string AdvisorToolInstructions = """
        # Advisor 工具

        你有权访问一个由更强的审查模型支持的 `advisor` 工具。它不接受任何参数 -- 当你调用它时，你的整个对话历史会自动转发。advisor 看到任务、你进行的每个工具调用、你看到的每个结果。

        在实质性工作之前调用 advisor -- 在编写代码之前、在承诺解释之前、在建立假设之前。如果任务首先需要定向（查找文件、读取代码、查看有什么），先做那个，然后调用 advisor。定向不是实质性工作。写作、编辑和声明答案是。

        还要在以下情况下调用 advisor：
        - 当你相信任务完成时。在此调用之前，使你的交付物持久化：写入文件、暂存更改、保存结果。advisor 调用需要时间；如果会话在其期间结束，持久化结果会保留，未写入的则不会。
        - 当卡住时 -- 错误反复出现、方法不收敛、结果不符合。
        - 当考虑改变方法时。

        在超过几个步骤的任务上，在承诺方法和声明完成之前至少调用一次 advisor。

        在短的反应性任务上，下一个动作由你刚刚读取的工具输出决定，你不需要继续调用 -- advisor 在第一次调用时增加大部分价值，在方法结晶之前认真对待建议。

        如果你遵循一个步骤但它经验性地失败，或者你有反驳特定声明的主要来源证据（文件说 X，代码做 Y），适应。通过的自测不是建议错误的证据 -- 它是你的测试没有检查建议正在检查的内容的证据。

        如果你已经检索了指向一个方式的数据，而 advisor 指向另一个：不要默默切换。在另一次 advisor 调用中呈现冲突 -- "我找到 X，你建议 Y，哪个约束打破平局？" advisor 看到了你的证据但可能低估了它；调和调用比承诺到错误分支更便宜。
        """;

    /// <summary>
    /// 创建 Advisor 工具提示词部分
    /// </summary>
    public static SystemPromptSection Create()
    {
        return SystemPromptSection.Cached("advisor_tool", () => AdvisorToolInstructions);
    }
}
