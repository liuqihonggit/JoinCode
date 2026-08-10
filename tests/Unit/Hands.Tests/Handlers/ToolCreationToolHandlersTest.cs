namespace Hands.Tests.Handlers;

/// <summary>
/// ToolCreationToolHandlers 单元测试 — 验证参数校验、模板创建、模板列表、模板详情
/// </summary>
public sealed class ToolCreationToolHandlersTest : IAsyncLifetime
{
    private MockTemplateService _templateService = null!;
    private MockToolRegistry _registry = null!;
    private ToolCreationToolHandlers _handlers = null!;

    private static string GetText(ToolResult result) =>
        result.Content.FirstOrDefault(c => c.Type == ToolContentType.Text)?.Text ?? "";

    public Task InitializeAsync()
    {
        _templateService = new MockTemplateService();
        _registry = new MockToolRegistry();
        _handlers = new ToolCreationToolHandlers(_templateService, _registry);
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        await _registry.DisposeAsync();
    }

    [Fact]
    public async Task CreateToolAsync_EmptyName_ReturnsError()
    {
        var result = await _handlers.CreateToolAsync("", "desc", "shell", "echo");
        result.IsError.Should().BeTrue();
        GetText(result).Should().Contain("不能为空");
    }

    [Fact]
    public async Task CreateToolAsync_InvalidNameChars_ReturnsError()
    {
        var result = await _handlers.CreateToolAsync("bad name!", "desc", "shell", "echo");
        result.IsError.Should().BeTrue();
        GetText(result).Should().Contain("字母、数字、下划线和连字符");
    }

    [Fact]
    public async Task CreateToolAsync_ValidShellTool_SavesAndRegisters()
    {
        var result = await _handlers.CreateToolAsync(
            "my_validator", "Validates files", "shell", "echo",
            """[{"name":"path","description":"File path","type":"string","required":true}]""",
            "{{path}}");

        result.IsError.Should().BeFalse();
        GetText(result).Should().Contain("创建成功");
        GetText(result).Should().Contain("my_validator");
        _templateService.SavedTemplates.Should().ContainKey("my_validator");
        _registry.RegisteredHandlers.Should().ContainKey("my_validator");
    }

    [Fact]
    public async Task CreateToolAsync_McpCallTool_SavesWithMcpTarget()
    {
        var result = await _handlers.CreateToolAsync(
            "my_mcp_tool", "Calls MCP", "mcp_call", "server.method");

        result.IsError.Should().BeFalse();
        GetText(result).Should().Contain("创建成功");
        var saved = _templateService.SavedTemplates["my_mcp_tool"];
        saved.Execution.McpTarget.Should().Be("server.method");
        saved.Execution.Command.Should().BeNull();
    }

    [Fact]
    public async Task CreateToolAsync_WithGroupName_SavesGroupName()
    {
        var result = await _handlers.CreateToolAsync(
            "grouped_tool", "Grouped", "shell", "ls", groupName: "my_group");

        result.IsError.Should().BeFalse();
        var saved = _templateService.SavedTemplates["grouped_tool"];
        saved.GroupName.Should().Be("my_group");
    }

    [Fact]
    public async Task CreateToolAsync_WithParameters_ParsesCorrectly()
    {
        var paramsJson = """[{"name":"input","description":"Input param","type":"string","required":true},{"name":"count","description":"Count","type":"integer","required":false}]""";

        var result = await _handlers.CreateToolAsync(
            "param_tool", "Has params", "shell", "echo", paramsJson);

        result.IsError.Should().BeFalse();
        var saved = _templateService.SavedTemplates["param_tool"];
        saved.Parameters.Should().HaveCount(2);
        saved.Parameters[0].Name.Should().Be("input");
        saved.Parameters[0].Required.Should().BeTrue();
        saved.Parameters[1].Name.Should().Be("count");
        saved.Parameters[1].Required.Should().BeFalse();
    }

    [Fact]
    public async Task CreateToolAsync_InvalidParametersJson_SkipsParameters()
    {
        var result = await _handlers.CreateToolAsync(
            "bad_params", "Bad params", "shell", "echo", "not json {{{");

        result.IsError.Should().BeFalse();
        var saved = _templateService.SavedTemplates["bad_params"];
        saved.Parameters.Should().BeEmpty();
    }

