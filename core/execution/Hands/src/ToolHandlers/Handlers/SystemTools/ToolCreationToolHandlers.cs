namespace Tools.Handlers;

/// <summary>
/// 工具制造工具 — 让LLM在运行时发现缺失工具后动态创建新工具模板
/// LLM遍历某个组工具后发现缺失，调用此工具创建模板，下次会话即可使用
/// 模板保存到 ~/.jcc/tool-templates/ 目录
/// </summary>
[McpToolDispatch(ToolCategory.Skill)]
public class ToolCreationToolHandlers
{
    private readonly IToolTemplateService _templateService;
    private readonly IToolRegistry _registry;
    private readonly ILogger<ToolCreationToolHandlers>? _logger;

    public ToolCreationToolHandlers(
        IToolTemplateService templateService,
        IToolRegistry registry,
        ILogger<ToolCreationToolHandlers>? logger = null)
    {
        _templateService = templateService;
        _registry = registry;
        _logger = logger;
    }

    /// <summary>
    /// 创建工具模板 — LLM定义工具名称、描述、参数和执行方式，保存到 ~/.jcc/tool-templates/
    /// 创建后自动注册到当前会话，立即可用
    /// </summary>
    [McpTool("tool_create", "创建新的工具模板并注册到当前会话。当您发现缺少某个工具时，使用此工具动态创建。", "tool_creation",
        ConcurrencySafe = true)]
    public async Task<ToolResult> CreateToolAsync(
        [McpToolParameter("工具名称（小写+下划线，如 my_validator）", Required = true)] string toolName,
        [McpToolParameter("工具描述（LLM看到的功能说明）", Required = true)] string description,
        [McpToolParameter("执行类型: shell 或 mcp_call", Required = true)] string executionType,
        [McpToolParameter("执行命令或MCP目标（shell: 命令路径; mcp_call: 服务器.方法）", Required = true)] string command,
        [McpToolParameter("参数定义JSON数组，格式: [{\"name\":\"param1\",\"description\":\"参数1\",\"type\":\"string\",\"required\":true}]", Required = false)] string? parametersJson = null,
        [McpToolParameter("命令参数模板（shell类型），支持{{param}}占位符", Required = false)] string? argsTemplate = null,
        [McpToolParameter("二级分组名", Required = false)] string? groupName = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(toolName) || string.IsNullOrWhiteSpace(description))
        {
            var diag = BuildEmptyNameOrDescriptionDiagnostic();
            return ToolResultBuilder.Error().WithText(diag.FormattedMessage).WithDiagnostic(diag).Build();
        }

        if (toolName.Any(c => !char.IsLetterOrDigit(c) && c != '_' && c != '-'))
        {
            var diag = BuildInvalidToolNameDiagnostic(toolName);
            return ToolResultBuilder.Error().WithText(diag.FormattedMessage).WithDiagnostic(diag).Build();
        }

        var parameters = ParseParameters(parametersJson);
        var templateId = toolName.Replace("-", "_");

        var template = new ToolTemplate
        {
            Id = templateId,
            ToolName = toolName,
            Description = description,
            Kind = ToolKind.Mcp,
            GroupName = groupName,
            Parameters = parameters,
            Execution = new ToolTemplateExecution
            {
                Type = executionType,
                Command = executionType == "shell" ? command : null,
                McpTarget = executionType == "mcp_call" ? command : null,
                Args = ParseArgsTemplate(argsTemplate),
                TimeoutSeconds = 30
            }
        };

        await _templateService.SaveTemplateAsync(template, ct).ConfigureAwait(false);

        try
        {
            await _templateService.CreateAndRegisterAsync(template, _registry, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "动态注册工具 {ToolName} 失败，但模板已保存", toolName);
            return ToolResultBuilder.Success().WithText(
                $"工具模板 '{toolName}' 已保存到 ~/.jcc/tool-templates/{templateId}.json，" +
                $"但注册到当前会话失败: {ex.Message}。下次启动时将自动加载。").Build();
        }

        var paramList = parameters.Length > 0
            ? string.Join(", ", parameters.Select(p => $"{p.Name}({p.Type})"))
            : "无参数";

