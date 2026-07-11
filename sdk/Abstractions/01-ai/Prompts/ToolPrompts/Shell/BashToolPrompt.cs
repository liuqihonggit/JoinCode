namespace JoinCode.Abstractions.Prompts.ToolPrompts;

/// <summary>
/// Bash工具提示词
/// </summary>
[ToolPrompt(ToolName = "Bash", Category = ToolPromptCategory.Shell)]
public static class BashToolPrompt
{
    public static string GetDescription() => """
        通过shell执行命令。

        使用方法：
        - command参数是要执行的shell命令（必需）
        - cwd参数是命令执行的工作目录（可选，默认为当前目录）
        - timeout参数是命令超时时间，以毫秒为单位（可选）
        - wait_ms_before_async参数用于异步命令，设置异步前的等待时间（可选）

        重要提示：
        - 使用此工具执行需要shell执行的系统命令和终端操作
        - 避免使用此工具执行可以通过专用工具完成的任务（如读取文件、搜索文件）
        - 如果您不确定并且有相关的专用工具，默认使用专用工具
        - 对于需要交互式输入的命令（如vim、nano），使用此工具
        - 对于长时间运行的命令（如npm install、git clone），设置适当的超时时间
        """;
}