    [Fact]
    public async Task CreateToolAsync_RegistrationFails_StillSavesTemplate()
    {
        _registry.ShouldFailRegistration = true;

        var result = await _handlers.CreateToolAsync(
            "fail_reg", "Will fail reg", "shell", "echo");

        result.IsError.Should().BeFalse();
        GetText(result).Should().Contain("已保存");
        GetText(result).Should().Contain("注册到当前会话失败");
        _templateService.SavedTemplates.Should().ContainKey("fail_reg");
    }

    [Fact]
    public async Task ListTemplatesAsync_NoTemplates_ReturnsEmptyMessage()
    {
        var result = await _handlers.ListTemplatesAsync();
        result.IsError.Should().BeFalse();
        GetText(result).Should().Contain("暂无工具模板");
    }

    [Fact]
    public async Task ListTemplatesAsync_WithTemplates_ShowsList()
    {
        await _handlers.CreateToolAsync("tool_a", "Tool A", "shell", "echo");
        await _handlers.CreateToolAsync("tool_b", "Tool B", "mcp_call", "srv.method");

        var result = await _handlers.ListTemplatesAsync();
        result.IsError.Should().BeFalse();
        GetText(result).Should().Contain("tool_a");
        GetText(result).Should().Contain("tool_b");
        GetText(result).Should().Contain("共 2");
    }

    [Fact]
    public async Task ShowTemplateAsync_ExistingTemplate_ShowsDetails()
    {
        await _handlers.CreateToolAsync(
            "detail_tool", "A detailed tool", "shell", "ls -la",
            """[{"name":"dir","description":"Directory","type":"string","required":true}]""",
            "{{dir}}");

        var result = await _handlers.ShowTemplateAsync("detail_tool");
        result.IsError.Should().BeFalse();
        var text = GetText(result);
        text.Should().Contain("detail_tool");
        text.Should().Contain("A detailed tool");
        text.Should().Contain("shell");
        text.Should().Contain("dir");
    }

    [Fact]
    public async Task ShowTemplateAsync_NonExistent_ReturnsError()
    {
        var result = await _handlers.ShowTemplateAsync("nonexistent");
        result.IsError.Should().BeTrue();
        GetText(result).Should().Contain("不存在");
    }

    [Fact]
    public async Task ShowTemplateAsync_SearchById_Works()
    {
        await _handlers.CreateToolAsync("search-tool", "Searchable", "shell", "find");

        var result = await _handlers.ShowTemplateAsync("search_tool");
        result.IsError.Should().BeFalse();
        GetText(result).Should().Contain("search_tool");
    }

    [Fact]
    public async Task ExecuteToolAsync_UnknownTool_ShouldReturnErrorNotThrow()
    {
        var act = async () => await _registry.ExecuteToolAsync("unknown", new Dictionary<string, JsonElement>(), default).ConfigureAwait(true);

        await act.Should().NotThrowAsync().ConfigureAwait(true);
        var result = await _registry.ExecuteToolAsync("unknown", new Dictionary<string, JsonElement>(), default).ConfigureAwait(true);
        result.IsError.Should().BeTrue();
    }

    private sealed class MockTemplateService : IToolTemplateService
    {
        public Dictionary<string, ToolTemplate> SavedTemplates { get; } = new(StringComparer.OrdinalIgnoreCase);
        private List<ToolTemplate> _templates = [];

        public Task<IReadOnlyList<ToolTemplate>> LoadTemplatesAsync(CancellationToken ct = default)
        {
            _templates = [.. SavedTemplates.Values];
            return Task.FromResult<IReadOnlyList<ToolTemplate>>(_templates);
        }

        public Task<IToolHandler> CreateAndRegisterAsync(ToolTemplate template, IToolRegistry registry, CancellationToken ct = default)
        {
            var handler = new DelegateToolHandler(
                template.ToolName, template.Description,
                new ToolSchema(),
                static (name, args, ct, onProgress) => Task.FromResult(new ToolResult
                {
                    Content = [new() { Type = ToolContentType.Text, Text = $"executed {name}" }]
                }),
                ToolKind.Mcp, template.GroupName);

            return registry.RegisterToolAsync(handler, ct).ContinueWith(_ => (IToolHandler)handler, ct);
        }

