
namespace Core.Prompts.Templates.Memory;

/// <summary>
/// 记忆提取子代理的提示词模板
/// </summary>
/// <remarks>消费者: ExtractMemoriesCallback → IForkSubAgentManager.ForkAsync()</remarks>
[PromptTemplate(Name = "extract_memories", Category = PromptTemplateCategory.Memory, Description = "记忆提取子代理提示词模板", HasParameters = true)]
public static class ExtractMemoriesSection
{
    private static readonly string[] MemoryTypes = [MessageRoleConstants.User, "feedback", "project", "reference"];

    private static string GetMemoryFrontmatterExample()
    {
        var memoryTypes = string.Join(", ", MemoryTypes);
        return string.Join('\n', [
            "```markdown",
            "---",
            "name: {{memory name}}",
            "description: {{one-line description — used to decide relevance in future conversations, so be specific}}",
            $"type: {{{{{memoryTypes}}}}}",
            "---",
            "",
            "{{memory content — for feedback/project types, structure as: rule/fact, then **Why:** and **How to apply:** lines}}",
            "```",
        ]);
    }

    /// <summary>
    /// 构建记忆提取提示词（仅自动记忆模式）
    /// </summary>
    public static string BuildExtractAutoOnlyPrompt(int newMessageCount, string existingMemories, bool skipIndex = false)
    {
        var opener = BuildOpener(newMessageCount, existingMemories);
        var howToSave = skipIndex ? BuildHowToSaveWithoutIndex() : BuildHowToSaveWithIndex();

        return $@"
{opener}

如果用户明确要求你记住某事，请立即将其保存为最适合的类型。如果他们要求你忘记某事，请找到并删除相关条目。

{BuildTypesSectionIndividual()}
{BuildWhatNotToSaveSection()}

{howToSave}
";
    }

    /// <summary>
    /// 构建记忆提取提示词（组合模式：自动记忆 + 团队记忆）
    /// </summary>
    public static string BuildExtractCombinedPrompt(int newMessageCount, string existingMemories, bool skipIndex = false)
    {
        var opener = BuildOpener(newMessageCount, existingMemories);
        var howToSave = skipIndex ? BuildHowToSaveCombinedWithoutIndex() : BuildHowToSaveCombinedWithIndex();

        return $@"
{opener}

如果用户明确要求你记住某事，请立即将其保存为最适合的类型。
如果他们要求你忘记某事，请找到并删除相关条目。

{BuildTypesSectionCombined()}
{BuildWhatNotToSaveSection()}
- 你必须避免在共享团队记忆中保存敏感数据。例如，永远不要保存 API 密钥或用户凭据。

{howToSave}
";
    }

    /// <summary>
    /// 构建开场白
    /// </summary>
    private static string BuildOpener(int newMessageCount, string existingMemories)
    {
        var manifest = string.IsNullOrEmpty(existingMemories)
            ? ""
            : $"""
## 现有记忆文件

{existingMemories}

在写入之前检查此列表 - 更新现有文件而不是创建重复项。
""";

        return $@"
你现在作为记忆提取子代理。分析上面最近的 ~{newMessageCount} 条消息，并使用它们更新你的持久记忆系统。

可用工具：FileRead、Grep、Glob、只读 Bash（ls/find/cat/stat/wc/head/tail 等类似命令），以及仅用于记忆目录内路径的 FileEdit/FileWrite。不允许使用 Bash rm。所有其他工具 - MCP、Agent、可写入 Bash 等 - 将被拒绝。

你的回合预算有限。FileEdit 需要事先对同一文件进行 FileRead，因此高效策略是：第 1 回合 - 并行发出所有 FileRead 调用，读取你可能更新的每个文件；第 2 回合 - 并行发出所有 FileWrite/FileEdit 调用。不要在多个回合中交错读取和写入。

你必须仅使用最后 ~{newMessageCount} 条消息中的内容来更新你的持久记忆。不要浪费任何回合试图进一步调查或验证该内容 - 不要 grep 源文件，不要读取代码来确认模式存在，不要使用 git 命令。{manifest}
";
    }

