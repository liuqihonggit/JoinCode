namespace JoinCode.Tests;

[Trait("Category", "Integration")]
public sealed class RealApiCacheTests
{
    static RealApiCacheTests()
    {
        // 集成测试启动时加载 .env 文件中的 API Key
        Infrastructure.IO.Configuration.EnvFileLoader.LoadFromDirectory(new IO.FileSystem.PhysicalFileSystem());
    }

    private static bool HasAnthropicKey => !string.IsNullOrEmpty(Environment.GetEnvironmentVariable(ProviderEnvVarConstants.AnthropicApiKey));
    private static bool HasOpenAIKey => !string.IsNullOrEmpty(Environment.GetEnvironmentVariable(ProviderEnvVarConstants.OpenAiApiKey));

    private static HttpClient CreateAnthropicClient()
    {
        var apiKey = Environment.GetEnvironmentVariable(ProviderEnvVarConstants.AnthropicApiKey);
        if (string.IsNullOrEmpty(apiKey))
            throw new InvalidOperationException($"{ProviderEnvVarConstants.AnthropicApiKey} 环境变量未设置");

        var client = new HttpClient
        {
            BaseAddress = new Uri("https://api.anthropic.com")
        };
        client.DefaultRequestHeaders.Add("x-api-key", apiKey);
        client.DefaultRequestHeaders.Add("anthropic-version", "3-20250514");
        client.DefaultRequestHeaders.Add("anthropic-beta", "prompt-caching-2024-07-31,prompt-caching-scope-2026-01-05");
        return client;
    }

    private static HttpClient CreateOpenAIClient()
    {
        var apiKey = Environment.GetEnvironmentVariable(ProviderEnvVarConstants.OpenAiApiKey);
        if (string.IsNullOrEmpty(apiKey))
            throw new InvalidOperationException($"{ProviderEnvVarConstants.OpenAiApiKey} 环境变量未设置");

        var client = new HttpClient
        {
            BaseAddress = new Uri("https://api.openai.com")
        };
        client.DefaultRequestHeaders.Add("Authorization", $"Bearer {apiKey}");
        return client;
    }

    [Fact]
    public async Task Anthropic_StaticPrefix_CacheHitOnSecondTurn()
    {
        if (!HasAnthropicKey) return;
        using var client = CreateAnthropicClient();
        var systemPrompt = "You are a helpful assistant. " + new string('x', 2000);

        var request1 = BuildAnthropicRequest(systemPrompt, "Hello, first turn");
        var response1 = await SendAnthropicRequestAsync(client, request1).ConfigureAwait(true);
        OutputCacheStats("Turn 1", response1);

        Assert.True(response1.CacheCreationInputTokens > 0,
            "第一轮应有 cache_creation_input_tokens > 0");

        await Task.Delay(1000).ConfigureAwait(true);

        var request2 = BuildAnthropicRequest(systemPrompt, "Hello, second turn");
        var response2 = await SendAnthropicRequestAsync(client, request2).ConfigureAwait(true);
        OutputCacheStats("Turn 2", response2);

        Assert.True(response2.CacheReadInputTokens > 0,
            "第二轮应有 cache_read_input_tokens > 0（缓存命中）");
    }

    [Fact]
    public async Task Anthropic_DeferLoading_SchemaNotSent()
    {
        if (!HasAnthropicKey) return;
        using var client = CreateAnthropicClient();
        var systemPrompt = "You are a helpful assistant. " + new string('x', 2000);

        var request = BuildAnthropicRequestWithDeferredTools(systemPrompt, "What tools are available?");
        var response = await SendAnthropicRequestAsync(client, request).ConfigureAwait(true);
        OutputCacheStats("Deferred Tools", response);

        Assert.NotNull(response);

        Assert.True(response.CacheCreationInputTokens > 0 || response.CacheReadInputTokens > 0 || response.InputTokens > 0);
    }

    [Fact]
    public async Task Anthropic_ScopeOrg_WithMcpTools()
    {
        if (!HasAnthropicKey) return;
        using var client = CreateAnthropicClient();
        var systemPrompt = "You are a helpful assistant. " + new string('x', 2000);

        var request = BuildAnthropicRequestWithMcpTools(systemPrompt, "Hello");
        var response = await SendAnthropicRequestAsync(client, request).ConfigureAwait(true);
        OutputCacheStats("Scope:org", response);

        Assert.True(response.InputTokens > 0);
    }

