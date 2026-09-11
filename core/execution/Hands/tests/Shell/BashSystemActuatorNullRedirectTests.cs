namespace Services.SystemActuator;

/// <summary>
/// BashSystemActuator.RewriteWindowsNullRedirect 单元测试 — 验证 Windows NUL 重定向改写为 /dev/null。
/// <para>BUG#10: bash 不识别 nul 特殊设备名,会创建普通文件。所有变体都应改写。</para>
/// </summary>
public sealed class BashSystemActuatorNullRedirectTests
{
    [Fact]
    public void Rewrite_2GreaterThanNul_ShouldReplaceWithDevNull()
    {
        var result = BashSystemActuator.RewriteWindowsNullRedirect("echo test 2>nul");
        result.Should().Be("echo test 2>/dev/null");
    }

    [Fact]
    public void Rewrite_GreaterThanNul_ShouldReplaceWithDevNull()
    {
        var result = BashSystemActuator.RewriteWindowsNullRedirect("echo test >nul");
        result.Should().Be("echo test >/dev/null");
    }

    [Fact]
    public void Rewrite_1GreaterThanNul_ShouldReplaceWithDevNull()
    {
        var result = BashSystemActuator.RewriteWindowsNullRedirect("echo test 1>nul");
        result.Should().Be("echo test 1>/dev/null");
    }

    [Fact]
    public void Rewrite_2DoubleGreaterThanNul_ShouldReplaceWithDevNull()
    {
        var result = BashSystemActuator.RewriteWindowsNullRedirect("echo test 2>>nul");
        result.Should().Be("echo test 2>>/dev/null");
    }

    [Fact]
    public void Rewrite_DoubleGreaterThanNul_ShouldReplaceWithDevNull()
    {
        var result = BashSystemActuator.RewriteWindowsNullRedirect("echo test >>nul");
        result.Should().Be("echo test >>/dev/null");
    }

    [Fact]
    public void Rewrite_LessThanNul_ShouldReplaceWithDevNull()
    {
        var result = BashSystemActuator.RewriteWindowsNullRedirect("cat <nul");
        result.Should().Be("cat </dev/null");
    }

    [Fact]
    public void Rewrite_0LessThanNul_ShouldReplaceWithDevNull()
    {
        var result = BashSystemActuator.RewriteWindowsNullRedirect("cat 0<nul");
        result.Should().Be("cat 0</dev/null");
    }

    [Fact]
    public void Rewrite_MultipleNulRedirects_ShouldReplaceAll()
    {
        var result = BashSystemActuator.RewriteWindowsNullRedirect("echo a 2>nul; echo b >nul; echo c 1>nul");
        result.Should().Be("echo a 2>/dev/null; echo b >/dev/null; echo c 1>/dev/null");
    }

    [Fact]
    public void Rewrite_NulWithSpaces_ShouldHandleWhitespace()
    {
        var result = BashSystemActuator.RewriteWindowsNullRedirect("echo test 2> nul");
        result.Should().Be("echo test 2>/dev/null");
    }

    [Fact]
    public void Rewrite_NoNul_ShouldReturnUnchanged()
    {
        var result = BashSystemActuator.RewriteWindowsNullRedirect("echo test 2>/dev/null");
        result.Should().Be("echo test 2>/dev/null");
    }

    [Fact]
    public void Rewrite_NulAsArgument_ShouldNotReplace()
    {
        var result = BashSystemActuator.RewriteWindowsNullRedirect("echo nul");
        result.Should().Be("echo nul");
    }

    [Theory]
    [InlineData("2>nul")]
    [InlineData("2>NUL")]
    [InlineData("2>Nul")]
    public void Rewrite_CaseInsensitive_ShouldReplaceAllCases(string redirect)
    {
        var result = BashSystemActuator.RewriteWindowsNullRedirect($"echo test {redirect}");
        result.Should().Contain("/dev/null");
    }
}
