namespace Core.Tests.Lsp;

public sealed class LspModelTests
{
    [Fact]
    public void LspPosition_Serialize_Deserialize_Roundtrip()
    {
        var position = new LspPosition { Line = 10, Character = 5 };

        var json = JsonSerializer.Serialize(position, LspJsonContext.Default.LspPosition);
        var deserialized = JsonSerializer.Deserialize(json, LspJsonContext.Default.LspPosition);

        deserialized.Should().NotBeNull();
        deserialized!.Line.Should().Be(10);
        deserialized.Character.Should().Be(5);
    }

    [Fact]
    public void LspRange_Serialize_Deserialize_Roundtrip()
    {
        var range = new LspRange
        {
            Start = new LspPosition { Line = 1, Character = 2 },
            End = new LspPosition { Line = 3, Character = 4 }
        };

        var json = JsonSerializer.Serialize(range, LspJsonContext.Default.LspRange);
        var deserialized = JsonSerializer.Deserialize(json, LspJsonContext.Default.LspRange);

        deserialized.Should().NotBeNull();
        deserialized!.Start.Line.Should().Be(1);
        deserialized.Start.Character.Should().Be(2);
        deserialized.End.Line.Should().Be(3);
        deserialized.End.Character.Should().Be(4);
    }

    [Fact]
    public void LspLocation_Serialize_Deserialize_Roundtrip()
    {
        var location = new LspLocation
        {
            Uri = "file:///src/Service.cs",
            Range = new LspRange
            {
                Start = new LspPosition { Line = 5, Character = 0 },
                End = new LspPosition { Line = 5, Character = 20 }
            }
        };

        var json = JsonSerializer.Serialize(location, LspJsonContext.Default.LspLocation);
        var deserialized = JsonSerializer.Deserialize(json, LspJsonContext.Default.LspLocation);

        deserialized.Should().NotBeNull();
        deserialized!.Uri.Should().Be("file:///src/Service.cs");
        deserialized.Range.Start.Line.Should().Be(5);
        deserialized.Range.End.Character.Should().Be(20);
    }

    [Fact]
    public void LspHoverResult_WithMarkdownContent_Serialize_Deserialize()
    {
        var hover = new LspHoverResult
        {
            Contents = JsonSerializer.SerializeToElement(
                new Dictionary<string, JsonElement>
                {
                    ["kind"] = JsonSerializer.SerializeToElement("markdown"),
                    ["value"] = JsonSerializer.SerializeToElement("```csharp\npublic class Foo { }\n```")
                }),
            Range = new LspRange
            {
                Start = new LspPosition { Line = 10, Character = 4 },
                End = new LspPosition { Line = 10, Character = 7 }
            }
        };

        var json = JsonSerializer.Serialize(hover, LspJsonContext.Default.LspHoverResult);
        var deserialized = JsonSerializer.Deserialize(json, LspJsonContext.Default.LspHoverResult);

        deserialized.Should().NotBeNull();
        deserialized!.Contents.Should().NotBeNull();
        deserialized.Range.Should().NotBeNull();
        deserialized.Range!.Start.Line.Should().Be(10);
    }

    [Fact]
    public void LspHoverResult_WithPlainText_Deserialize()
    {
        var json = """{"contents":"This is a string value","range":null}""";

        var result = JsonSerializer.Deserialize(json, LspJsonContext.Default.LspHoverResult);

        result.Should().NotBeNull();
        result!.Range.Should().BeNull();
    }

    [Fact]
    public void LspCompletionItem_Roundtrip()
    {
        var item = new LspCompletionItem
        {
            Label = "ToString",
            Kind = 6,
            Detail = "string Object.ToString()",
            InsertText = "ToString()"
        };

        var json = JsonSerializer.Serialize(item, LspJsonContext.Default.LspCompletionItem);
        var deserialized = JsonSerializer.Deserialize(json, LspJsonContext.Default.LspCompletionItem);

        deserialized.Should().NotBeNull();
        deserialized!.Label.Should().Be("ToString");
        deserialized.Kind.Should().Be(6);
        deserialized.Detail.Should().Be("string Object.ToString()");
        deserialized.InsertText.Should().Be("ToString()");
    }

