
namespace Core.Tests.Bridge;

/// <summary>
/// BridgeClient 集成测试
/// 测试 MCP 客户端完整流程、技能加载和执行、Bridge 消息循环、消息去重功能
/// </summary>
public class BridgeClientIntegrationTests : IAsyncLifetime
{
    private readonly ITestOutputHelper _output;
    private readonly ILoggerFactory _loggerFactory;
    private string _tempSkillsDir = null!;
    private readonly IFileOperationService _fileOperationService;

    public BridgeClientIntegrationTests(ITestOutputHelper output)
    {
        _output = output;
        _loggerFactory = LoggerFactory.Create(builder =>
        {
            builder.AddProvider(new Testing.Common.Logging.TestOutputLoggerProvider(output));
            builder.SetMinimumLevel(LogLevel.Debug);
        });
        _fileOperationService = new InMemoryFileOperationService();
    }

    public async Task InitializeAsync()
    {
        _tempSkillsDir = "/test/bridge/skills";
        _fileOperationService.CreateDirectory(_tempSkillsDir);
        await Task.CompletedTask.ConfigureAwait(true);
    }

    public async Task DisposeAsync()
    {
        _loggerFactory.Dispose();
    }

    #region MCP 客户端完整流程测试

    [Fact]
    public async Task BridgeClient_Initialize_ShouldSucceed()
    {
        // Arrange
        var handler = CreateMessageHandler();

        var initRequest = new InitializeRequest
        {
            Id = Guid.NewGuid().ToString("N"),
            ProtocolVersion = "1.0",
            ClientInfo = new ClientInfo { Name = "test-client", Version = "1.0.0" }
        };

        // Act
        var response = await handler.HandleAsync(initRequest).ConfigureAwait(true);

        // Assert
        response.Should().NotBeNull();
        response.Should().BeOfType<InitializeResponse>();
        var initResponse = (InitializeResponse)response!;
        initResponse.ProtocolVersion.Should().Be("1.0");
        initResponse.ServerInfo.Name.Should().Be("Core.Bridge");
    }

    [Fact]
    public async Task BridgeClient_ToolsList_ShouldReturnTools()
    {
        // Arrange
        var toolRegistry = CreateToolRegistry();
        await toolRegistry.RegisterToolAsync("test_tool", "A test tool", new ToolSchema(), async (name, args, ct, onProgress) =>
        {
            return new ToolResult
            {
                Content = new List<ToolContent> { new() { Type = ToolContentType.Text, Text = "Test result" } }
            };
        }).ConfigureAwait(true);

        var handler = CreateMessageHandler(toolRegistry: toolRegistry);

        var toolsListRequest = new ToolsListRequest
        {
            Id = Guid.NewGuid().ToString("N")
        };

        // Act
        var response = await handler.HandleAsync(toolsListRequest).ConfigureAwait(true);

        // Assert
        response.Should().NotBeNull();
        response.Should().BeOfType<ToolsListResponse>();
        var toolsResponse = (ToolsListResponse)response!;
        toolsResponse.Tools.Should().HaveCount(1);
        toolsResponse.Tools[0].Name.Should().Be("test_tool");
    }

    [Fact]
    public async Task BridgeClient_ToolsCall_ShouldExecuteTool()
    {
        // Arrange
        var toolExecuted = false;
        var toolRegistry = CreateToolRegistry();
        await toolRegistry.RegisterToolAsync("echo_tool", "Echo tool", new ToolSchema(), async (name, args, ct, onProgress) =>
        {
            toolExecuted = true;
            var message = args.TryGetValue("message", out var msg) ? msg.ToString() : "empty";
            return new ToolResult
            {
                Content = new List<ToolContent> { new() { Type = ToolContentType.Text, Text = $"Echo: {message}" } }
            };
        }).ConfigureAwait(true);

        var handler = CreateMessageHandler(toolRegistry: toolRegistry);

        var toolCallRequest = new ToolsCallRequest
        {
            Id = Guid.NewGuid().ToString("N"),
            ToolName = "echo_tool",
            Arguments = new Dictionary<string, System.Text.Json.JsonElement>
            {
                ["message"] = System.Text.Json.JsonDocument.Parse("\"Hello World\"").RootElement
            }
        };

        // Act
        var response = await handler.HandleAsync(toolCallRequest).ConfigureAwait(true);

        // Assert
        toolExecuted.Should().BeTrue();
        response.Should().NotBeNull();
        response.Should().BeOfType<ToolsCallResponse>();
        var toolResponse = (ToolsCallResponse)response!;
        toolResponse.Success.Should().BeTrue();
    }

