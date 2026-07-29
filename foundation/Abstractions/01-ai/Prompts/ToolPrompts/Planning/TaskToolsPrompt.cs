namespace JoinCode.Abstractions.Prompts.ToolPrompts;

/// <summary>
/// TaskCreateTool 提示词
/// </summary>
[ToolPrompt(ToolName = "TaskCreate", Category = ToolPromptCategory.Planning)]
public static class TaskCreateToolPrompt {
    public const string ToolName = "TaskCreate";
    public const string Description = "在任务列表中创建新任务";

    /// <summary>
    /// 获取工具提示词
    /// </summary>
    public static string GetPrompt(bool agentSwarmsEnabled = false) {
        var teammateContext = agentSwarmsEnabled ? "并可能分配给团队成员" : "";

        var teammateTips = agentSwarmsEnabled
            ? @"- 在描述中包含足够的细节，以便另一个代理理解和完成任务
- 新任务以状态 'pending' 创建，没有所有者 - 使用 TaskUpdate 并设置 `owner` 参数来分配它们
"
            : "";

        return $@"
使用此工具为当前编码会话创建结构化任务列表。这有助于你跟踪进度、组织复杂任务，并向用户展示彻底性。
它还有助于用户理解任务进度和整体请求进度。

## 何时使用此工具

在以下场景中主动使用此工具：

- 复杂多步骤任务 - 当任务需要 3 个或更多不同的步骤或操作时
- 非平凡和复杂任务 - 需要仔细规划或多个操作的任务{teammateContext}
- 计划模式 - 使用计划模式时，创建任务列表以跟踪工作
- 用户明确要求待办列表 - 当用户直接要求你使用待办列表时
- 用户提供多个任务 - 当用户提供要做的事情列表（编号或逗号分隔）时
- 收到新指令后 - 立即将用户需求捕获为任务
- 当你开始处理任务时 - 在开始工作之前将其标记为 in_progress
- 完成任务后 - 将其标记为 completed 并添加在实现过程中发现的任何新后续任务

## 何时不使用此工具

在以下情况下跳过使用此工具：
- 只有一个简单直接的任务
- 任务微不足道，跟踪它没有组织上的好处
- 任务可以在少于 3 个微不足道的步骤中完成
- 任务纯粹是对话性或信息性的

注意，如果只有一个微不足道的任务要做，你不应该使用此工具。在这种情况下，你最好直接完成任务。

## 任务字段

- **subject**: 一个简短的、可操作的命令式标题（例如，""修复登录流程中的认证错误""）
- **description**: 需要做什么
- **activeForm** (可选): 任务处于 in_progress 状态时旋转器中显示的现在进行时形式（例如，""修复认证错误""）。如果省略，旋转器显示 subject。

所有任务都以状态 `pending` 创建。

## 提示

- 创建具有清晰、具体主题的任务，描述结果
- 创建任务后，如果需要，使用 TaskUpdate 设置依赖关系（blocks/blockedBy）
{teammateTips}- 首先检查 TaskList 以避免创建重复任务
";
    }
}

/// <summary>
/// TaskGetTool 提示词
/// </summary>
[ToolPrompt(ToolName = "TaskGet", Category = ToolPromptCategory.Planning)]
public static class TaskGetToolPrompt {
    public const string ToolName = TaskToolNameConstants.TaskGet;
    public const string Description = "通过 ID 从任务列表获取任务";

    public const string Prompt = @"
        使用此工具通过其 ID 从任务列表检索任务。

        ## 何时使用此工具

        - 当你需要在开始处理任务之前获取完整描述和上下文时
        - 了解任务依赖关系（它阻塞什么，什么阻塞它）
        - 被分配任务后，获取完整要求

        ## 输出

        返回完整的任务详情：
        - **subject**: 任务标题
        - **description**: 详细要求和上下文
        - **status**: 'pending'、'in_progress' 或 'completed'
        - **blocks**: 等待此任务完成的任务
        - **blockedBy**: 必须在此任务可以开始之前完成的任务

        ## 提示

        - 获取任务后，在开始工作之前验证其 blockedBy 列表为空。
        - 使用 TaskList 以摘要形式查看所有任务。
        ";
}

/// <summary>
/// TaskListTool 提示词
/// </summary>
[ToolPrompt(ToolName = "TaskList", Category = ToolPromptCategory.Planning)]
public static class TaskListToolPrompt {
    public const string ToolName = TaskToolNameConstants.TaskList;
    public const string Description = "列出任务列表中的所有任务";