    [Fact]
    public void LspDocumentSymbol_Roundtrip()
    {
        var symbol = new LspDocumentSymbol
        {
            Name = "MyClass",
            Detail = "class",
            Kind = 5,
            Range = new LspRange
            {
                Start = new LspPosition { Line = 10, Character = 0 },
                End = new LspPosition { Line = 20, Character = 1 }
            },
            SelectionRange = new LspRange
            {
                Start = new LspPosition { Line = 10, Character = 0 },
                End = new LspPosition { Line = 10, Character = 7 }
            },
            Children = new List<LspDocumentSymbol>
            {
                new()
                {
                    Name = "MyMethod",
                    Kind = 12,
                    Range = new LspRange
                    {
                        Start = new LspPosition { Line = 12, Character = 4 },
                        End = new LspPosition { Line = 15, Character = 5 }
                    },
                    SelectionRange = new LspRange
                    {
                        Start = new LspPosition { Line = 12, Character = 4 },
                        End = new LspPosition { Line = 12, Character = 12 }
                    }
                }
            }
        };

        var json = JsonSerializer.Serialize(symbol, LspJsonContext.Default.LspDocumentSymbol);
        var deserialized = JsonSerializer.Deserialize(json, LspJsonContext.Default.LspDocumentSymbol);

        deserialized.Should().NotBeNull();
        deserialized!.Name.Should().Be("MyClass");
        deserialized.Kind.Should().Be(5);
        deserialized.Children.Should().NotBeNull();
        deserialized.Children!.Count.Should().Be(1);
        deserialized.Children[0].Name.Should().Be("MyMethod");
    }

    [Fact]
    public void LspSymbolInformation_Roundtrip()
    {
        var info = new LspSymbolInformation
        {
            Name = "Calculate",
            Kind = 12,
            Location = new LspLocation
            {
                Uri = "file:///src/Math.cs",
                Range = new LspRange
                {
                    Start = new LspPosition { Line = 42, Character = 4 },
                    End = new LspPosition { Line = 42, Character = 13 }
                }
            },
            ContainerName = "MathHelper"
        };

        var json = JsonSerializer.Serialize(info, LspJsonContext.Default.LspSymbolInformation);
        var deserialized = JsonSerializer.Deserialize(json, LspJsonContext.Default.LspSymbolInformation);

        deserialized.Should().NotBeNull();
        deserialized!.Name.Should().Be("Calculate");
        deserialized.Kind.Should().Be(12);
        deserialized.Location.Uri.Should().Be("file:///src/Math.cs");
        deserialized.ContainerName.Should().Be("MathHelper");
    }

    [Fact]
    public void LspJsonRpcRequest_Serializes_Correctly()
    {
        var request = new LspJsonRpcRequest
        {
            Id = "1",
            Method = "textDocument/definition",
            Params = JsonSerializer.SerializeToNode(new LspPosition { Line = 10, Character = 5 },
                LspJsonContext.Default.LspPosition)
        };

        var json = JsonSerializer.Serialize(request, LspJsonContext.Default.LspJsonRpcRequest);

        json.Should().Contain("\"jsonrpc\"");
        json.Should().Contain("\"2.0\"");
        json.Should().Contain("\"id\"");
        json.Should().Contain("\"1\"");
        json.Should().Contain("\"method\"");
        json.Should().Contain("\"textDocument/definition\"");
        json.Should().Contain("\"line\"");
        json.Should().Contain("10");
    }

    [Fact]
    public void LspJsonRpcNotification_Serializes_Correctly()
    {
        var notification = new LspJsonRpcNotification
        {
            Method = "textDocument/didOpen",
            Params = null
        };

        var json = JsonSerializer.Serialize(notification, LspJsonContext.Default.LspJsonRpcNotification);

        json.Should().Contain("\"jsonrpc\"");
        json.Should().Contain("\"2.0\"");
        json.Should().Contain("\"method\"");
        json.Should().Contain("\"textDocument/didOpen\"");
        json.Should().NotContain("\"id\"");
    }

    [Fact]
    public void LspLocationList_Serialize_Deserialize_Roundtrip()
    {
        var locations = new List<LspLocation>
        {
            new() { Uri = "file:///a.cs", Range = new LspRange() },
            new() { Uri = "file:///b.cs", Range = new LspRange() }
        };

        var json = JsonSerializer.Serialize(locations, LspJsonContext.Default.ListLspLocation);
        var deserialized = JsonSerializer.Deserialize(json, LspJsonContext.Default.ListLspLocation);

        deserialized.Should().NotBeNull();
        deserialized!.Count.Should().Be(2);
        deserialized[0].Uri.Should().Be("file:///a.cs");
        deserialized[1].Uri.Should().Be("file:///b.cs");
    }

