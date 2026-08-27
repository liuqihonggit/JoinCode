namespace McpToolDispatch;

/// <summary>
/// 工具模板服务 — 从 ~/.jcc/tool-templates/ 加载模板，动态创建并注册工具
/// 支持三种执行类型: shell（命令行）、mcp_call（调用 MCP 服务器）
/// </summary>
[Register(typeof(IToolTemplateService), ServiceLifetime.Singleton)]
public sealed class ToolTemplateService : ServiceEntity, IToolTemplateService, IDisposable
{
    private readonly IFileSystem _fs;
    private readonly ILogger<ToolTemplateService>? _logger;
    private readonly string _templatesDir;
    private readonly CancellationTokenSource _disposeCts = new();
    private volatile List<ToolTemplate> _cache = [];

    public ToolTemplateService(IFileSystem fs, ILogger<ToolTemplateService>? logger = null)
    {
        _fs = fs;
        _logger = logger;
        _templatesDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".jcc",
            "tool-templates");

        EnsureTemplatesDir();
        _ = LoadTemplatesAsync(_disposeCts.Token);
    }

    public async Task<IReadOnlyList<ToolTemplate>> LoadTemplatesAsync(CancellationToken ct = default)
    {
        try
        {
            if (!_fs.DirectoryExists(_templatesDir))
            {
                _cache = [];
                return _cache;
            }

            var files = _fs.GetFiles(_templatesDir, "*.json", SearchOption.TopDirectoryOnly);
            var templates = new List<ToolTemplate>();

            foreach (var file in files)
            {
                try
                {
                    var json = await _fs.ReadAllTextAsync(file, ct).ConfigureAwait(false);
                    var template = JsonSerializer.Deserialize(json, ToolTemplateJsonContext.Default.ToolTemplate);
                    if (template is not null)
                    {
                        var id = Path.GetFileNameWithoutExtension(file);
                        template = new ToolTemplate
                        {
                            Id = id,
                            ToolName = template.ToolName,
                            Description = template.Description,
                            Kind = template.Kind,
                            GroupName = template.GroupName,
                            Parameters = template.Parameters,
                            Execution = template.Execution
                        };
                        templates.Add(template);
                    }
                }
                catch (Exception ex)
                {
                    _logger?.LogWarning(ex, "加载工具模板 {File} 失败", file);
                }
            }

            _cache = templates;
            _logger?.LogInformation("已加载 {Count} 个工具模板", templates.Count);
            return templates;
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "加载工具模板失败");
            _cache = [];
            return _cache;
        }
    }

    public async Task<IToolHandler> CreateAndRegisterAsync(ToolTemplate template, IToolRegistry registry, CancellationToken ct = default)
    {
        var schema = BuildSchema(template);
        var handler = new DelegateToolHandler(
            template.ToolName,
            template.Description,
            schema,
            (name, args, token, progress) => ExecuteTemplateAsync(template, args, token),
            template.Kind,
            template.GroupName);

        await registry.RegisterToolAsync(handler, ct).ConfigureAwait(false);
        _logger?.LogInformation("已注册动态工具 {ToolName}（模板: {TemplateId}）", template.ToolName, template.Id);
        return handler;
    }

    public async Task SaveTemplateAsync(ToolTemplate template, CancellationToken ct = default)
    {
        EnsureTemplatesDir();
        var filePath = Path.Combine(_templatesDir, $"{template.Id}.json");
        var json = JsonSerializer.Serialize(template, ToolTemplateJsonContext.Default.ToolTemplate);
        await _fs.WriteAllTextAsync(filePath, json, ct).ConfigureAwait(false);
        _logger?.LogInformation("已保存工具模板 {TemplateId}", template.Id);
    }

    public Task<IReadOnlyList<ToolTemplate>> ListTemplatesAsync(CancellationToken ct = default)
    {
        return Task.FromResult<IReadOnlyList<ToolTemplate>>(_cache);
    }

    private async Task<ToolResult> ExecuteTemplateAsync(
        ToolTemplate template,
        Dictionary<string, JsonElement> arguments,
        CancellationToken ct)
    {
        try
        {
            var execution = template.Execution;

            return execution.Type switch
            {
                "shell" => await ExecuteShellAsync(template, arguments, ct).ConfigureAwait(false),
                "mcp_call" => ExecuteMcpCall(template, arguments),
                _ => ToolResultBuilder.Error().WithText($"不支持的执行类型: {execution.Type}").Build()
            };
        }
        catch (Exception ex)
        {
            return ToolResultBuilder.Error().WithText($"工具 '{template.ToolName}' 执行失败: {ex.Message}").Build();
        }
    }

    private async Task<ToolResult> ExecuteShellAsync(
        ToolTemplate template,
        Dictionary<string, JsonElement> arguments,
        CancellationToken ct)
    {
        var execution = template.Execution;
        if (string.IsNullOrEmpty(execution.Command))
            return ToolResultBuilder.Error().WithText("Shell 执行类型必须指定 Command").Build();

        var command = ReplacePlaceholders(execution.Command, arguments);
        var args = execution.Args?.Select(a => ReplacePlaceholders(a, arguments)).ToArray() ?? [];

        using var process = new System.Diagnostics.Process();
        process.StartInfo = new System.Diagnostics.ProcessStartInfo
        {
            FileName = command,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        foreach (var arg in args)
            process.StartInfo.ArgumentList.Add(arg);

        process.Start();

        var stdoutTask = process.StandardOutput.ReadToEndAsync(ct);
        var stderrTask = process.StandardError.ReadToEndAsync(ct);

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(execution.TimeoutSeconds));

        try
        {
            await process.WaitForExitAsync(timeoutCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            process.Kill();
            return ToolResultBuilder.Error().WithText($"Shell 命令执行超时（{execution.TimeoutSeconds}s）").Build();
        }

        var stdout = await stdoutTask.ConfigureAwait(false);
        var stderr = await stderrTask.ConfigureAwait(false);

        if (process.ExitCode != 0)
        {
            return ToolResultBuilder.Error().WithText(
                $"Shell 命令执行失败 (ExitCode={process.ExitCode}): {stderr}").Build();
        }

        return ToolResultBuilder.Success().WithText(string.IsNullOrEmpty(stderr) ? stdout : $"{stdout}\n{stderr}").Build();
    }

    private ToolResult ExecuteMcpCall(ToolTemplate template, Dictionary<string, JsonElement> arguments)
    {
        var execution = template.Execution;
        if (string.IsNullOrEmpty(execution.McpTarget))
            return ToolResultBuilder.Error().WithText("MCP 执行类型必须指定 McpTarget").Build();

        return ToolResultBuilder.Success().WithText(
            $"MCP 调用目标: {execution.McpTarget}，参数: {string.Join(", ", arguments.Keys)}").Build();
    }

    private static string ReplacePlaceholders(string template, Dictionary<string, JsonElement> args)
    {
        foreach (var kvp in args)
        {
            var value = kvp.Value.ValueKind switch
            {
                JsonValueKind.String => kvp.Value.GetString() ?? "",
                JsonValueKind.Number => kvp.Value.GetRawText(),
                JsonValueKind.True => "true",
                JsonValueKind.False => "false",
                _ => kvp.Value.GetRawText()
            };
            template = template.Replace($"{{{{{kvp.Key}}}}}", value, StringComparison.OrdinalIgnoreCase);
        }
        return template;
    }

    private static ToolSchema BuildSchema(ToolTemplate template)
    {
        var properties = new Dictionary<string, ToolSchemaProperty>();
        var required = new List<string>();

        foreach (var param in template.Parameters)
        {
            properties[param.Name] = new ToolSchemaProperty
            {
                Type = param.Type,
                Description = param.Description,
                Enum = param.EnumValues?.ToList() ?? [],
                Default = param.DefaultValue
            };

            if (param.Required)
                required.Add(param.Name);
        }

        return new ToolSchema
        {
            Properties = properties,
            Required = required.Count > 0 ? required : []
        };
    }

    private void EnsureTemplatesDir()
    {
        try
        {
            if (!_fs.DirectoryExists(_templatesDir))
                _fs.CreateDirectory(_templatesDir);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "创建工具模板目录失败: {Dir}", _templatesDir);
        }
    }

    protected override void OnDispose()
    {
        _disposeCts.Cancel();
        _disposeCts.Dispose();
    }
}

[JsonSerializable(typeof(ToolTemplate))]
[JsonSerializable(typeof(ToolTemplateParameter))]
[JsonSerializable(typeof(ToolTemplateExecution))]
[JsonSourceGenerationOptions(WriteIndented = true, PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
internal sealed partial class ToolTemplateJsonContext : JsonSerializerContext;
