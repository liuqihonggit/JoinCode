#pragma warning disable JCC3010
namespace Hands.Tests.ToolHandlers;

using JoinCode.Abstractions.Security.Sandbox;

/// <summary>
/// FileToolHandlers 沙箱路径解析测试
/// 验证 SandboxManager 未激活沙箱时，FileReadAsync 不应崩溃
/// </summary>
public sealed class FileToolHandlersSandboxTests
{
    [Fact]
    public async Task FileReadAsync_WithSandboxManagerNotActive_ShouldNotFailWithSandboxError()
    {
        var fileOpMock = new Mock<IFileOperationService>();
        fileOpMock
            .Setup(x => x.ReadFileAsync(It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(FileReadResult.SuccessResult("test.txt", "hello world", 1, 0, 1));

        var sandboxManagerMock = new Mock<ISandboxManager>();
        sandboxManagerMock.SetupGet(m => m.IsInSandbox).Returns(false);

        var service = new FileToolHandlers(
            fileOpMock.Object,
            new IO.FileSystem.PhysicalFileSystem(),
            new FileToolHandlersContext(SandboxManager: sandboxManagerMock.Object));

        var result = await service.FileReadAsync("test.txt").ConfigureAwait(true);

        result.IsError.Should().BeFalse();
        result.Content.Should().NotBeEmpty();
    }

    [Fact]
    public async Task FileReadAsync_WithNullSandboxManager_ShouldReadSuccessfully()
    {
        var fileOpMock = new Mock<IFileOperationService>();
        fileOpMock
            .Setup(x => x.ReadFileAsync(It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(FileReadResult.SuccessResult("test.txt", "hello world", 1, 0, 1));

        var service = new FileToolHandlers(
            fileOpMock.Object,
            new IO.FileSystem.PhysicalFileSystem(),
            context: null);

        var result = await service.FileReadAsync("test.txt").ConfigureAwait(true);

        result.IsError.Should().BeFalse();
        result.Content.Should().NotBeEmpty();
    }
}
#pragma warning restore JCC3010, JCC3011, JCC3012
