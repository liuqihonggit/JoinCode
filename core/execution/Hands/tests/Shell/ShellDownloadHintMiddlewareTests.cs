namespace Hands.Tests.Shell;

/// <summary>
/// ShellDownloadHintMiddleware 单元测试 — 验证 curl/wget 下载检测、提示构建、--no-intercept 放行
/// </summary>
public class ShellDownloadHintMiddlewareTests
{
    // === IsDownloadCommand ===

    [Theory]
    [InlineData("curl -o file.zip https://example.com/file.zip", true)]
    [InlineData("curl https://example.com/file.zip -o file.zip", true)]
    [InlineData("curl --output file.zip https://example.com/file.zip", true)]
    [InlineData("curl -L -o file.zip https://example.com/file.zip", true)]
    [InlineData("wget https://example.com/file.zip", true)]
    [InlineData("wget -O file.zip https://example.com/file.zip", true)]
    [InlineData("wget --output-document=file.zip https://example.com/file.zip", true)]
    [InlineData("curl https://example.com/file.zip", false)]
    [InlineData("curl -o - https://example.com/file.zip", false)]
    [InlineData("curl --output - https://example.com/file.zip", false)]
    [InlineData("wget -O - https://example.com/file.zip", false)]
    [InlineData("wget --output-document=- https://example.com/file.zip", false)]
    [InlineData("dotnet build", false)]
    [InlineData("echo hello", false)]
    [InlineData("git pull", false)]
    [InlineData("", false)]
    public void IsDownloadCommand_DetectsCorrectly(string command, bool expected)
    {
        ShellDownloadHintMiddleware.IsDownloadCommand(command).Should().Be(expected);
    }

    // === StripNoInterceptFlag ===

    [Fact]
    public void StripNoInterceptFlag_RemovesFlagFromMiddle()
    {
        var result = ShellDownloadHintMiddleware.StripNoInterceptFlag("curl --no-intercept -o file.zip https://example.com/file.zip");
        result.Should().Be("curl -o file.zip https://example.com/file.zip");
    }

    [Fact]
    public void StripNoInterceptFlag_RemovesFlagFromEnd()
    {
        var result = ShellDownloadHintMiddleware.StripNoInterceptFlag("curl -o file.zip https://example.com/file.zip --no-intercept");
        result.Should().Be("curl -o file.zip https://example.com/file.zip");
    }

    [Fact]
    public void StripNoInterceptFlag_NoFlag_ReturnsOriginal()
    {
        var result = ShellDownloadHintMiddleware.StripNoInterceptFlag("curl -o file.zip https://example.com/file.zip");
        result.Should().Be("curl -o file.zip https://example.com/file.zip");
    }

    // === BuildDownloadHint ===

    [Fact]
    public void BuildDownloadHint_ContainsDownloadFileToolName()
    {
        var hint = ShellDownloadHintMiddleware.BuildDownloadHint("curl -o file.zip https://example.com/file.zip");
        hint.Should().Contain("download_file");
    }

    [Fact]
    public void BuildDownloadHint_ContainsNoInterceptFlag()
    {
        var hint = ShellDownloadHintMiddleware.BuildDownloadHint("curl -o file.zip https://example.com/file.zip");
        hint.Should().Contain("--no-intercept");
    }

    [Fact]
    public void BuildDownloadHint_ContainsOriginalCommand()
    {
        var command = "curl -o file.zip https://example.com/file.zip";
        var hint = ShellDownloadHintMiddleware.BuildDownloadHint(command);
        hint.Should().Contain(command);
    }

    [Fact]
    public void BuildDownloadHint_ContainsMultithreadHint()
    {
        var hint = ShellDownloadHintMiddleware.BuildDownloadHint("wget https://example.com/file.zip");
        hint.Should().Contain("多线程").And.Contain("断点续传");
    }
}