    /// <summary>
    /// 获取工具提示词
    /// </summary>
    public static string GetPrompt(bool agentSwarmsEnabled = false) {
        var teammateUseCase = agentSwarmsEnabled
            ? "- 在将任务分配给团队成员之前，查看有什么可用的\n"
            : "";

        var teammateWorkflow = agentSwarmsEnabled
            ? $@"


## 团队成员工作流

作为团队成员工作时：
1. 完成当前任务后，调用 TaskList 查找可用工作
2. 查找状态为 'pending'、没有所有者且 blockedBy 为空的任务
3. **优先按 ID 顺序处理任务**（ID 最小的优先），因为较早的任务通常为较晚的任务设置上下文
4. 使用 TaskUpdate 声明可用任务（将 `owner` 设置为你的名字），或等待负责人分配
5. 如果被阻塞，专注于解除阻塞任务或通知团队负责人
"
            : "";

        return $@"使用此工具列出任务列表中的所有任务。

## 何时使用此工具

- 查看有什么任务可以处理（状态：'pending'，没有所有者，没有被阻塞）
- 检查项目的整体进度
- 查找被阻塞且需要解决依赖关系的任务
{teammateUseCase}- 完成任务后，检查新解除阻塞的工作或声明下一个可用任务
- **优先按 ID 顺序处理任务**（ID 最小的优先），因为较早的任务通常为较晚的任务设置上下文

## 输出

返回每个任务的摘要：
- **id**: 任务标识符（与 TaskGet、TaskUpdate 一起使用）
- **subject**: 任务的简短描述
- **status**: 'pending'、'in_progress' 或 'completed'
- **owner**: 如果已分配则为代理 ID，如果可用则为空
- **blockedBy**: 必须首先解决的开放任务 ID 列表（有 blockedBy 的任务在依赖关系解决之前不能被声明）

使用 TaskGet 并指定特定任务 ID 以查看完整详情，包括描述和评论。{teammateWorkflow}
";
    }
}

/// <summary>
/// TaskUpdateTool 提示词
/// </summary>
[ToolPrompt(ToolName = "TaskUpdate", Category = ToolPromptCategory.Planning)]
public static class TaskUpdateToolPrompt {
    public const string ToolName = TaskToolNameConstants.TaskUpdate;
    public const string Description = "更新任务列表中的任务";

    public const string Prompt = @"
        使用此工具更新任务列表中的任务。

        ## 何时使用此工具

        **将任务标记为已解决：**
        - 当你已完成任务中描述的工作时
        - 当任务不再需要或已被取代时
        - 重要：完成分配给你的任务时，始终将其标记为已解决
        - 解决后，调用 TaskList 查找你的下一个任务

        - 仅当你完全完成任务时才将任务标记为 completed
        - 如果你遇到错误、阻塞或无法完成，保持任务为 in_progress
        - 当被阻塞时，创建一个新任务描述需要解决什么
        - 永远不要将任务标记为 completed 如果：
          - 测试失败
          - 实现不完整
          - 你遇到未解决的错误
          - 你找不到必要的文件或依赖项

        **删除任务：**
        - 当任务不再相关或创建错误时
        - 将状态设置为 `deleted` 会永久删除任务

        **更新任务详情：**
        - 当要求改变或变得更清晰时
        - 在任务之间建立依赖关系时

        ## 你可以更新的字段

        - **status**: 任务状态（见下面的状态工作流）
        - **subject**: 更改任务标题（命令式，例如，""运行测试""）
        - **description**: 更改任务描述
        - **activeForm**: 任务处于 in_progress 状态时旋转器中显示的现在进行时形式（例如，""正在运行测试""）
        - **owner**: 更改任务所有者（代理名称）
        - **metadata**: 将元数据键合并到任务中（将键设置为 null 以删除它）
        - **addBlocks**: 标记必须等待此任务完成的任务
        - **addBlockedBy**: 标记必须在此任务可以开始之前完成的任务

        ## 状态工作流

        状态进度：`pending` → `in_progress` → `completed`

        使用 `deleted` 永久删除任务。

        ## 陈旧性

        确保在更新之前使用 `TaskGet` 读取任务的最新状态。

        ## 示例

        开始工作时将任务标记为进行中：
        ```json
        {""taskId"": ""1"", ""status"": ""in_progress""}
        ```

        完成工作后将任务标记为已完成：
        ```json
        {""taskId"": ""1"", ""status"": ""completed""}
        ```

        删除任务：
        ```json
        {""taskId"": ""1"", ""status"": ""deleted""}
        ```

        通过设置所有者声明任务：
        ```json
        {""taskId"": ""1"", ""owner"": ""my-name""}
        ```

        设置任务依赖关系：
        ```json
        {""taskId"": ""2"", ""addBlockedBy"": [""1""]}
        ```
        ";
}

/// <summary>
/// TaskStopTool 提示词
/// </summary>
[ToolPrompt(ToolName = "TaskStop", Category = ToolPromptCategory.Planning)]
public static class TaskStopToolPrompt {
    public const string ToolName = TaskToolNameConstants.TaskStop;

    public const string Description = @"
        - 通过其 ID 停止正在运行的后台任务
        - 接受一个 task_id 参数标识要停止的任务
        - 返回成功或失败状态
        - 当你需要终止长时间运行的任务时使用此工具
        ";
}
