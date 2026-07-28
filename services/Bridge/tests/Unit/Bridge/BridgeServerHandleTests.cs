namespace Bridge.Tests;

using System.Text.Json;
using JoinCode.Abstractions.Models.Shell;

/// <summary>
/// BridgeServer.executeCommand/setSelection 单元测试 — P0-B TDD
/// 验证 executeCommand 调用 ShellService，setSelection 调用 IdeService
/// </summary>
public sealed class BridgeServerHandleTests
{
    private static Mock<IFileOperationService> CreateFileOpMock()
    {
        var mock = new Mock<IFileOperationService>();
        mock.Setup(f => f.FileExists(It.IsAny<string>())).Returns(false);
        return mock;
    }

    private static BridgeServer CreateServer(
        IShellExecutionService? shellService = null,
        IIdeIntegrationService? ideService = null)
    {
        return new BridgeServer(
            CreateFileOpMock().Object,
            port: 0,
            logger: NullLogger<BridgeServer>.Instance,
            shellService: shellService,
            ideService: ideService);
    }

    private static BridgeServerMessage CreateExecuteCommandMessage(string command)
    {
        var json = $$"""{"command":"{{command}}"}""";
        return new BridgeServerMessage
        {
            Type = "executeCommand",
            Data = JsonDocument.Parse(json).RootElement
        };
    }

    private static BridgeServerMessage CreateSetSelectionMessage(string file, int startLine, int startCol, int endLine, int endCol)
    {
        var json = $$"""{"file":"{{file}}","startLine":{{startLine}},"startCol":{{startCol}},"endLine":{{endLine}},"endCol":{{endCol}}}""";
        return new BridgeServerMessage
        {
            Type = "setSelection",
            Data = JsonDocument.Parse(json).RootElement
        };
    }

    private static BridgeCommandExecutedData ParseCommandExecuted(BridgeServerMessage response)
    {
        response.Type.Should().Be("commandExecuted");
        response.Data.Should().NotBeNull();
        return response.Data!.Value.Deserialize<BridgeCommandExecutedData>(BridgeJsonContext.Default.BridgeCommandExecutedData)
            ?? throw new InvalidOperationException("反序列化失败");
    }

    private static BridgeSelectionSetData ParseSelectionSet(BridgeServerMessage response)
    {
        response.Type.Should().Be("selectionSet");
        response.Data.Should().NotBeNull();
        return response.Data!.Value.Deserialize<BridgeSelectionSetData>(BridgeJsonContext.Default.BridgeSelectionSetData)
            ?? throw new InvalidOperationException("反序列化失败");
    }

    // ============================================================
    // executeCommand 单元测试
    // ============================================================

