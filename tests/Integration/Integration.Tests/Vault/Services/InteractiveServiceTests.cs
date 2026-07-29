
namespace Integration.Tests.Vault.Services;

public class InteractiveServiceTests
{
    private readonly Mock<ILogger<InteractiveService>> _loggerMock;
    private readonly InteractiveService _interactiveService;

    public InteractiveServiceTests()
    {
        _loggerMock = new Mock<ILogger<InteractiveService>>();
        _interactiveService = new InteractiveService(logger: _loggerMock.Object);
    }

    [Fact]
    public async Task AskUserQuestionAsync_ShouldReturnSuccessResult()
    {
        // Arrange
        var question = "Test question";

        // Act
        var result = await _interactiveService.AskUserQuestionAsync(question).ConfigureAwait(true);

        // Assert
        Assert.True(result.Success);
        Assert.NotNull(result.Answer);
        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains(question)),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task AskUserQuestionAsync_WithOptions_ShouldLogOptions()
    {
        // Arrange
        var question = "Test question";
        var options = new List<string> { "Option 1", "Option 2" };

        // Act
        var result = await _interactiveService.AskUserQuestionAsync(question, options).ConfigureAwait(true);

        // Assert
        Assert.True(result.Success);
        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Debug,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => true),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Exactly(2));
    }

    [Fact]
    public void Constructor_WithNullLogger_ShouldNotThrow()
    {
        // Act & Assert
        var exception = Record.Exception(() => new InteractiveService(logger: null));
        Assert.Null(exception);
    }
}
