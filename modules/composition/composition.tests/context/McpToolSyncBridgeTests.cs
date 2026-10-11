// JCC11003 抑制: 存量代码可空抑制, 后续逐步修复
#pragma warning disable JCC11003
namespace Core.Tests.Context;

public sealed partial class McpToolSyncBridgeTests {
    private readonly Mock<IToolRegistry> _toolRegistry;
    private readonly Mock<IChatContextManager> _contextManager;
    [Inject] private readonly ILogger<McpToolSyncBridge> _logger;

    public McpToolSyncBridgeTests() {
        _toolRegistry = new Mock<IToolRegistry>();
        _contextManager = new Mock<IChatContextManager>();
        _logger = NullLogger<McpToolSyncBridge>.Instance;
    }

    private McpToolSyncBridge CreateSut() =>
        new(_toolRegistry.Object, _contextManager.Object, _logger);

    [Fact]
    public async Task OnToolsListChangedAsync_UpdatesToolSpecs() {
        var toolInfos = new List<ToolInfo>
        {
            new() { Name = "read_file", Description = "Read a file" },
            new() { Name = "write_file", Description = "Write a file" }
        };

        _toolRegistry.Setup(r => r.GetAllToolInfosAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(toolInfos.AsReadOnly());

        _contextManager.Setup(m => m.UpdateToolSpecsAsync(It.IsAny<IReadOnlyList<ToolSpec>>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        await using var sut = CreateSut();
        await sut.OnToolsListChangedAsync().ConfigureAwait(true);

        _contextManager.Verify(m => m.UpdateToolSpecsAsync(
            It.Is<IReadOnlyList<ToolSpec>>(specs => specs.Count == 2
                && specs[0].Name == "read_file"
                && specs[1].Name == "write_file"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task OnToolsListChangedAsync_EmptyTools_StillUpdates() {
        _toolRegistry.Setup(r => r.GetAllToolInfosAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        _contextManager.Setup(m => m.UpdateToolSpecsAsync(It.IsAny<IReadOnlyList<ToolSpec>>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        await using var sut = CreateSut();
        await sut.OnToolsListChangedAsync().ConfigureAwait(true);

        _contextManager.Verify(m => m.UpdateToolSpecsAsync(
            It.Is<IReadOnlyList<ToolSpec>>(specs => specs.Count == 0),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task OnToolsListChangedAsync_RegistryException_DoesNotThrow() {
        _toolRegistry.Setup(r => r.GetAllToolInfosAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("registry error"));

        await using var sut = CreateSut();
        var act = async () => await sut.OnToolsListChangedAsync().ConfigureAwait(true);
        await act.Should().NotThrowAsync().ConfigureAwait(true);
    }

    [Fact]
    public async Task OnToolsListChangedAsync_ContextManagerException_DoesNotThrow() {
        _toolRegistry.Setup(r => r.GetAllToolInfosAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([new ToolInfo { Name = "tool_a", Description = "desc" }]);

        _contextManager.Setup(m => m.UpdateToolSpecsAsync(It.IsAny<IReadOnlyList<ToolSpec>>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("context error"));

        await using var sut = CreateSut();
        var act = async () => await sut.OnToolsListChangedAsync().ConfigureAwait(true);
        await act.Should().NotThrowAsync().ConfigureAwait(true);
    }

    [Fact]
    public async Task OnToolsListChangedAsync_TransformsToolInfoToToolSpec() {
        var toolInfos = new List<ToolInfo>
        {
            new() { Name = "search", Description = "Search code" }
        };

        _toolRegistry.Setup(r => r.GetAllToolInfosAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(toolInfos.AsReadOnly());

        IReadOnlyList<ToolSpec>? capturedSpecs = null;
        _contextManager.Setup(m => m.UpdateToolSpecsAsync(It.IsAny<IReadOnlyList<ToolSpec>>(), It.IsAny<CancellationToken>()))
            .Callback<IReadOnlyList<ToolSpec>, CancellationToken>((specs, _) => capturedSpecs = specs)
            .Returns(Task.CompletedTask);

        await using var sut = CreateSut();
        await sut.OnToolsListChangedAsync().ConfigureAwait(true);

        capturedSpecs.Should().NotBeNull();
        capturedSpecs![0].Name.Should().Be("search");
        capturedSpecs[0].Description.Should().Be("Search code");
    }

    [Fact]
    public async Task OnResourcesListChangedAsync_Success_AddsSystemMessage() {
        var syncResult = OperationResult<IReadOnlyList<string>>.Ok(
            new List<string> { "resource1", "resource2" }.AsReadOnly());

        _contextManager.Setup(m => m.AddDynamicSystemMessageAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        await using var sut = CreateSut();
        await sut.OnResourcesListChangedAsync("server1", syncResult).ConfigureAwait(true);

        _contextManager.Verify(m => m.AddDynamicSystemMessageAsync(
            It.Is<string>(msg => msg.Contains("server1") && msg.Contains("resource1")),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task OnResourcesListChangedAsync_FailedResult_DoesNotAddMessage() {
        var syncResult = OperationResult<IReadOnlyList<string>>.Fail("sync failed");

        await using var sut = CreateSut();
        await sut.OnResourcesListChangedAsync("server1", syncResult).ConfigureAwait(true);

        _contextManager.Verify(m => m.AddDynamicSystemMessageAsync(
            It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task OnResourcesListChangedAsync_EmptyData_DoesNotAddMessage() {
        var syncResult = OperationResult<IReadOnlyList<string>>.Ok(
            new List<string>().AsReadOnly());

        await using var sut = CreateSut();
        await sut.OnResourcesListChangedAsync("server1", syncResult).ConfigureAwait(true);

        _contextManager.Verify(m => m.AddDynamicSystemMessageAsync(
            It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task OnResourcesListChangedAsync_Exception_DoesNotThrow() {
        var syncResult = OperationResult<IReadOnlyList<string>>.Ok(
            new List<string> { "res1" }.AsReadOnly());

        _contextManager.Setup(m => m.AddDynamicSystemMessageAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("error"));

        await using var sut = CreateSut();
        var act = async () => await sut.OnResourcesListChangedAsync("server1", syncResult).ConfigureAwait(true);
        await act.Should().NotThrowAsync().ConfigureAwait(true);
    }

    [Fact]
    public async Task OnPromptsListChangedAsync_Success_AddsSystemMessage() {
        var syncResult = OperationResult<IReadOnlyList<string>>.Ok(
            new List<string> { "prompt1", "prompt2" }.AsReadOnly());

        _contextManager.Setup(m => m.AddDynamicSystemMessageAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        await using var sut = CreateSut();
        await sut.OnPromptsListChangedAsync("server1", syncResult).ConfigureAwait(true);

        _contextManager.Verify(m => m.AddDynamicSystemMessageAsync(
            It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task OnPromptsListChangedAsync_FailedResult_DoesNotAddMessage() {
        var syncResult = OperationResult<IReadOnlyList<string>>.Fail("sync failed");

        await using var sut = CreateSut();
        await sut.OnPromptsListChangedAsync("server1", syncResult).ConfigureAwait(true);

        _contextManager.Verify(m => m.AddDynamicSystemMessageAsync(
            It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task OnPromptsListChangedAsync_EmptyData_DoesNotAddMessage() {
        var syncResult = OperationResult<IReadOnlyList<string>>.Ok(
            new List<string>().AsReadOnly());

        await using var sut = CreateSut();
        await sut.OnPromptsListChangedAsync("server1", syncResult).ConfigureAwait(true);

        _contextManager.Verify(m => m.AddDynamicSystemMessageAsync(
            It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task OnPromptsListChangedAsync_Exception_DoesNotThrow() {
        var syncResult = OperationResult<IReadOnlyList<string>>.Ok(
            new List<string> { "prompt1" }.AsReadOnly());

        _contextManager.Setup(m => m.AddDynamicSystemMessageAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("error"));

        await using var sut = CreateSut();
        var act = async () => await sut.OnPromptsListChangedAsync("server1", syncResult).ConfigureAwait(true);
        await act.Should().NotThrowAsync().ConfigureAwait(true);
    }

    [Fact]
    public async Task OnToolsListChangedAsync_WithSchema_SerializesToJson() {
        var toolInfo = new ToolInfo {
            Name = "search",
            Description = "Search",
            InputSchema = new ToolSchema {
                Type = "object",
                Properties = new Dictionary<string, ToolSchemaProperty> {
                    ["query"] = new ToolSchemaProperty { Type = "string", Description = "search query" }
                },
                Required = new List<string> { "query" }
            }
        };

        _toolRegistry.Setup(r => r.GetAllToolInfosAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ToolInfo> { toolInfo }.AsReadOnly());

        IReadOnlyList<ToolSpec>? capturedSpecs = null;
        _contextManager.Setup(m => m.UpdateToolSpecsAsync(It.IsAny<IReadOnlyList<ToolSpec>>(), It.IsAny<CancellationToken>()))
            .Callback<IReadOnlyList<ToolSpec>, CancellationToken>((specs, _) => capturedSpecs = specs)
            .Returns(Task.CompletedTask);

        await using var sut = CreateSut();
        await sut.OnToolsListChangedAsync().ConfigureAwait(true);

        capturedSpecs.Should().NotBeNull();
        capturedSpecs![0].InputSchemaJson.Should().NotBeNullOrEmpty();
        capturedSpecs[0].InputSchemaJson.Should().Contain("\"type\":\"object\"");
        capturedSpecs[0].InputSchemaJson.Should().Contain("\"required\"");
    }

    [Fact]
    public async Task OnToolsListChangedAsync_DefaultSchema_SerializesEmptyObject() {
        var toolInfo = new ToolInfo { Name = "tool", Description = "desc" };

        _toolRegistry.Setup(r => r.GetAllToolInfosAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ToolInfo> { toolInfo }.AsReadOnly());

        IReadOnlyList<ToolSpec>? capturedSpecs = null;
        _contextManager.Setup(m => m.UpdateToolSpecsAsync(It.IsAny<IReadOnlyList<ToolSpec>>(), It.IsAny<CancellationToken>()))
            .Callback<IReadOnlyList<ToolSpec>, CancellationToken>((specs, _) => capturedSpecs = specs)
            .Returns(Task.CompletedTask);

        await using var sut = CreateSut();
        await sut.OnToolsListChangedAsync().ConfigureAwait(true);

        capturedSpecs.Should().NotBeNull();
        capturedSpecs![0].InputSchemaJson.Should().Contain("\"type\":\"object\"");
    }

    // ===== SerializeToolSchema 确定性测试（internal static 纯计算） =====

    [Fact]
    public void SerializeToolSchema_NullSchema_ReturnsNull() {
        var result = McpToolSyncBridge.SerializeToolSchema(null);
        result.Should().BeNull();
    }

    [Fact]
    public void SerializeToolSchema_EmptyPropertiesAndRequired_ReturnsMinimalObject() {
        var schema = new ToolSchema { Type = "object" };
        var result = McpToolSyncBridge.SerializeToolSchema(schema);
        result.Should().Be("{\"type\":\"object\",\"properties\":{}}");
    }

    [Fact]
    public void SerializeToolSchema_PropertyWithoutDescription_OmitsDescriptionField() {
        var schema = new ToolSchema {
            Type = "object",
            Properties = new Dictionary<string, ToolSchemaProperty> {
                ["query"] = new ToolSchemaProperty { Type = "string" }
            }
        };
        var result = McpToolSyncBridge.SerializeToolSchema(schema);
        result.Should().Be("{\"type\":\"object\",\"properties\":{\"query\":{\"type\":\"string\"}}}");
        result.Should().NotContain("description");
    }

    [Fact]
    public void SerializeToolSchema_PropertyWithDescription_IncludesDescriptionField() {
        var schema = new ToolSchema {
            Type = "object",
            Properties = new Dictionary<string, ToolSchemaProperty> {
                ["query"] = new ToolSchemaProperty { Type = "string", Description = "search query" }
            }
        };
        var result = McpToolSyncBridge.SerializeToolSchema(schema);
        result.Should().Contain("\"description\":\"search query\"");
    }

    [Fact]
    public void SerializeToolSchema_DescriptionWithBackslash_EscapesBackslash() {
        var schema = new ToolSchema {
            Type = "object",
            Properties = new Dictionary<string, ToolSchemaProperty> {
                ["path"] = new ToolSchemaProperty { Type = "string", Description = "C:\\Users\\test" }
            }
        };
        var result = McpToolSyncBridge.SerializeToolSchema(schema);
        result.Should().Contain("\"description\":\"C:\\\\Users\\\\test\"");
    }

    [Fact]
    public void SerializeToolSchema_DescriptionWithDoubleQuote_EscapesQuote() {
        var schema = new ToolSchema {
            Type = "object",
            Properties = new Dictionary<string, ToolSchemaProperty> {
                ["text"] = new ToolSchemaProperty { Type = "string", Description = "say \"hello\"" }
            }
        };
        var result = McpToolSyncBridge.SerializeToolSchema(schema);
        result.Should().Contain("\"description\":\"say \\\"hello\\\"\"");
    }

    [Fact]
    public void SerializeToolSchema_DescriptionWithBackslashAndQuote_EscapesBoth() {
        var schema = new ToolSchema {
            Type = "object",
            Properties = new Dictionary<string, ToolSchemaProperty> {
                ["text"] = new ToolSchemaProperty { Type = "string", Description = "\\path\"end" }
            }
        };
        var result = McpToolSyncBridge.SerializeToolSchema(schema);
        result.Should().Contain("\"description\":\"\\\\path\\\"end\"");
    }

    [Fact]
    public void SerializeToolSchema_WithRequired_IncludesRequiredArray() {
        var schema = new ToolSchema {
            Type = "object",
            Properties = new Dictionary<string, ToolSchemaProperty> {
                ["query"] = new ToolSchemaProperty { Type = "string" }
            },
            Required = new List<string> { "query" }
        };
        var result = McpToolSyncBridge.SerializeToolSchema(schema);
        result.Should().Contain("\"required\":[\"query\"]");
    }

    [Fact]
    public void SerializeToolSchema_WithMultipleRequired_IncludesAllInArray() {
        var schema = new ToolSchema {
            Type = "object",
            Properties = new Dictionary<string, ToolSchemaProperty> {
                ["a"] = new ToolSchemaProperty { Type = "string" },
                ["b"] = new ToolSchemaProperty { Type = "string" }
            },
            Required = new List<string> { "a", "b" }
        };
        var result = McpToolSyncBridge.SerializeToolSchema(schema);
        result.Should().Contain("\"required\":[\"a\",\"b\"]");
    }

    [Fact]
    public void SerializeToolSchema_EmptyRequired_OmitsRequiredField() {
        var schema = new ToolSchema {
            Type = "object",
            Properties = new Dictionary<string, ToolSchemaProperty> {
                ["query"] = new ToolSchemaProperty { Type = "string" }
            },
            Required = new List<string>()
        };
        var result = McpToolSyncBridge.SerializeToolSchema(schema);
        result.Should().NotContain("required");
    }

    [Fact]
    public void SerializeToolSchema_MultipleProperties_SerializesAll() {
        var schema = new ToolSchema {
            Type = "object",
            Properties = new Dictionary<string, ToolSchemaProperty> {
                ["name"] = new ToolSchemaProperty { Type = "string", Description = "name field" },
                ["age"] = new ToolSchemaProperty { Type = "integer", Description = "age field" }
            }
        };
        var result = McpToolSyncBridge.SerializeToolSchema(schema);
        result.Should().Contain("\"name\":{\"type\":\"string\",\"description\":\"name field\"}");
        result.Should().Contain("\"age\":{\"type\":\"integer\",\"description\":\"age field\"}");
    }

    [Fact]
    public void SerializeToolSchema_CustomType_UsesCustomType() {
        var schema = new ToolSchema {
            Type = "array",
            Properties = new Dictionary<string, ToolSchemaProperty>()
        };
        var result = McpToolSyncBridge.SerializeToolSchema(schema);
        result.Should().StartWith("{\"type\":\"array\"");
    }

    [Fact]
    public void SerializeToolSchema_FullSchema_ProducesValidJsonStructure() {
        var schema = new ToolSchema {
            Type = "object",
            Properties = new Dictionary<string, ToolSchemaProperty> {
                ["query"] = new ToolSchemaProperty { Type = "string", Description = "search" }
            },
            Required = new List<string> { "query" }
        };
        var result = McpToolSyncBridge.SerializeToolSchema(schema);
        result.Should().StartWith("{\"type\":\"object\",\"properties\":{");
        result.Should().EndWith("}");
        result.Should().Contain("\"required\":[\"query\"]");
    }
}