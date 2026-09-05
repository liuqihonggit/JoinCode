namespace JoinCode.Hands.Desktop.Tests;

/// <summary>
/// MacroToolHandlers 单元测试 — 验证 list_macros 等宏工具逻辑
/// </summary>
public sealed class MacroToolHandlersTests
{
    /// <summary>空字符串 directory 应回退到默认目录，而非传空路径给 FileSystem 抛异常</summary>
    [Fact]
    public async Task ListMacros_EmptyDirectory_FallsBackToDefault()
    {
        var recorderMock = new Mock<IMacroRecorder>();
        var fsMock = new Mock<IFileSystem>();
        fsMock.Setup(f => f.DirectoryExists(It.IsAny<string>())).Returns(true);
        fsMock.Setup(f => f.GetFiles(It.IsAny<string>(), "*.json", SearchOption.TopDirectoryOnly))
            .Returns(Array.Empty<string>());
        var handler = new MacroToolHandlers(recorderMock.Object, fsMock.Object);

        await handler.ListMacrosAsync("");

        fsMock.Verify(f => f.GetFiles(It.Is<string>(p => !string.IsNullOrEmpty(p)), "*.json", SearchOption.TopDirectoryOnly), Times.Once);
    }
}
