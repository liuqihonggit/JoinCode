
namespace Hands.Tests.Tools;

public sealed class LocalToolRegistryTests : IAsyncDisposable {
    private readonly LocalToolRegistry _registry = new();
    private EventHandler<ToolRegisteredEventArgs>? _onToolRegistered;
    private EventHandler<ToolUnregisteredEventArgs>? _onToolUnregistered;
    private EventHandler? _onToolsCleared;
    private bool _disposed;

    private static IToolHandler CreateHandler(
        string name, string description, Func<Dictionary<string, JsonElement>, Task<ToolResult>>? execute = null) {
        execute ??= _ => Task.FromResult(new ToolResult {
            Content = new List<ToolContent> { new() { Type = ToolContentType.Text, Text = "ok" } }
        });

        return new DelegateToolHandler(name, description, new ToolSchema(), (toolName, args, ct, progress) => execute(args));
    }

    public ValueTask DisposeAsync() {
        if (_disposed) return ValueTask.CompletedTask;
        _disposed = true;
        if (_onToolRegistered is not null) _registry.ToolRegistered -= _onToolRegistered;
        if (_onToolUnregistered is not null) _registry.ToolUnregistered -= _onToolUnregistered;
        if (_onToolsCleared is not null) _registry.ToolsCleared -= _onToolsCleared;
        return _registry.DisposeAsync();
    }

    [Fact]
    public async Task Registry_Creation_ShouldBeEmpty() {
        var count = await _registry.GetCountAsync();
        count.Should().Be(0);
    }

    [Fact]
    public async Task RegisterTool_ShouldIncrementCount() {
        await _registry.RegisterToolAsync(CreateHandler("file_read", "Read file contents"));
        var count = await _registry.GetCountAsync();
        count.Should().Be(1);
    }

    [Fact]
    public async Task RegisterTool_ShouldBeRetrievable() {
        await _registry.RegisterToolAsync(CreateHandler("file_read", "Read file contents"));
        var handler = await _registry.GetToolAsync("file_read");

        handler.Should().NotBeNull();
        handler!.Name.Should().Be("file_read");
        handler.Description.Should().Be("Read file contents");
    }

    [Fact]
    public async Task RegisterTool_ShouldFireToolRegisteredEvent() {
        string? registeredName = null;
        _onToolRegistered = (_, e) => registeredName = e.ToolName;
        _registry.ToolRegistered += _onToolRegistered;

        await _registry.RegisterToolAsync(CreateHandler("file_read", "Read file contents"));

        registeredName.Should().Be("file_read");
    }

    [Fact]
    public async Task RegisterTool_DuplicateName_ShouldOverwrite() {
        await _registry.RegisterToolAsync(CreateHandler("tool1", "v1"));
        await _registry.RegisterToolAsync(CreateHandler("tool1", "v2"));

        var count = await _registry.GetCountAsync();
        count.Should().Be(1);

        var handler = await _registry.GetToolAsync("tool1");
        handler!.Description.Should().Be("v2");
    }

    [Fact]
    public async Task UnregisterTool_ShouldDecrementCount() {
        await _registry.RegisterToolAsync(CreateHandler("tool1", "desc"));
        var removed = await _registry.UnregisterToolAsync("tool1");

        removed.Should().BeTrue();
        var count = await _registry.GetCountAsync();
        count.Should().Be(0);
    }

    [Fact]
    public async Task UnregisterTool_NonExistent_ShouldReturnFalse() {
        var removed = await _registry.UnregisterToolAsync("nonexistent");
        removed.Should().BeFalse();
    }

    [Fact]
    public async Task UnregisterTool_ShouldFireToolUnregisteredEvent() {
        string? unregisteredName = null;
        _onToolUnregistered = (_, e) => unregisteredName = e.ToolName;
        _registry.ToolUnregistered += _onToolUnregistered;

        await _registry.RegisterToolAsync(CreateHandler("tool1", "desc"));
        await _registry.UnregisterToolAsync("tool1");

        unregisteredName.Should().Be("tool1");
    }

    [Fact]
    public async Task GetTool_NonExistent_ShouldReturnNull() {
        var handler = await _registry.GetToolAsync("nonexistent");
        handler.Should().BeNull();
    }