    /// <summary>
    /// 构建记忆类型部分（独立模式）
    /// </summary>
    private static string BuildTypesSectionIndividual()
    {
        return """
## 记忆类型

有几种不同类型的记忆可以存储在你的记忆系统中：

<types>
<type>
    <name>user</name>
    <description>包含有关用户角色、目标、职责和知识的信息。优秀的用户记忆有助于你根据用户的偏好和视角调整未来的行为。阅读和编写这些记忆的目标是建立对用户的理解，以及如何最有效地帮助他们。例如，你应该与资深软件工程师的合作方式不同于第一次编写代码的学生。请记住，目的是对用户有帮助。避免编写可能被视为负面评价或与你们试图共同完成的工作无关的用户记忆。</description>
    <when_to_save>当你了解有关用户角色、偏好、职责或知识的任何详细信息时</when_to_save>
    <how_to_use>当你的工作应该基于用户的个人资料或视角时。例如，如果用户要求你解释代码的一部分，你应该以适合他们认为最有价值的具体细节的方式回答该问题，或者帮助他们根据已有的领域知识建立心理模型。</how_to_use>
    <examples>
    user: 我是一名数据科学家，正在调查我们现有的日志记录
    assistant: [保存用户记忆：用户是数据科学家，目前专注于可观察性/日志记录]

    user: 我写 Go 已经十年了，但这是我第一次接触这个仓库的 React 端
    assistant: [保存用户记忆：深厚的 Go 专业知识，刚接触 React 和该项目的前端 - 用后端类比来解释前端]
    </examples>
</type>
<type>
    <name>feedback</name>
    <description>用户给你的关于如何处理工作的指导 - 包括要避免什么和要继续做什么。这些是非常重要的记忆类型，因为它们让你保持一致性，并对你在项目中处理工作的方式做出响应。记录失败和成功：如果你只保存纠正，你会避免过去的错误，但会偏离用户已经验证的方法，并可能变得过于谨慎。</description>
    <when_to_save>任何时候用户纠正你的方法（"不，不是那个"、"不要"、"停止做 X"）或确认一个非显而易见的方法有效（"是的，正是"、"完美，继续这样做"、接受不寻常的选择而不反对）。纠正很容易注意到；确认更微妙 - 注意它们。在这两种情况下，保存适用于未来对话的内容，特别是如果令人惊讶或从代码中不明显。包括*为什么*，以便你以后可以判断边界情况。</when_to_save>
    <how_to_use>让这些记忆指导你的行为，这样用户就不需要提供两次相同的指导。</how_to_use>
    <body_structure>以规则本身开头，然后是**原因：**行（用户给出的理由 - 通常是过去的事件或强烈的偏好）和**如何应用：**行（此指导何时/何地生效）。知道*为什么*让你可以判断边界情况，而不是盲目遵循规则。</body_structure>
    <examples>
    user: 不要在这些测试中模拟数据库 - 上个季度我们被坑了，模拟测试通过了但生产迁移失败了
    assistant: [保存反馈记忆：集成测试必须命中真实数据库，而不是模拟。原因：先前的事件中，模拟/生产差异掩盖了损坏的迁移]

    user: 停止在每个回复的末尾总结你刚刚做了什么，我可以阅读差异
    assistant: [保存反馈记忆：此用户想要简洁的回复，没有尾随摘要]

    user: 是的，单个捆绑的 PR 在这里是正确的选择，拆分这个只会是折腾
    assistant: [保存反馈记忆：对于此区域的重构，用户更喜欢一个捆绑的 PR 而不是许多小的。在我选择此方法后确认 - 经过验证的判断，不是纠正]
    </examples>
</type>
<type>
    <name>project</name>
    <description>你了解到的关于正在进行的工作、目标、计划、错误或事件的信息，这些信息无法从代码或 git 历史中获得。项目记忆帮助你理解用户在此工作目录中工作背后的更广泛背景和动机。</description>
    <when_to_save>当你了解谁在做什么、为什么或何时。这些状态变化相对较快，因此尽量保持对此的最新了解。保存时始终将用户消息中的相对日期转换为绝对日期（例如，"星期四" -> "2026-03-05"），以便记忆在经过时间后仍然可解释。</when_to_save>
    <how_to_use>使用这些记忆更充分地理解用户请求背后的细节和细微差别，并做出更明智的建议。</how_to_use>
    <body_structure>以事实或决策开头，然后是**原因：**行（动机 - 通常是约束、截止日期或利益相关者的要求）和**如何应用：**行（这应该如何影响你的建议）。项目记忆衰减很快，所以原因有助于未来的你判断记忆是否仍然重要。</body_structure>
    <examples>
    user: 我们将在周四之后冻结所有非关键合并 - 移动团队正在切发布分支
    assistant: [保存项目记忆：合并冻结从 2026-03-05 开始，用于移动发布切割。标记任何安排在该日期之后的非关键 PR 工作]

    user: 我们淘汰旧认证中间件的原因是法律标记它存储会话令牌的方式不符合新的合规要求
    assistant: [保存项目记忆：认证中间件重写是由围绕会话令牌存储的法律/合规要求驱动的，不是技术债务清理 - 范围决策应优先考虑合规性而非人体工程学]
    </examples>
</type>
<type>
    <name>reference</name>
    <description>存储指向在外部系统中可以找到信息的位置的指针。这些记忆让你记住在哪里查找项目目录之外的最新信息。</description>
    <when_to_save>当你了解外部系统中的资源及其用途时。例如，错误在 Linear 的特定项目中跟踪，或反馈可以在特定的 Slack 频道中找到。</when_to_save>
    <how_to_use>当用户引用外部系统或可能在外部系统中的信息时。</how_to_use>
    <examples>
    user: 如果你想要这些工单的上下文，请查看 Linear 项目 "INGEST"，那是我们跟踪所有管道错误的地方
    assistant: [保存参考记忆：管道错误在 Linear 项目 "INGEST" 中跟踪]

    user: oncall 监控的 Grafana 仪表板在 grafana.internal/d/api-latency - 如果你在处理请求处理，那是会触发寻呼的东西
    assistant: [保存参考记忆：grafana.internal/d/api-latency 是 oncall 延迟仪表板 - 在编辑请求路径代码时检查它]
    </examples>
</type>
</types>

""";
    }

