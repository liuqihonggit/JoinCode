namespace McpToolRegistry.Tests;


public class OnErrorInjectionTests
{
    [Fact]
    public async Task ToolExecutionMiddleware_CatchesException_SetsIsErrorTrue()
    {
        var middleware = new ToolExecutionMiddleware(NullLogger<ToolExecutionMiddleware>.Instance);

        var throwingHandler = new Mock<IToolHandler>();
        throwingHandler.SetupGet(h => h.Name).Returns("failing_tool");
        throwingHandler.Setup(h => h.ExecuteAsync(It.IsAny<Dictionary<string, JsonElement>>(), It.IsAny<CancellationToken>(), It.IsAny<ToolProgressCallback?>()))
            .ThrowsAsync(new InvalidOperationException("File does not exist"));

        var context = new ToolExecutionContext
        {
            ToolName = "failing_tool",
            Arguments = [],
            Handler = throwingHandler.Object
        };

        await middleware.InvokeAsync(context, (_, _) => Task.CompletedTask, CancellationToken.None);

        context.Result.Should().NotBeNull();
        context.Result!.IsError.Should().BeTrue();
        context.Result.GetFirstText().Should().Contain("File does not exist");
    }

    [Fact]
    public async Task ToolExecutionMiddleware_CatchesException_ResultHasExceptionTypeAndMessage()
    {
        var middleware = new ToolExecutionMiddleware(NullLogger<ToolExecutionMiddleware>.Instance);

        var throwingHandler = new Mock<IToolHandler>();
        throwingHandler.SetupGet(h => h.Name).Returns("failing_tool");
        throwingHandler.Setup(h => h.ExecuteAsync(It.IsAny<Dictionary<string, JsonElement>>(), It.IsAny<CancellationToken>(), It.IsAny<ToolProgressCallback?>()))
            .ThrowsAsync(new FileNotFoundException("File not found: /nonexistent.txt"));

        var context = new ToolExecutionContext
        {
            ToolName = "failing_tool",
            Arguments = [],
            Handler = throwingHandler.Object
        };

        await middleware.InvokeAsync(context, (_, _) => Task.CompletedTask, CancellationToken.None);

        context.Result.Should().NotBeNull();
        context.Result!.IsError.Should().BeTrue();
        var text = context.Result.GetFirstText();
        text.Should().Contain("FileNotFoundException");
        text.Should().Contain("File not found: /nonexistent.txt");
    }

    [Fact]
    public async Task OnErrorToolInjectionMiddleware_TriggeredWhenIsErrorTrue_InjectsSchemaJson()
    {
        var onErrorTool = new Mock<IToolHandler>();
        onErrorTool.SetupGet(h => h.Name).Returns("diagnose_error");
        onErrorTool.SetupGet(h => h.Description).Returns("分析工具执行失败的错误信息");
        onErrorTool.SetupGet(h => h.InputSchema).Returns(new ToolSchema
        {
            Properties = new Dictionary<string, ToolSchemaProperty>
            {
                ["errorMessage"] = new() { Type = "string", Description = "失败的错误信息" },
                ["failedToolName"] = new() { Type = "string", Description = "失败的工具名称" }
            },
            Required = ["errorMessage", "failedToolName"]
        });
        onErrorTool.SetupGet(h => h.Kind).Returns(ToolKind.OnError);
        onErrorTool.SetupGet(h => h.GroupName).Returns("diagnostic");

        var registry = new Mock<IToolRegistry>();
        registry.Setup(r => r.GetToolsByKindAsync(ToolKind.OnError, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, IToolHandler> { ["diagnose_error"] = onErrorTool.Object });

        var monitor = new Mock<IToolHealthMonitor>();
        monitor.Setup(m => m.GetRecordAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ToolHealthRecord?)null);
        monitor.Setup(m => m.GetAllRecordsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, ToolHealthRecord>());

        var scorer = new ToolHypergraphScorer();

        var middleware = new OnErrorToolInjectionMiddleware(
            registry.Object, monitor.Object, scorer,
            NullLogger<OnErrorToolInjectionMiddleware>.Instance);

        var context = new ToolExecutionContext
        {
            ToolName = "failing_tool",
            Arguments = [],
            Result = new ToolResult
            {
                Content = [new() { Type = ToolContentType.Text, Text = "File does not exist" }],
                IsError = true
            }
        };

        await middleware.InvokeAsync(context, (_, _) => Task.CompletedTask, CancellationToken.None);

        context.Result!.InjectedMessages.Should().NotBeNull();
        context.Result.InjectedMessages!.Count.Should().BeGreaterThan(0);
        var injectedText = context.Result.InjectedMessages[0].Content ?? "";
        injectedText.Should().Contain("diagnose_error");
        injectedText.Should().Contain("\"function\"");
        injectedText.Should().Contain("\"parameters\"");
    }
}