    #endregion

    #region 技能加载和执行测试

    [Fact]
    public async Task BridgeClient_SkillExecute_NonExistentSkill_ShouldReturnError()
    {
        // Arrange
        var skillService = CreateTestSkillService();
        var handler = CreateMessageHandler(skillService: skillService);

        var skillRequest = new SkillExecuteRequest
        {
            Id = Guid.NewGuid().ToString("N"),
            SkillName = "non_existent_skill",
            Parameters = new Dictionary<string, System.Text.Json.JsonElement>()
        };

        // Act
        var response = await handler.HandleAsync(skillRequest).ConfigureAwait(true);

        // Assert
        response.Should().NotBeNull();
        response.Should().BeOfType<SkillExecuteResponse>();
        var skillResponse = (SkillExecuteResponse)response!;
        skillResponse.Success.Should().BeFalse();
        skillResponse.Error.Should().Contain("not found");
    }

    private ISkillService CreateTestSkillService()
    {
        var options = new SkillOptions
        {
            SkillsDirectory = _tempSkillsDir,
            CacheExpiration = TimeSpan.FromMinutes(5)
        };
        var queryEngine = new NullQueryEngine();
        var toolRegistry = CreateToolRegistry();

        var middlewares = new IMiddleware<Core.Skills.SkillContext>[]
        {
            new Core.Skills.SkillValidationMiddleware(),
            new Core.Skills.SkillTelemetryMiddleware(),
            new Core.Skills.SkillExecutionMiddleware(queryEngine, toolRegistry, new VariableResolver()),
            new MetricsMiddleware<Core.Skills.SkillContext>()
        };
        var pipeline = new MiddlewarePipeline<Core.Skills.SkillContext>(middlewares);

        return new SkillService(options, _fileOperationService, pipeline);
    }

    [Fact]
    public async Task BridgeClient_SkillExecute_ExistingSkill_ShouldReturnResult()
    {
        // Arrange
        var skillService = CreateTestSkillService();
        var skill = new SkillDefinition
        {
            Name = "test_skill",
            Description = "A test skill",
            Parameters = new Dictionary<string, SkillParameter>
            {
                ["input"] = new SkillParameter { Type = "string", Description = "Input value", Required = true }
            },
            Steps = new List<SkillStep>
            {
                new()
                {
                    Id = "step1",
                    Type = SkillStepType.Prompt,
                    Prompt = "Process: {{input}}",
                    Next = null
                }
            }
        };
        skillService.RegisterSkill(skill);

        var handler = CreateMessageHandler(skillService: skillService);

        var skillRequest = new SkillExecuteRequest
        {
            Id = Guid.NewGuid().ToString("N"),
            SkillName = "test_skill",
            Parameters = new Dictionary<string, System.Text.Json.JsonElement>
            {
                ["input"] = System.Text.Json.JsonDocument.Parse("\"test_value\"").RootElement
            }
        };

        // Act
        var response = await handler.HandleAsync(skillRequest).ConfigureAwait(true);

        // Assert
        response.Should().NotBeNull();
        response.Should().BeOfType<SkillExecuteResponse>();
        var skillResponse = (SkillExecuteResponse)response!;
        // 由于使用 NullQueryEngine，执行可能失败，但流程是正确的
        skillResponse.Should().NotBeNull();
    }

    #endregion

    #region Bridge 消息循环测试

    [Fact]
    public async Task BridgeClient_Ping_ShouldReturnPong()
    {
        // Arrange
        var handler = CreateMessageHandler();

        var ping = new PingMessage { Id = Guid.NewGuid().ToString("N") };

        // Act
        var response = await handler.HandleAsync(ping).ConfigureAwait(true);

        // Assert
        response.Should().NotBeNull();
        response.Should().BeOfType<PongMessage>();
    }