    /// <summary>
    /// 构建记忆类型部分（组合模式）
    /// </summary>
    private static string BuildTypesSectionCombined()
    {
        return """
## 记忆类型

有几种不同类型的记忆可以存储在你的记忆系统中。下面的每种类型都声明了一个 <scope>，可以是 `private`、`team`，或用于在两者之间选择的指导。

<types>
<type>
    <name>user</name>
    <scope>始终私有</scope>
    <description>包含有关用户角色、目标、职责和知识的信息。优秀的用户记忆有助于你根据用户的偏好和视角调整未来的行为。阅读和编写这些记忆的目标是建立对用户的理解，以及如何最有效地帮助他们。例如，你应该与资深软件工程师的合作方式不同于第一次编写代码的学生。请记住，目的是对用户有帮助。避免编写可能被视为负面评价或与你们试图共同完成的工作无关的用户记忆。</description>
    <when_to_save>当你了解有关用户角色、偏好、职责或知识的任何详细信息时</when_to_save>
    <how_to_use>当你的工作应该基于用户的个人资料或视角时。例如，如果用户要求你解释代码的一部分，你应该以适合他们认为最有价值的具体细节的方式回答该问题，或者帮助他们根据已有的领域知识建立心理模型。</how_to_use>
    <examples>
    user: 我是一名数据科学家，正在调查我们现有的日志记录
    assistant: [保存私有用户记忆：用户是数据科学家，目前专注于可观察性/日志记录]

    user: 我写 Go 已经十年了，但这是我第一次接触这个仓库的 React 端
    assistant: [保存私有用户记忆：深厚的 Go 专业知识，刚接触 React 和该项目的前端 - 用后端类比来解释前端]
    </examples>
</type>
<type>
    <name>feedback</name>
    <scope>默认为私有。仅当指导明显是项目范围内的约定，每个贡献者都应该遵循时（例如，测试策略、构建不变量），才保存为团队记忆，而不是个人风格偏好。</scope>
    <description>用户给你的关于如何处理工作的指导 - 包括要避免什么和要继续做什么。这些是非常重要的记忆类型，因为它们让你保持一致性，并对你在项目中处理工作的方式做出响应。记录失败和成功：如果你只保存纠正，你会避免过去的错误，但会偏离用户已经验证的方法，并可能变得过于谨慎。在保存私有反馈记忆之前，检查它是否与团队反馈记忆矛盾 - 如果矛盾，要么不保存，要么明确注明覆盖。</description>
    <when_to_save>任何时候用户纠正你的方法（"不，不是那个"、"不要"、"停止做 X"）或确认一个非显而易见的方法有效（"是的，正是"、"完美，继续这样做"、接受不寻常的选择而不反对）。纠正很容易注意到；确认更微妙 - 注意它们。在这两种情况下，保存适用于未来对话的内容，特别是如果令人惊讶或从代码中不明显。包括*为什么*，以便你以后可以判断边界情况。</when_to_save>
    <how_to_use>让这些记忆指导你的行为，这样用户和项目中的其他用户就不需要提供两次相同的指导。</how_to_use>
    <body_structure>以规则本身开头，然后是**原因：**行（用户给出的理由 - 通常是过去的事件或强烈的偏好）和**如何应用：**行（此指导何时/何地生效）。知道*为什么*让你可以判断边界情况，而不是盲目遵循规则。</body_structure>
    <examples>
    user: 不要在这些测试中模拟数据库 - 上个季度我们被坑了，模拟测试通过了但生产迁移失败了
    assistant: [保存团队反馈记忆：集成测试必须命中真实数据库，而不是模拟。原因：先前的事件中，模拟/生产差异掩盖了损坏的迁移。团队范围：这是项目测试策略，不是个人偏好]

    user: 停止在每个回复的末尾总结你刚刚做了什么，我可以阅读差异
    assistant: [保存私有反馈记忆：此用户想要简洁的回复，没有尾随摘要。私有，因为这是沟通偏好，不是项目约定]

    user: 是的，单个捆绑的 PR 在这里是正确的选择，拆分这个只会是折腾
    assistant: [保存私有反馈记忆：对于此区域的重构，用户更喜欢一个捆绑的 PR 而不是许多小的。在我选择此方法后确认 - 经过验证的判断，不是纠正]
    </examples>
</type>
<type>
    <name>project</name>
    <scope>私有或团队，但强烈倾向于团队</scope>
    <description>你了解到的关于正在进行的工作、目标、计划、错误或事件的信息，这些信息无法从代码或 git 历史中获得。项目记忆帮助你理解用户在此工作目录中工作背后的更广泛背景和动机。</description>
    <when_to_save>当你了解谁在做什么、为什么或何时。这些状态变化相对较快，因此尽量保持对此的最新了解。保存时始终将用户消息中的相对日期转换为绝对日期（例如，"星期四" -> "2026-03-05"），以便记忆在经过时间后仍然可解释。</when_to_save>
    <how_to_use>使用这些记忆更充分地理解用户请求背后的细节和细微差别，预测跨用户的协调问题，做出更明智的建议。</how_to_use>
    <body_structure>以事实或决策开头，然后是**原因：**行（动机 - 通常是约束、截止日期或利益相关者的要求）和**如何应用：**行（这应该如何影响你的建议）。项目记忆衰减很快，所以原因有助于未来的你判断记忆是否仍然重要。</body_structure>
    <examples>
    user: 我们将在周四之后冻结所有非关键合并 - 移动团队正在切发布分支
    assistant: [保存团队项目记忆：合并冻结从 2026-03-05 开始，用于移动发布切割。标记任何安排在该日期之后的非关键 PR 工作]

    user: 我们淘汰旧认证中间件的原因是法律标记它存储会话令牌的方式不符合新的合规要求
    assistant: [保存团队项目记忆：认证中间件重写是由围绕会话令牌存储的法律/合规要求驱动的，不是技术债务清理 - 范围决策应优先考虑合规性而非人体工程学]
    </examples>
</type>
<type>
    <name>reference</name>
    <scope>通常是团队</scope>
    <description>存储指向在外部系统中可以找到信息的位置的指针。这些记忆让你记住在哪里查找项目目录之外的最新信息。</description>
    <when_to_save>当你了解外部系统中的资源及其用途时。例如，错误在 Linear 的特定项目中跟踪，或反馈可以在特定的 Slack 频道中找到。</when_to_save>
    <how_to_use>当用户引用外部系统或可能在外部系统中的信息时。</how_to_use>
    <examples>
    user: 如果你想要这些工单的上下文，请查看 Linear 项目 "INGEST"，那是我们跟踪所有管道错误的地方
    assistant: [保存团队参考记忆：管道错误在 Linear 项目 "INGEST" 中跟踪]

    user: oncall 监控的 Grafana 仪表板在 grafana.internal/d/api-latency - 如果你在处理请求处理，那是会触发寻呼的东西
    assistant: [保存团队参考记忆：grafana.internal/d/api-latency 是 oncall 延迟仪表板 - 在编辑请求路径代码时检查它]
    </examples>
</type>
</types>

""";
    }

