

namespace McpToolHandlers;

/// <summary>
/// MCP 客户端工具处理器 - 提供与远程 MCP 服务器交互的能力
/// </summary>
[McpToolHandler(ToolCategory.McpClient)]
public partial class McpClientToolHandlers : IAsyncDisposable
{
    private readonly Dictionary<string, IMcpClient> _clients = new();
    [Inject] private readonly ILogger<McpClientToolHandlers>? _logger;
    private readonly SemaphoreSlim _clientLock = new(1, 1);
    private readonly McpClientToolDeps _deps;

    public McpClientToolHandlers(McpClientToolDeps? deps = null, ILogger<McpClientToolHandlers>? logger = null)
    {
        _deps = deps ?? new McpClientToolDeps();
        _logger = logger;
    }

    /// <summary>
    /// 连接到 MCP 服务器
    /// </summary>
    [McpTool(McpToolNameConstants.McpConnect, "Connect to MCP server", "mcp")]
    public async Task<ToolResult> McpConnectAsync(
        [McpToolParameter("Connection name for subsequent reference")] string connection_name,
        [McpToolParameter("Server endpoint (command or URL)")] string endpoint,
        [McpToolParameter("Transport type: stdio, sse, http", Required = false, DefaultValue = McpTransportTypeConstants.Stdio)] string transport_type = McpTransportTypeConstants.Stdio,
        [McpToolParameter("Whether to use OAuth authentication", Required = false, DefaultValue = "false")] bool use_oauth = false,
        [McpToolParameter("Authentication config name (from mcp_auth_*)", Required = false)] string? auth_name = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(connection_name))
        {
            return McpResultBuilder.Error().WithText(L.T(StringKey.ConnectionNameCannotBeEmpty)).Build();
        }

        if (string.IsNullOrWhiteSpace(endpoint))
        {
            return McpResultBuilder.Error().WithText(L.T(StringKey.EndpointCannotBeEmpty)).Build();
        }

        if (_deps.ServerStateManager?.IsDisabled(connection_name) == true)
        {
            return McpResultBuilder.Error().WithText($"MCP 服务器 '{connection_name}' 已被禁用，请先启用后再连接").Build();
        }

