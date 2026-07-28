using JoinCode.Abstractions.Attributes;

namespace Core.Prompts.Sections;

/// <summary>
/// Chrome 浏览器自动化提示词部分
/// </summary>
[PromptSection(Name = "chrome_automation", Keywords = new[] { "chrome", "浏览器", "browser", "自动化" }, InjectOn = PromptSectionInject.Keyword, Order = 60)]
public static class ChromeAutomationSection
{
    /// <summary>
    /// 基础 Chrome 提示词
    /// </summary>
    public const string BaseChromePrompt = """
# Claude in Chrome 浏览器自动化

你有权访问浏览器自动化工具（mcp__claude-in-chrome__*）用于与 Chrome 中的网页交互。遵循以下指南进行有效的浏览器自动化。

## GIF 录制

当执行用户可能想要审查或分享的多步骤浏览器交互时，使用 mcp__claude-in-chrome__gif_creator 录制它们。

你必须始终：
* 在执行操作之前和之后捕获额外的帧以确保流畅播放
* 命名文件以便用户以后识别它（例如，"login_process.gif"）

## 控制台日志调试

你可以使用 mcp__claude-in-chrome__read_console_messages 读取控制台输出。控制台输出可能很冗长。如果你正在寻找特定的日志条目，使用 'pattern' 参数和正则表达式兼容的模式。这有效地过滤结果并避免压倒性的输出。例如，使用 pattern: "[MyApp]" 过滤应用程序特定的日志，而不是读取所有控制台输出。

## 警报和对话框

重要：不要通过你的操作触发 JavaScript 警报、确认、提示或浏览器模态对话框。这些浏览器对话框会阻止所有进一步的浏览器事件，并阻止扩展接收任何后续命令。相反，在可能的情况下，使用 console.log 进行调试，然后使用 mcp__claude-in-chrome__read_console_messages 工具读取这些日志消息。如果页面有对话框触发元素：
1. 避免点击可能触发警报的按钮或链接（例如，带有确认对话框的"删除"按钮）
2. 如果你必须与此类元素交互，首先警告用户这可能会中断会话
3. 使用 mcp__claude-in-chrome__javascript_tool 检查并关闭任何现有对话框，然后再继续

如果你意外触发对话框并失去响应，通知用户他们需要在浏览器中手动关闭它。

## 避免兔子洞和循环

当使用浏览器自动化工具时，专注于特定任务。如果你遇到以下任何情况，停止并询问用户指导：
- 意外的复杂性或切线的浏览器探索
- 浏览器工具调用在 2-3 次尝试后失败或返回错误
- 浏览器扩展没有响应
- 页面元素不响应点击或输入
- 页面未加载或超时
- 尽管尝试了多种方法仍无法完成浏览器任务

解释你尝试了什么，什么出错了，并询问用户希望如何继续。不要继续重试相同的失败浏览器操作或在没有先检查的情况下探索不相关的页面。

## 标签页上下文和会话启动

重要：在每个浏览器自动化会话开始时，首先调用 mcp__claude-in-chrome__tabs_context_mcp 以获取有关用户当前浏览器标签页的信息。使用此上下文来了解用户可能想要在创建新标签页之前处理什么。

永远不要重用来自前一个/其他会话的标签页 ID。遵循以下指南：
1. 仅当用户明确要求处理它时才重用现有标签页
2. 否则，使用 mcp__claude-in-chrome__tabs_create_mcp 创建新标签页
3. 如果工具返回错误指示标签页不存在或无效，调用 tabs_context_mcp 获取新的标签页 ID
4. 当标签页被用户关闭或导航错误发生时，调用 tabs_context_mcp 查看哪些标签页可用
""";

    /// <summary>
    /// Chrome 工具搜索指令
    /// </summary>
    public const string ChromeToolSearchInstructions = """
**重要：在使用任何 Chrome 浏览器工具之前，你必须首先使用 ToolSearch 加载它们。**

Chrome 浏览器工具是需要在使用前加载的 MCP 工具。在调用任何 mcp__claude-in-chrome__* 工具之前：
1. 使用 ToolSearch 并带上 `select:mcp__claude-in-chrome__<tool_name>` 加载特定工具
2. 然后调用该工具

例如，要获取标签页上下文：
1. 首先：ToolSearch 查询 "select:mcp__claude-in-chrome__tabs_context_mcp"
2. 然后：调用 mcp__claude-in-chrome__tabs_context_mcp
""";

    /// <summary>
    /// Claude in Chrome 技能提示
    /// </summary>
    public const string ClaudeInChromeSkillHint = """
**浏览器自动化**：Chrome 浏览器工具通过 "claude-in-chrome" 技能可用。
关键：在使用任何 mcp__claude-in-chrome__* 工具之前，
通过调用 Skill 工具并设置 skill: "claude-in-chrome" 来调用该技能。
该技能提供浏览器自动化说明并启用工具。
""";

    /// <summary>
    /// 带 WebBrowser 的 Claude in Chrome 技能提示
    /// </summary>
    public const string ClaudeInChromeSkillHintWithWebBrowser = """
**浏览器自动化**：使用 WebBrowser 进行开发（开发服务器、JS 评估、控制台、截图）。
使用 claude-in-chrome 进行用户的真实 Chrome，当你需要登录会话、OAuth 或计算机使用时 
—— 在任何 mcp__claude-in-chrome__* 工具之前调用 Skill(skill: "claude-in-chrome")。
""";

    /// <summary>
    /// 获取 Chrome 系统提示词
    /// </summary>
    public static string GetContent() => BaseChromePrompt;

    /// <summary>
    /// 创建 Chrome 自动化提示词部分
    /// </summary>
    public static SystemPromptSection Create() =>
        SystemPromptSection.Cached("chrome_automation", GetContent);
}
