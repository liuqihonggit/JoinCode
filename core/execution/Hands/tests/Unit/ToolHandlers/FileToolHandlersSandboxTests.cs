#pragma warning disable JCC3010
namespace Hands.Tests.ToolHandlers;

/// <summary>
/// FileToolHandlers 沙箱路径解析测试
/// 验证 ScratchpadSandbox 已注入但未创建沙箱时，FileReadAsync 不应崩溃
/// 场景: 生产环境 IScratchpadSandbox 通过 DI 注入（非 null），
/// 但未调用 CreateSandboxAsync，GetSandboxInfo 抛出 KeyNotFoundException
/// </summary>
public sealed class FileToolHandlersSandboxTests
{
    [Fact]
    public async Task FileReadAsync_WithScratchpadSandboxNotCreated_ShouldNotFailWithSandboxError()
    {
        // Arrange: ScratchpadSandbox 已注入但未创建任何沙箱（模拟生产环境）
        var fileOpMock = new Mock<IFileOperationService>();
        fileOpMock
            .Setup(x => x.ReadFileAsync(It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(FileReadResult.SuccessResult("test.txt", "hello world", 1, 0, 1));

        // 创建真实的 ScratchpadSandbox（已通过 DI 注入），但不调用 CreateSandboxAsync
        var sandbox = new Core.Permission.Services.ScratchpadSandbox(fileOpMock.Object);

        var service = new FileToolHandlers(
            fileOpMock.Object,
            new IO.FileSystem.PhysicalFileSystem(),
            new FileToolHandlersContext(ScratchpadSandbox: sandbox));

        // Act
        var result = await service.FileReadAsync("test.txt").ConfigureAwait(true);

        // Assert: 不应返回沙箱错误（"沙箱 'ScratchpadSandbox' 不存在"）
        result.IsError.Should().BeFalse();
        result.Content.Should().NotBeEmpty();
    }

    [Fact]
    public async Task FileReadAsync_WithNullScratchpadSandbox_ShouldReadSuccessfully()
    {
        // Arrange: ScratchpadSandbox 为 null（未注入）
        var fileOpMock = new Mock<IFileOperationService>();
        fileOpMock
            .Setup(x => x.ReadFileAsync(It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(FileReadResult.SuccessResult("test.txt", "hello world", 1, 0, 1));

        var service = new FileToolHandlers(
            fileOpMock.Object,
            new IO.FileSystem.PhysicalFileSystem(),
            context: null);

        // Act
        var result = await service.FileReadAsync("test.txt").ConfigureAwait(true);

        // Assert: 正常读取（无沙箱时直接返回原路径）
        result.IsError.Should().BeFalse();
        result.Content.Should().NotBeEmpty();
    }
}
#pragma warning restore JCC3010, JCC3011, JCC3012
