namespace Hands.Tests.Registry;

/// <summary>
/// LocalToolRegistry 单元测试 — 验证注册/注销/索引维护/分组查询/事件
/// </summary>
public sealed class LocalToolRegistryTest : IAsyncLifetime
{
    private LocalToolRegistry _registry = null!;

    public Task InitializeAsync()
    {
        _registry = new LocalToolRegistry();
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        await _registry.DisposeAsync();
    }

    // === 辅助方法 ===

    private static IToolHandler CreateHandler(
        string name, string description = "test tool",
        ToolKind kind = ToolKind.System, string? groupName = null)
    {
        return new DelegateToolHandler(
            name, description, new ToolSchema(),
            static (toolName, args, ct, onProgress) => Task.FromResult(new ToolResult
            {
                Content = [new() { Type = ToolContentType.Text, Text = $"executed {toolName}" }]
            }),
            kind, groupName);
    }

    // === RegisterToolAsync + GetToolAsync ===

    [Fact]
    public async Task RegisterToolAsync_NullHandler_ThrowsArgumentNullException()
    {
        var act = async () => await _registry.RegisterToolAsync(null!);
        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task RegisterToolAsync_ValidHandler_CanBeRetrieved()
    {
        var handler = CreateHandler("read_file");
        await _registry.RegisterToolAsync(handler);

        var retrieved = await _registry.GetToolAsync("read_file");
        retrieved.Should().BeSameAs(handler);
    }

    [Fact]
    public async Task RegisterToolAsync_Overwrite_ReplacesExisting()
    {
        var handler1 = CreateHandler("tool_a", "first");
        var handler2 = CreateHandler("tool_a", "second");
        await _registry.RegisterToolAsync(handler1);
        await _registry.RegisterToolAsync(handler2);

        var retrieved = await _registry.GetToolAsync("tool_a");
        retrieved.Should().BeSameAs(handler2);
        (await _registry.GetCountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task RegisterToolAsync_WithKindAndGroup_RegistersCorrectly()
    {
        await _registry.RegisterToolAsync(
            "bash", "shell tool", new ToolSchema(),
            static (name, args, ct, onProgress) => Task.FromResult(new ToolResult()),
            kind: ToolKind.Mcp, groupName: "shell");

        var handler = await _registry.GetToolAsync("bash");
        handler.Should().NotBeNull();
        handler!.Kind.Should().Be(ToolKind.Mcp);
        handler.GroupName.Should().Be("shell");
    }

    // === UnregisterToolAsync ===

    [Fact]
    public async Task UnregisterToolAsync_ExistingTool_ReturnsTrue()
    {
        await _registry.RegisterToolAsync(CreateHandler("tool_a"));
        var result = await _registry.UnregisterToolAsync("tool_a");
        result.Should().BeTrue();
        (await _registry.GetToolAsync("tool_a")).Should().BeNull();
    }

    [Fact]
    public async Task UnregisterToolAsync_NonExistentTool_ReturnsFalse()
    {
        var result = await _registry.UnregisterToolAsync("nonexistent");
        result.Should().BeFalse();
    }

    [Fact]
    public async Task UnregisterToolAsync_NullOrEmptyName_ThrowsArgumentException()
    {
        var act1 = async () => await _registry.UnregisterToolAsync(null!);
        var act2 = async () => await _registry.UnregisterToolAsync("");
        await act1.Should().ThrowAsync<ArgumentException>();
        await act2.Should().ThrowAsync<ArgumentException>();
    }

    // === GetGroupNamesAsync ===

    [Fact]
    public async Task GetGroupNamesAsync_NoGroups_ReturnsEmptySet()
    {
        var groups = await _registry.GetGroupNamesAsync();
        groups.Should().BeEmpty();
    }

    [Fact]
    public async Task GetGroupNamesAsync_WithGroups_ReturnsAllGroupNames()
    {
        await _registry.RegisterToolAsync(CreateHandler("a", groupName: "file_ops"));
        await _registry.RegisterToolAsync(CreateHandler("b", groupName: "shell"));
        await _registry.RegisterToolAsync(CreateHandler("c", groupName: "file_ops"));

        var groups = await _registry.GetGroupNamesAsync();
        groups.Should().Contain("file_ops");
        groups.Should().Contain("shell");
        groups.Count.Should().Be(2);
    }

    [Fact]
    public async Task GetGroupNamesAsync_ReturnsFrozenSet()
    {
        await _registry.RegisterToolAsync(CreateHandler("a", groupName: "g1"));
        var groups = await _registry.GetGroupNamesAsync();
        groups.Should().BeAssignableTo<IReadOnlySet<string>>();
    }

    // === GetToolsByKindAsync ===

    [Fact]
    public async Task GetToolsByKindAsync_NoToolsOfKind_ReturnsEmptyDictionary()
    {
        var result = await _registry.GetToolsByKindAsync(ToolKind.Mcp);
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetToolsByKindAsync_ReturnsToolsOfCorrectKind()
    {
        await _registry.RegisterToolAsync(CreateHandler("sys_tool", kind: ToolKind.System));
        await _registry.RegisterToolAsync(CreateHandler("mcp_tool", kind: ToolKind.Mcp));
        await _registry.RegisterToolAsync(CreateHandler("mcp_tool2", kind: ToolKind.Mcp));

        var systemTools = await _registry.GetToolsByKindAsync(ToolKind.System);
        var mcpTools = await _registry.GetToolsByKindAsync(ToolKind.Mcp);

        systemTools.Should().ContainSingle(kvp => kvp.Key == "sys_tool");
        mcpTools.Count.Should().Be(2);
        mcpTools.Should().ContainKey("mcp_tool");
        mcpTools.Should().ContainKey("mcp_tool2");
    }

    // === GetToolsByGroupAsync ===

    [Fact]
    public async Task GetToolsByGroupAsync_NullOrEmptyName_ThrowsArgumentException()
    {
        var act1 = async () => await _registry.GetToolsByGroupAsync(null!);
        var act2 = async () => await _registry.GetToolsByGroupAsync("");
        await act1.Should().ThrowAsync<ArgumentException>();
        await act2.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task GetToolsByGroupAsync_NoToolsInGroup_ReturnsEmptyDictionary()
    {
        var result = await _registry.GetToolsByGroupAsync("nonexistent");
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetToolsByGroupAsync_ReturnsToolsInCorrectGroup()
    {
        await _registry.RegisterToolAsync(CreateHandler("read", groupName: "file_ops"));
        await _registry.RegisterToolAsync(CreateHandler("write", groupName: "file_ops"));
        await _registry.RegisterToolAsync(CreateHandler("bash", groupName: "shell"));

        var fileOps = await _registry.GetToolsByGroupAsync("file_ops");
        fileOps.Count.Should().Be(2);
        fileOps.Should().ContainKey("read");
        fileOps.Should().ContainKey("write");
    }

    [Fact]
    public async Task GetToolsByGroupAsync_CaseInsensitive_ReturnsCorrectGroup()
    {
        await _registry.RegisterToolAsync(CreateHandler("read", groupName: "FileOps"));
        var result = await _registry.GetToolsByGroupAsync("fileops");
        result.Should().ContainKey("read");
    }

    // === _kindIndex 维护 ===

    [Fact]
    public async Task KindIndex_RegisterThenUnregister_IndexIsEmpty()
    {
        await _registry.RegisterToolAsync(CreateHandler("tool_a", kind: ToolKind.Mcp));
        (await _registry.GetToolsByKindAsync(ToolKind.Mcp)).Should().ContainKey("tool_a");

        await _registry.UnregisterToolAsync("tool_a");
        (await _registry.GetToolsByKindAsync(ToolKind.Mcp)).Should().BeEmpty();
    }

    [Fact]
    public async Task KindIndex_Overwrite_KindIndexUpdated()
    {
        await _registry.RegisterToolAsync(CreateHandler("tool_a", kind: ToolKind.System));
        await _registry.RegisterToolAsync(CreateHandler("tool_a", kind: ToolKind.Mcp));

        // 旧 kind 索引应被清除
        (await _registry.GetToolsByKindAsync(ToolKind.System)).Should().BeEmpty();
        // 新 kind 索引应有记录
        (await _registry.GetToolsByKindAsync(ToolKind.Mcp)).Should().ContainKey("tool_a");
    }

    // === _groupIndex 维护 ===

    [Fact]
    public async Task GroupIndex_RegisterThenUnregister_GroupIsEmpty()
    {
        await _registry.RegisterToolAsync(CreateHandler("tool_a", groupName: "g1"));
        (await _registry.GetToolsByGroupAsync("g1")).Should().ContainKey("tool_a");

        await _registry.UnregisterToolAsync("tool_a");
        (await _registry.GetToolsByGroupAsync("g1")).Should().BeEmpty();
    }

    [Fact]
    public async Task GroupIndex_OverwriteWithDifferentGroup_GroupIndexUpdated()
    {
        await _registry.RegisterToolAsync(CreateHandler("tool_a", groupName: "g1"));
        await _registry.RegisterToolAsync(CreateHandler("tool_a", groupName: "g2"));

        // 旧组应不再包含该工具
        (await _registry.GetToolsByGroupAsync("g1")).Should().BeEmpty();
        // 新组应包含该工具
        (await _registry.GetToolsByGroupAsync("g2")).Should().ContainKey("tool_a");
    }

    [Fact]
    public async Task GroupIndex_ToolWithoutGroup_DoesNotAppearInGroupIndex()
    {
        await _registry.RegisterToolAsync(CreateHandler("tool_a", groupName: null));
        var groups = await _registry.GetGroupNamesAsync();
        groups.Should().BeEmpty();
    }

    // === ClearAsync ===

    [Fact]
    public async Task ClearAsync_RemovesAllToolsAndIndexes()
    {
        await _registry.RegisterToolAsync(CreateHandler("a", kind: ToolKind.System, groupName: "g1"));
        await _registry.RegisterToolAsync(CreateHandler("b", kind: ToolKind.Mcp, groupName: "g2"));

        await _registry.ClearAsync();

        (await _registry.GetCountAsync()).Should().Be(0);
        (await _registry.GetGroupNamesAsync()).Should().BeEmpty();
        (await _registry.GetToolsByKindAsync(ToolKind.System)).Should().BeEmpty();
        (await _registry.GetToolsByKindAsync(ToolKind.Mcp)).Should().BeEmpty();
    }

    // === 事件 ===

    [Fact]
    public async Task ToolRegistered_Event_RaisedOnRegister()
    {
        ToolRegisteredEventArgs? eventArgs = null;
        _registry.ToolRegistered += (_, e) => eventArgs = e;

        await _registry.RegisterToolAsync(CreateHandler("my_tool", "my description"));

        eventArgs.Should().NotBeNull();
        eventArgs!.ToolName.Should().Be("my_tool");
        eventArgs.Description.Should().Be("my description");
    }

    [Fact]
    public async Task ToolUnregistered_Event_RaisedOnUnregister()
    {
        ToolUnregisteredEventArgs? eventArgs = null;
        _registry.ToolUnregistered += (_, e) => eventArgs = e;

        await _registry.RegisterToolAsync(CreateHandler("my_tool"));
        await _registry.UnregisterToolAsync("my_tool");

        eventArgs.Should().NotBeNull();
        eventArgs!.ToolName.Should().Be("my_tool");
    }

    [Fact]
    public async Task ToolsCleared_Event_RaisedOnClear()
    {
        var raised = false;
        _registry.ToolsCleared += (_, _) => raised = true;

        await _registry.RegisterToolAsync(CreateHandler("a"));
        await _registry.ClearAsync();

        raised.Should().BeTrue();
    }

    // === ContainsToolAsync ===

    [Fact]
    public async Task ContainsToolAsync_ExistingTool_ReturnsTrue()
    {
        await _registry.RegisterToolAsync(CreateHandler("tool_a"));
        (await _registry.ContainsToolAsync("tool_a")).Should().BeTrue();
    }

    [Fact]
    public async Task ContainsToolAsync_NonExistentTool_ReturnsFalse()
    {
        (await _registry.ContainsToolAsync("nonexistent")).Should().BeFalse();
    }

    // === GetCountAsync ===

    [Fact]
    public async Task GetCountAsync_ReturnsCorrectCount()
    {
        (await _registry.GetCountAsync()).Should().Be(0);

        await _registry.RegisterToolAsync(CreateHandler("a"));
        (await _registry.GetCountAsync()).Should().Be(1);

        await _registry.RegisterToolAsync(CreateHandler("b"));
        (await _registry.GetCountAsync()).Should().Be(2);

        await _registry.UnregisterToolAsync("a");
        (await _registry.GetCountAsync()).Should().Be(1);
    }

    // === GetAllToolsAsync ===

    [Fact]
    public async Task GetAllToolsAsync_ReturnsAllRegisteredTools()
    {
        await _registry.RegisterToolAsync(CreateHandler("a"));
        await _registry.RegisterToolAsync(CreateHandler("b"));

        var all = await _registry.GetAllToolsAsync();
        all.Count.Should().Be(2);
        all.Should().ContainKey("a");
        all.Should().ContainKey("b");
    }

    // === ExecuteToolAsync ===

    [Fact]
    public async Task ExecuteToolAsync_NonExistentTool_ReturnsError()
    {
        var result = await _registry.ExecuteToolAsync("nonexistent", new Dictionary<string, JsonElement>());
        result.IsError.Should().BeTrue();
    }

    [Fact]
    public async Task ExecuteToolAsync_ExistingTool_ExecutesSuccessfully()
    {
        await _registry.RegisterToolAsync(CreateHandler("echo"));
        var result = await _registry.ExecuteToolAsync("echo", new Dictionary<string, JsonElement>());
        result.IsError.Should().BeFalse();
    }
}