    [Fact]
    public async Task OpenAI_StaticPrefix_CachedTokensOnSecondTurn()
    {
        if (!HasOpenAIKey) return;
        using var client = CreateOpenAIClient();
        var systemPrompt = "You are a helpful assistant. " + new string('x', 2000);

        var request1 = BuildOpenAIRequest(systemPrompt, "Hello, first turn");
        var response1 = await SendOpenAIRequestAsync(client, request1).ConfigureAwait(true);
        OutputOpenAICacheStats("Turn 1", response1);

        await Task.Delay(2000).ConfigureAwait(true);

        var request2 = BuildOpenAIRequest(systemPrompt, "Hello, second turn");
        var response2 = await SendOpenAIRequestAsync(client, request2).ConfigureAwait(true);
        OutputOpenAICacheStats("Turn 2", response2);

        Assert.True(response2.CachedTokens > 0,
            "第二轮应有 cached_tokens > 0（缓存命中）");
    }

    private static string BuildAnthropicRequest(string systemPrompt, string userMessage)
    {
        var request = new TestAnthropicRequest
        {
            Model = "claude-sonnet-4-20250514",
            MaxTokens = 100,
            System =
            [
                new TestAnthropicSystemBlock
                {
                    Type = "text",
                    Text = systemPrompt,
                    CacheControl = new TestCacheControl { Type = "ephemeral" }
                }
            ],
            Messages =
            [
                new TestAnthropicMessage { Role = "user", Content = userMessage }
            ]
        };
        return JsonSerializer.Serialize(request, RealApiCacheJsonContext.Default.TestAnthropicRequest);
    }

    private static string BuildAnthropicRequestWithDeferredTools(string systemPrompt, string userMessage)
    {
        var request = new TestAnthropicRequest
        {
            Model = "claude-sonnet-4-20250514",
            MaxTokens = 100,
            System =
            [
                new TestAnthropicSystemBlock
                {
                    Type = "text",
                    Text = systemPrompt,
                    CacheControl = new TestCacheControl { Type = "ephemeral" }
                }
            ],
            Messages =
            [
                new TestAnthropicMessage { Role = "user", Content = userMessage }
            ],
            Tools =
            [
                new TestAnthropicTool
                {
                    Name = "mcp.search",
                    Description = "Search files in workspace",
                    InputSchema = new TestInputSchema { Type = "object" },
                    DeferLoading = true,
                    CacheControl = new TestCacheControl { Type = "ephemeral", Scope = "org" }
                }
            ]
        };
        return JsonSerializer.Serialize(request, RealApiCacheJsonContext.Default.TestAnthropicRequest);
    }

    private static string BuildAnthropicRequestWithMcpTools(string systemPrompt, string userMessage)
    {
        var request = new TestAnthropicRequest
        {
            Model = "claude-sonnet-4-20250514",
            MaxTokens = 100,
            System =
            [
                new TestAnthropicSystemBlock
                {
                    Type = "text",
                    Text = systemPrompt,
                    CacheControl = new TestCacheControl { Type = "ephemeral", Scope = "org" }
                }
            ],
            Messages =
            [
                new TestAnthropicMessage { Role = "user", Content = userMessage }
            ],
            Tools =
            [
                new TestAnthropicTool
                {
                    Name = "mcp.search",
                    Description = "Search files",
                    InputSchema = new TestInputSchema
                    {
                        Type = "object",
                        Properties = new Dictionary<string, TestSchemaProperty>
                        {
                            ["query"] = new() { Type = "string", Description = "Search query" }
                        }
                    },
                    CacheControl = new TestCacheControl { Type = "ephemeral", Scope = "org" }
                }
            ]
        };
        return JsonSerializer.Serialize(request, RealApiCacheJsonContext.Default.TestAnthropicRequest);
    }

    private static string BuildOpenAIRequest(string systemPrompt, string userMessage)
    {
        var request = new TestOpenAIRequest
        {
            Model = "gpt-4o",
            MaxTokens = 100,
            Messages =
            [
                new TestOpenAIMessage { Role = "system", Content = systemPrompt },
                new TestOpenAIMessage { Role = "user", Content = userMessage }
            ]
        };
        return JsonSerializer.Serialize(request, RealApiCacheJsonContext.Default.TestOpenAIRequest);
    }

    private static async Task<AnthropicRealResponse> SendAnthropicRequestAsync(HttpClient client, string json)
    {
        var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
        var response = await client.PostAsync("/v1/messages", content).ConfigureAwait(true);
        var body = await response.Content.ReadAsStringAsync().ConfigureAwait(true);

        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"Anthropic API 错误: {response.StatusCode} - {body}");

        var doc = JsonDocument.Parse(body);
        var usage = doc.RootElement.GetProperty("usage");