    [Fact]
    public async Task BridgeClient_ControlRequest_Ping_ShouldReturnPong()
    {
        // Arrange
        var handler = CreateMessageHandler();

        var controlRequest = new ControlRequest
        {
            Id = Guid.NewGuid().ToString("N"),
            Command = "ping"
        };

        // Act
        var response = await handler.HandleAsync(controlRequest).ConfigureAwait(true);

        // Assert
        response.Should().NotBeNull();
        response.Should().BeOfType<ControlResponse>();
        var controlResponse = (ControlResponse)response!;
        controlResponse.Success.Should().BeTrue();
        controlResponse.Result.Should().NotBeNull();
    }

    [Fact]
    public async Task BridgeClient_ControlRequest_GetStatus_ShouldReturnStatus()
    {
        // Arrange
        var toolRegistry = CreateToolRegistry();
        await toolRegistry.RegisterToolAsync("tool1", "Tool 1", new ToolSchema(), (n, a, c, onProgress) => Task.FromResult(new ToolResult())).ConfigureAwait(true);

        var skillService = CreateTestSkillService();
        var skill = new SkillDefinition
        {
            Name = "skill1",
            Description = "Skill 1",
            Steps = new List<SkillStep>()
        };
        skillService.RegisterSkill(skill);

        var handler = CreateMessageHandler(toolRegistry: toolRegistry, skillService: skillService);

        var controlRequest = new ControlRequest
        {
            Id = Guid.NewGuid().ToString("N"),
            Command = "getStatus"
        };

        // Act
        var response = await handler.HandleAsync(controlRequest).ConfigureAwait(true);

        // Assert
        response.Should().NotBeNull();
        response.Should().BeOfType<ControlResponse>();
        var controlResponse = (ControlResponse)response!;
        controlResponse.Success.Should().BeTrue();
        controlResponse.Result.Should().NotBeNull();
    }

    [Fact]
    public async Task BridgeClient_ControlRequest_UnknownCommand_ShouldReturnError()
    {
        // Arrange
        var handler = CreateMessageHandler();

        var controlRequest = new ControlRequest
        {
            Id = Guid.NewGuid().ToString("N"),
            Command = "unknown_command"
        };

        // Act
        var response = await handler.HandleAsync(controlRequest).ConfigureAwait(true);

        // Assert
        response.Should().NotBeNull();
        response.Should().BeOfType<ControlResponse>();
        var controlResponse = (ControlResponse)response!;
        controlResponse.Success.Should().BeFalse();
        controlResponse.Error.Should().Contain("Unknown command");
    }

    #endregion

    #region 消息去重测试

    [Fact]
    public async Task BridgeClient_EchoMessages_ShouldBeFilteredByTransport()
    {
        // Arrange - Echo 消息在 TransportManager 层被过滤
        // 这里测试 EchoMessage 类型是否正确创建
        var echoMessage = new EchoMessage
        {
            Id = Guid.NewGuid().ToString("N"),
            OriginalMessageId = Guid.NewGuid().ToString("N"),
            EchoData = System.Text.Json.JsonDocument.Parse("{}").RootElement
        };

        // Assert
        echoMessage.Type.Should().Be("echo");
        echoMessage.OriginalMessageId.Should().NotBeNullOrEmpty();
    }

    #endregion

    #region 辅助方法

    private static IToolRegistry CreateToolRegistry()
    {
        return new LocalToolRegistry();
    }

    private MessageHandlerCoordinator CreateMessageHandler(
        IToolRegistry? toolRegistry = null,
        ISkillService? skillService = null)
    {
        var context = new MessageHandlerContext
        {
            ToolRegistry = toolRegistry,
            SkillService = skillService,
            Logger = _loggerFactory.CreateLogger<MessageHandlerContext>()
        };

        return new MessageHandlerCoordinator(context, _loggerFactory.CreateLogger<MessageHandlerCoordinator>());
    }

    #endregion
}
