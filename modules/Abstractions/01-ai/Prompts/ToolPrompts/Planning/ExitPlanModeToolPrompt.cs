namespace JoinCode.Abstractions.Prompts.ToolPrompts;

/// <summary>
/// ExitPlanModeTool 提示词
/// </summary>
[ToolPrompt(ToolName = "ExitPlanMode", Category = ToolPromptCategory.Planning)]
public static class ExitPlanModeToolPrompt
{
    public const string ToolName = PlanToolNameConstants.ExitPlanMode;

    /// <summary>
    /// 获取工具提示词
    /// </summary>
    public static string GetPrompt(string askUserQuestionToolName)
    {
        return $@"当你处于计划模式并且已完成将计划写入计划文件并准备等待用户批准时使用此工具。

## 此工具的工作原理
- 你应该已经将计划写入计划模式系统消息中指定的计划文件
- 此工具不接受计划内容作为参数 - 它将从你写入的文件中读取计划
- 此工具只是表示你已完成计划并准备让用户审查和批准
- 用户在审查时将看到你的计划文件内容

## 何时使用此工具
重要：仅当任务需要规划需要编写代码的任务的实施步骤时才使用此工具。对于研究任务，你在收集信息、搜索文件、读取文件或一般试图理解代码库时 - 不要使用此工具。

## 使用此工具之前
确保你的计划完整且明确：
- 如果你对要求或方法有未解决的问题，首先使用 {askUserQuestionToolName}（在早期阶段）
- 一旦你的计划最终确定，使用此工具请求批准

**重要：** 不要使用 {askUserQuestionToolName} 询问""这个计划可以吗？""或""我应该继续吗？"" - 这正是此工具的作用。ExitPlanMode 本质上请求用户批准你的计划。

## 示例

1. 初始任务：""搜索并理解代码库中 vim 模式的实现"" - 不要使用退出计划模式工具，因为你不是在规划任务的实施步骤。
2. 初始任务：""帮我实现 vim 的 yank 模式"" - 在你完成规划任务的实施步骤后使用退出计划模式工具。
3. 初始任务：""添加一个新功能来处理用户认证"" - 如果不确定认证方法（OAuth、JWT 等），首先使用 {askUserQuestionToolName}，然后在澄清方法后使用退出计划模式工具。
";
    }
}