        public Task SaveTemplateAsync(ToolTemplate template, CancellationToken ct = default)
        {
            SavedTemplates[template.Id] = template;
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<ToolTemplate>> ListTemplatesAsync(CancellationToken ct = default)
        {
            if (_templates.Count == 0 && SavedTemplates.Count > 0)
                _templates = [.. SavedTemplates.Values];
            return Task.FromResult<IReadOnlyList<ToolTemplate>>(_templates);
        }
    }

    private sealed class MockToolRegistry : IToolRegistry
    {
        public Dictionary<string, IToolHandler> RegisteredHandlers { get; } = new(StringComparer.OrdinalIgnoreCase);
        public bool ShouldFailRegistration { get; set; }

        public Task RegisterToolAsync(IToolHandler handler, CancellationToken cancellationToken = default)
        {
            if (ShouldFailRegistration) throw new InvalidOperationException("Mock registration failure");
            RegisteredHandlers[handler.Name] = handler;
            return Task.CompletedTask;
        }

        public Task RegisterToolAsync(string name, string description, ToolSchema inputSchema, ToolHandler handler, CancellationToken cancellationToken = default, ToolKind kind = ToolKind.System, string? groupName = null, ToolTimeoutPolicy? timeoutPolicy = null)
        {
            if (ShouldFailRegistration) throw new InvalidOperationException("Mock registration failure");
            var toolHandler = new DelegateToolHandler(name, description, inputSchema, handler, kind, groupName, timeoutPolicy);
            RegisteredHandlers[name] = toolHandler;
            return Task.CompletedTask;
        }

        public Task<bool> UnregisterToolAsync(string toolName, CancellationToken cancellationToken = default) =>
            Task.FromResult(RegisteredHandlers.Remove(toolName));

        public Task<IToolHandler?> GetToolAsync(string toolName, CancellationToken cancellationToken = default) =>
            Task.FromResult(RegisteredHandlers.GetValueOrDefault(toolName));

        public Task<IReadOnlyDictionary<string, IToolHandler>> GetAllToolsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyDictionary<string, IToolHandler>>(RegisteredHandlers);

        public async Task<ToolResult> ExecuteToolAsync(string toolName, Dictionary<string, JsonElement> arguments, CancellationToken cancellationToken = default, ToolProgressCallback? onProgress = null)
        {
            if (RegisteredHandlers.TryGetValue(toolName, out var handler))
            {
                return await handler.ExecuteAsync(arguments, cancellationToken, onProgress);
            }
            return new ToolResult
            {
                IsError = true,
                Content = [new() { Type = ToolContentType.Text, Text = $"Tool '{toolName}' not found" }]
            };
        }

        public Task<ToolInfo?> GetToolInfoAsync(string toolName, CancellationToken cancellationToken = default) =>
            Task.FromResult<ToolInfo?>(null);

        public Task<IReadOnlyList<ToolInfo>> GetAllToolInfosAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ToolInfo>>([]);

        public Task<bool> ContainsToolAsync(string toolName, CancellationToken cancellationToken = default) =>
            Task.FromResult(RegisteredHandlers.ContainsKey(toolName));

        public Task<int> GetCountAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(RegisteredHandlers.Count);

        public Task ClearAsync(CancellationToken cancellationToken = default)
        {
            RegisteredHandlers.Clear();
            return Task.CompletedTask;
        }

        public Task<FrozenSet<string>> GetGroupNamesAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(RegisteredHandlers.Values
                .Where(h => h.GroupName is not null)
                .Select(h => h.GroupName!)
                .ToFrozenSet(StringComparer.OrdinalIgnoreCase));

        public Task<IReadOnlyDictionary<string, IToolHandler>> GetToolsByKindAsync(ToolKind kind, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyDictionary<string, IToolHandler>>(
                RegisteredHandlers.Where(kvp => kvp.Value.Kind == kind).ToDictionary(kvp => kvp.Key, kvp => kvp.Value));

        public Task<IReadOnlyDictionary<string, IToolHandler>> GetToolsByGroupAsync(string groupName, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyDictionary<string, IToolHandler>>(
                RegisteredHandlers.Where(kvp => kvp.Value.GroupName == groupName).ToDictionary(kvp => kvp.Key, kvp => kvp.Value));

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