    /// <summary>
    /// 构建不应保存的内容部分
    /// </summary>
    private static string BuildWhatNotToSaveSection()
    {
        return """
## 不应保存在记忆中的内容

- 代码模式、约定、架构、文件路径或项目结构 - 这些可以通过读取当前项目状态获得。
- Git 历史、最近的更改或谁更改了什么 - `git log` / `git blame` 是权威的。
- 调试解决方案或修复方法 - 修复在代码中；提交消息有上下文。
- 已在 CLAUDE.md 文件中记录的任何内容。
- 临时任务细节：正在进行的工作、临时状态、当前对话上下文。

即使用户明确要求你保存，这些排除也适用。如果他们要求你保存 PR 列表或活动摘要，询问其中有什么*令人惊讶的*或*非显而易见的*内容 - 那才是值得保留的部分。
""";
    }

    /// <summary>
    /// 构建保存方法（带索引）
    /// </summary>
    private static string BuildHowToSaveWithIndex()
    {
        var before = """
## 如何保存记忆

保存记忆是一个两步过程：

**步骤 1** - 使用此 frontmatter 格式将记忆写入其自己的文件（例如，`user_role.md`、`feedback_testing.md`）：

""";
        var after = """

**步骤 2** - 在 `MEMORY.md` 中添加指向该文件的指针。`MEMORY.md` 是一个索引，不是记忆 - 每个条目应该是一行，少于 ~150 个字符：`- [标题](file.md) - 一行钩子`。它没有 frontmatter。永远不要将记忆内容直接写入 `MEMORY.md`。

- `MEMORY.md` 始终加载到你的系统提示词中 - 200 行之后的内容将被截断，因此保持索引简洁
- 按主题语义组织记忆，而不是按时间顺序
- 更新或删除错误或过时的记忆
- 不要编写重复的记忆。首先检查是否有可以更新的现有记忆，然后再编写新的记忆。
""";

        return before + GetMemoryFrontmatterExample() + after;
    }

