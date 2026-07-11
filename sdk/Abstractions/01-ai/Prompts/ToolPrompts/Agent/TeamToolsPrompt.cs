namespace JoinCode.Abstractions.Prompts.ToolPrompts;

[ToolPrompt(ToolName = "TeamCreate", Category = ToolPromptCategory.Agent)]
public static class TeamCreateToolPrompt
{
    public const string ToolName = TeamToolNameConstants.TeamCreate;

    public static string Prompt { get; } = $$"""
        # TeamCreate

        ## 何时使用

        在以下情况下主动使用此工具：
        - 用户明确要求使用团队、群体或代理组
        - 用户提到希望代理一起工作、协调或协作
        - 任务足够复杂，可以从多个代理的并行工作中受益（例如，构建具有前端和后端工作的全栈功能，在保持测试通过的同时重构代码库，实现具有研究、规划和编码阶段的多步骤项目）

        当不确定任务是否需要团队时，优先生成团队。

        ## 为队友选择代理类型

        当通过 Agent 工具生成队友时，根据代理完成任务所需的工具选择 `subagent_type`。每种代理类型都有不同的可用工具集 —— 将代理与工作匹配：

        - **只读代理**（例如，Explore、Plan）不能编辑或写入文件。只分配给他们研究、搜索或规划任务。永远不要分配给他们实施工作。
        - **全功能代理**（例如，通用）可以访问所有工具，包括文件编辑、写入和 bash。将这些用于需要进行更改的任务。
        - **自定义代理** 在 `~/{{AppDataConstants.AppDataFolder}}/{{AppDataConstants.AgentsFolderName}}/` 中定义可能有自己的工具限制。检查他们的描述以了解他们能做什么和不能做什么。

        在为队友选择 `subagent_type` 之前，始终查看 Agent 工具提示中列出的代理类型描述及其可用工具。

        创建一个新团队来协调多个代理在一个项目上的工作。团队与任务列表有 1:1 的对应关系（Team = TaskList）。

        ```
        {
          "team_name": "my-project",
          "description": "Working on feature X"
        }
        ```

        这会创建：
        - 团队文件位于 `~/{{AppDataConstants.AppDataFolder}}/{{AppDataConstants.TeamsFolderName}}/{team-name}/config.json`
        - 相应的任务列表目录位于 `~/{{AppDataConstants.AppDataFolder}}/{{AppDataConstants.TasksFolderName}}/{team-name}/`

        ## 团队工作流

        1. **使用 TeamCreate 创建团队** - 这会同时创建团队及其任务列表
        2. **使用 Task 工具创建任务**（TaskCreate、TaskList 等）- 它们自动使用团队的任务列表
        3. **使用 Agent 工具生成队友**，带上 `team_name` 和 `name` 参数来创建加入团队的队友
        4. **使用 TaskUpdate 并设置 `owner` 分配任务** 给空闲的队友
        5. **队友处理分配的任务** 并通过 TaskUpdate 标记完成
        6. **队友在回合之间进入空闲状态** - 每次回合后，队友自动进入空闲状态并发送通知。重要：对空闲队友要有耐心！不要评论他们的空闲状态，直到它实际影响你的工作。
        7. **关闭你的团队** - 当任务完成时，通过 SendMessage 并设置 `message: {type: "shutdown_request"}` 优雅地关闭你的队友。

        ## 任务所有权

        任务使用 TaskUpdate 并设置 `owner` 参数进行分配。任何代理都可以通过 TaskUpdate 设置或更改任务所有权。

        ## 自动消息传递

        **重要**：来自队友的消息自动传递给你。你不需要手动检查收件箱。

        当你生成队友时：
        - 当他们完成任务或需要帮助时，他们会向你发送消息
        - 这些消息作为新的对话回合自动出现（像用户消息一样）
        - 如果你很忙（回合中），消息会排队并在你的回合结束时传递
        - UI 在消息等待时显示带有发送者姓名的简短通知

        消息将自动传递。

        在报告队友消息时，你不需要引用原始消息 —— 它已经渲染给用户了。

        ## 队友空闲状态

        队友在每次回合后进入空闲状态 —— 这是完全正常和预期的。队友在向你发送消息后进入空闲状态并不意味着他们完成或不可用。空闲只是意味着他们在等待输入。

        - **空闲队友可以接收消息。** 向空闲队友发送消息会唤醒他们，他们会正常处理它。
        - **空闲通知是自动的。** 每当队友的回合结束时，系统会发送空闲通知。除非你希望分配新工作或发送后续消息，否则你不需要对空闲通知做出反应。
        - **不要将空闲视为错误。** 队友发送消息然后进入空闲状态是正常的流程 —— 他们发送了消息，现在正在等待响应。
        - **Peer DM 可见性。** 当一个队友向另一个队友发送 DM 时，他们的空闲通知中包含一个简短的摘要。这让你了解同行协作，而无需完整的消息内容。你不需要响应这些摘要 —— 它们是信息性的。

        ## 发现团队成员

        队友可以读取团队配置文件以发现其他团队成员：
        - **团队配置位置**：`~/{{AppDataConstants.AppDataFolder}}/{{AppDataConstants.TeamsFolderName}}/{team-name}/config.json`

        配置文件包含每个队友的 `members` 数组：
        - `name`：人类可读的名称（**始终使用这个** 进行消息传递和任务分配）
        - `agentId`：唯一标识符（仅供参考 - 不要用于通信）
        - `agentType`：代理的角色/类型

        **重要**：始终通过他们的 NAME（例如，"team-lead"、"researcher"、"tester"）引用队友。名称用于：
        - 发送消息时的 `to`
        - 识别任务所有者

        读取团队配置的示例：
        ```
        使用 Read 工具读取 ~/{{AppDataConstants.AppDataFolder}}/{{AppDataConstants.TeamsFolderName}}/{team-name}/config.json
        ```

        ## 任务列表协调

        团队在 `~/{{AppDataConstants.AppDataFolder}}/{{AppDataConstants.TasksFolderName}}/{team-name}/` 共享一个所有队友都可以访问的任务列表。

        队友应该：
        1. 定期检查 TaskList，**特别是在完成每个任务后**，以查找可用工作或查看新解除阻塞的任务
        2. 使用 TaskUpdate 声明未分配、未阻塞的任务（将 `owner` 设置为你的名字）。**优先按 ID 顺序**（ID 最小的优先）处理任务，因为较早的任务通常为较晚的任务设置上下文
        3. 在识别额外工作时使用 `TaskCreate` 创建新任务
        4. 完成后使用 `TaskUpdate` 标记任务为已完成，然后检查 TaskList 查找下一个工作
        5. 通过读取任务列表状态与其他队友协调
        6. 如果所有可用任务都被阻塞，通知团队负责人或帮助解决阻塞任务

        **与团队通信的重要说明**：
        - 不要使用终端工具查看团队的活动；始终向队友发送消息（记住，通过名称引用他们）。
        - 如果你不使用 SendMessage 工具，你的团队听不到你的声音。如果你正在响应他们，始终向队友发送消息。
        - 不要发送结构化 JSON 状态消息，如 `{"type":"idle",...}` 或 `{"type":"task_completed",...}`。当你需要向队友发送消息时，只需用纯文本通信。
        - 使用 TaskUpdate 标记任务完成。
        - 如果你是团队中的代理，当你停止时，系统会自动向团队负责人发送空闲通知。
        """;
}