    [Fact]
    public void LspCompletionItemList_Deserialize_FromArray()
    {
        var json = """[{"label":"Foo","kind":6},{"label":"Bar","kind":3}]""";

        var items = JsonSerializer.Deserialize(json, LspJsonContext.Default.ListLspCompletionItem);

        items.Should().NotBeNull();
        items!.Count.Should().Be(2);
        items[0].Label.Should().Be("Foo");
        items[0].Kind.Should().Be(6);
        items[1].Label.Should().Be("Bar");
        items[1].Kind.Should().Be(3);
    }

    [Fact]
    public void LspSymbolInformationList_Deserialize_FromArray()
    {
        var json = """[{"name":"MyFunc","kind":12,"location":{"uri":"file:///test.cs","range":{"start":{"line":1,"character":0},"end":{"line":1,"character":6}}}}]""";

        var symbols = JsonSerializer.Deserialize(json, LspJsonContext.Default.ListLspSymbolInformation);

        symbols.Should().NotBeNull();
        symbols!.Count.Should().Be(1);
        symbols[0].Name.Should().Be("MyFunc");
        symbols[0].Kind.Should().Be(12);
        symbols[0].Location.Uri.Should().Be("file:///test.cs");
    }
}

public sealed class LspConfigTests
{
    [Theory]
    [InlineData(LspServerType.CSharp, "csharp")]
    [InlineData(LspServerType.TypeScript, "typescript")]
    [InlineData(LspServerType.Python, "python")]
    [InlineData(LspServerType.Rust, "rust")]
    [InlineData(LspServerType.Go, "go")]
    [InlineData(LspServerType.Java, "java")]
    [InlineData(LspServerType.Cpp, "cpp")]
    [InlineData(LspServerType.Generic, "generic")]
    public void ToValue_Returns_Correct_Id(LspServerType serverType, string expected)
    {
        var result = serverType.ToValue();

        result.Should().Be(expected);
    }

    [Fact]
    public void LspServerConfigEntry_ToLspInstanceConfig_MapsCorrectly()
    {
        var entry = new LspServerConfigEntry
        {
            ServerId = "test-server",
            Name = "Test Server",
            Command = "test-lsp",
            Arguments = new List<string> { "--stdio" },
            FileExtensions = new List<string> { ".ts", ".tsx" },
            LanguageId = "typescript",
            StartupTimeoutSeconds = 60,
            EnvironmentVariables = new Dictionary<string, string> { ["NODE_PATH"] = "/usr/lib/node" }
        };

        var config = entry.ToLspInstanceConfig();

        config.Name.Should().Be("test-server");
        config.LanguageId.Should().Be("typescript");
        config.Command.Should().Be("test-lsp");
        config.Arguments.Should().Contain("--stdio");
        config.StartupTimeout.Should().Be(TimeSpan.FromSeconds(60));
        config.ExtensionToLanguage.Should().ContainKey(".ts");
        config.ExtensionToLanguage[".ts"].Should().Be("typescript");
        config.ExtensionToLanguage.Should().ContainKey(".tsx");
        config.ExtensionToLanguage[".tsx"].Should().Be("typescript");
        config.Environment.Should().ContainKey("NODE_PATH");
    }

    [Fact]
    public void LspServerConfigEntry_ToLspInstanceConfig_DefaultTimeout()
    {
        var entry = new LspServerConfigEntry
        {
            ServerId = "test",
            Name = "Test",
            Command = "test",
            LanguageId = "test"
        };

        var config = entry.ToLspInstanceConfig();

        config.StartupTimeout.Should().Be(TimeSpan.FromSeconds(30));
        config.Environment.Should().BeNull();
        config.ExtensionToLanguage.Should().BeEmpty();
    }
}

public sealed class LspServiceTests
{
    private readonly Mock<ILspManager> _mockManager;
    private readonly Mock<ILspConfigLoader> _mockConfigLoader;
    private readonly Mock<IFileOperationService> _mockFileService;
    private readonly Mock<IFileSystem> _mockFs;

