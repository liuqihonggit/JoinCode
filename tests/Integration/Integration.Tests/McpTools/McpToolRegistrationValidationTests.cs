namespace Integration.Tests.McpTools;

public sealed class McpToolRegistrationValidationTests
{
    private static async Task<(Tools.LocalToolRegistry Registry, IReadOnlyList<ToolInfo> Tools, List<string> RegistrationFailures)> BuildAndRegisterAllToolsAsync()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"jcc-test-{Guid.NewGuid():N}");
        var fileSystem = new IO.FileSystem.InMemoryFileSystem();
        fileSystem.CreateDirectory(tempDir);
        Environment.SetEnvironmentVariable(JccEnvVarConstants.AppDataFolder, tempDir);

        var services = new ServiceCollection();
        services.AddLogging(b => b.AddConsole().SetMinimumLevel(LogLevel.Debug));
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddSingleton<IFileSystem>(fileSystem);

        var config = new WorkflowConfig();
        services.AddWorkflowServices(config);
        services.AddTestPipelines();

        var provider = services.BuildServiceProvider();

        var mcpService = provider.GetRequiredService<IMcpService>();
        await mcpService.InitializeAsync(provider).ConfigureAwait(true);

        var registry = provider.GetRequiredService<Tools.LocalToolRegistry>();
        var allTools = await registry.GetAllToolInfosAsync().ConfigureAwait(true);

        var handlerTypes = typeof(McpToolHandlers.ToolHandlerExtensions).Assembly.GetTypes()
            .Where(t => t.GetCustomAttributes(typeof(JoinCode.Abstractions.Attributes.McpToolHandlerAttribute), false).Length > 0)
            .ToList();

        var registrationFailures = new List<string>();

        foreach (var handlerType in handlerTypes)
        {
            try
            {
                var handler = provider.GetService(handlerType);
                if (handler is null)
                    registrationFailures.Add($"{handlerType.Name}: GetService returned null (missing DI dependencies)");
            }
            catch (Exception ex)
            {
                registrationFailures.Add($"{handlerType.Name}: {ex.InnerException?.Message ?? ex.Message}");
            }
        }

        return (registry, allTools, registrationFailures);
    }

    [Fact]
    public async Task All_Tools_Should_Register_Successfully()
    {
        var (_, allTools, registrationErrors) = await BuildAndRegisterAllToolsAsync().ConfigureAwait(true);

        allTools.Should().NotBeEmpty("至少应注册一个工具");

        var report = new StringBuilder();
        report.AppendLine("# MCP 工具注册验证报告");
        report.AppendLine($"时间: {DateTimeOffset.UtcNow:O}");
        report.AppendLine($"工具总数: {allTools.Count}");
        report.AppendLine();

        var errors = new List<string>();
        var warnings = new List<string>();

        foreach (var tool in allTools.OrderBy(t => t.Name))
        {
            var toolErrors = new List<string>();
            var toolWarnings = new List<string>();

            if (string.IsNullOrWhiteSpace(tool.Name))
                toolErrors.Add("工具名为空");
            if (string.IsNullOrWhiteSpace(tool.Description))
                toolWarnings.Add($"工具 '{tool.Name}' 缺少描述");
            if (tool.InputSchema is not { Properties: not null })
                toolErrors.Add($"工具 '{tool.Name}' InputSchema 为 null");
            else if (tool.InputSchema.Properties.Count == 0)
                toolWarnings.Add($"工具 '{tool.Name}' 没有定义任何参数");

            if (tool.InputSchema?.Required != null)
            {
                foreach (var req in tool.InputSchema.Required)
                {
                    if (!tool.InputSchema.Properties!.ContainsKey(req))
                        toolErrors.Add($"工具 '{tool.Name}' Required 参数 '{req}' 不在 Properties 中");
                }
            }

            if (tool.InputSchema?.Properties != null)
            {
                foreach (var prop in tool.InputSchema.Properties)
                {
                    if (string.IsNullOrWhiteSpace(prop.Key))
                        toolErrors.Add($"工具 '{tool.Name}' 有空属性名");
                    if (string.IsNullOrWhiteSpace(prop.Value.Description))
                        toolWarnings.Add($"工具 '{tool.Name}' 参数 '{prop.Key}' 缺少描述");
                }
            }

            var status = toolErrors.Count > 0 ? "ERROR" : toolWarnings.Count > 0 ? "WARN" : "OK";
            report.AppendLine($"| {status} | {tool.Name} | {tool.Description?.Length ?? 0} chars | {tool.InputSchema?.Properties?.Count ?? 0} params | {tool.InputSchema?.Required?.Count ?? 0} required | {toolErrors.Count} err | {toolWarnings.Count} warn |");

            errors.AddRange(toolErrors);
            warnings.AddRange(toolWarnings);
        }

        report.AppendLine();
        report.AppendLine("## 汇总");
        report.AppendLine($"- 工具总数: {allTools.Count}");
        report.AppendLine($"- 错误数: {errors.Count}");
        report.AppendLine($"- 警告数: {warnings.Count}");

        if (errors.Count > 0)
        {
            report.AppendLine();
            report.AppendLine("## 错误详情");
            foreach (var e in errors)
                report.AppendLine($"- {e}");
        }

        if (warnings.Count > 0)
        {
            report.AppendLine();
            report.AppendLine("## 警告详情（前50条）");
            foreach (var w in warnings.Take(50))
                report.AppendLine($"- {w}");
        }

        Console.WriteLine(report.ToString());

        if (registrationErrors.Count > 0)
        {
            Console.WriteLine();
            Console.WriteLine("## Handler 注册失败详情");
            foreach (var e in registrationErrors)
                Console.WriteLine($"- {e}");
        }

        errors.Should().BeEmpty($"工具注册验证发现 {errors.Count} 个错误");
    }

    [Fact]
    public async Task No_Duplicate_Tool_Names()
    {
        var (_, allTools, _) = await BuildAndRegisterAllToolsAsync().ConfigureAwait(true);
        var names = allTools.Select(t => t.Name).ToList();
        var duplicates = names.GroupBy(n => n).Where(g => g.Count() > 1).Select(g => g.Key).ToList();

        duplicates.Should().BeEmpty($"发现重复工具名: {string.Join(", ", duplicates)}");
    }

    [Fact]
    public async Task All_Required_Parameters_Have_Descriptions()
    {
        var (_, allTools, _) = await BuildAndRegisterAllToolsAsync().ConfigureAwait(true);
        var missingDescParams = new List<string>();

        foreach (var tool in allTools)
        {
            if (tool.InputSchema?.Required == null) continue;
            foreach (var reqParam in tool.InputSchema.Required)
            {
                if (tool.InputSchema.Properties.TryGetValue(reqParam, out var prop) && string.IsNullOrWhiteSpace(prop.Description))
                    missingDescParams.Add($"{tool.Name}.{reqParam}");
            }
        }

        missingDescParams.Should().BeEmpty($"以下必需参数缺少描述: {string.Join(", ", missingDescParams.Take(20))}");
    }
}
