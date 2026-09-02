namespace Hands.Tests.ToolHandlers;

/// <summary>
/// LspToolHandlers 单元测试 — 验证 LSP 工具的未配置错误和正常调用
/// </summary>
public class LspToolHandlersTests
{
    [Fact]
    public async Task GoToDefinition_NoLspService_ReturnsError()
    {
        var handler = new LspToolHandlers();
        var result = await handler.GoToDefinitionAsync("test.cs", 1, 1);
        result.IsError.Should().BeTrue();
    }

    [Fact]
    public async Task FindReferences_NoLspService_ReturnsError()
    {
        var handler = new LspToolHandlers();
        var result = await handler.FindReferencesAsync("test.cs", 1, 1);
        result.IsError.Should().BeTrue();
    }

    [Fact]
    public async Task Hover_NoLspService_ReturnsError()
    {
        var handler = new LspToolHandlers();
        var result = await handler.HoverAsync("test.cs", 1, 1);
        result.IsError.Should().BeTrue();
    }

    [Fact]
    public async Task GetCompletions_NoLspService_ReturnsError()
    {
        var handler = new LspToolHandlers();
        var result = await handler.GetCompletionsAsync("test.cs", 1, 1);
        result.IsError.Should().BeTrue();
    }

    [Fact]
    public async Task GetDocumentSymbols_NoLspService_ReturnsError()
    {
        var handler = new LspToolHandlers();
        var result = await handler.GetDocumentSymbolsAsync("test.cs");
        result.IsError.Should().BeTrue();
    }

    [Fact]
    public async Task SearchWorkspaceSymbols_NoLspService_ReturnsError()
    {
        var handler = new LspToolHandlers();
        var result = await handler.SearchWorkspaceSymbolsAsync("TestClass");
        result.IsError.Should().BeTrue();
    }

    [Fact]
    public async Task GoToImplementation_NoLspService_ReturnsError()
    {
        var handler = new LspToolHandlers();
        var result = await handler.GoToImplementationAsync("test.cs", 1, 1);
        result.IsError.Should().BeTrue();
    }

    [Fact]
    public async Task PrepareCallHierarchy_NoLspService_ReturnsError()
    {
        var handler = new LspToolHandlers();
        var result = await handler.PrepareCallHierarchyAsync("test.cs", 1, 1);
        result.IsError.Should().BeTrue();
    }

    [Fact]
    public async Task GoToDefinition_WithMockService_ReturnsResults()
    {
        var mockService = new Mock<ILspService>();
        mockService.Setup(s => s.GotoDefinitionAsync("test.cs", 1, 1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<LspLocation>
            {
                new() { Uri = "file:///test.cs", Range = new LspRange { Start = new LspPosition { Line = 5, Character = 10 } } }
            });

        var handler = new LspToolHandlers(mockService.Object);
        var result = await handler.GoToDefinitionAsync("test.cs", 1, 1);
        result.IsError.Should().BeFalse();
    }

    [Fact]
    public async Task FindReferences_WithMockService_ReturnsResults()
    {
        var mockService = new Mock<ILspService>();
        mockService.Setup(s => s.FindReferencesAsync("test.cs", 1, 1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<LspLocation>
            {
                new() { Uri = "file:///ref1.cs", Range = new LspRange() },
                new() { Uri = "file:///ref2.cs", Range = new LspRange() }
            });

        var handler = new LspToolHandlers(mockService.Object);
        var result = await handler.FindReferencesAsync("test.cs", 1, 1);
        result.IsError.Should().BeFalse();
    }

    [Fact]
    public async Task GetDocumentSymbols_WithMockService_ReturnsResults()
    {
        var mockService = new Mock<ILspService>();
        mockService.Setup(s => s.GetDocumentSymbolsAsync("test.cs", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<LspDocumentSymbol>
            {
                new() { Name = "TestClass", Kind = 5 }
            });

        var handler = new LspToolHandlers(mockService.Object);
        var result = await handler.GetDocumentSymbolsAsync("test.cs");
        result.IsError.Should().BeFalse();
    }
}