[ToolPrompt(ToolName = "TeamDelete", Category = ToolPromptCategory.Agent)]
public static class TeamDeleteToolPrompt
{
    public const string ToolName = TeamToolNameConstants.TeamDelete;

    public static string Prompt { get; } = $$"""
        # TeamDelete

        当群体工作完成时移除团队和任务目录。

        此操作：
        - 移除团队目录（`~/{{AppDataConstants.AppDataFolder}}/{{AppDataConstants.TeamsFolderName}}/{team-name}/`）
        - 移除任务目录（`~/{{AppDataConstants.AppDataFolder}}/{{AppDataConstants.TasksFolderName}}/{team-name}/`）
        - 从当前会话中清除团队上下文

        **重要**：如果团队仍有活动成员，TeamDelete 将失败。首先优雅地终止队友，然后在所有队友关闭后调用 TeamDelete。

        当所有队友完成他们的工作并且你想清理团队资源时使用此工具。团队名称自动从当前会话的团队上下文中确定。
        """;
}

[ToolPrompt(ToolName = "ToolSearch", Category = ToolPromptCategory.Agent)]
public static class ToolSearchToolPrompt
{
    public const string ToolName = SystemToolNameConstants.ToolSearch;

    public static string GetPrompt(bool deltaEnabled = false)
    {
        var toolLocationHint = deltaEnabled
            ? "延迟工具按名称出现在 <system-reminder> 消息中。"
            : "延迟工具按名称出现在 <available-deferred-tools> 消息中。";

        return string.Concat(
            "获取延迟工具的完整模式定义，以便可以调用它们。\n\n",
            toolLocationHint,
            " 在获取之前，只有名称是已知的 —— 没有参数模式，因此无法调用该工具。",
            "此工具接受一个查询，与延迟工具列表匹配，并返回匹配工具的完整 JSONSchema 定义在 <functions> 块内。",
            "一旦工具的模式出现在该结果中，它就可以像提示词顶部定义的任何工具一样被调用。\n\n",
            """结果格式：每个匹配的工具在 <functions> 块内显示为一行 <function>{"description": "...", "name": "...", "parameters": {...}}</function> —— 与提示词顶部的工具列表相同的编码。""",
            "\n\n查询形式：\n",
            """- "select:Read,Edit,Grep" —— 按名称获取这些确切的工具\n""",
            """- "notebook jupyter" —— 关键字搜索，最多 max_results 个最佳匹配\n""",
            """- "+slack send" —— 要求名称中包含 "slack"，按剩余术语排序\n"""
        );
    }
}
