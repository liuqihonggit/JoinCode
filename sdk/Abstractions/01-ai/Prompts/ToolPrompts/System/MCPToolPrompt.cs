namespace JoinCode.Abstractions.Prompts.ToolPrompts;

/// <summary>
/// MCP工具提示词
/// </summary>
[ToolPrompt(ToolName = "MCP", Category = ToolPromptCategory.System)]
public static class MCPToolPrompt
{
    public static string GetDescription() => """
        与MCP（Model Context Protocol）服务器交互。

        MCP是一种标准化协议，允许AI模型与外部工具和服务进行通信。通过MCP工具，您可以调用配置的各种MCP服务器提供的功能。

        使用方法：
        - 调用MCP服务器提供的工具
        - 访问MCP服务器提供的资源
        - 与MCP服务器进行交互

        具体的提示词和描述由MCP服务器配置动态提供。
        """;
}
