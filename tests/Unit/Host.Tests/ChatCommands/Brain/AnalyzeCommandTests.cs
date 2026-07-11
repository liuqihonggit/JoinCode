
namespace Core.Tests.ChatCommands;

public class AnalyzeCommandTests
{
    private readonly Mock<ICodeService> _codeServiceMock;
    private readonly AnalyzeCommand _analyzeCommand;

    public AnalyzeCommandTests()
    {
        _codeServiceMock = new Mock<ICodeService>();
        _analyzeCommand = new AnalyzeCommand();
    }

    [Fact]
    public async Task ExecuteAsync_WithEmptyArguments_ShouldContinue()
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
        var result = await _analyzeCommand.ExecuteAsync(context).ConfigureAwait(true);

        // Assert
        Assert.True(result.ShouldContinue);
        Assert.True(result.IsHandled);
        _codeServiceMock.Verify(x => x.AnalyzeCodeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ExecuteAsync_WithArguments_ShouldAnalyzeCode()
    {
        // Arrange
        var arguments = "function test() { }";
        var analysisResult = "Code analysis result";
        _codeServiceMock.Setup(x => x.AnalyzeCodeAsync(arguments, It.IsAny<CancellationToken>()))
            .ReturnsAsync(analysisResult);

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
        var result = await _analyzeCommand.ExecuteAsync(context).ConfigureAwait(true);

        // Assert
        Assert.True(result.ShouldContinue);
        Assert.True(result.IsHandled);
        _codeServiceMock.Verify(x => x.AnalyzeCodeAsync(arguments, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public void Name_ShouldReturnAnalyze()
    {
        Assert.Equal("analyze", _analyzeCommand.Name);
    }

    [Fact]
    public void Description_ShouldNotBeEmpty()
    {
        Assert.NotEmpty(_analyzeCommand.Description);
    }

    [Fact]
    public void Usage_ShouldNotBeEmpty()
    {
        Assert.NotEmpty(_analyzeCommand.Usage);
    }

    [Fact]
    public void Constructor_WithNullLogger_ShouldNotThrow()
    {
        var exception = Record.Exception(() => new AnalyzeCommand(null));
        Assert.Null(exception);
    }
}