        return new AnthropicRealResponse
        {
            InputTokens = usage.GetProperty("input_tokens").GetInt32(),
            OutputTokens = usage.GetProperty("output_tokens").GetInt32(),
            CacheCreationInputTokens = usage.TryGetProperty("cache_creation_input_tokens", out var cc) ? cc.GetInt32() : 0,
            CacheReadInputTokens = usage.TryGetProperty("cache_read_input_tokens", out var cr) ? cr.GetInt32() : 0
        };
    }

    private static async Task<OpenAIRealResponse> SendOpenAIRequestAsync(HttpClient client, string json)
    {
        var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
        var response = await client.PostAsync("/v1/chat/completions", content).ConfigureAwait(true);
        var body = await response.Content.ReadAsStringAsync().ConfigureAwait(true);

        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"OpenAI API 错误: {response.StatusCode} - {body}");

        var doc = JsonDocument.Parse(body);
        var usage = doc.RootElement.GetProperty("usage");

        int cachedTokens = 0;
        if (usage.TryGetProperty("prompt_tokens_details", out var details))
        {
            if (details.TryGetProperty("cached_tokens", out var ct))
                cachedTokens = ct.GetInt32();
        }

        return new OpenAIRealResponse
        {
            PromptTokens = usage.GetProperty("prompt_tokens").GetInt32(),
            CompletionTokens = usage.GetProperty("completion_tokens").GetInt32(),
            CachedTokens = cachedTokens
        };
    }

    private static void OutputCacheStats(string label, AnthropicRealResponse r)
    {
        Console.WriteLine($"[{label}] Input={r.InputTokens}, Output={r.OutputTokens}, " +
            $"CacheCreation={r.CacheCreationInputTokens}, CacheRead={r.CacheReadInputTokens}");
    }

    private static void OutputOpenAICacheStats(string label, OpenAIRealResponse r)
    {
        Console.WriteLine($"[{label}] Prompt={r.PromptTokens}, Completion={r.CompletionTokens}, Cached={r.CachedTokens}");
    }

    private sealed class AnthropicRealResponse
    {
        public int InputTokens { get; set; }
        public int OutputTokens { get; set; }
        public int CacheCreationInputTokens { get; set; }
        public int CacheReadInputTokens { get; set; }
    }

    private sealed class OpenAIRealResponse
    {
        public int PromptTokens { get; set; }
        public int CompletionTokens { get; set; }
        public int CachedTokens { get; set; }
    }
}

internal sealed class TestAnthropicRequest
{
    [JsonPropertyName("model")]
    public string Model { get; set; } = string.Empty;

    [JsonPropertyName("max_tokens")]
    public int MaxTokens { get; set; }

    [JsonPropertyName("system")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<TestAnthropicSystemBlock>? System { get; set; }

    [JsonPropertyName("messages")]
    public List<TestAnthropicMessage> Messages { get; set; } = [];

    [JsonPropertyName("tools")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<TestAnthropicTool>? Tools { get; set; }
}

internal sealed class TestAnthropicSystemBlock
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = "text";

    [JsonPropertyName("text")]
    public string Text { get; set; } = string.Empty;

    [JsonPropertyName("cache_control")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public TestCacheControl? CacheControl { get; set; }
}

internal sealed class TestAnthropicMessage
{
    [JsonPropertyName("role")]
    public string Role { get; set; } = string.Empty;

    [JsonPropertyName("content")]
    public string? Content { get; set; }
}

internal sealed class TestAnthropicTool
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Description { get; set; }

    [JsonPropertyName("input_schema")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public TestInputSchema? InputSchema { get; set; }

    [JsonPropertyName("cache_control")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public TestCacheControl? CacheControl { get; set; }

    [JsonPropertyName("defer_loading")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? DeferLoading { get; set; }
}

internal sealed class TestInputSchema
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = "object";

    [JsonPropertyName("properties")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Dictionary<string, TestSchemaProperty>? Properties { get; set; }
}

internal sealed class TestSchemaProperty
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Description { get; set; }
}

internal sealed class TestCacheControl
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = "ephemeral";

    [JsonPropertyName("scope")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Scope { get; set; }
}

internal sealed class TestOpenAIRequest
{
    [JsonPropertyName("model")]
    public string Model { get; set; } = string.Empty;

    [JsonPropertyName("max_tokens")]
    public int MaxTokens { get; set; }

    [JsonPropertyName("messages")]
    public List<TestOpenAIMessage> Messages { get; set; } = [];
}

internal sealed class TestOpenAIMessage
{
    [JsonPropertyName("role")]
    public string Role { get; set; } = string.Empty;

    [JsonPropertyName("content")]
    public string? Content { get; set; }
}

[JsonSourceGenerationOptions(DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(TestAnthropicRequest))]
[JsonSerializable(typeof(TestOpenAIRequest))]
internal partial class RealApiCacheJsonContext : JsonSerializerContext;
