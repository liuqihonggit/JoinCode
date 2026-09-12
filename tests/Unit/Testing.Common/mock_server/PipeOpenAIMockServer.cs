
namespace Testing.Common.MockServer;

public sealed class PipeOpenAIMockServer : IAsyncDisposable
{
    private readonly MockServerOptions _options;
    private readonly ILogger<PipeOpenAIMockServer> _logger;
    private readonly RequestRecorder? _requestRecorder;
    private CancellationTokenSource? _cts;
    private Task? _processingTask;

    private const int ReadBufferSize = 8192;

    public bool IsRunning => _processingTask is { IsCompleted: false };

    public PipeOpenAIMockServer(MockServerOptions options, ILogger<PipeOpenAIMockServer> logger, RequestRecorder? requestRecorder = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);
        _options = options;
        _logger = logger;
        _requestRecorder = requestRecorder;
    }

    public string GetPipeName() => _options.PipeName;

    public async Task StartAsync(CancellationToken ct = default)
    {
        if (_processingTask != null)
        {
            throw new InvalidOperationException("Server already started.");
        }

        _logger.LogInformation("[MockServer] 启动管道 Mock Server，PipeName: {PipeName}", _options.PipeName);

        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _processingTask = ProcessConnectionsAsync(_cts.Token);

        _logger.LogInformation("[MockServer] Server started, pipe name: {PipeName}", _options.PipeName);
    }

    public async Task StopAsync(CancellationToken ct = default)
    {
        _logger.LogInformation("[MockServer] 停止 Mock Server");

        _cts?.Cancel();

        if (_processingTask != null)
        {
            try
            {
                await _processingTask.WaitAsync(TimeSpan.FromSeconds(5), ct).ConfigureAwait(true);
            }
            catch (TimeoutException)
            {
                _logger.LogWarning("[MockServer] 等待处理任务超时");
            }
            catch (OperationCanceledException)
            {
            }
        }

        await DisposeAsync().ConfigureAwait(true);

        _logger.LogInformation("[MockServer] Server stopped");
    }

    private async Task ProcessConnectionsAsync(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                using var pipeServer = new NamedPipeServerStream(
                    _options.PipeName,
                    PipeDirection.InOut,
                    NamedPipeServerStream.MaxAllowedServerInstances,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous);

                _logger.LogInformation("[MockServer] 等待客户端连接...");
                await pipeServer.WaitForConnectionAsync(ct).ConfigureAwait(true);
                _logger.LogInformation("[MockServer] 客户端已连接");

                await ProcessSingleConnectionAsync(pipeServer, ct).ConfigureAwait(true);

                _logger.LogInformation("[MockServer] 连接已关闭，等待下一个连接");
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("[MockServer] 连接处理循环已取消");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[MockServer] 处理连接时发生错误");
        }
    }

    private async Task ProcessSingleConnectionAsync(NamedPipeServerStream pipeServer, CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested && pipeServer.IsConnected)
            {
                var request = await ReadRequestAsync(pipeServer, ct).ConfigureAwait(true);
                if (request == null)
                {
                    break;
                }

                var response = HandleRequest(request);
                await WriteResponseAsync(pipeServer, response, ct).ConfigureAwait(true);

                if (request.Headers.TryGetValue("Connection", out var connection) &&
                    connection.Equals("close", StringComparison.OrdinalIgnoreCase))
                {
                    break;
                }
            }
        }
        catch (IOException ex) when (ex.Message.Contains("broken pipe", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogInformation("[MockServer] 客户端断开连接");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[MockServer] 处理单次连接时发生错误");
        }
    }

    private static async Task<HttpRequest?> ReadRequestAsync(NamedPipeServerStream pipeServer, CancellationToken ct)
    {
        if (!pipeServer.IsConnected)
        {
            return null;
        }

        try
        {
            var buffer = new byte[ReadBufferSize];
            using var ms = new MemoryStream();

            while (!ct.IsCancellationRequested)
            {
                var readTask = pipeServer.ReadAsync(buffer, ct).AsTask();
                var completed = await Task.WhenAny(readTask, Task.Delay(5000, ct)).ConfigureAwait(true);
                if (completed != readTask)
                {
                    return null;
                }

                var bytesRead = await readTask.ConfigureAwait(true);
                if (bytesRead == 0)
                {
                    return null;
                }

                await ms.WriteAsync(buffer.AsMemory(0, bytesRead), ct).ConfigureAwait(true);

                var requestText = Encoding.UTF8.GetString(ms.ToArray());
                var headerEndIndex = requestText.IndexOf("\r\n\r\n", StringComparison.Ordinal);
                if (headerEndIndex < 0)
                {
                    continue;
                }

                var headerPart = requestText[..headerEndIndex];
                var contentLengthMatch = Regex.Match(
                    headerPart, @"Content-Length:\s*(\d+)", RegexOptions.IgnoreCase);

                if (!contentLengthMatch.Success)
                {
                    break;
                }

                var contentLength = int.Parse(contentLengthMatch.Groups[1].Value);
                var bodyStart = headerEndIndex + 4;
                var bodyReceived = ms.Length - bodyStart;

                if (bodyReceived >= contentLength)
                {
                    break;
                }
            }

            var fullRequest = Encoding.UTF8.GetString(ms.ToArray());
            if (string.IsNullOrEmpty(fullRequest))
            {
                return null;
            }

            return ParseHttpRequest(fullRequest);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (IOException)
        {
            return null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static HttpRequest? ParseHttpRequest(string requestText)
    {
        var headerEndIndex = requestText.IndexOf("\r\n\r\n", StringComparison.Ordinal);
        if (headerEndIndex < 0)
        {
            return null;
        }

        var headerPart = requestText[..headerEndIndex];
        var bodyPart = requestText[(headerEndIndex + 4)..];

        var lines = headerPart.Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        if (lines.Length == 0)
        {
            return null;
        }

        var requestLineParts = lines[0].Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (requestLineParts.Length < 2)
        {
            return null;
        }

        var method = requestLineParts[0];
        var path = requestLineParts[1];

        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 1; i < lines.Length; i++)
        {
            var colonIndex = lines[i].IndexOf(':');
            if (colonIndex > 0)
            {
                var key = lines[i][..colonIndex].Trim();
                var value = lines[i][(colonIndex + 1)..].Trim();
                headers[key] = value;
            }
        }

        return new HttpRequest(method, path, headers, string.IsNullOrEmpty(bodyPart) ? null : bodyPart);
    }

    private HttpResponse HandleRequest(HttpRequest request)
    {
        _logger.LogInformation("[MockServer] 处理请求: {Method} {Path}", request.Method, request.Path);

        // 记录请求到 RequestRecorder（如果配置了）
        _requestRecorder?.Record(new HttpRequestInfo
        {
            Method = request.Method,
            Path = request.Path,
            Headers = request.Headers,
            Body = request.Body ?? string.Empty
        });

        if (!ValidateApiKey(request.Headers))
        {
            return new HttpResponse(401, """{"error": {"message": "Invalid API Key"}}""");
        }

        if (request.Path.Contains("/chat/completions", StringComparison.OrdinalIgnoreCase))
        {
            return HandleChatCompletion(request);
        }

        if (request.Path.Contains("/models", StringComparison.OrdinalIgnoreCase))
        {
            return HandleModelsList();
        }

        return new HttpResponse(404, """{"error": {"message": "Not Found"}}""");
    }

    private bool ValidateApiKey(Dictionary<string, string> headers)
    {
        if (!headers.TryGetValue("Authorization", out var auth))
        {
            return false;
        }

        return auth.Contains(_options.ApiKey);
    }

    private HttpResponse HandleChatCompletion(HttpRequest request)
    {
        try
        {
            if (string.IsNullOrEmpty(request.Body))
            {
                return new HttpResponse(400, """{"error": {"message": "Empty request body"}}""");
            }

            using var doc = JsonDocument.Parse(request.Body);
            var root = doc.RootElement;

            var isStream = root.TryGetProperty("stream", out var streamProp) && streamProp.GetBoolean();

            var lastUserMessage = ExtractUserMessage(root);
            var hasToolResult = HasToolResult(root);

            // 如果请求中包含 tool 结果，返回最终文本响应
            if (hasToolResult)
            {
                return isStream ? HandleStreamingFinalResponse(root) : HandleNonStreamingFinalResponse(root);
            }

            // 如果用户消息触发工具调用，返回 tool_calls
            if (ShouldTriggerToolCall(lastUserMessage))
            {
                return isStream ? HandleStreamingToolCall(root) : HandleNonStreamingToolCall(root);
            }

            return isStream ? HandleStreamingChatCompletion(root) : HandleNonStreamingChatCompletion(root);
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "[MockServer] 解析请求体失败");
            return new HttpResponse(400, """{"error": {"message": "Invalid JSON"}}""");
        }
    }

    /// <summary>
    /// 检测请求中是否包含工具调用结果（role=tool 的消息）
    /// </summary>
    private static bool HasToolResult(JsonElement root)
    {
        if (!root.TryGetProperty("messages", out var messages))
            return false;

        foreach (var message in messages.EnumerateArray())
        {
            if (message.TryGetProperty("role", out var role) &&
                role.GetString() == "tool")
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// 判断用户消息是否应触发工具调用
    /// </summary>
    private static bool ShouldTriggerToolCall(string userMessage)
    {
        if (string.IsNullOrEmpty(userMessage))
            return false;

        return userMessage.Contains("目录") || userMessage.Contains("路径") ||
               userMessage.Contains("当前目录") || userMessage.Contains("pwd") ||
               userMessage.Contains("directory", StringComparison.OrdinalIgnoreCase);
    }

    private HttpResponse HandleNonStreamingToolCall(JsonElement root)
    {
        var requestId = $"mock-{Guid.NewGuid():N}";
        var content = ExtractUserMessage(root);
        var callId = $"call_{Guid.NewGuid():N}";

        var response = new MockChatCompletionResponse
        {
            Id = requestId,
            Object = "chat.completion",
            Created = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            Model = _options.Model,
            Choices =
            [
                new MockChatChoice
                {
                    Index = 0,
                    Message = new MockApiMessage
                    {
                        Role = "assistant",
                        Content = null,
                        ToolCalls =
                        [
                            new MockToolCall
                            {
                                Id = callId,
                                Type = "function",
                                Function = new MockToolCallFunction
                                {
                                    Name = "Bash",
                                    Arguments = "{\"command\":\"cd\"}"
                                }
                            }
                        ]
                    },
                    FinishReason = OpenAIFinishReasonConstants.ToolCalls
                }
            ],
            Usage = new MockUsage
            {
                PromptTokens = content.Length / 4,
                CompletionTokens = 10,
                TotalTokens = content.Length / 4 + 10
            }
        };

        _logger.LogInformation("[MockServer] 返回 tool_calls: Bash cd (callId={CallId})", callId);
        return new HttpResponse(200, JsonSerializer.Serialize(response, MockServerJsonContext.Default.MockChatCompletionResponse));
    }

    private HttpResponse HandleStreamingToolCall(JsonElement root)
    {
        var requestId = $"mock-{Guid.NewGuid():N}";
        var callId = $"call_{Guid.NewGuid():N}";

        var sb = new StringBuilder();

        // chunk 1: tool_calls 开始（id + function.name）
        var startChunk = new MockChatCompletionResponse
        {
            Id = requestId,
            Object = "chat.completion.chunk",
            Created = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            Model = _options.Model,
            Choices =
            [
                new MockChatChoice
                {
                    Index = 0,
                    Delta = new MockChatDelta
                    {
                        ToolCalls =
                        [
                            new MockToolCallDelta
                            {
                                Index = 0,
                                Id = callId,
                                Type = "function",
                                Function = new MockToolCallFunctionDelta { Name = "Bash", Arguments = "" }
                            }
                        ]
                    }
                }
            ]
        };
        sb.AppendLine($"data: {JsonSerializer.Serialize(startChunk, MockServerJsonContext.Default.MockChatCompletionResponse)}");

        // chunk 2: function.arguments
        var argsChunk = new MockChatCompletionResponse
        {
            Id = requestId,
            Object = "chat.completion.chunk",
            Created = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            Model = _options.Model,
            Choices =
            [
                new MockChatChoice
                {
                    Index = 0,
                    Delta = new MockChatDelta
                    {
                        ToolCalls =
                        [
                            new MockToolCallDelta
                            {
                                Index = 0,
                                Function = new MockToolCallFunctionDelta { Arguments = "{\"command\":\"cd\"}" }
                            }
                        ]
                    }
                }
            ]
        };
        sb.AppendLine($"data: {JsonSerializer.Serialize(argsChunk, MockServerJsonContext.Default.MockChatCompletionResponse)}");

        // chunk 3: finish_reason = tool_calls
        var finishChunk = new MockChatCompletionResponse
        {
            Id = requestId,
            Object = "chat.completion.chunk",
            Created = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            Model = _options.Model,
            Choices =
            [
                new MockChatChoice
                {
                    Index = 0,
                    Delta = new MockChatDelta(),
                    FinishReason = OpenAIFinishReasonConstants.ToolCalls
                }
            ]
        };
        sb.AppendLine($"data: {JsonSerializer.Serialize(finishChunk, MockServerJsonContext.Default.MockChatCompletionResponse)}");

        sb.AppendLine("data: [DONE]");

        _logger.LogInformation("[MockServer] 返回流式 tool_calls: Bash cd (callId={CallId})", callId);
        return new HttpResponse(200, sb.ToString(), "text/event-stream");
    }

    private HttpResponse HandleNonStreamingFinalResponse(JsonElement root)
    {
        var requestId = $"mock-{Guid.NewGuid():N}";
        var toolResult = ExtractLastToolResult(root);
        var responseText = $"当前工作目录为：{toolResult}";

        var response = new MockChatCompletionResponse
        {
            Id = requestId,
            Object = "chat.completion",
            Created = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            Model = _options.Model,
            Choices =
            [
                new MockChatChoice
                {
                    Index = 0,
                    Message = new MockApiMessage { Role = "assistant", Content = responseText },
                    FinishReason = OpenAIFinishReasonConstants.Stop
                }
            ],
            Usage = new MockUsage { PromptTokens = 50, CompletionTokens = 20, TotalTokens = 70 }
        };

        return new HttpResponse(200, JsonSerializer.Serialize(response, MockServerJsonContext.Default.MockChatCompletionResponse));
    }

    private HttpResponse HandleStreamingFinalResponse(JsonElement root)
    {
        var requestId = $"mock-{Guid.NewGuid():N}";
        var toolResult = ExtractLastToolResult(root);
        var responseText = $"当前工作目录为：{toolResult}";

        var sb = new StringBuilder();
        var words = SplitToChunks(responseText);
        foreach (var word in words)
        {
            var chunk = new MockChatCompletionResponse
            {
                Id = requestId,
                Object = "chat.completion.chunk",
                Created = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                Model = _options.Model,
                Choices =
                [
                    new MockChatChoice
                    {
                        Index = 0,
                        Delta = new MockChatDelta { Content = word }
                    }
                ]
            };
            sb.AppendLine($"data: {JsonSerializer.Serialize(chunk, MockServerJsonContext.Default.MockChatCompletionResponse)}");
        }

        sb.AppendLine("data: [DONE]");
        return new HttpResponse(200, sb.ToString(), "text/event-stream");
    }

    /// <summary>
    /// 提取最后一条 tool 结果
    /// </summary>
    private static string ExtractLastToolResult(JsonElement root)
    {
        if (!root.TryGetProperty("messages", out var messages))
            return "(未知)";

        string? lastToolResult = null;
        foreach (var message in messages.EnumerateArray())
        {
            if (message.TryGetProperty("role", out var role) &&
                role.GetString() == "tool" &&
                message.TryGetProperty("content", out var content))
            {
                lastToolResult = content.GetString();
            }
        }

        return lastToolResult ?? "(未知)";
    }

    private HttpResponse HandleNonStreamingChatCompletion(JsonElement root)
    {
        var requestId = $"mock-{Guid.NewGuid():N}";
        var content = ExtractUserMessage(root);
        var responseText = GenerateContextAwareResponse(content);

        var response = new MockChatCompletionResponse
        {
            Id = requestId,
            Object = "chat.completion",
            Created = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            Model = _options.Model,
            Choices =
            [
                new MockChatChoice
                {
                    Index = 0,
                    Message = new MockApiMessage
                    {
                        Role = "assistant",
                        Content = responseText
                    },
                    FinishReason = OpenAIFinishReasonConstants.Stop
                }
            ],
            Usage = new MockUsage
            {
                PromptTokens = content.Length / 4,
                CompletionTokens = 20,
                TotalTokens = content.Length / 4 + 20
            }
        };

        return new HttpResponse(200, JsonSerializer.Serialize(response, MockServerJsonContext.Default.MockChatCompletionResponse));
    }

    private HttpResponse HandleStreamingChatCompletion(JsonElement root)
    {
        var content = ExtractUserMessage(root);
        var requestId = $"mock-{Guid.NewGuid():N}";

        var sb = new StringBuilder();

        var responseText = GenerateContextAwareResponse(content);
        var words = SplitToChunks(responseText);
        foreach (var word in words)
        {
            var chunk = new MockChatCompletionResponse
            {
                Id = requestId,
                Object = "chat.completion.chunk",
                Created = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                Model = _options.Model,
                Choices =
                [
                    new MockChatChoice
                    {
                        Index = 0,
                        Delta = new MockChatDelta { Content = word },
                        FinishReason = null
                    }
                ]
            };

            sb.AppendLine($"data: {JsonSerializer.Serialize(chunk, MockServerJsonContext.Default.MockChatCompletionResponse)}");
        }

        sb.AppendLine("data: [DONE]");

        return new HttpResponse(200, sb.ToString(), "text/event-stream");
    }

    /// <summary>
    /// 根据用户消息内容生成差异化响应，方便验证多轮对话是否正确区分
    /// </summary>
    private static string GenerateContextAwareResponse(string userMessage)
    {
        if (string.IsNullOrEmpty(userMessage))
            return "收到空消息。";

        // 模拟真实 AI 对不同类型请求的差异化响应
        if (userMessage.Contains("你好") || userMessage.Contains("hello", StringComparison.OrdinalIgnoreCase))
            return "你好！我是AI助手，有什么可以帮你的？";

        if (userMessage.Contains("查看") || userMessage.Contains("工程") || userMessage.Contains("文件") || userMessage.Contains("项目"))
            return "当前工作目录下共有 15 个项目文件，支持 C#、TypeScript 等多种工程类型。主要包含 JoinCode 主项目和 7 个子系统。";

        if (userMessage.Contains("测试") || userMessage.Contains("test", StringComparison.OrdinalIgnoreCase))
            return "已运行测试套件，共 42 个测试用例，全部通过。覆盖率 87.3%。";

        if (userMessage.Contains("编译") || userMessage.Contains("build", StringComparison.OrdinalIgnoreCase))
            return "编译成功，0 个错误，0 个警告。耗时 18.5 秒。";

        // 默认：回显用户消息，确保每轮响应不同
        return $"收到：{userMessage}";
    }

    /// <summary>
    /// 将文本按 2-4 字符分块，模拟流式输出
    /// </summary>
    private static string[] SplitToChunks(string text)
    {
        var chunks = new List<string>();
        for (var i = 0; i < text.Length;)
        {
            var chunkSize = Math.Min(2 + (i % 3), text.Length - i);
            chunks.Add(text.Substring(i, chunkSize));
            i += chunkSize;
        }
        return chunks.ToArray();
    }

    private static string ExtractUserMessage(JsonElement root)
    {
        if (!root.TryGetProperty("messages", out var messages))
        {
            return "";
        }

        // 取最后一条用户消息（多轮对话中 API 请求包含所有历史消息）
        string? lastUserMessage = null;
        foreach (var message in messages.EnumerateArray())
        {
            if (message.TryGetProperty("role", out var role) &&
                role.GetString() == "user" &&
                message.TryGetProperty("content", out var content))
            {
                lastUserMessage = content.GetString();
            }
        }

        return lastUserMessage ?? "";
    }

    private HttpResponse HandleModelsList()
    {
        var response = new MockModelsResponse
        {
            Object = "list",
            Data =
            [
                new MockModelItem
                {
                    Id = _options.Model,
                    Object = "model",
                    Created = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                    OwnedBy = "mock"
                }
            ]
        };

        return new HttpResponse(200, JsonSerializer.Serialize(response, MockServerJsonContext.Default.MockModelsResponse));
    }

    private static async Task WriteResponseAsync(NamedPipeServerStream pipeServer, HttpResponse response, CancellationToken ct)
    {
        if (!pipeServer.IsConnected)
        {
            return;
        }

        var sb = new StringBuilder();
        sb.Append($"HTTP/1.1 {response.StatusCode} {GetStatusText(response.StatusCode)}\r\n");
        sb.Append($"Content-Type: {response.ContentType}\r\n");
        sb.Append($"Content-Length: {Encoding.UTF8.GetByteCount(response.Body)}\r\n");
        sb.Append("Connection: close\r\n");
        sb.Append("\r\n");
        sb.Append(response.Body);

        var responseBytes = Encoding.UTF8.GetBytes(sb.ToString());
        await pipeServer.WriteAsync(responseBytes, ct).ConfigureAwait(true);
        await pipeServer.FlushAsync(ct).ConfigureAwait(true);
    }

    private static string GetStatusText(int statusCode) => statusCode switch
    {
        200 => "OK",
        401 => "Unauthorized",
        404 => "Not Found",
        400 => "Bad Request",
        500 => "Internal Server Error",
        _ => "Unknown"
    };

    public async ValueTask DisposeAsync()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;
        _processingTask = null;
    }

    private sealed record HttpRequest(string Method, string Path, Dictionary<string, string> Headers, string? Body);

    private sealed record HttpResponse(int StatusCode, string Body, string ContentType = "application/json");
}
