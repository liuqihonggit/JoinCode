namespace Hands.Tests.Shell;

/// <summary>
/// ShellSedInterceptMiddleware 单元测试 — 验证 sed 拦截中间件的结构化诊断
/// </summary>
public class ShellSedInterceptMiddlewareTests
{
    [Fact]
    public void BuildFileSystemUnavailableDiagnostic_ReturnsCorrectStructure()
    {
        var diagnostic = ShellSedInterceptMiddleware.BuildFileSystemUnavailableDiagnostic();

        diagnostic.Reason.Should().Be("服务不可用");
        diagnostic.FormattedMessage.Should().Contain("IFileSystem");
    }

    [Fact]
    public void BuildWriteFailedDiagnostic_ReturnsCorrectStructure()
    {
        var diagnostic = ShellSedInterceptMiddleware.BuildWriteFailedDiagnostic("/test/file.txt", "Access denied");

        diagnostic.Reason.Should().Be("写入文件失败");
        diagnostic.FormattedMessage.Should().Contain("Access denied");
        diagnostic.Details.Should().Contain(d => d.Key == "file_path" && d.Value == "/test/file.txt");
        diagnostic.Details.Should().Contain(d => d.Key == "error" && d.Value == "Access denied");
    }

    [Fact]
    public void BuildFileNotFoundDiagnostic_ReturnsCorrectStructure()
    {
        var diagnostic = ShellSedInterceptMiddleware.BuildFileNotFoundDiagnostic("missing.txt");

        diagnostic.Reason.Should().Be("文件未找到");
        diagnostic.FormattedMessage.Should().Contain("missing.txt");
        diagnostic.Details.Should().ContainSingle(d => d.Key == "file_path" && d.Value == "missing.txt");
        diagnostic.Suggestions.Should().HaveCount(2);
    }

    [Fact]
    public void BuildReadFailedDiagnostic_ReturnsCorrectStructure()
    {
        var diagnostic = ShellSedInterceptMiddleware.BuildReadFailedDiagnostic("/test/file.txt", "IO error");

        diagnostic.Reason.Should().Be("读取文件失败");
        diagnostic.FormattedMessage.Should().Contain("IO error");
        diagnostic.Details.Should().Contain(d => d.Key == "file_path" && d.Value == "/test/file.txt");
        diagnostic.Details.Should().Contain(d => d.Key == "error" && d.Value == "IO error");
    }
}
