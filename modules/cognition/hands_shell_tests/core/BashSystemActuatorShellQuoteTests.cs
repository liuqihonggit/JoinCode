namespace Hands.Tests.Shell;

/// <summary>
/// C5: JCC_SHELL_PREFIX ShellQuote 测试 — 验证前缀中的 shell 元字符被引号包裹,不被 shell 解释
/// </summary>
public class BashSystemActuatorShellQuoteTest {
    [Theory]
    [InlineData("nice", "'nice'")]
    [InlineData("nice -n 10", "'nice' '-n' '10'")]
    [InlineData("timeout 30", "'timeout' '30'")]
    public void ShellQuotePrefix_MultiWordPrefix_EachWordQuoted(string prefix, string expected) {
        BashSystemActuator.ShellQuotePrefix(prefix).Should().Be(expected);
    }

    [Theory]
    [InlineData("; rm -rf /", "';' 'rm' '-rf' '/'")]
    [InlineData("$(whoami)", "'$(whoami)'")]
    [InlineData("`whoami`", "'`whoami`'")]
    [InlineData("foo && bar", "'foo' '&&' 'bar'")]
    [InlineData("foo | bar", "'foo' '|' 'bar'")]
    public void ShellQuotePrefix_ShellMetacharacters_Quoted_NotInterpreted(string prefix, string expected) {
        BashSystemActuator.ShellQuotePrefix(prefix).Should().Be(expected);
    }

    [Fact]
    public void ShellQuotePrefix_SingleQuoteInPrefix_Escaped() {
        var result = BashSystemActuator.ShellQuotePrefix("echo 'hello'");
        result.Should().Contain("'\\''");
    }

    [Fact]
    public void ShellQuotePrefix_EmptyString_ReturnsEmpty() {
        BashSystemActuator.ShellQuotePrefix("").Should().BeEmpty();
    }

    [Fact]
    public void ShellQuotePrefix_OnlySpaces_ReturnsEmpty() {
        BashSystemActuator.ShellQuotePrefix("   ").Should().BeEmpty();
    }
}
