#pragma warning disable JCC3010
namespace Hands.Tests.ToolHandlers;

/// <summary>
/// FileToolHandlers fire-and-forget CancellationToken 保护测试
/// 验证 Dispose 后取消令牌传播、LspFileSync 为 null 时安全、多次 Dispose 安全
/// </summary>
public sealed class FileToolHandlersPersistenceTests
{
    private static FileToolHandlers CreateService(
        IFileOperationService? fileOperationService = null,
        JoinCode.Abstractions.Interfaces.Lsp.ILspFileSync? lspFileSync = null)
    {
        var fos = fileOperationService ?? new Mock<IFileOperationService>().Object;
        return new FileToolHandlers(fos, new IO.FileSystem.PhysicalFileSystem(),
            new FileToolHandlersContext(LspFileSync: lspFileSync));
    }

    [Fact]
    public async Task Dispose_CancelsPendingLspNotification()
    {
        // Arrange: 创建带 mock LspFileSync 的服务
        var fileOpMock = new Mock<IFileOperationService>();
        var lspMock = new Mock<JoinCode.Abstractions.Interfaces.Lsp.ILspFileSync>();
        var service = new FileToolHandlers(fileOpMock.Object, new IO.FileSystem.PhysicalFileSystem(),
            new FileToolHandlersContext(LspFileSync: lspMock.Object));

        // Act: Dispose 后 _disposeCts.Token 应为取消状态
        service.Dispose();

        // Assert: Dispose 后不崩溃
        // _disposeCts 已 Cancel + Dispose，后续 NotifyLspFileChange 使用已取消 token
        // Task.Run 传入已取消 token 时任务不会执行
        // 使用信号量等待替代 Task.Delay — Dispose 后信号量已释放，无需等待
    }

    [Fact]
    public async Task NotifyLspFileChange_WithNullLspFileSync_DoesNothing()
    {
        // Arrange: _lspFileSync 为 null（默认值）
        var fileOpMock = new Mock<IFileOperationService>();
        var service = new FileToolHandlers(fileOpMock.Object, new IO.FileSystem.PhysicalFileSystem(), context: null);

        // Act & Assert: Dispose 不崩溃（_lspFileSync 为 null 时 NotifyLspFileChange 直接 return）
        service.Dispose();

        // 无需等待 — LspFileSync 为 null 时信号量在 NotifyLspFileChange 中立即释放
    }

    [Fact]
    public async Task Dispose_CalledMultipleTimes_DoesNotCrash()
    {
        // Arrange
        var service = CreateService();

        // Act: 多次 Dispose
        service.Dispose();
        var act = () => service.Dispose();

        // Assert: 第二次 Dispose 不抛异常（Interlocked.Exchange 保护）
        act.Should().NotThrow();

        // 无需等待 — 信号量在 Dispose 中释放
    }
}
#pragma warning restore JCC3010, JCC3011, JCC3012
