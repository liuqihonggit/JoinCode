


namespace McpToolHandlers;

[McpToolHandler(ToolCategory.McpResource)]
public class McpResourceToolHandlers
{
    private readonly IMcpToolRegistry _toolRegistry;

    public McpResourceToolHandlers(IMcpToolRegistry toolRegistry)
    {
        _toolRegistry = toolRegistry ?? throw new ArgumentNullException(nameof(toolRegistry));
    }

    /// <summary>
    /// 列出所有MCP资源
    /// </summary>
    [McpTool(McpToolNameConstants.McpRemoteListResources, "List all available MCP remote resources", "mcp")]
    public async Task<ToolResult> McpRemoteListResourcesAsync(
        [McpToolParameter("Remote client ID (optional, list resources for a specific client)", Required = false)] string? client_id = null,
        CancellationToken cancellationToken = default)
    {
        var response = new System.Text.StringBuilder();
        response.AppendLine($"{ObjectSymbol.DiamondFilled.ToValue()} MCP Resources");
        response.AppendLine();

        // 获取所有远程客户端
        var clients = await _toolRegistry.GetAllRemoteClientsAsync(cancellationToken);

        if (clients.Count == 0)
        {
            response.AppendLine("No connected MCP remote clients");
            return McpResultBuilder.Success().WithText(response.ToString()).Build();
        }

        foreach (var (clientId, client) in clients)
        {
            if (!string.IsNullOrEmpty(client_id) && clientId != client_id)
            {
                continue;
            }

            response.AppendLine($"{ObjectSymbol.ArrowRight.ToValue()} Client: {clientId}");

            try
            {
                var result = await client.ListResourcesAsync(cancellationToken);

                if (!result.Success)
                {
                    response.AppendLine($"   {StatusSymbol.Cross.ToValue()} Failed to list resources: {result.ErrorMessage}");
                }
                else if (result.Resources.Count == 0)
                {
                    response.AppendLine("   No resources available");
                }
                else
                {
                    foreach (var resource in result.Resources)
                    {
                        response.AppendLine($"   {ObjectSymbol.File.ToValue()} {resource.Name}");
                        response.AppendLine($"      URI: {resource.Uri}");

                        if (!string.IsNullOrEmpty(resource.Description))
                        {
                            response.AppendLine($"      Description: {resource.Description}");
                        }

                        if (!string.IsNullOrEmpty(resource.MimeType))
                        {
                            response.AppendLine($"      MIME Type: {resource.MimeType}");
                        }

                        response.AppendLine();
                    }
                }
            }
            catch (Exception ex)
            {
                response.AppendLine($"   {StatusSymbol.Cross.ToValue()} Failed to list resources: {ex.Message}");
            }

            response.AppendLine();
        }

        return McpResultBuilder.Success().WithText(response.ToString()).Build();
    }

    /// <summary>
    /// 读取MCP资源
    /// </summary>
    [McpTool(McpToolNameConstants.McpRemoteReadResource, "Read the content of an MCP remote resource by URI", "mcp")]
    public async Task<ToolResult> McpRemoteReadResourceAsync(
        [McpToolParameter("Resource URI")] string uri,
        [McpToolParameter("Remote client ID (optional)", Required = false)] string? client_id = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(uri))
        {
            return McpResultBuilder.Error().WithText("uri cannot be empty").Build();
        }

        // 获取所有远程客户端
        var clients = await _toolRegistry.GetAllRemoteClientsAsync(cancellationToken);

        if (clients.Count == 0)
        {
            return McpResultBuilder.Error().WithText("No connected MCP remote clients").Build();
        }

        // 如果指定了客户端ID，优先使用该客户端
        if (!string.IsNullOrEmpty(client_id))
        {
            if (!clients.TryGetValue(client_id, out var specificClient))
            {
                return McpResultBuilder.Error().WithText($"Client not found: {client_id}").Build();
            }

            return await ReadResourceFromClientAsync(specificClient, uri, client_id, cancellationToken);
        }

        // 尝试所有客户端
        foreach (var (clientId, client) in clients)
        {
            var result = await ReadResourceFromClientAsync(client, uri, clientId, cancellationToken);

            // 如果成功，返回结果
            if (result.IsError != true)
            {
                return result;
            }
        }

