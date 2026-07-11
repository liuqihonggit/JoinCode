namespace Integration.Tests.McpTools;

/// <summary>
/// MCP 工具冒烟测试 — 验证每个已注册工具能被调用且返回有效 ToolResult
/// 不验证工具的业务逻辑，只验证不崩溃
/// </summary>
public sealed class McpToolSmokeTests
{
    private static async Task<(Tools.LocalToolRegistry Registry, IReadOnlyList<ToolInfo> Tools)> BuildAndRegisterAllToolsAsync()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"jcc-test-{Guid.NewGuid():N}");
        var fileSystem = new IO.FileSystem.InMemoryFileSystem();
        fileSystem.CreateDirectory(tempDir);
        Environment.SetEnvironmentVariable(JccEnvVarConstants.AppDataFolder, tempDir);

        var services = new ServiceCollection();
        services.AddLogging(b => b.AddConsole().SetMinimumLevel(LogLevel.Warning));
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddSingleton<IFileSystem>(fileSystem);

        var config = new WorkflowConfig();
        services.AddWorkflowServices(config);
        services.AddTestPipelines();

        var provider = services.BuildServiceProvider();

        var mcpService = provider.GetRequiredService<IMcpService>();
        await mcpService.InitializeAsync(provider).ConfigureAwait(true);

        var toolRegistry = provider.GetRequiredService<IMcpToolRegistry>();
        var allTools = await toolRegistry.GetAllToolsAsync().ConfigureAwait(true);

        var registry = provider.GetRequiredService<Tools.LocalToolRegistry>();
        var toolInfos = await registry.GetAllToolInfosAsync().ConfigureAwait(true);
        return (registry, toolInfos);
    }

    /// <summary>
    /// 为工具构造最小参数集 — 对必需参数提供最小有效值
    /// </summary>
    private static Dictionary<string, JsonElement> BuildMinimalArguments(ToolInfo tool)
    {
        var args = new Dictionary<string, JsonElement>();

        if (tool.InputSchema?.Required == null) return args;

        foreach (var reqParam in tool.InputSchema.Required)
        {
            if (!tool.InputSchema.Properties.TryGetValue(reqParam, out var prop)) continue;

            var value = prop.Type switch
            {
                "string" => reqParam switch
                {
                    "file_path" or "path" => JsonDocument.Parse("\"test.txt\"").RootElement.Clone(),
                    "directory_path" => JsonDocument.Parse("\".\"").RootElement.Clone(),
                    "command" => JsonDocument.Parse("\"echo hello\"").RootElement.Clone(),
                    "pattern" => JsonDocument.Parse("\"*.txt\"").RootElement.Clone(),
                    "query" => JsonDocument.Parse("\"test\"").RootElement.Clone(),
                    "url" => JsonDocument.Parse("\"http://localhost\"").RootElement.Clone(),
                    "prompt" => JsonDocument.Parse("\"test\"").RootElement.Clone(),
                    "code" => JsonDocument.Parse("\"var x = 1;\"").RootElement.Clone(),
                    "expression" => JsonDocument.Parse("\"1+1\"").RootElement.Clone(),
                    "schema_name" => JsonDocument.Parse("\"test_schema\"").RootElement.Clone(),
                    "schema_json" => JsonDocument.Parse("\"{}\"").RootElement.Clone(),
                    "action" => JsonDocument.Parse("\"list\"").RootElement.Clone(),
                    "target" => JsonDocument.Parse("\"test\"").RootElement.Clone(),
                    "confirm" => JsonDocument.Parse("\"yes\"").RootElement.Clone(),
                    "agent_pattern" => JsonDocument.Parse("\"*\"").RootElement.Clone(),
                    "agent_name" => JsonDocument.Parse("\"test\"").RootElement.Clone(),
                    "cassette_name" => JsonDocument.Parse("\"test\"").RootElement.Clone(),
                    "client_id" => JsonDocument.Parse("\"test\"").RootElement.Clone(),
                    "tool_name" => JsonDocument.Parse("\"test\"").RootElement.Clone(),
                    "resource_uri" => JsonDocument.Parse("\"test://resource\"").RootElement.Clone(),
                    "auth_id" => JsonDocument.Parse("\"test\"").RootElement.Clone(),
                    "api_key" => JsonDocument.Parse("\"test-key\"").RootElement.Clone(),
                    "token" => JsonDocument.Parse("\"test-token\"").RootElement.Clone(),
                    "username" => JsonDocument.Parse("\"test\"").RootElement.Clone(),
                    "password" => JsonDocument.Parse("\"test\"").RootElement.Clone(),
                    "refresh_token" => JsonDocument.Parse("\"test\"").RootElement.Clone(),
                    "client_secret" => JsonDocument.Parse("\"test\"").RootElement.Clone(),
                    "auth_url" => JsonDocument.Parse("\"http://localhost/auth\"").RootElement.Clone(),
                    "token_url" => JsonDocument.Parse("\"http://localhost/token\"").RootElement.Clone(),
                    _ => JsonDocument.Parse("\"test\"").RootElement.Clone(),
                },
                "integer" => JsonDocument.Parse("1").RootElement.Clone(),
                "boolean" => JsonDocument.Parse("false").RootElement.Clone(),
                "array" => JsonDocument.Parse("[]").RootElement.Clone(),
                "object" => JsonDocument.Parse("{}").RootElement.Clone(),
                _ => JsonDocument.Parse("\"test\"").RootElement.Clone()
            };

            args[reqParam] = value;
        }

        return args;
    }

    [Fact]
    public async Task All_Registered_Tools_Can_Be_Called_Without_Crash()
    {
        var (registry, allTools) = await BuildAndRegisterAllToolsAsync().ConfigureAwait(true);

        allTools.Should().NotBeEmpty("至少应注册一个工具");

        var results = new List<(string ToolName, bool IsError, string? ErrorMessage)>();
        var crashed = new List<(string ToolName, Exception Ex)>();

        foreach (var tool in allTools.OrderBy(t => t.Name))
        {
            try
            {
                var args = BuildMinimalArguments(tool);
                var result = await registry.ExecuteToolAsync(tool.Name, args, CancellationToken.None).ConfigureAwait(true);

                result.Should().NotBeNull($"工具 {tool.Name} 返回 null");
                result.Content.Should().NotBeNull($"工具 {tool.Name} 返回 null Content");

                results.Add((tool.Name, result.IsError, result.IsError ? result.Content.FirstOrDefault()?.Text : null));
            }
            catch (Exception ex)
            {
                crashed.Add((tool.Name, ex));
                results.Add((tool.Name, true, $"CRASH: {ex.GetType().Name}: {ex.Message}"));
            }
        }

        // 生成报告
        var report = new StringBuilder();
        report.AppendLine("# MCP 工具冒烟测试报告");
        report.AppendLine($"时间: {DateTimeOffset.UtcNow:O}");
        report.AppendLine($"工具总数: {allTools.Count}");
        report.AppendLine($"成功: {results.Count(r => !r.IsError)}");
        report.AppendLine($"错误(非崩溃): {results.Count(r => r.IsError && !r.ErrorMessage?.StartsWith("CRASH") == true)}");
        report.AppendLine($"崩溃: {crashed.Count}");
        report.AppendLine();

        report.AppendLine("## 详细结果");
        foreach (var (toolName, isError, errorMessage) in results.OrderBy(r => r.ToolName))
        {
            var status = isError ? (errorMessage?.StartsWith("CRASH") == true ? "CRASH" : "ERROR") : "OK";
            var truncatedError = errorMessage switch
            {
                null => "",
                _ when errorMessage.Length > 100 => errorMessage[..100] + "...",
                _ => errorMessage
            };
            report.AppendLine($"| {status} | {toolName} | {truncatedError} |");
        }

        Console.WriteLine(report.ToString());

        // 崩溃的工具应该为 0（工具可以返回错误，但不能崩溃）
        crashed.Should().BeEmpty($"以下工具崩溃: {string.Join(", ", crashed.Select(c => c.ToolName))}");
    }

    [Fact]
    public async Task Git_Status_Tool_Returns_Valid_Result()
    {
        var (registry, _) = await BuildAndRegisterAllToolsAsync().ConfigureAwait(true);

        var result = await registry.ExecuteToolAsync("git_status", new Dictionary<string, JsonElement>(), CancellationToken.None).ConfigureAwait(true);

        result.Should().NotBeNull();
        result.Content.Should().NotBeNull();
        // git_status 在非 git 仓库中可能返回错误，但不应该崩溃
    }

    [Fact]
    public async Task Sleep_Tool_Returns_Valid_Result()
    {
        var (registry, _) = await BuildAndRegisterAllToolsAsync().ConfigureAwait(true);

        var args = new Dictionary<string, JsonElement>
        {
            ["duration_seconds"] = JsonDocument.Parse("1").RootElement.Clone()
        };

        var result = await registry.ExecuteToolAsync("sleep", args, CancellationToken.None).ConfigureAwait(true);

        result.Should().NotBeNull();
        result.Content.Should().NotBeNull();
        // Sleep 工具可能因测试环境限制返回错误，但不应该崩溃
    }

    [Fact]
    public async Task Mcp_Auth_Status_Tool_Returns_Valid_Result()
    {
        var (registry, _) = await BuildAndRegisterAllToolsAsync().ConfigureAwait(true);

        var result = await registry.ExecuteToolAsync("mcp_auth_status", new Dictionary<string, JsonElement>(), CancellationToken.None).ConfigureAwait(true);

        result.Should().NotBeNull();
        result.Content.Should().NotBeNull();
    }

    [Fact]
    public async Task Unknown_Tool_Returns_Error_Or_Throws()
    {
        var (registry, _) = await BuildAndRegisterAllToolsAsync().ConfigureAwait(true);

        // 调用不存在的工具 — 注册表可能抛出异常或返回错误结果，不能静默成功
        Exception? caughtEx = null;
        ToolResult? result = null;
        try
        {
            result = await registry.ExecuteToolAsync("nonexistent_tool", new Dictionary<string, JsonElement>(), CancellationToken.None).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            // 抛出异常也是可接受的行为 — 记录以供断言
            caughtEx = ex;
        }

        if (caughtEx is not null)
        {
            // 抛出异常 — 验证通过
            return;
        }

        result.Should().NotBeNull();
        result!.IsError.Should().BeTrue("不存在的工具应该返回错误结果");
    }
}