    public LspServiceTests()
    {
        _mockManager = new Mock<ILspManager>();
        _mockConfigLoader = new Mock<ILspConfigLoader>();
        _mockFileService = new Mock<IFileOperationService>();
        _mockFs = new Mock<IFileSystem>();

        _mockManager.Setup(m => m.IsInitialized).Returns(true);
        _mockManager.Setup(m => m.IsFileOpen(It.IsAny<string>())).Returns(false);
        _mockManager.Setup(m => m.GetAllServers()).Returns(new Dictionary<string, ILspServerInstance>());
        _mockConfigLoader.Setup(c => c.LoadAsync(It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<LspServerConfigEntry>());
    }

    [Fact]
    public void Constructor_WithNullEngineContext_ThrowsArgumentNullException()
    {
        var act = () => new LspService(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Constructor_WithNullManager_ThrowsArgumentNullException()
    {
        var ctx = new LspEngineContext { LspManager = null!, ConfigLoader = _mockConfigLoader.Object };
        var act = () => new LspService(ctx);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Constructor_WithNullConfigLoader_ThrowsArgumentNullException()
    {
        var ctx = new LspEngineContext { LspManager = _mockManager.Object, ConfigLoader = null! };
        var act = () => new LspService(ctx);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Constructor_WithNullFileService_ThrowsArgumentNullException()
    {
        var ctx = CreateEngineContext();
        var act = () => new LspService(ctx, new LspServiceDeps { FileOperationService = null });

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Constructor_WithValidParams_CreatesInstance()
    {
        var act = () => new LspService(CreateEngineContext(), CreateDeps());

        act.Should().NotThrow();
    }

    [Fact]
    public async Task GotoDefinitionAsync_NoServerForFile_ReturnsEmpty()
    {
        _mockManager.Setup(m => m.GetServerForFile(It.IsAny<string>())).Returns((ILspServerInstance?)null);
        var service = new LspService(CreateEngineContext(), CreateDeps());

        var result = await service.GotoDefinitionAsync("/src/readme.md", 1, 1).ConfigureAwait(true);

        result.Should().NotBeNull();
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task FindReferencesAsync_NoServerForFile_ReturnsEmpty()
    {
        _mockManager.Setup(m => m.GetServerForFile(It.IsAny<string>())).Returns((ILspServerInstance?)null);
        var service = new LspService(CreateEngineContext(), CreateDeps());

        var result = await service.FindReferencesAsync("/src/readme.md", 1, 1).ConfigureAwait(true);

        result.Should().NotBeNull();
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task HoverAsync_NoServerForFile_ReturnsNull()
    {
        _mockManager.Setup(m => m.GetServerForFile(It.IsAny<string>())).Returns((ILspServerInstance?)null);
        var service = new LspService(CreateEngineContext(), CreateDeps());

        var result = await service.HoverAsync("/src/readme.md", 1, 1).ConfigureAwait(true);

        result.Should().BeNull();
    }

    [Fact]
    public async Task GetCompletionsAsync_NoServerForFile_ReturnsEmpty()
    {
        _mockManager.Setup(m => m.GetServerForFile(It.IsAny<string>())).Returns((ILspServerInstance?)null);
        var service = new LspService(CreateEngineContext(), CreateDeps());

        var result = await service.GetCompletionsAsync("/src/readme.md", 1, 1).ConfigureAwait(true);

        result.Should().NotBeNull();
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetDocumentSymbolsAsync_NoServerForFile_ReturnsEmpty()
    {
        _mockManager.Setup(m => m.GetServerForFile(It.IsAny<string>())).Returns((ILspServerInstance?)null);
        var service = new LspService(CreateEngineContext(), CreateDeps());

        var result = await service.GetDocumentSymbolsAsync("/src/readme.md").ConfigureAwait(true);

        result.Should().NotBeNull();
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task SearchWorkspaceSymbolsAsync_NoServers_ReturnsEmpty()
    {
        var service = new LspService(CreateEngineContext(), CreateDeps());

        var result = await service.SearchWorkspaceSymbolsAsync("MyClass").ConfigureAwait(true);

        result.Should().NotBeNull();
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task CloseClientAsync_DoesNotThrow()
    {
        var service = new LspService(CreateEngineContext(), CreateDeps());

        var act = async () => await service.CloseClientAsync("/src/readme.md").ConfigureAwait(true);

        await act.Should().NotThrowAsync().ConfigureAwait(true);
    }

    [Fact]
    public async Task DisposeAsync_DoesNotThrow()
    {
        var service = new LspService(CreateEngineContext(), CreateDeps());

        var act = async () => await service.DisposeAsync().ConfigureAwait(true);

        await act.Should().NotThrowAsync().ConfigureAwait(true);
    }

    private LspEngineContext CreateEngineContext() => new()
    {
        LspManager = _mockManager.Object,
        ConfigLoader = _mockConfigLoader.Object
    };

    private LspServiceDeps CreateDeps() => new()
    {
        FileOperationService = _mockFileService.Object,
        FileSystem = _mockFs.Object
    };}