        return McpResultBuilder.Error().WithText($"Failed to read resource from any client: {uri}").Build();
    }

    /// <summary>
    /// 列出MCP提示模板
    /// </summary>
    [McpTool(McpToolNameConstants.McpRemoteListPrompts, "List all available MCP remote prompt templates", "mcp")]
    public async Task<ToolResult> McpRemoteListPromptsAsync(
        [McpToolParameter("Remote client ID (optional)", Required = false)] string? client_id = null,
        CancellationToken cancellationToken = default)
    {
        var response = new System.Text.StringBuilder();
        response.AppendLine($"{ObjectSymbol.Pencil.ToValue()} MCP Prompt Templates");
        response.AppendLine();

        var clients = await _toolRegistry.GetAllRemoteClientsAsync(cancellationToken);

        if (clients.Count == 0)
        {
            response.AppendLine("No connected MCP remote clients");
            return McpResultBuilder.Success().WithText(response.ToString()).Build();
        }

        foreach (var (clientId, client) in clients)
        {
            if (!string.IsNullOrEmpty(client_id) && clientId != client_id)
            {
                continue;
            }

            response.AppendLine($"{ObjectSymbol.ArrowRight.ToValue()} Client: {clientId}");

            try
            {
                // 尝试获取提示模板列表
                var result = await client.ListPromptsAsync(cancellationToken);

                if (!result.Success)
                {
                    response.AppendLine($"   {StatusSymbol.Cross.ToValue()} Failed to list prompts: {result.ErrorMessage}");
                }
                else if (result.Prompts.Count == 0)
                {
                    response.AppendLine("   No prompt templates available");
                }
                else
                {
                    foreach (var prompt in result.Prompts)
                    {
                        response.AppendLine($"   {ObjectSymbol.Pencil.ToValue()} {prompt.Name}");

                        if (!string.IsNullOrEmpty(prompt.Description))
                        {
                            response.AppendLine($"      Description: {prompt.Description}");
                        }

                        if (prompt.Arguments != null && prompt.Arguments.Count > 0)
                        {
                            response.AppendLine($"      Arguments:");
                            response.Append(string.Join(Environment.NewLine, prompt.Arguments.Select(arg =>
                            {
                                var required = arg.Required ? "(required)" : "(optional)";
                                return $"        - {arg.Name} {required}";
                            })));
                            response.AppendLine();
                        }

                        response.AppendLine();
                    }
                }
            }
            catch (Exception ex)
            {
                response.AppendLine($"   {StatusSymbol.Cross.ToValue()} Failed to list prompts: {ex.Message}");
            }

            response.AppendLine();
        }

        return McpResultBuilder.Success().WithText(response.ToString()).Build();
    }

    /// <summary>
    /// 获取MCP提示模板
    /// </summary>
    [McpTool(McpToolNameConstants.McpGetPrompt, "Get an MCP prompt template by name", "mcp")]
    public async Task<ToolResult> McpGetPromptAsync(
        [McpToolParameter("Prompt template name")] string prompt_name,
        [McpToolParameter("Arguments (JSON format)", Required = false)] string? arguments = null,
        [McpToolParameter("Remote client ID (optional)", Required = false)] string? client_id = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(prompt_name))
        {
            return McpResultBuilder.Error().WithText("prompt_name cannot be empty").Build();
        }

        // 获取所有远程客户端
        var clients = await _toolRegistry.GetAllRemoteClientsAsync(cancellationToken);

        if (clients.Count == 0)
        {
            return McpResultBuilder.Error().WithText("No connected MCP remote clients").Build();
        }

        // 解析参数
        Dictionary<string, JsonElement>? args = null;
        if (!string.IsNullOrEmpty(arguments))
        {
            try
            {
                args = JsonSerializer.Deserialize(arguments, McpToolHandlersJsonContext.Default.DictionaryStringJsonElement);
            }
            catch
            {
                return McpResultBuilder.Error().WithText("arguments must be valid JSON format").Build();
            }
        }

        // 如果指定了客户端ID，优先使用该客户端
        if (!string.IsNullOrEmpty(client_id))
        {
            if (!clients.TryGetValue(client_id, out var specificClient))
            {
                return McpResultBuilder.Error().WithText($"Client not found: {client_id}").Build();
            }

            return await GetPromptFromClientAsync(specificClient, prompt_name, args, client_id, cancellationToken);
        }

        // 尝试所有客户端
        foreach (var (clientId, client) in clients)
        {
            var result = await GetPromptFromClientAsync(client, prompt_name, args, clientId, cancellationToken);

            // 如果成功，返回结果
            if (result.IsError != true)
            {
                return result;
            }
        }

        return McpResultBuilder.Error().WithText($"Failed to get prompt from any client: {prompt_name}").Build();
    }

    /// <summary>
    /// 列出所有已连接的MCP客户端
    /// </summary>
    [McpTool(McpToolNameConstants.McpListClients, "List all connected MCP remote clients", "mcp")]
    public async Task<ToolResult> McpListClientsAsync(
        CancellationToken cancellationToken = default)
    {
        var clients = await _toolRegistry.GetAllRemoteClientsAsync(cancellationToken);

        var response = new System.Text.StringBuilder();
        response.AppendLine($"{ObjectSymbol.ArrowRight.ToValue()} MCP Remote Clients");
        response.AppendLine();

        if (clients.Count == 0)
        {
            response.AppendLine("No connected MCP remote clients");
        }
        else
        {
            response.AppendLine($"Total {clients.Count} client(s):");
            response.AppendLine();

            foreach (var (clientId, client) in clients)
            {
                response.AppendLine($"• {clientId}");
                response.AppendLine($"  Type: {client.GetType().Name}");
                response.AppendLine($"  Connected: {client.IsConnected}");

                if (client.ServerInfo != null)
                {
                    response.AppendLine($"  Server: {client.ServerInfo.Name} {client.ServerInfo.Version}");
                }

                response.AppendLine();
            }
        }

        return McpResultBuilder.Success().WithText(response.ToString()).Build();
    }

    #region Private Methods

    private async Task<ToolResult> ReadResourceFromClientAsync(
        IMcpClient client,
        string uri,
        string clientId,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await client.ReadResourceAsync(uri, cancellationToken);

            if (!result.Success)
            {
                return McpResultBuilder.Error().WithText($"Failed to read resource: {result.ErrorMessage}").Build();
            }

            if (result.Content == null)
            {
                return McpResultBuilder.Error().WithText($"Resource content is empty: {uri}").Build();
            }

            var response = new System.Text.StringBuilder();
            response.AppendLine($"{ObjectSymbol.File.ToValue()} Resource content: {uri}");
            response.AppendLine($"Source client: {clientId}");

            if (!string.IsNullOrEmpty(result.Content.MimeType))
            {
                response.AppendLine($"MIME Type: {result.Content.MimeType}");
            }

            response.AppendLine();
            response.AppendLine("---");
            response.AppendLine();

            if (!string.IsNullOrEmpty(result.Content.Text))
            {
                response.AppendLine(result.Content.Text);
            }
            else if (!string.IsNullOrEmpty(result.Content.Blob))
            {
                response.AppendLine($"[Binary data: {result.Content.Blob.Length} characters]");
            }

            response.AppendLine();

            return McpResultBuilder.Success().WithText(response.ToString()).Build();
        }
        catch (Exception ex)
        {
            return McpResultBuilder.Error().WithText($"Failed to read resource: {ex.Message}").Build();
        }
    }

    private async Task<ToolResult> GetPromptFromClientAsync(
        IMcpClient client,
        string promptName,
        Dictionary<string, JsonElement>? arguments,
        string clientId,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await client.GetPromptAsync(promptName, arguments, cancellationToken);

            if (!result.Success)
            {
                return McpResultBuilder.Error().WithText($"Failed to get prompt: {result.ErrorMessage}").Build();
            }

            if (result.Message == null)
            {
                return McpResultBuilder.Error().WithText($"Prompt content is empty: {promptName}").Build();
            }

            var response = new System.Text.StringBuilder();
            response.AppendLine($"{ObjectSymbol.Pencil.ToValue()} Prompt: {promptName}");
            response.AppendLine($"Source client: {clientId}");

            if (!string.IsNullOrEmpty(result.Message.Description))
            {
                response.AppendLine($"Description: {result.Message.Description}");
            }

            response.AppendLine();
            response.AppendLine("Messages:");
            response.AppendLine();

            foreach (var message in result.Message.Messages)
            {
                var roleIcon = message.Role switch
                {
                    MessageRoleConstants.User => StructureSymbol.Bullet.ToValue(),
                    MessageRoleConstants.Assistant => ObjectSymbol.Agent.ToValue(),
                    MessageRoleConstants.System => ObjectSymbol.Gear.ToValue(),
                    _ => StatusSymbol.Circle.ToValue()
                };

                response.AppendLine($"{roleIcon} [{message.Role}]");

                if (message.Content.Type == "text" && !string.IsNullOrEmpty(message.Content.Text))
                {
                    response.AppendLine(message.Content.Text);
                }
                else
                {
                    response.AppendLine($"[{message.Content.Type} content]");
                }

                response.AppendLine();
            }

            return McpResultBuilder.Success().WithText(response.ToString()).Build();
        }
        catch (Exception ex)
        {
            return McpResultBuilder.Error().WithText($"Failed to get prompt: {ex.Message}").Build();
        }
    }

    #endregion
}