    [Fact]
    public async Task ContainsTool_ShouldReturnCorrectResult() {
        await _registry.RegisterToolAsync(CreateHandler("tool1", "desc"));

        (await _registry.ContainsToolAsync("tool1")).Should().BeTrue();
        (await _registry.ContainsToolAsync("nonexistent")).Should().BeFalse();
    }

    [Fact]
    public async Task GetAllTools_ShouldReturnAllRegistered() {
        await _registry.RegisterToolAsync(CreateHandler("file_read", "Read"));
        await _registry.RegisterToolAsync(CreateHandler("file_write", "Write"));
        await _registry.RegisterToolAsync(CreateHandler("execute_command", "Execute"));

        var all = await _registry.GetAllToolsAsync();
        all.Should().HaveCount(3);
        all.Keys.Should().Contain("file_read", "file_write", "execute_command");
    }

    [Fact]
    public async Task GetAllToolInfos_ShouldReturnCorrectInfos() {
        await _registry.RegisterToolAsync(CreateHandler("file_read", "Read file contents"));

        var infos = await _registry.GetAllToolInfosAsync();
        infos.Should().HaveCount(1);
        infos[0].Name.Should().Be("file_read");
        infos[0].Description.Should().Be("Read file contents");
    }

    [Fact]
    public async Task GetToolInfo_ShouldReturnCorrectInfo() {
        await _registry.RegisterToolAsync(CreateHandler("git_operations", "Git operations tool"));

        var info = await _registry.GetToolInfoAsync("git_operations");
        info.Should().NotBeNull();
        info!.Name.Should().Be("git_operations");
        info.Description.Should().Contain("Git");
    }

    [Fact]
    public async Task ExecuteTool_ShouldReturnSuccess() {
        await _registry.RegisterToolAsync(CreateHandler("echo", "Echo tool", args => {
            var text = args.TryGetValue("text", out var v) ? v.GetString() : "default";
            return Task.FromResult(new ToolResult {
                Content = new List<ToolContent> { new() { Type = ToolContentType.Text, Text = text ?? "default" } }
            });
        }));

        var args = new Dictionary<string, JsonElement> {
            ["text"] = JsonSerializer.SerializeToElement("hello")
        };

        var result = await _registry.ExecuteToolAsync("echo", args);
        result.IsError.Should().BeFalse();
        result.GetTextContent().Should().Be("hello");
    }

    [Fact]
    public async Task ExecuteTool_NonExistent_ShouldReturnError() {
        var result = await _registry.ExecuteToolAsync("nonexistent", new Dictionary<string, JsonElement>());
        result.IsError.Should().BeTrue();
        result.GetTextContent().Should().Contain("not found");
    }

    [Fact]
    public async Task ExecuteTool_HandlerThrows_ShouldReturnError() {
        await _registry.RegisterToolAsync(CreateHandler("failing", "Fails", _ =>
            throw new InvalidOperationException("boom")));

        var result = await _registry.ExecuteToolAsync("failing", new Dictionary<string, JsonElement>());
        result.IsError.Should().BeTrue();
        result.GetTextContent().Should().Contain("boom");
    }

    [Fact]
    public async Task Clear_ShouldRemoveAllTools() {
        await _registry.RegisterToolAsync(CreateHandler("tool1", "d1"));
        await _registry.RegisterToolAsync(CreateHandler("tool2", "d2"));

        await _registry.ClearAsync();

        var count = await _registry.GetCountAsync();
        count.Should().Be(0);
    }

    [Fact]
    public async Task Clear_ShouldFireToolsClearedEvent() {
        var fired = false;
        _onToolsCleared = (_, _) => fired = true;
        _registry.ToolsCleared += _onToolsCleared;

        await _registry.RegisterToolAsync(CreateHandler("tool1", "d1"));
        await _registry.ClearAsync();

        fired.Should().BeTrue();
    }