        await _clientLock.WaitAsync(cancellationToken);
        try
        {
            if (_clients.ContainsKey(connection_name))
            {
                return McpResultBuilder.Error().WithText(L.T(StringKey.ConnectionAlreadyExists, connection_name)).Build();
            }

            var expandedEndpoint = endpoint.Contains('$') ? McpEnvExpander.ExpandEndpoint(endpoint) : endpoint;

            var config = new McpServerConnectionConfig
            {
                Name = connection_name,
                Endpoint = expandedEndpoint,
                TransportType = ParseTransportType(transport_type)
            };

            // 优先使用 auth_name 查询已配置的认证
            if (!string.IsNullOrWhiteSpace(auth_name) && _deps.AuthToolHandlers != null)
            {
                var authConfig = _deps.AuthToolHandlers.GetAuthConfig(auth_name);
                if (authConfig == null)
                {
                    return McpResultBuilder.Error().WithText(L.T(StringKey.AuthConfigNotFound, auth_name)).Build();
                }
                config = new McpServerConnectionConfig
                {
                    Name = config.Name,
                    Endpoint = config.Endpoint,
                    TransportType = config.TransportType,
                    Auth = authConfig
                };
            }
            else if (use_oauth && _deps.OAuthService != null)
            {
                if (!_deps.OAuthService.IsAuthenticated)
                {
                    var authSuccess = await _deps.OAuthService.StartAuthorizationFlowAsync(cancellationToken).ConfigureAwait(false);
                    if (!authSuccess)
                    {
                        return McpResultBuilder.Error().WithText(L.T(StringKey.OAuthAuthenticationFailed)).Build();
                    }
                }

                var accessToken = await _deps.OAuthService.GetAccessTokenAsync(cancellationToken).ConfigureAwait(false);
                if (string.IsNullOrEmpty(accessToken))
                {
                    return McpResultBuilder.Error().WithText(L.T(StringKey.CannotGetOAuthAccessToken)).Build();
                }

                config = new McpServerConnectionConfig
                {
                    Name = config.Name,
                    Endpoint = config.Endpoint,
                    TransportType = config.TransportType,
                    Auth = new McpAuthConfig
                    {
                        Type = McpAuthType.Bearer,
                        BearerToken = accessToken
                    }
                };
            }

            IMcpClient client = config.TransportType switch
            {
                McpClientTransportType.Stdio => new McpStdioClient(config, logger: _logger),
                McpClientTransportType.Sse => new McpSseClient(config, logger: _logger),
                McpClientTransportType.Http => new McpHttpClient(config, logger: _logger),
                McpClientTransportType.WebSocket => new McpWebSocketClient(config, logger: _logger),
                _ => throw new NotSupportedException(L.T(StringKey.UnsupportedTransportType, transport_type))
            };

            await client.ConnectAsync(cancellationToken);
            _clients[connection_name] = client;

            if (_deps.ElicitationHandler is not null)
            {
                client.SetElicitationHandler(_deps.ElicitationHandler);
            }

            if (_deps.ToolRegistry is not null)
            {
                _deps.ToolRegistry.RegisterRemoteClient(connection_name, client);
            }

            var response = new System.Text.StringBuilder();
            response.AppendLine(L.T(StringKey.ConnectedToMcpServer, connection_name));
            response.AppendLine(L.T(StringKey.LabelServer, client.ServerInfo?.Name ?? "Unknown"));
            response.AppendLine(L.T(StringKey.LabelVersion, client.ServerInfo?.Version ?? "Unknown"));

            if (client.ServerCapabilities?.Tools != null)
            {
                response.AppendLine(L.T(StringKey.SupportsTools));
            }

            if (client.ServerCapabilities?.Resources != null)
            {
                response.AppendLine(L.T(StringKey.SupportsResources));
            }

            if (client.ServerCapabilities?.Prompts != null)
            {
                response.AppendLine(L.T(StringKey.SupportsPrompts));
            }

            return McpResultBuilder.Success().WithText(response.ToString()).Build();
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, L.T(StringKey.ConnectMcpServerFailedLog), connection_name);
            return McpResultBuilder.Error().WithText(L.T(StringKey.ConnectionFailed, ex.Message)).Build();
        }
        finally
        {
            _clientLock.Release();
        }
    }

    /// <summary>
    /// 断开 MCP 服务器连接
    /// </summary>
    [McpTool(McpToolNameConstants.McpDisconnect, "Disconnect from MCP server", "mcp")]
    public async Task<ToolResult> McpDisconnectAsync(
        [McpToolParameter("Connection name")] string connection_name,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(connection_name))
        {
            return McpResultBuilder.Error().WithText(L.T(StringKey.ConnectionNameCannotBeEmpty)).Build();
        }

        await _clientLock.WaitAsync(cancellationToken);
        try
        {
            if (!_clients.TryGetValue(connection_name, out var client))
            {
                return McpResultBuilder.Error().WithText(L.T(StringKey.ConnectionNotFound, connection_name)).Build();
            }

            await client.DisconnectAsync(cancellationToken);
            _clients.Remove(connection_name);

            if (_deps.ToolRegistry is not null)
            {
                await _deps.ToolRegistry.UnregisterRemoteClientAsync(connection_name, cancellationToken).ConfigureAwait(false);
            }

            return McpResultBuilder.Success()
                .WithText(L.T(StringKey.Disconnected, connection_name))
                .Build();
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, L.T(StringKey.DisconnectFailedLog), connection_name);
            return McpResultBuilder.Error().WithText(L.T(StringKey.DisconnectFailed, ex.Message)).Build();
        }
        finally
        {
            _clientLock.Release();
        }
    }

    [McpTool("mcp_disable_server", "Disable an MCP server (persisted to disk)", "mcp")]
    public async Task<ToolResult> McpDisableServerAsync(
        [McpToolParameter("Connection name")] string connection_name,
        CancellationToken cancellationToken = default)
    {
        if (_deps.ServerStateManager == null)
        {
            return McpResultBuilder.Error().WithText("MCP 服务器状态管理器未配置").Build();
        }

        var disabled = await _deps.ServerStateManager.DisableAsync(connection_name, cancellationToken).ConfigureAwait(false);

        await _clientLock.WaitAsync(cancellationToken);
        try
        {
            if (_clients.TryGetValue(connection_name, out var client))
            {
                await client.DisconnectAsync(cancellationToken).ConfigureAwait(false);
                _clients.Remove(connection_name);

                if (_deps.ToolRegistry is not null)
                {
                    await _deps.ToolRegistry.UnregisterRemoteClientAsync(connection_name, cancellationToken).ConfigureAwait(false);
                }
            }
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "禁用 MCP 服务器 {ServerName} 时断开连接失败", connection_name);
        }
        finally
        {
            _clientLock.Release();
        }

        return disabled
            ? McpResultBuilder.Success().WithText($"MCP 服务器 '{connection_name}' 已禁用并断开连接").Build()
            : McpResultBuilder.Success().WithText($"MCP 服务器 '{connection_name}' 已处于禁用状态").Build();
    }

    [McpTool("mcp_enable_server", "Enable an MCP server (persisted to disk)", "mcp")]
    public async Task<ToolResult> McpEnableServerAsync(
        [McpToolParameter("Connection name")] string connection_name,
        CancellationToken cancellationToken = default)
    {
        if (_deps.ServerStateManager == null)
        {
            return McpResultBuilder.Error().WithText("MCP 服务器状态管理器未配置").Build();
        }

        var enabled = await _deps.ServerStateManager.EnableAsync(connection_name, cancellationToken).ConfigureAwait(false);

        return enabled
            ? McpResultBuilder.Success().WithText($"MCP 服务器 '{connection_name}' 已启用，可使用 mcp_connect 重新连接").Build()
            : McpResultBuilder.Success().WithText($"MCP 服务器 '{connection_name}' 已处于启用状态").Build();
    }

    /// <summary>
    /// 列出 MCP 服务器上的工具
    /// </summary>
    [McpTool(McpToolNameConstants.McpListTools, "List tools on MCP server", "mcp")]
    public async Task<ToolResult> McpListToolsAsync(
        [McpToolParameter("Connection name")] string connection_name,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(connection_name))
        {
            return McpResultBuilder.Error().WithText(L.T(StringKey.ConnectionNameCannotBeEmpty)).Build();
        }

        var client = await GetClientAsync(connection_name, cancellationToken);
        if (client == null)
        {
            return McpResultBuilder.Error().WithText(L.T(StringKey.ConnectionNotFound, connection_name)).Build();
        }

        try
        {
            var result = await client.ListToolsAsync(cancellationToken);

            if (!result.Success)
            {
                return McpResultBuilder.Error().WithText(L.T(StringKey.ListToolsFailed, result.ErrorMessage)).Build();
            }

            var response = new System.Text.StringBuilder();
            response.AppendLine(L.T(StringKey.McpServerToolsList, result.Tools.Count));
            response.AppendLine();

            for (int i = 0; i < result.Tools.Count; i++)
            {
                var tool = result.Tools[i];
                response.AppendLine($"{i + 1}. {tool.Name}");
                if (!string.IsNullOrEmpty(tool.Description))
                {
                    response.AppendLine($"   {L.T(StringKey.LabelDescription, tool.Description)}");
                }
                response.AppendLine();
            }

            return McpResultBuilder.Success().WithText(response.ToString()).Build();
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, L.T(StringKey.ListToolsFailedLog));
            return McpResultBuilder.Error().WithText(L.T(StringKey.ListToolsFailedEx, ex.Message)).Build();
        }
    }

    /// <summary>
    /// 调用 MCP 服务器上的工具
    /// </summary>
    [McpTool(McpToolNameConstants.McpCallTool, "Call a tool on MCP server", "mcp")]
    public async Task<ToolResult> McpCallToolAsync(
        [McpToolParameter("Connection name")] string connection_name,
        [McpToolParameter("Tool name")] string tool_name,
        [McpToolParameter("Tool arguments (JSON object)", Required = false)] string? arguments_json = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(connection_name))
        {
            return McpResultBuilder.Error().WithText(L.T(StringKey.ConnectionNameCannotBeEmpty)).Build();
        }

        if (string.IsNullOrWhiteSpace(tool_name))
        {
            return McpResultBuilder.Error().WithText(L.T(StringKey.ToolNameCannotBeEmpty)).Build();
        }

        var client = await GetClientAsync(connection_name, cancellationToken);
        if (client == null)
        {
            return McpResultBuilder.Error().WithText(L.T(StringKey.ConnectionNotFound, connection_name)).Build();
        }

        try
        {
            Dictionary<string, JsonElement>? arguments = null;
            if (!string.IsNullOrEmpty(arguments_json))
            {
                arguments = JsonSerializer.Deserialize(arguments_json, McpToolHandlersJsonContext.Default.DictionaryStringJsonElement);
            }

            var result = await client.CallToolAsync(tool_name, arguments, cancellationToken);

            // 对齐 TS convertResultContentToContentBlocks — 分发 text/image/binary
            var builder = result.IsError ? McpResultBuilder.Error() : McpResultBuilder.Success();

            if (result.IsError)
            {
                builder.WithText(L.T(StringKey.ToolCallError));
            }
            else
            {
                builder.WithText(L.T(StringKey.ToolCallSuccess));
            }

            foreach (var content in result.Content)
            {
                // 图片类型 — base64 内联 + 降采样（对齐 TS image 路径）
                if (content.Type == ToolContentType.Image && !string.IsNullOrEmpty(content.Data) && !string.IsNullOrEmpty(content.MimeType))
                {
                    var imageData = await MaybeResizeImageAsync(content.Data, content.MimeType).ConfigureAwait(false);
                    builder.WithImage(imageData.base64, imageData.mediaType);
                }
                // 二进制资源类型 — 写盘持久化（对齐 TS resource blob 路径）
                else if (content.Type == ToolContentType.Resource && !string.IsNullOrEmpty(content.Data) && !string.IsNullOrEmpty(content.MimeType))
                {
                    var binaryText = await PersistBlobToTextBlockAsync(content.Data, content.MimeType, connection_name).ConfigureAwait(false);
                    builder.WithText(binaryText);
                }
                // 文本类型
                else if (!string.IsNullOrEmpty(content.Text))
                {
                    builder.WithText(content.Text);
                }
            }

            return builder.Build();
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, L.T(StringKey.CallToolFailedLog), tool_name);
            return McpResultBuilder.Error().WithText(L.T(StringKey.CallToolFailed, ex.Message)).Build();
        }
    }

    /// <summary>
    /// 列出 MCP 服务器上的资源
    /// </summary>
    [McpTool(McpToolNameConstants.McpListResources, "List resources on MCP server", "mcp")]
    public async Task<ToolResult> McpListResourcesAsync(
        [McpToolParameter("Connection name")] string connection_name,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(connection_name))
        {
            return McpResultBuilder.Error().WithText(L.T(StringKey.ConnectionNameCannotBeEmpty)).Build();
        }

        var client = await GetClientAsync(connection_name, cancellationToken);
        if (client == null)
        {
            return McpResultBuilder.Error().WithText(L.T(StringKey.ConnectionNotFound, connection_name)).Build();
        }

        try
        {
            var result = await client.ListResourcesAsync(cancellationToken);

            if (!result.Success)
            {
                return McpResultBuilder.Error().WithText(L.T(StringKey.ListResourcesFailed, result.ErrorMessage)).Build();
            }

            var response = new System.Text.StringBuilder();
            response.AppendLine(L.T(StringKey.McpServerResourcesList, result.Resources.Count));
            response.AppendLine();

            for (int i = 0; i < result.Resources.Count; i++)
            {
                var resource = result.Resources[i];
                response.AppendLine($"{i + 1}. {resource.Name}");
                response.AppendLine($"   {L.T(StringKey.LabelUri, resource.Uri)}");
                if (!string.IsNullOrEmpty(resource.Description))
                {
                    response.AppendLine($"   {L.T(StringKey.LabelDescription, resource.Description)}");
                }
                if (!string.IsNullOrEmpty(resource.MimeType))
                {
                    response.AppendLine($"   {L.T(StringKey.LabelMimeType, resource.MimeType)}");
                }
                response.AppendLine();
            }

            return McpResultBuilder.Success().WithText(response.ToString()).Build();
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, L.T(StringKey.ListResourcesFailedLog));
            return McpResultBuilder.Error().WithText(L.T(StringKey.ListResourcesFailedEx, ex.Message)).Build();
        }
    }

    /// <summary>
    /// 读取 MCP 服务器上的资源
    /// </summary>
    [McpTool(McpToolNameConstants.McpReadResource, "Read a resource from MCP server", "mcp")]
    public async Task<ToolResult> McpReadResourceAsync(
        [McpToolParameter("Connection name")] string connection_name,
        [McpToolParameter("Resource URI")] string resource_uri,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(connection_name))
        {
            return McpResultBuilder.Error().WithText(L.T(StringKey.ConnectionNameCannotBeEmpty)).Build();
        }

        if (string.IsNullOrWhiteSpace(resource_uri))
        {
            return McpResultBuilder.Error().WithText(L.T(StringKey.ResourceUriCannotBeEmpty)).Build();
        }

        var client = await GetClientAsync(connection_name, cancellationToken);
        if (client == null)
        {
            return McpResultBuilder.Error().WithText(L.T(StringKey.ConnectionNotFound, connection_name)).Build();
        }

        try
        {
            var result = await client.ReadResourceAsync(resource_uri, cancellationToken);

            if (!result.Success)
            {
                return McpResultBuilder.Error().WithText(L.T(StringKey.ReadResourceFailed, result.ErrorMessage)).Build();
            }

            if (result.Content == null)
            {
                return McpResultBuilder.Error().WithText(L.T(StringKey.ResourceContentEmpty)).Build();
            }

            var response = new System.Text.StringBuilder();
            response.AppendLine(L.T(StringKey.LabelResource, result.Content.Uri));
            if (!string.IsNullOrEmpty(result.Content.MimeType))
            {
                response.AppendLine(L.T(StringKey.LabelMimeType, result.Content.MimeType));
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
                // 对齐 TS persistBlobToTextBlock — 解码 base64 + 写盘持久化
                var mimeType = result.Content.MimeType;
                if (McpBinaryHelper.IsImageMimeType(mimeType))
                {
                    // 图片走 base64 内联路径 + 降采样
                    var builder = McpResultBuilder.Success();
                    builder.WithText(response.ToString());
                    var imageData = await MaybeResizeImageAsync(result.Content.Blob!, mimeType ?? "image/png").ConfigureAwait(false);
                    builder.WithImage(imageData.base64, imageData.mediaType);
                    return builder.Build();
                }
                else
                {
                    var binaryText = await PersistBlobToTextBlockAsync(result.Content.Blob!, mimeType, connection_name).ConfigureAwait(false);
                    response.AppendLine(binaryText);
                }
            }

            return McpResultBuilder.Success().WithText(response.ToString()).Build();
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, L.T(StringKey.ReadResourceFailedLog), resource_uri);
            return McpResultBuilder.Error().WithText(L.T(StringKey.ReadResourceFailedEx, ex.Message)).Build();
        }
    }

    /// <summary>
    /// 列出 MCP 服务器上的提示模板
    /// </summary>
    [McpTool(McpToolNameConstants.McpListPrompts, "List prompt templates on MCP server", "mcp")]
    public async Task<ToolResult> McpListPromptsAsync(
        [McpToolParameter("Connection name")] string connection_name,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(connection_name))
        {
            return McpResultBuilder.Error().WithText(L.T(StringKey.ConnectionNameCannotBeEmpty)).Build();
        }

        var client = await GetClientAsync(connection_name, cancellationToken);
        if (client == null)
        {
            return McpResultBuilder.Error().WithText(L.T(StringKey.ConnectionNotFound, connection_name)).Build();
        }

        try
        {
            var result = await client.ListPromptsAsync(cancellationToken);

            if (!result.Success)
            {
                return McpResultBuilder.Error().WithText(L.T(StringKey.ListPromptsFailed, result.ErrorMessage)).Build();
            }

            var response = new System.Text.StringBuilder();
            response.AppendLine(L.T(StringKey.McpServerPromptsList, result.Prompts.Count));
            response.AppendLine();

            for (int i = 0; i < result.Prompts.Count; i++)
            {
                var prompt = result.Prompts[i];
                response.AppendLine($"{i + 1}. {prompt.Name}");
                if (!string.IsNullOrEmpty(prompt.Description))
                {
                    response.AppendLine($"   {L.T(StringKey.LabelDescription, prompt.Description)}");
                }
                if (prompt.Arguments?.Count > 0)
                {
                    response.AppendLine($"   {L.T(StringKey.LabelArguments, string.Join(", ", prompt.Arguments.Select(a => a.Name)))}");
                }
                response.AppendLine();
            }

            return McpResultBuilder.Success().WithText(response.ToString()).Build();
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, L.T(StringKey.ListPromptsFailedLog));
            return McpResultBuilder.Error().WithText(L.T(StringKey.ListPromptsFailedEx, ex.Message)).Build();
        }
    }

    private async Task<IMcpClient?> GetClientAsync(string connectionName, CancellationToken cancellationToken)
    {
        await _clientLock.WaitAsync(cancellationToken);
        try
        {
            _clients.TryGetValue(connectionName, out var client);
            return client;
        }
        finally
        {
            _clientLock.Release();
        }
    }

    private static McpClientTransportType ParseTransportType(string transportType)
    {
        return transportType.ToLowerInvariant() switch
        {
            McpTransportTypeConstants.Stdio => McpClientTransportType.Stdio,
            McpTransportTypeConstants.Sse => McpClientTransportType.Sse,
            McpTransportTypeConstants.Http => McpClientTransportType.Http,
            McpTransportTypeConstants.WebSocket => McpClientTransportType.WebSocket,
            _ => throw new ArgumentException(L.T(StringKey.UnsupportedTransportType, transportType))
        };
    }

    public async ValueTask DisposeAsync()
    {
        var tasks = _clients.Values.Select(client => client.DisposeAsync().AsTask());
        await Task.WhenAll(tasks).ConfigureAwait(false);
        _clients.Clear();
        _clientLock.Dispose();
    }

    /// <summary>
    /// 将 base64 编码的二进制内容持久化到磁盘 — 对齐 TS persistBlobToTextBlock
    /// 图片走 base64 内联路径，非图片二进制走写盘路径
    /// </summary>
    private async Task<string> PersistBlobToTextBlockAsync(string base64Data, string? mimeType, string serverName)
    {
        try
        {
            var bytes = Convert.FromBase64String(base64Data);
            var sourceDescription = $"[MCP:{serverName}] ";

            if (_deps.OutputStorage is not null)
            {
                var persistId = McpBinaryHelper.GeneratePersistId(serverName);
                var result = _deps.OutputStorage.PersistBinaryContent(bytes, mimeType, persistId);
                if (result is not null)
                {
                    return McpBinaryHelper.GetBinaryBlobSavedMessage(result.Filepath, mimeType, result.Size, sourceDescription);
                }
            }

            // 回退：无持久化服务时输出 base64 长度信息
            return $"{sourceDescription}Binary content ({mimeType ?? "unknown type"}, {bytes.Length} bytes) - persistence service not available";
        }
        catch (FormatException ex)
        {
            _logger?.LogWarning(ex, "Failed to decode base64 binary content from MCP server {Server}", serverName);
            return $"[Binary content from {serverName}] Failed to decode: {ex.Message}";
        }
    }

    /// <summary>
    /// 对图片进行降采样 — 对齐 TS maybeResizeAndDownsampleImageBuffer
    /// 当图片超过大小限制时自动压缩/缩放
    /// </summary>
    private async Task<(string base64, string mediaType)> MaybeResizeImageAsync(string base64Data, string mimeType)
    {
        if (_deps.ImageResizer is null)
        {
            return (base64Data, mimeType);
        }

        try
        {
            var bytes = Convert.FromBase64String(base64Data);
            var extension = GetExtensionFromMimeType(mimeType);

            var result = await _deps.ImageResizer.ResizeAsync(bytes, bytes.Length, extension).ConfigureAwait(false);

            var resizedBase64 = Convert.ToBase64String(result.Buffer);
            return (resizedBase64, result.MediaType);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to resize MCP image, using original");
            return (base64Data, mimeType);
        }
    }

    private static string GetExtensionFromMimeType(string mimeType)
    {
        return mimeType switch
        {
            "image/png" => "png",
            "image/jpeg" or "image/jpg" => "jpg",
            "image/gif" => "gif",
            "image/webp" => "webp",
            "image/svg+xml" => "svg",
            _ => "png"
        };
    }
}
