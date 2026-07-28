namespace JoinCode.Abstractions.Prompts.ToolPrompts;

/// <summary>
/// ListMcpResourcesTool 提示词
/// </summary>
[ToolPrompt(ToolName = "ListMcpResourcesTool", Category = ToolPromptCategory.System)]
public static class ListMcpResourcesToolPrompt
{
    public const string ToolName = McpToolNameConstants.McpListResources;

    public const string Description = """
        列出配置好的 MCP 服务器中可用的资源。
        每个资源对象包含一个 'server' 字段，指示它来自哪个服务器。

        使用示例：
        - 列出所有服务器中的所有资源：`listMcpResources`
        - 列出特定服务器中的资源：`listMcpResources({ server: "myserver" })`
        """;

    public const string Prompt = """
        列出配置好的 MCP 服务器中可用的资源。
        每个返回的资源将包含所有标准 MCP 资源字段以及一个 'server' 字段，
        指示该资源属于哪个服务器。

        参数：
        - server（可选）：要获取资源的特定 MCP 服务器的名称。如果未提供，
          将返回所有服务器的资源。
        """;
}

/// <summary>
/// ReadMcpResourceTool 提示词
/// </summary>
[ToolPrompt(ToolName = "ReadMcpResourceTool", Category = ToolPromptCategory.System)]
public static class ReadMcpResourceToolPrompt
{
    public const string ToolName = McpToolNameConstants.McpReadResource;

    public const string Description = """
        从 MCP 服务器读取特定资源。
        - server：要读取的 MCP 服务器的名称
        - uri：要读取的资源的 URI

        使用示例：
        - 从服务器读取资源：`readMcpResource({ server: "myserver", uri: "my-resource-uri" })`
        """;

    public const string Prompt = """
        从 MCP 服务器读取特定资源，通过服务器名称和资源 URI 标识。

        参数：
        - server（必需）：要从中读取资源的 MCP 服务器的名称
        - uri（必需）：要读取的资源的 URI
        """;
}

/// <summary>
/// RemoteTriggerTool 提示词
/// </summary>
[ToolPrompt(ToolName = "RemoteTrigger", Category = ToolPromptCategory.System)]
public static class RemoteTriggerToolPrompt
{
    public const string ToolName = McpToolNameConstants.McpRemoteTrigger;

    public const string Description = """
        通过 claude.ai CCR API 管理计划的远程 Claude Code 代理（触发器）。
        身份验证在进程中处理 —— token 永远不会到达 shell。
        """;

    public const string Prompt = """
        调用 claude.ai 远程触发 API。使用此工具代替 curl —— OAuth token 在进程中自动添加，永远不会暴露。

        操作：
        - list: GET /v1/code/triggers
        - get: GET /v1/code/triggers/{trigger_id}
        - create: POST /v1/code/triggers（需要 body）
        - update: POST /v1/code/triggers/{trigger_id}（需要 body，部分更新）
        - run: POST /v1/code/triggers/{trigger_id}/run

        响应是来自 API 的原始 JSON。
        """;
}

/// <summary>
/// NotebookEditTool 提示词
/// </summary>
[ToolPrompt(ToolName = "NotebookEdit", Category = ToolPromptCategory.System)]
public static class NotebookEditToolPrompt
{
    public const string ToolName = NotebookToolNameConstants.NotebookEdit;

    public const string Description = "替换 Jupyter notebook 中特定单元格的内容。";

    public const string Prompt = """
        完全替换 Jupyter notebook（.ipynb 文件）中特定单元格的内容为新源代码。
        Jupyter notebooks 是交互式文档，结合了代码、文本和可视化，通常用于数据分析和科学计算。
        notebook_path 参数必须是绝对路径，不是相对路径。cell_number 是 0 索引的。
        使用 edit_mode=insert 在 cell_number 指定的索引处添加新单元格。
        使用 edit_mode=delete 删除 cell_number 指定的索引处的单元格。
        """;
}