    [Fact]
    public async Task RegisterTool_WithDelegateOverload_ShouldWork() {
        await _registry.RegisterToolAsync(
            "delegate_tool",
            "Delegate tool",
            new ToolSchema(),
            (name, args, ct, progress) => Task.FromResult(new ToolResult {
                Content = new List<ToolContent> { new() { Type = ToolContentType.Text, Text = "delegate result" } }
            }));

        var handler = await _registry.GetToolAsync("delegate_tool");
        handler.Should().NotBeNull();
        handler!.Name.Should().Be("delegate_tool");
    }

    [Fact]
    public async Task MultipleToolsRegistration_ShouldTrackAll() {
        var toolNames = new[] { "file_read", "file_edit", "file_write", "execute_command", "search", "list_files" };

        foreach (var name in toolNames) {
            await _registry.RegisterToolAsync(CreateHandler(name, $"{name} description"));
        }

        var count = await _registry.GetCountAsync();
        count.Should().Be(toolNames.Length);

        foreach (var name in toolNames) {
            (await _registry.ContainsToolAsync(name)).Should().BeTrue($"tool '{name}' should be registered");
        }
    }

    [Fact]
    public async Task RegisterToolsBatch_ShouldRegisterAllInOneLock() {
        var handlers = new List<IToolHandler> {
            CreateHandler("batch_1", "desc1"),
            CreateHandler("batch_2", "desc2"),
            CreateHandler("batch_3", "desc3"),
        };

        await _registry.RegisterToolsBatchAsync(handlers);

        var count = await _registry.GetCountAsync();
        count.Should().Be(3);
        (await _registry.ContainsToolAsync("batch_1")).Should().BeTrue();
        (await _registry.ContainsToolAsync("batch_2")).Should().BeTrue();
        (await _registry.ContainsToolAsync("batch_3")).Should().BeTrue();
    }

    [Fact]
    public async Task RegisterToolsBatch_EmptyList_ShouldBeNoOp() {
        await _registry.RegisterToolsBatchAsync([]);
        (await _registry.GetCountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task RegisterToolsBatch_WithDuplicateNames_ShouldOverwrite() {
        var firstBatch = new List<IToolHandler> {
            CreateHandler("dup", "v1"),
            CreateHandler("other", "keep"),
        };
        await _registry.RegisterToolsBatchAsync(firstBatch);

        var secondBatch = new List<IToolHandler> {
            CreateHandler("dup", "v2"),
        };
        await _registry.RegisterToolsBatchAsync(secondBatch);

        (await _registry.GetCountAsync()).Should().Be(2);
        var dup = await _registry.GetToolAsync("dup");
        dup!.Description.Should().Be("v2");
    }

    [Fact]
    public async Task RegisterToolsBatch_ShouldFireToolRegisteredEventForEach() {
        var registeredNames = new List<string>();
        _onToolRegistered = (_, e) => registeredNames.Add(e.ToolName);
        _registry.ToolRegistered += _onToolRegistered;

        var handlers = new List<IToolHandler> {
            CreateHandler("evt_1", "d1"),
            CreateHandler("evt_2", "d2"),
        };
        await _registry.RegisterToolsBatchAsync(handlers);

        registeredNames.Should().Contain("evt_1", "evt_2");
        registeredNames.Should().HaveCount(2);
    }

    [Fact]
    public async Task RegisterToolsBatch_LargeBatch_ShouldBeFasterThanIndividualRegistration() {
        const int n = 500;
        var handlers = Enumerable.Range(0, n).Select(i => CreateHandler($"perf_{i}", $"desc_{i}")).ToList();

        var batchSw = System.Diagnostics.Stopwatch.StartNew();
        var batchRegistry = new LocalToolRegistry();
        await batchRegistry.RegisterToolsBatchAsync(handlers);
        batchSw.Stop();
        await batchRegistry.DisposeAsync();

        var individualSw = System.Diagnostics.Stopwatch.StartNew();
        var individualRegistry = new LocalToolRegistry();
        foreach (var h in handlers) {
            await individualRegistry.RegisterToolAsync(h);
        }
        individualSw.Stop();
        await individualRegistry.DisposeAsync();

        batchSw.ElapsedMilliseconds.Should().BeLessThanOrEqualTo(individualSw.ElapsedMilliseconds + 5,
            "批量注册应不慢于逐个注册（允许 5ms 容差）");
    }
}