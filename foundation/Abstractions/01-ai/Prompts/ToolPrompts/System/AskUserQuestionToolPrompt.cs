namespace JoinCode.Abstractions.Prompts.ToolPrompts;

/// <summary>
/// AskUserQuestionTool 提示词
/// </summary>
[ToolPrompt(ToolName = "AskUserQuestion", Category = ToolPromptCategory.System)]
public static class AskUserQuestionToolPrompt
{
    public const string ToolName = InteractionToolNameConstants.AskUserQuestion;

    public const string Description = "向用户询问多项选择问题以收集信息、澄清歧义、了解偏好、做出决策或向他们提供选择。";

    /// <summary>
    /// 预览功能提示词（Markdown 版本）
    /// </summary>
    public const string PreviewFeaturePromptMarkdown = @"
        预览功能：
        当呈现用户需要直观比较的具体工件时，在选项上使用可选的 `preview` 字段：
        - UI 布局或组件的 ASCII 模型
        - 显示不同实现的代码片段
        - 图表变体
        - 配置示例

        预览内容在等宽框中渲染为 markdown。支持带换行符的多行文本。当任何选项有预览时，UI 切换到并排布局，左侧为垂直选项列表，右侧为预览。不要对简单的偏好问题使用预览，其中标签和描述就足够了。注意：预览仅支持单选问题（不支持 multiSelect）。
        ";

    /// <summary>
    /// 预览功能提示词（HTML 版本）
    /// </summary>
    public const string PreviewFeaturePromptHtml = @"
        预览功能：
        当呈现用户需要直观比较的具体工件时，在选项上使用可选的 `preview` 字段：
        - UI 布局或组件的 HTML 模型
        - 显示不同实现的格式化代码片段
        - 视觉比较或图表

        预览内容必须是自包含的 HTML 片段（没有 <html>/<body> 包装器，没有 <script> 或 <style> 标签 —— 改用内联 style 属性）。不要对简单的偏好问题使用预览，其中标签和描述就足够了。注意：预览仅支持单选问题（不支持 multiSelect）。
        ";

    /// <summary>
    /// 获取工具提示词
    /// </summary>
    public static string GetPrompt(string exitPlanModeToolName)
    {
        return $@"当你需要在执行期间向用户提问时使用此工具。这允许你：
1. 收集用户偏好或要求
2. 澄清模糊的指令
3. 在你工作时获得实现选择的决策
4. 向用户提供关于采取什么方向的选择。

用法说明：
- 用户将始终能够选择""Other""以提供自定义文本输入
- 使用 multiSelect: true 允许为一个问题选择多个答案
- 如果你推荐特定选项，将其作为列表中的第一个选项，并在标签末尾添加""(Recommended)""

计划模式说明：在计划模式下，使用此工具在最终确定计划之前澄清要求或选择方法。不要使用此工具询问""我的计划准备好了吗？""或""我应该继续吗？"" —— 使用 {exitPlanModeToolName} 进行计划批准。重要：不要在问题中引用""计划""（例如，""你对计划有反馈吗？""、""计划看起来好吗？""），因为用户在调用 {exitPlanModeToolName} 之前无法在 UI 中看到计划。如果你需要计划批准，使用 {exitPlanModeToolName}。
";
    }
}
