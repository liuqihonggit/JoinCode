namespace Abs.Tests.Security;

/// <summary>
/// <see cref="ShellCommand.SplitSubcommands"/> 的边界测试。
/// <para>
/// 这个方法存在的唯一理由是「复合命令要逐段判定」——分类器按首词查表，
/// 不拆段就会让 <c>echo hi; rm -rf ./build</c> 因首词是 echo 被判 Safe（所有模式直接放行）。
/// 因此拆错方向的两个后果都必须锁住：**该拆的不拆**会让危险子命令逃过判定；
/// **不该拆的乱拆**会把引号里的字符串、管道数据流、heredoc 正文切碎，制造误报。
/// </para>
/// </summary>
public sealed class ShellCommandSplitSubcommandsTests {

    // ── 该拆的：独立子命令 ──

    [Fact]
    public void Semicolon_SplitsIntoTwoSegments() {
        ShellCommand.SplitSubcommands("echo hi; rm -rf ./build")
            .Should().Equal("echo hi", "rm -rf ./build");
    }

    [Fact]
    public void LogicalAnd_SplitsIntoTwoSegments() {
        ShellCommand.SplitSubcommands("true && rm -rf ./build")
            .Should().Equal("true", "rm -rf ./build");
    }

    [Fact]
    public void LogicalOr_SplitsOnce_NotEmptySegments() {
        ShellCommand.SplitSubcommands("false || rm -rf ./build")
            .Should().Equal("false", "rm -rf ./build");
    }

    [Fact]
    public void Newline_SplitsIntoTwoSegments() {
        ShellCommand.SplitSubcommands("echo hi\nrm -rf ./build")
            .Should().Equal("echo hi", "rm -rf ./build");
    }

    [Fact]
    public void EmptySegments_AreDropped() {
        ShellCommand.SplitSubcommands("a;;b;\n c").Should().Equal("a", "b", "c");
    }

    // ── 不该拆的：同一命令的内部结构 ──

    [Fact]
    public void SinglePipe_IsNotASeparator() {
        // 管道两侧是同一命令的数据流；上游有「管道目标是解释器」的专门检查，拆开会绕过它
        ShellCommand.SplitSubcommands("git log | sh").Should().Equal("git log | sh");
    }

    [Fact]
    public void SemicolonInsideSingleQuotes_IsPreserved() {
        ShellCommand.SplitSubcommands("awk '{print $1; $2}' file.txt")
            .Should().Equal("awk '{print $1; $2}' file.txt");
    }

    [Fact]
    public void LogicalAndInsideDoubleQuotes_IsPreserved() {
        ShellCommand.SplitSubcommands("echo \"a && b\"").Should().Equal("echo \"a && b\"");
    }

    [Fact]
    public void EscapedQuoteInsideDoubleQuotes_DoesNotCloseString() {
        ShellCommand.SplitSubcommands("echo \"say \\\"hi; bye\\\"\"")
            .Should().Equal("echo \"say \\\"hi; bye\\\"\"");
    }

    [Fact]
    public void SemicolonInsideCommandSubstitution_IsPreserved() {
        ShellCommand.SplitSubcommands("echo $(date; whoami)").Should().Equal("echo $(date; whoami)");
    }

    [Fact]
    public void SemicolonInsideBackticks_IsPreserved() {
        ShellCommand.SplitSubcommands("echo `date; whoami`").Should().Equal("echo `date; whoami`");
    }

    [Fact]
    public void Heredoc_KeepsNewlinesInsideBody() {
        // heredoc 正文里的 rm 只是被写入文件的内容，不是被执行的命令
        ShellCommand.SplitSubcommands("cat > /tmp/x.sh <<EOF\nrm -rf ./build\nEOF")
            .Should().ContainSingle();
    }

    [Fact]
    public void RedirectWithAmpersand_IsNotMistakenForLogicalAnd() {
        ShellCommand.SplitSubcommands("echo hi 2>&1").Should().Equal("echo hi 2>&1");
    }

    [Fact]
    public void BlankCommand_ReturnsEmpty() {
        ShellCommand.SplitSubcommands("   ").Should().BeEmpty();
    }
}
