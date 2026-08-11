namespace JoinCode.Abstractions.Prompts.ToolPrompts;

/// <summary>
/// 文件编辑工具提示词
/// </summary>
[ToolPrompt(ToolName = FileToolName.FileEdit, Category = ToolPromptCategory.File)]
public static class FileEditToolPrompt
{
    public static string GetDescription() => """
        对文件执行精确的字符串替换。

        使用方法：
        - file_path：要编辑的文件路径（必须是绝对路径）
        - old_str：要替换的原始字符串（必须是文件中唯一的）
        - new_str：替换后的新字符串
        - 可选参数replace_all：true表示替换所有匹配项，false或不提供表示只替换第一个

        重要提示：
        - 在编辑之前，必须在对话中至少使用过一次Read工具
        - 当编辑Read工具输出中的文本时，确保保留精确的缩进（制表符/空格），取行号前缀之后的内容
        - 行号前缀格式：紧凑模式为行号+制表符，宽模式为空格填充+行号+→。绝不要在old_str或new_str中包含行号前缀的任何部分，前缀之后才是要匹配的实际文件内容
        - 始终优先编辑代码库中的现有文件。除非明确要求，否则不要写新文件
        - 只有在用户明确要求时才使用表情符号
        - 如果old_str在文件中不唯一，编辑将失败。需要提供更多上下文使其唯一
        - 使用replace_all参数可以替换和重命名整个文件中的字符串
        """;
}
