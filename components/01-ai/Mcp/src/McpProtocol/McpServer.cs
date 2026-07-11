namespace McpProtocol;

public class McpServer : IMcpServer
{
    private readonly Dictionary<string, McpProtocol.IToolHandler> _tools = new(StringComparer.Ordinal);
    private readonly Dictionary<string, IResourceHandler> _resources = new(StringComparer.Ordinal);
    private readonly Dictionary<string, IPromptHandler> _prompts = new(StringComparer.Ordinal);
    private readonly string _serverName;
    private readonly string _serverVersion;
    private readonly string? _instructions;
    private readonly TextReader? _inputReader;
    private readonly TextWriter? _outputWriter;
    private string _logLevel = "info";

    public event EventHandler<McpServerNotificationEventArgs>? NotificationReceived;

    public McpServer(string serverName = "McpServer", string? serverVersion = null, string? instructions = null)
    {
        _serverName = serverName;
        _serverVersion = serverVersion ?? "1.0.0";
        _instructions = instructions;
    }

    internal McpServer(string serverName, string? serverVersion, string? instructions, TextReader inputReader, TextWriter outputWriter)
        : this(serverName, serverVersion, instructions)
    {
        _inputReader = inputReader;
        _outputWriter = outputWriter;
    }

    public void RegisterTool<T>(T toolInstance) where T : class
    {
        ArgumentNullException.ThrowIfNull(toolInstance);

        if (toolInstance is McpProtocol.IToolHandler handler)
        {
            _tools[handler.Name] = handler;
        }
        else
        {
            throw new ArgumentException($"Tool instance must implement {nameof(McpProtocol.IToolHandler)}", nameof(toolInstance));
        }
    }

    public void RegisterToolHandler(McpProtocol.IToolHandler handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        _tools[handler.Name] = handler;
    }

    public void RegisterResourceHandler(IResourceHandler handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        _resources[handler.Uri] = handler;
    }

    public void RegisterPromptHandler(IPromptHandler handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        _prompts[handler.Name] = handler;
    }

    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        var reader = _inputReader ?? new StreamReader(Console.OpenStandardInput());
        var writer = _outputWriter ?? new StreamWriter(Console.OpenStandardOutput()) { AutoFlush = true };

        while (!cancellationToken.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
            if (line == null)
            {
                break;
            }
            if (string.IsNullOrEmpty(line))
            {
                await Task.Delay(100, cancellationToken).ConfigureAwait(false);
                continue;
            }

            var trimmedLine = line.TrimStart();

            if (trimmedLine.StartsWith("{"))
            {
                var response = await ProcessMessageAsync(trimmedLine, cancellationToken).ConfigureAwait(false);
                if (response != null)
                {
                    var responseJson = McpJsonSerializer.Serialize(response);
                    await writer.WriteLineAsync(responseJson).ConfigureAwait(false);
                    await writer.FlushAsync(cancellationToken).ConfigureAwait(false);
                }
                continue;
            }

            if (!line.StartsWith(JsonRpc.ContentLengthPrefix)) continue;

            var contentLength = int.Parse(line.AsSpan(JsonRpc.ContentLengthPrefix.Length).Trim());
            await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);

            var buffer = new char[contentLength];
            var read = await reader.ReadAsync(buffer, 0, contentLength).ConfigureAwait(false);
            var json = new string(buffer, 0, read);

