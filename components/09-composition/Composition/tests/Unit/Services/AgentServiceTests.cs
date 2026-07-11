
namespace Core.Tests.Services;

public partial class AgentServiceTests
{
    private readonly Mock<IChatContextManager> _contextManagerMock;
    private readonly Mock<ILogger<AgentService>> _loggerMock;
    private readonly WorkflowConfig _config;

    public AgentServiceTests()
    {
        _contextManagerMock = new Mock<IChatContextManager>();
        _loggerMock = new Mock<ILogger<AgentService>>();
        _config = new WorkflowConfig
        {
            LlmExecution = new LlmExecutionSettings
            {
                Temperature = 0.7,
                MaxTokens = 2000,
                TopP = 0.9,
                FrequencyPenalty = 0.0,
                PresencePenalty = 0.0
            }
        };
    }

    private static IChatClient CreateKernel() =>
        ApiRegistration.CreateEmptyKernel();

    [Fact]
    public void Constructor_WithValidParameters_ShouldNotThrow()
    {
        var kernel = CreateKernel();

        var exception = Record.Exception(
            () => new AgentService(kernel, _contextManagerMock.Object, _config, _loggerMock.Object));
        Assert.Null(exception);
    }

    [Fact]
    public void Constructor_WithNullLogger_ShouldNotThrow()
    {
        var kernel = CreateKernel();

        var exception = Record.Exception(
            () => new AgentService(kernel, _contextManagerMock.Object, _config, null));
        Assert.Null(exception);
    }

    [Fact]
    public async Task ClearContextAsync_ShouldClearMessages()
    {
        var kernel = CreateKernel();
        var agentService = new AgentService(kernel, _contextManagerMock.Object, _config, _loggerMock.Object);

        await agentService.ClearContextAsync().ConfigureAwait(true);

        _contextManagerMock.Verify(
            x => x.ClearMessagesAsync(It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ClearContextAsync_ShouldAddSystemMessage()
    {
        var kernel = CreateKernel();
        var agentService = new AgentService(kernel, _contextManagerMock.Object, _config, _loggerMock.Object);

        await agentService.ClearContextAsync().ConfigureAwait(true);

        _contextManagerMock.Verify(
            x => x.AddSystemMessageAsync("You are an AI assistant.", It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task GetContextAsync_ShouldReturnAgentContext()
    {
        var kernel = CreateKernel();
        var chatHistory = new MessageList();
        chatHistory.AddUserMessage("User message");
        chatHistory.AddAssistantMessage("Assistant response");

        _contextManagerMock
            .Setup(x => x.GetMessageListAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(chatHistory);

        var agentService = new AgentService(kernel, _contextManagerMock.Object, _config, _loggerMock.Object);

        var result = await agentService.GetContextAsync().ConfigureAwait(true);

        Assert.NotNull(result);
        Assert.NotNull(result.Messages);
        Assert.Equal(2, result.Messages.Count);
        Assert.Equal("user", result.Messages[0].Role);
        Assert.Equal("assistant", result.Messages[1].Role);
    }

    [Fact]
    public async Task GetContextAsync_WithEmptyHistory_ShouldReturnEmptyMessages()
    {
        var kernel = CreateKernel();
        var chatHistory = new MessageList();

        _contextManagerMock
            .Setup(x => x.GetMessageListAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(chatHistory);

        var agentService = new AgentService(kernel, _contextManagerMock.Object, _config, _loggerMock.Object);

        var result = await agentService.GetContextAsync().ConfigureAwait(true);

        Assert.NotNull(result);
        Assert.Empty(result.Messages);
    }

    [Fact]
    public async Task GetContextAsync_WithSystemMessage_ShouldMapCorrectly()
    {
        var kernel = CreateKernel();
        var chatHistory = new MessageList();
        chatHistory.AddSystemMessage("System instruction");

        _contextManagerMock
            .Setup(x => x.GetMessageListAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(chatHistory);

        var agentService = new AgentService(kernel, _contextManagerMock.Object, _config, _loggerMock.Object);

        var result = await agentService.GetContextAsync().ConfigureAwait(true);

        Assert.NotNull(result);
        Assert.Single(result.Messages);
        Assert.Equal("system", result.Messages[0].Role);
    }
}