    [Fact]
    public async Task BuildExecuteCommandResponseAsync_WithShellService_ShouldCallExecuteAndReturnSuccess()
    {
        // Arrange
        var shellMock = new Mock<IShellExecutionService>();
        shellMock.Setup(s => s.ExecuteAsync(
                It.Is<string>(c => c == "echo hello"),
                It.IsAny<int?>(),
                It.IsAny<string?>(),
                It.IsAny<bool>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ShellExecutionResult
            {
                Stdout = "hello",
                Stderr = "",
                ExitCode = 0
            });
        var server = CreateServer(shellService: shellMock.Object);
        var message = CreateExecuteCommandMessage("echo hello");

        // Act
        var response = await server.BuildExecuteCommandResponseAsync(message, CancellationToken.None).ConfigureAwait(true);

        // Assert
        shellMock.Verify(s => s.ExecuteAsync(
            It.Is<string>(c => c == "echo hello"),
            It.IsAny<int?>(),
            It.IsAny<string?>(),
            It.IsAny<bool>(),
            It.IsAny<CancellationToken>()), Times.Once);

        var data = ParseCommandExecuted(response);
        data.Success.Should().BeTrue();
        data.Output.Should().Be("hello");
        data.ExitCode.Should().Be(0);
        data.DurationMs.Should().NotBeNull().And.BeGreaterThanOrEqualTo(0);
    }

    [Fact]
    public async Task BuildExecuteCommandResponseAsync_WithoutShellService_ShouldReturnNotSupported()
    {
        // Arrange
        var server = CreateServer(shellService: null);
        var message = CreateExecuteCommandMessage("echo hello");

        // Act
        var response = await server.BuildExecuteCommandResponseAsync(message, CancellationToken.None).ConfigureAwait(true);

        // Assert
        var data = ParseCommandExecuted(response);
        data.Success.Should().BeFalse();
        data.Error.Should().NotBeNullOrEmpty();
        data.Error.Should().Contain("Shell");
    }

    [Fact]
    public async Task BuildExecuteCommandResponseAsync_EmptyCommand_ShouldReturnFailureWithErrorMessage()
    {
        // Arrange
        var shellMock = new Mock<IShellExecutionService>();
        var server = CreateServer(shellService: shellMock.Object);
        var message = CreateExecuteCommandMessage("");

        // Act
        var response = await server.BuildExecuteCommandResponseAsync(message, CancellationToken.None).ConfigureAwait(true);

        // Assert
        var data = ParseCommandExecuted(response);
        data.Success.Should().BeFalse();
        data.Error.Should().NotBeNullOrEmpty();
        shellMock.Verify(s => s.ExecuteAsync(
            It.IsAny<string>(),
            It.IsAny<int?>(),
            It.IsAny<string?>(),
            It.IsAny<bool>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task BuildExecuteCommandResponseAsync_WhenShellThrows_ShouldReturnExceptionMessage()
    {
        // Arrange
        var shellMock = new Mock<IShellExecutionService>();
        shellMock.Setup(s => s.ExecuteAsync(
                It.IsAny<string>(),
                It.IsAny<int?>(),
                It.IsAny<string?>(),
                It.IsAny<bool>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("模拟执行失败"));
        var server = CreateServer(shellService: shellMock.Object);
        var message = CreateExecuteCommandMessage("bad-command");

        // Act
        var response = await server.BuildExecuteCommandResponseAsync(message, CancellationToken.None).ConfigureAwait(true);

        // Assert
        var data = ParseCommandExecuted(response);
        data.Success.Should().BeFalse();
        data.Error.Should().Contain("模拟执行失败");
    }

    // ============================================================
    // setSelection 单元测试
    // ============================================================

    [Fact]
    public async Task BuildSetSelectionResponseAsync_WithIdeService_ShouldCallSetSelection()
    {
        // Arrange
        var ideMock = new Mock<IIdeIntegrationService>();
        ideMock.Setup(i => i.SetSelectionAsync(
                It.IsAny<string>(),
                It.IsAny<int>(),
                It.IsAny<int>(),
                It.IsAny<int>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        var server = CreateServer(ideService: ideMock.Object);
        var message = CreateSetSelectionMessage("test.cs", startLine: 5, startCol: 1, endLine: 5, endCol: 10);

        // Act
        var response = await server.BuildSetSelectionResponseAsync(message, CancellationToken.None).ConfigureAwait(true);

        // Assert
        ideMock.Verify(i => i.SetSelectionAsync(
            It.Is<string>(f => f == "test.cs"),
            It.Is<int>(l => l == 5),
            It.Is<int>(c => c == 1),
            It.Is<int>(l => l == 5),
            It.Is<int>(c => c == 10),
            It.IsAny<CancellationToken>()), Times.Once);

        var data = ParseSelectionSet(response);
        data.Success.Should().BeTrue();
    }

    [Fact]
    public async Task BuildSetSelectionResponseAsync_WithoutIdeService_ShouldReturnFailure()
    {
        // Arrange
        var server = CreateServer(ideService: null);
        var message = CreateSetSelectionMessage("test.cs", startLine: 5, startCol: 1, endLine: 5, endCol: 10);

        // Act
        var response = await server.BuildSetSelectionResponseAsync(message, CancellationToken.None).ConfigureAwait(true);

        // Assert
        var data = ParseSelectionSet(response);
        data.Success.Should().BeFalse();
        data.Error.Should().NotBeNullOrEmpty();
        data.Error.Should().Contain("IDE");
    }

    [Fact]
    public async Task BuildSetSelectionResponseAsync_WhenIdeReturnsFalse_ShouldReturnFailureWithMessage()
    {
        // Arrange: IdeService 未连接 IDE，SetSelectionAsync 返回 false
        var ideMock = new Mock<IIdeIntegrationService>();
        ideMock.Setup(i => i.SetSelectionAsync(
                It.IsAny<string>(),
                It.IsAny<int>(),
                It.IsAny<int>(),
                It.IsAny<int>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        var server = CreateServer(ideService: ideMock.Object);
        var message = CreateSetSelectionMessage("test.cs", startLine: 5, startCol: 1, endLine: 5, endCol: 10);

        // Act
        var response = await server.BuildSetSelectionResponseAsync(message, CancellationToken.None).ConfigureAwait(true);

        // Assert
        var data = ParseSelectionSet(response);
        data.Success.Should().BeFalse();
        data.Error.Should().NotBeNullOrEmpty();
    }
}