        return ToolResultBuilder.Success().WithText(
            $"工具 '{toolName}' 创建成功！\n" +
            $"- 描述: {description}\n" +
            $"- 执行类型: {executionType}\n" +
            $"- 命令: {command}\n" +
            $"- 参数: {paramList}\n" +
            $"- 模板已保存: ~/.jcc/tool-templates/{templateId}.json\n" +
            $"- 已注册到当前会话，可立即使用").Build();
    }

    /// <summary>
    /// 列出所有工具模板 — 查看已创建的动态工具
    /// </summary>
    [McpTool("tool_list_templates", "列出所有已创建的工具模板", "tool_creation",
        ConcurrencySafe = true)]
    public async Task<ToolResult> ListTemplatesAsync(CancellationToken ct = default)
    {
        var templates = await _templateService.ListTemplatesAsync(ct).ConfigureAwait(false);

        if (templates.Count == 0)
            return ToolResultBuilder.Success().WithText("暂无工具模板。使用 tool_create 创建新工具。").Build();

        var sb = new StringBuilder(512);
        sb.AppendLine($"共 {templates.Count} 个工具模板：");
        foreach (var t in templates)
        {
            sb.AppendLine($"- {t.ToolName}: {t.Description} ({t.Execution.Type})");
        }

        return ToolResultBuilder.Success().WithText(sb.ToString().TrimEnd()).Build();
    }

    /// <summary>
    /// 查看工具模板详情 — 查看某个模板的完整定义
    /// </summary>
    [McpTool("tool_show_template", "查看工具模板的完整定义", "tool_creation",
        ConcurrencySafe = true)]
    public async Task<ToolResult> ShowTemplateAsync(
        [McpToolParameter("模板ID或工具名称", Required = true)] string templateId,
        CancellationToken ct = default)
    {
        var templates = await _templateService.ListTemplatesAsync(ct).ConfigureAwait(false);
        var template = templates.FirstOrDefault(t =>
            string.Equals(t.Id, templateId, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(t.ToolName, templateId, StringComparison.OrdinalIgnoreCase));

        if (template is null)
        {
            var diag = BuildTemplateNotFoundDiagnostic(templateId);
            return ToolResultBuilder.Error().WithText(diag.FormattedMessage).WithDiagnostic(diag).Build();
        }

        var sb = new StringBuilder(512);
        sb.AppendLine($"## 工具模板: {template.ToolName}");
        sb.AppendLine($"- ID: {template.Id}");
        sb.AppendLine($"- 描述: {template.Description}");
        sb.AppendLine($"- 类型: {template.Kind}");
        sb.AppendLine($"- 分组: {template.GroupName ?? "(无)"}");
        sb.AppendLine($"- 执行类型: {template.Execution.Type}");
        sb.AppendLine($"- 命令: {template.Execution.Command ?? template.Execution.McpTarget ?? "(无)"}");
        sb.AppendLine($"- 超时: {template.Execution.TimeoutSeconds}s");
        sb.AppendLine("### 参数:");
        if (template.Parameters.Length == 0)
        {
            sb.AppendLine("(无参数)");
        }
        else
        {
            foreach (var p in template.Parameters)
            {
                var required = p.Required ? "必填" : "可选";
                sb.AppendLine($"- {p.Name} ({p.Type}, {required}): {p.Description}");
            }
        }

        return ToolResultBuilder.Success().WithText(sb.ToString().TrimEnd()).Build();
    }

    private static ToolTemplateParameter[] ParseParameters(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return [];

        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Array) return [];

            var result = new List<ToolTemplateParameter>();
            foreach (var e in root.EnumerateArray())
            {
                var name = e.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
                if (string.IsNullOrEmpty(name)) continue;

                result.Add(new ToolTemplateParameter
                {
                    Name = name,
                    Description = e.TryGetProperty("description", out var d) ? d.GetString() ?? "" : "",
                    Type = e.TryGetProperty("type", out var t) ? t.GetString() ?? "string" : "string",
                    Required = !e.TryGetProperty("required", out var r) || r.GetBoolean(),
                    DefaultValue = e.TryGetProperty("default", out var dv) ? dv.GetRawText() : null,
                    EnumValues = e.TryGetProperty("enum", out var ev)
                        ? ev.EnumerateArray().Select(v => v.GetString() ?? "").Where(s => s.Length > 0).ToArray()
                        : null
                });
            }

            return result.ToArray();
        }
        catch
        {
            return [];
        }
    }

    private static string[]? ParseArgsTemplate(string? argsTemplate)
    {
        if (string.IsNullOrWhiteSpace(argsTemplate)) return null;
        return argsTemplate.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    internal static ToolDiagnostic BuildEmptyNameOrDescriptionDiagnostic() =>
        ToolDiagnostic.Create(
            reason: "参数验证失败",
            formattedMessage: "工具名称和描述不能为空",
            details: [new DiagnosticDetail("fields", "toolName, description")],
            suggestions: ["提供非空的工具名称和描述"]);

    internal static ToolDiagnostic BuildInvalidToolNameDiagnostic(string toolName) =>
        ToolDiagnostic.Create(
            reason: "参数验证失败",
            formattedMessage: "工具名称只能包含字母、数字、下划线和连字符",
            details: [new DiagnosticDetail("tool_name", toolName)],
            suggestions: ["使用小写字母、数字、下划线和连字符命名工具"]);

    internal static ToolDiagnostic BuildTemplateNotFoundDiagnostic(string templateId) =>
        ToolDiagnostic.Create(
            reason: "模板未找到",
            formattedMessage: $"模板 '{templateId}' 不存在",
            details: [new DiagnosticDetail("template_id", templateId)],
            suggestions: ["使用 tool_list_templates 查看所有可用模板"]);
}