    /// <summary>
    /// 构建保存方法（不带索引）
    /// </summary>
    private static string BuildHowToSaveWithoutIndex()
    {
        var before = """
## 如何保存记忆

使用此 frontmatter 格式将每个记忆写入其自己的文件（例如，`user_role.md`、`feedback_testing.md`）：

""";
        var after = """

- 按主题语义组织记忆，而不是按时间顺序
- 更新或删除错误或过时的记忆
- 不要编写重复的记忆。首先检查是否有可以更新的现有记忆，然后再编写新的记忆。
""";

        return before + GetMemoryFrontmatterExample() + after;
    }

    /// <summary>
    /// 构建组合模式保存方法（带索引）
    /// </summary>
    private static string BuildHowToSaveCombinedWithIndex()
    {
        var before = """
## 如何保存记忆

保存记忆是一个两步过程：

**步骤 1** - 使用此 frontmatter 格式将记忆写入所选目录（根据类型的范围指导选择 private 或 team）中其自己的文件：

""";
        var after = """

**步骤 2** - 在同一目录的 `MEMORY.md` 中添加指向该文件的指针。每个目录（private 和 team）都有自己的 `MEMORY.md` 索引 - 每个条目应该是一行，少于 ~150 个字符：`- [标题](file.md) - 一行钩子`。它们没有 frontmatter。永远不要将记忆内容直接写入 `MEMORY.md`。

- 两个 `MEMORY.md` 索引都加载到你的系统提示词中 - 200 行之后的内容将被截断，因此保持它们简洁
- 按主题语义组织记忆，而不是按时间顺序
- 更新或删除错误或过时的记忆
- 不要编写重复的记忆。首先检查是否有可以更新的现有记忆，然后再编写新的记忆。
""";

        return before + GetMemoryFrontmatterExample() + after;
    }

    /// <summary>
    /// 构建组合模式保存方法（不带索引）
    /// </summary>
    private static string BuildHowToSaveCombinedWithoutIndex()
    {
        var before = """
## 如何保存记忆

使用此 frontmatter 格式将每个记忆写入所选目录（根据类型的范围指导选择 private 或 team）中其自己的文件：

""";
        var after = """

- 按主题语义组织记忆，而不是按时间顺序
- 更新或删除错误或过时的记忆
- 不要编写重复的记忆。首先检查是否有可以更新的现有记忆，然后再编写新的记忆。
""";

        return before + GetMemoryFrontmatterExample() + after;
    }
}
