namespace McpToolRegistry.Tests;

public class RequiredParamsMiddlewareTests
{
    private readonly ILogger<RequiredParamsMiddleware> _logger;

    public RequiredParamsMiddlewareTests()
    {
        _logger = NullLogger<RequiredParamsMiddleware>.Instance;
    }

    [Fact]
    public async Task InvokeAsync_NoHandler_CallsNext()
    {
        var middleware = new RequiredParamsMiddleware(_logger);
        var context = new ToolExecutionContext
        {
            ToolName = "test",
            Arguments = [],
            Handler = null
        };

        var nextCalled = false;
        await middleware.InvokeAsync(context, (_, _) =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        }, CancellationToken.None);

        nextCalled.Should().BeTrue();
        context.Result.Should().BeNull();
    }

    [Fact]
    public async Task InvokeAsync_AllRequiredPresent_CallsNext()
    {
        var middleware = new RequiredParamsMiddleware(_logger);
        var handler = CreateHandler(["command"], new Dictionary<string, ToolSchemaProperty>
        {
            ["command"] = new() { Type = "string", Description = "Command to run" }
        });

        var context = new ToolExecutionContext
        {
            ToolName = "bash",
            Arguments = new Dictionary<string, JsonElement>
            {
                ["command"] = JsonSerializer.SerializeToElement("echo hello")
            },
            Handler = handler
        };

        var nextCalled = false;
        await middleware.InvokeAsync(context, (_, _) =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        }, CancellationToken.None);

        nextCalled.Should().BeTrue();
        context.Result.Should().BeNull();
    }

    [Fact]
    public async Task InvokeAsync_MissingRequired_SetsErrorResult()
    {
        var middleware = new RequiredParamsMiddleware(_logger);
        var handler = CreateHandler(["command"], new Dictionary<string, ToolSchemaProperty>
        {
            ["command"] = new() { Type = "string", Description = "Command to run" }
        });

        var context = new ToolExecutionContext
        {
            ToolName = "bash",
            Arguments = [],
            Handler = handler
        };

        var nextCalled = false;
        await middleware.InvokeAsync(context, (_, _) =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        }, CancellationToken.None);

        nextCalled.Should().BeFalse();
        context.Result.Should().NotBeNull();
        context.Result!.IsError.Should().BeTrue();
    }

    [Fact]
    public async Task InvokeAsync_NoRequiredParams_CallsNext()
    {
        var middleware = new RequiredParamsMiddleware(_logger);
        var handler = CreateHandler([], new Dictionary<string, ToolSchemaProperty>
        {
            ["command"] = new() { Type = "string" }
        });

        var context = new ToolExecutionContext
        {
            ToolName = "tool",
            Arguments = [],
            Handler = handler
        };

        var nextCalled = false;
        await middleware.InvokeAsync(context, (_, _) =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        }, CancellationToken.None);

        nextCalled.Should().BeTrue();
    }

    [Fact]
    public async Task InvokeAsync_MissingWithDefault_StillReportsMissing()
    {
        var middleware = new RequiredParamsMiddleware(_logger);
        var handler = CreateHandler(["timeout"], new Dictionary<string, ToolSchemaProperty>
        {
            ["timeout"] = new() { Type = "integer", Description = "Timeout", Default = "30000" }
        });

        var context = new ToolExecutionContext
        {
            ToolName = "tool",
            Arguments = [],
            Handler = handler
        };

        await middleware.InvokeAsync(context, (_, _) => Task.CompletedTask, CancellationToken.None);

        context.Result.Should().NotBeNull();
        context.Result!.IsError.Should().BeTrue();
        var text = context.Result.GetFirstText();
        text.Should().Contain("timeout");
    }

    [Fact]
    public async Task InvokeAsync_MissingParamNotInProperties_ReportsUnknown()
    {
        var middleware = new RequiredParamsMiddleware(_logger);
        var handler = CreateHandler(["unknownParam"], new Dictionary<string, ToolSchemaProperty>());

        var context = new ToolExecutionContext
        {
            ToolName = "tool",
            Arguments = [],
            Handler = handler
        };

        await middleware.InvokeAsync(context, (_, _) => Task.CompletedTask, CancellationToken.None);

        context.Result.Should().NotBeNull();
        context.Result!.IsError.Should().BeTrue();
    }

    private static IToolHandler CreateHandler(
        List<string> required,
        Dictionary<string, ToolSchemaProperty> properties)
    {
        var mock = new Mock<IToolHandler>();
        mock.SetupGet(h => h.InputSchema).Returns(new ToolSchema
        {
            Type = "object",
            Required = required,
            Properties = properties
        });
        return mock.Object;
    }
}