            var responseLsp = await ProcessMessageAsync(json, cancellationToken).ConfigureAwait(false);
            if (responseLsp != null)
            {
                var responseJson = McpJsonSerializer.Serialize(responseLsp);
                var responseBytes = System.Text.Encoding.UTF8.GetBytes(responseJson);
                await writer.WriteLineAsync($"Content-Length: {responseBytes.Length}").ConfigureAwait(false);
                await writer.WriteLineAsync().ConfigureAwait(false);
                await writer.WriteAsync(responseJson).ConfigureAwait(false);
                await writer.FlushAsync(cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private async Task<JsonRpcResponse?> ProcessMessageAsync(string json, CancellationToken cancellationToken)
    {
        JsonRpcRequest? request = null;
        try
        {
            request = McpJsonSerializer.DeserializeJsonRpcRequest(json);
            if (request == null) return null;

            if (request.Id.IsNull)
            {
                HandleNotification(request.Method, request.Params, cancellationToken);
                return null;
            }

            var response = new JsonRpcResponse { Id = request.Id };

            var method = McpMethodExtensions.FromValue(request.Method);
            switch (method)
            {
                case McpMethod.Initialize:
                    response.Result = JsonSerializer.SerializeToElement(HandleInitialize(), McpJsonContext.Default.InitializeResult);
                    break;

                case McpMethod.Ping:
                    response.Result = JsonSerializer.SerializeToElement(HandlePing(), McpJsonContext.Default.PingResult);
                    break;

                case McpMethod.ToolsList:
                    response.Result = JsonSerializer.SerializeToElement(HandleListTools(), McpJsonContext.Default.ListToolsResult);
                    break;

                case McpMethod.ToolsCall:
                    {
                        var result = await HandleCallToolAsync(request.Params, cancellationToken).ConfigureAwait(false);
                        response.Result = JsonSerializer.SerializeToElement(result, McpJsonContext.Default.CallToolResult);
                    }
                    break;

                case McpMethod.ResourcesList:
                    response.Result = JsonSerializer.SerializeToElement(HandleListResources(), McpJsonContext.Default.McpResourcesListResponse);
                    break;

                case McpMethod.ResourcesRead:
                    {
                        var result = await HandleReadResourceAsync(request.Params, cancellationToken).ConfigureAwait(false);
                        response.Result = JsonSerializer.SerializeToElement(result, McpJsonContext.Default.McpResourceReadResponse);
                    }
                    break;

                case McpMethod.PromptsList:
                    response.Result = JsonSerializer.SerializeToElement(HandleListPrompts(), McpJsonContext.Default.McpPromptsListResponse);
                    break;

                case McpMethod.PromptsGet:
                    {
                        var result = await HandleGetPromptAsync(request.Params, cancellationToken).ConfigureAwait(false);
                        response.Result = JsonSerializer.SerializeToElement(result, McpJsonContext.Default.McpPromptGetResponse);
                    }
                    break;

                case McpMethod.LoggingSetLevel:
                    response.Result = JsonSerializer.SerializeToElement(HandleSetLogLevel(request.Params), McpJsonContext.Default.PingResult);
                    break;

                default:
                    response.Error = new JsonRpcError
                    {
                        Code = McpProtocol.Contracts.ErrorCodes.MethodNotFound,
                        Message = $"Method not found: {request.Method}"
                    };
                    break;
            }

            return response;
        }
        catch (Exception ex)
        {
            return new JsonRpcResponse
            {
                Id = request?.Id ?? JsonRpcId.Null,
                Error = new JsonRpcError
                {
                    Code = McpProtocol.Contracts.ErrorCodes.InternalError,
                    Message = $"Internal error: {ex.Message}"
                }
            };
        }
    }

    private void HandleNotification(string method, JsonElement? Params, CancellationToken cancellationToken)
    {
        NotificationReceived?.Invoke(this, new McpServerNotificationEventArgs
        {
            Method = method,
            Params = Params
        });

        var notificationMethod = McpMethodExtensions.FromValue(method);
        switch (notificationMethod)
        {
            case McpMethod.Initialized:
                break;

            case McpMethod.NotificationCancelled:
                break;

            case McpMethod.NotificationResourcesUpdated:
                break;

            case McpMethod.NotificationResourcesListChanged:
                break;

            case McpMethod.NotificationToolsListChanged:
                break;

            case McpMethod.NotificationPromptsListChanged:
                break;

            case McpMethod.NotificationMessage:
                break;

            default:
                break;
        }
    }

    private InitializeResult HandleInitialize()
    {
        return new InitializeResult
        {
            ProtocolVersion = McpProtocolVersion.Current,
            Capabilities = new ServerCapabilities
            {
                Tools = new ToolsCapability { ListChanged = false },
                Resources = _resources.Count > 0
                    ? new ResourcesCapability { Subscribe = false, ListChanged = false }
                    : null,
                Prompts = _prompts.Count > 0
                    ? new PromptsCapability { ListChanged = false }
                    : null,
                Logging = new LoggingCapability { Level = _logLevel }
            },
            ServerInfo = new Implementation
            {
                Name = _serverName,
                Version = _serverVersion
            },
            Instructions = _instructions
        };
    }

    private static PingResult HandlePing()
    {
        return new PingResult();
    }

    private ListToolsResult HandleListTools()
    {
        var tools = _tools.Values.Select(h => new ToolDefinition
        {
            Name = h.Name,
            Description = h.Description,
            InputSchema = h.InputSchema
        }).ToList();

        return new ListToolsResult { Tools = tools };
    }

    private async Task<CallToolResult> HandleCallToolAsync(JsonElement? paramsObj, CancellationToken cancellationToken)
    {
        if (paramsObj == null)
            return new CallToolResult { Content = [new McpToolContent { Text = "No parameters provided" }], IsError = true };

        var callParams = McpJsonSerializer.DeserializeCallToolRequestParams(paramsObj.Value.GetRawText());
        if (callParams == null)
            return new CallToolResult { Content = [new McpToolContent { Text = "Invalid parameters" }], IsError = true };

        if (!_tools.TryGetValue(callParams.Name, out var handler))
            return new CallToolResult { Content = [new McpToolContent { Text = $"Tool not found: {callParams.Name}" }], IsError = true };

        try
        {
            var arguments = ParseArguments(callParams.Arguments);
            var result = await handler.ExecuteAsync(arguments).ConfigureAwait(false);

            var resultText = result switch
            {
                null => "null",
                string s => s,
                _ => McpJsonSerializer.SerializeObject(result)
            };
            return new CallToolResult
            {
                Content = [new McpToolContent { Text = resultText }]
            };
        }
        catch (Exception ex)
        {
            return new CallToolResult
            {
                Content = [new McpToolContent { Text = $"Error: {ex.InnerException?.Message ?? ex.Message}" }],
                IsError = true
            };
        }
    }

    private McpResourcesListResponse HandleListResources()
    {
        var resources = _resources.Values.Select(h => new McpResource
        {
            Uri = h.Uri,
            Name = h.Name,
            Description = h.Description,
            MimeType = h.MimeType
        }).ToList();

        return new McpResourcesListResponse { Resources = resources };
    }

    private async Task<McpResourceReadResponse> HandleReadResourceAsync(JsonElement? paramsObj, CancellationToken cancellationToken)
    {
        if (paramsObj == null)
            return new McpResourceReadResponse();

        var readParams = McpJsonSerializer.DeserializeMcpResourceReadRequestParams(paramsObj.Value.GetRawText());

        if (readParams == null || string.IsNullOrEmpty(readParams.Uri))
            return new McpResourceReadResponse();

        if (!_resources.TryGetValue(readParams.Uri, out var handler))
            return new McpResourceReadResponse();

        try
        {
            var content = await handler.ReadAsync(cancellationToken).ConfigureAwait(false);
            return new McpResourceReadResponse { Contents = [content] };
        }
        catch
        {
            return new McpResourceReadResponse();
        }
    }

    private McpPromptsListResponse HandleListPrompts()
    {
        var prompts = _prompts.Values.Select(h => new McpPrompt
        {
            Name = h.Name,
            Description = h.Description,
            Arguments = h.Arguments
        }).ToList();

        return new McpPromptsListResponse { Prompts = prompts };
    }

    private async Task<McpPromptGetResponse> HandleGetPromptAsync(JsonElement? paramsObj, CancellationToken cancellationToken)
    {
        if (paramsObj == null)
            return new McpPromptGetResponse();

        var getParams = McpJsonSerializer.DeserializeMcpPromptGetRequestParams(paramsObj.Value.GetRawText());

        if (getParams == null || string.IsNullOrEmpty(getParams.Name))
            return new McpPromptGetResponse();

        if (!_prompts.TryGetValue(getParams.Name, out var handler))
            return new McpPromptGetResponse();

        try
        {
            var message = await handler.GetAsync(getParams.Arguments, cancellationToken).ConfigureAwait(false);
            return new McpPromptGetResponse
            {
                Description = message.Description,
                Messages = message.Messages
            };
        }
        catch
        {
            return new McpPromptGetResponse();
        }
    }

    private PingResult HandleSetLogLevel(JsonElement? paramsObj)
    {
        if (paramsObj is JsonElement element)
        {
            var setLevelParams = McpJsonSerializer.DeserializeLoggingSetLevelRequestParams(element.GetRawText());
            if (setLevelParams != null)
            {
                _logLevel = setLevelParams.Level;
            }
        }

        return new PingResult();
    }

    private static Dictionary<string, JsonElement> ParseArguments(JsonElement? arguments)
    {
        if (arguments == null) return new Dictionary<string, JsonElement>();

        return McpJsonSerializer.DeserializeDictionaryStringJsonElement(arguments.Value.GetRawText()) ?? new Dictionary<string, JsonElement>();
    }
}

public sealed class McpServerNotificationEventArgs : EventArgs
{
    public required string Method { get; init; }
    public JsonElement? Params { get; init; }
}
