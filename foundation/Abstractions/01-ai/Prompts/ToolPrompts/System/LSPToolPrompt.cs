namespace JoinCode.Abstractions.Prompts.ToolPrompts;

/// <summary>
/// LSP工具提示词
/// </summary>
[ToolPrompt(ToolName = "LSP", Category = ToolPromptCategory.System)]
public static class LSPToolPrompt
{
    public static string GetDescription() => """
        与语言服务器协议（LSP）服务器交互以获取代码智能功能。

        支持的操作：
        - goToDefinition：查找符号的定义位置
        - findReferences：查找符号的所有引用
        - hover：获取符号的悬停信息（文档、类型信息）
        - documentSymbol：获取文档中的所有符号（函数、类、变量）
        - workspaceSymbol：在整个工作区中搜索符号
        - goToImplementation：查找接口或抽象方法的实现
        - prepareCallHierarchy：获取位置的调用层次结构项（函数/方法）
        - incomingCalls：查找调用该位置函数的所有函数/方法
        - outgoingCalls：查找该位置函数调用的所有函数/方法

        所有操作需要：
        - filePath：要操作的文件
        - line：行号（从1开始，如编辑器中所示）
        - character：字符偏移量（从1开始，如编辑器中所示）

        注意：必须为文件类型配置LSP服务器。如果服务器不可用，将返回错误。
        """;
}
