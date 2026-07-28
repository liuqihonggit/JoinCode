namespace JoinCode.Abstractions.Prompts.ToolPrompts;

/// <summary>
/// 文件读取工具提示词
/// </summary>
[ToolPrompt(ToolName = "FileRead", Category = ToolPromptCategory.File)]
public static class FileReadToolPrompt
{
    public const int MaxLinesToRead = 2000;

    public static string GetDescription() => $"""
        从本地文件系统读取文件。您可以直接访问任何文件。

        使用方法：
        - file_path参数必须是绝对路径，不是相对路径
        - 默认从文件开头读取最多{MaxLinesToRead}行
        - 可以使用offset和limit参数读取文件的特定部分（对于大文件很有用）
        - 结果使用cat -n格式返回，行号从1开始

        重要提示：
        - 如果文件不存在，读取会返回错误
        - 此工具可以读取图像文件（如PNG、JPG等），以视觉方式呈现
        - 此工具可以读取Jupyter notebooks（.ipynb文件），返回所有单元格及其输出
        - 此工具只能读取文件，不能读取目录。读取目录使用Bash工具的ls命令
        - 您可能需要定期阅读截图。如果用户提供截图路径，始终使用此工具查看
        - 如果文件存在但为空，您将收到系统提醒警告
        """;
}
