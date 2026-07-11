namespace JoinCode.Abstractions.Prompts.ToolPrompts;

/// <summary>
/// FileWriteTool 提示词
/// </summary>
[ToolPrompt(ToolName = "Write", Category = ToolPromptCategory.File)]
public static class FileWriteToolPrompt {
    public const string ToolName = FileToolNameConstants.FileWrite;
    public const string Description = "将文件写入本地文件系统。";

    /// <summary>
    /// 获取写入工具描述
    /// </summary>
    public static string GetWriteToolDescription(string fileReadToolName = FileToolNameConstants.FileRead) {
        var preReadInstruction = GetPreReadInstruction(fileReadToolName);
        return $@"将文件写入本地文件系统。

用法：
- 此工具将覆盖现有文件（如果提供的路径上有一个）。{preReadInstruction}
- 优先使用 Edit 工具修改现有文件 - 它只发送差异。仅使用此工具创建新文件或完全重写。
- 永远不要创建文档文件 (*.md) 或 README 文件，除非用户明确要求。
- 仅当用户明确要求时使用表情符号。避免在文件中写入表情符号，除非被要求。
";
    }

    private static string GetPreReadInstruction(string fileReadToolName) {
        return $"\n- 如果这是现有文件，你必须首先使用 {fileReadToolName} 工具读取文件内容。如果你未先读取文件，此工具将失败。";
    }
}
