
namespace Core.Tests.ChatCommands;

public class GenerateCommandTests
{
    private readonly Mock<ILogger<GenerateCommand>> _loggerMock;
    private readonly Mock<ICodeService> _codeServiceMock;
    private readonly GenerateCommand _generateCommand;

    public GenerateCommandTests()
    {
        _loggerMock = new Mock<ILogger<GenerateCommand>>();
        _codeServiceMock = new Mock<ICodeService>();
        _generateCommand = new GenerateCommand(_loggerMock.Object);
    }

    [Fact]
    public async Task ExecuteAsync_WithEmptyArguments_ShouldLogWarningAndContinue()
    {
        // Arrange
        var context = new ChatCommandContext {
            Arguments = "",
            CancellationToken = CancellationToken.None,
             Services = new CommandServices
             {
                ChatService = null!,
                CodeService = _codeServiceMock.Object,
                PlanService = null!,
             FileSystem = TestFileSystem.Current,
             },
        };

        // Act
        var result = await _generateCommand.ExecuteAsync(context).ConfigureAwait(true);

        // Assert
        Assert.True(result.ShouldContinue);
        Assert.True(result.IsHandled);
        _loggerMock.Verify(
            x => x.Log(
                Microsoft.Extensions.Logging.LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("请提供代码描述")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
        _codeServiceMock.Verify(x => x.GenerateCodeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ExecuteAsync_WithArguments_ShouldGenerateCodeAndLog()
    {
        // Arrange
        var arguments = "create a hello world program";
        var generatedCode = "console.log('Hello World');";
        _codeServiceMock.Setup(x => x.GenerateCodeAsync(arguments, It.IsAny<CancellationToken>()))
            .ReturnsAsync(generatedCode);

        var context = new ChatCommandContext {
            Arguments = arguments,
            CancellationToken = CancellationToken.None,
             Services = new CommandServices
             {
                ChatService = null!,
                CodeService = _codeServiceMock.Object,
                PlanService = null!,
             FileSystem = TestFileSystem.Current,
             },
        };

        // Act
        var result = await _generateCommand.ExecuteAsync(context).ConfigureAwait(true);

        // Assert
        Assert.True(result.ShouldContinue);
        Assert.True(result.IsHandled);
        _codeServiceMock.Verify(x => x.GenerateCodeAsync(arguments, It.IsAny<CancellationToken>()), Times.Once);
        _loggerMock.Verify(
            x => x.Log(
                Microsoft.Extensions.Logging.LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("正在生成代码")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public void Name_ShouldReturnGenerate()
    {
        Assert.Equal("generate", _generateCommand.Name);
    }

    [Fact]
    public void Description_ShouldNotBeEmpty()
    {
        Assert.NotEmpty(_generateCommand.Description);
    }

    [Fact]
    public void Usage_ShouldNotBeEmpty()
    {
        Assert.NotEmpty(_generateCommand.Usage);
    }

    [Fact]
    public void Constructor_WithNullLogger_ShouldNotThrow()
    {
        var exception = Record.Exception(() => new GenerateCommand(null));
        Assert.Null(exception);
    }
}
