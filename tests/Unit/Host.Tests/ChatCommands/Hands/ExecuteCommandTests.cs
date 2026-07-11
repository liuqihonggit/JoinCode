
namespace Core.Tests.ChatCommands;

public class ExecuteCommandTests
{
    private readonly Mock<ILogger<ExecuteCommand>> _loggerMock;
    private readonly Mock<ICodeService> _codeServiceMock;
    private readonly ExecuteCommand _executeCommand;

    public ExecuteCommandTests()
    {
        _loggerMock = new Mock<ILogger<ExecuteCommand>>();
        _codeServiceMock = new Mock<ICodeService>();
        _executeCommand = new ExecuteCommand(_loggerMock.Object);
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
        var result = await _executeCommand.ExecuteAsync(context).ConfigureAwait(true);

        // Assert
        Assert.True(result.ShouldContinue);
        Assert.True(result.IsHandled);
        _loggerMock.Verify(
            x => x.Log(
                Microsoft.Extensions.Logging.LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("请提供要执行的代码")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
        _codeServiceMock.Verify(x => x.ExecuteCodeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ExecuteAsync_WithEmptyArguments_ShouldOutputToConsole()
    {
        // Arrange - 捕获 Console 输出验证 TerminalHelper.WriteLine 被调用
        // 修复 bug: E2E 环境下 logger 为 null,LogWarning 不执行,需 TerminalHelper.WriteLine 保证 stdout 有内容
        var originalOut = System.Console.Out;
        using var stringWriter = new System.IO.StringWriter();
        System.Console.SetOut(stringWriter);

        try
        {
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
            await _executeCommand.ExecuteAsync(context).ConfigureAwait(true);

            // Assert - 验证 Console 输出包含提示信息
            var output = stringWriter.ToString();
            Assert.Contains("请提供要执行的代码", output);
        }
        finally
        {
            System.Console.SetOut(originalOut);
        }
    }

    [Fact]
    public async Task ExecuteAsync_WithArguments_ShouldExecuteCodeAndLog()
    {
        // Arrange
        var arguments = "print('Hello World')";
        var executionResult = "Hello World";
        _codeServiceMock.Setup(x => x.ExecuteCodeAsync(arguments, It.IsAny<CancellationToken>()))
            .ReturnsAsync(executionResult);

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
        var result = await _executeCommand.ExecuteAsync(context).ConfigureAwait(true);

        // Assert
        Assert.True(result.ShouldContinue);
        Assert.True(result.IsHandled);
        _codeServiceMock.Verify(x => x.ExecuteCodeAsync(arguments, It.IsAny<CancellationToken>()), Times.Once);
        _loggerMock.Verify(
            x => x.Log(
                Microsoft.Extensions.Logging.LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("正在执行代码")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public void Name_ShouldReturnExecute()
    {
        Assert.Equal("execute", _executeCommand.Name);
    }

    [Fact]
    public void Description_ShouldNotBeEmpty()
    {
        Assert.NotEmpty(_executeCommand.Description);
    }

    [Fact]
    public void Usage_ShouldNotBeEmpty()
    {
        Assert.NotEmpty(_executeCommand.Usage);
    }

    [Fact]
    public void Constructor_WithNullLogger_ShouldNotThrow()
    {
        var exception = Record.Exception(() => new ExecuteCommand(null));
        Assert.Null(exception);
    }
}
