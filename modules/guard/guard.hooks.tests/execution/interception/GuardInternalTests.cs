namespace Guard.Hooks.Tests;

/// <summary>
/// 守卫内部纯函数确定性测试 — GitCommitGuard/CmdIndirectCallGuard/HeredocGuard/GhPrBodyGuard。
/// <para>VpnRouteGuard.DetectVpn 跳过 — 依赖 Process.GetProcessesByName/Environment.GetEnvironmentVariable,
/// 属于不可控外部状态,不适合确定性测试。</para>
/// </summary>
public class GuardInternalTests {

    #region GitCommitGuard.ExtractFirstToken

    [Fact]
    public void ExtractFirstToken_SimpleToken_Should_Extract() {
        var (token, remaining) = GitCommitGuard.ExtractFirstToken("git commit");
        token.Should().Be("git");
        remaining.Should().Be("commit");
    }

    [Fact]
    public void ExtractFirstToken_QuotedToken_Should_Extract_Without_Quotes() {
        var (token, remaining) = GitCommitGuard.ExtractFirstToken("\"git\" commit");
        token.Should().Be("git");
        remaining.Should().Be(" commit");
    }

    [Fact]
    public void ExtractFirstToken_SingleToken_Should_Return_Empty_Remaining() {
        var (token, remaining) = GitCommitGuard.ExtractFirstToken("git");
        token.Should().Be("git");
        remaining.Should().BeEmpty();
    }

    [Fact]
    public void ExtractFirstToken_EmptyString_Should_Return_Null() {
        var (token, remaining) = GitCommitGuard.ExtractFirstToken("");
        token.Should().BeNull();
    }

    [Fact]
    public void ExtractFirstToken_WhitespaceOnly_Should_Return_Null() {
        var (token, _) = GitCommitGuard.ExtractFirstToken("   ");
        token.Should().BeNull();
    }

    [Fact]
    public void ExtractFirstToken_UnclosedQuote_Should_Return_Null() {
        var (token, _) = GitCommitGuard.ExtractFirstToken("\"git commit");
        token.Should().BeNull();
    }

    [Fact]
    public void ExtractFirstToken_LeadingWhitespace_Should_Be_Trimmed() {
        var (token, remaining) = GitCommitGuard.ExtractFirstToken("   git commit");
        token.Should().Be("git");
        remaining.Should().Be("commit");
    }

    #endregion

    #region GitCommitGuard.IsGitCommitSubCommand

    [Theory]
    [InlineData("git", "commit", true)]
    [InlineData("git.exe", "commit", true)]
    [InlineData("GIT", "commit", true)]
    [InlineData("/usr/bin/git", "commit", true)]
    [InlineData("git", "status", false)]
    [InlineData("git", "Commit", true)]       // 大小写不敏感
    [InlineData("rm", "commit", false)]        // 非 git
    [InlineData("github", "commit", false)]    // 非 git
    public void IsGitCommitSubCommand_VariousInputs(string executable, string subCommand, bool expected) {
        GitCommitGuard.IsGitCommitSubCommand(executable, subCommand).Should().Be(expected);
    }

    #endregion

    #region CmdIndirectCallGuard.TryExtractCmdInner

    [Fact]
    public void TryExtractCmdInner_CmdC_Should_Extract_Inner() {
        var success = CmdIndirectCallGuard.TryExtractCmdInner("cmd /c echo hello".AsSpan(), out var inner);
        success.Should().BeTrue();
        inner.Should().Be("echo hello");
    }

    [Fact]
    public void TryExtractCmdInner_CmdK_Should_Extract_Inner() {
        var success = CmdIndirectCallGuard.TryExtractCmdInner("cmd /k dir".AsSpan(), out var inner);
        success.Should().BeTrue();
        inner.Should().Be("dir");
    }

    [Fact]
    public void TryExtractCmdInner_QuotedInner_Should_Extract_Without_Quotes() {
        var success = CmdIndirectCallGuard.TryExtractCmdInner("cmd /c \"echo hello\"".AsSpan(), out var inner);
        success.Should().BeTrue();
        inner.Should().Be("echo hello");
    }

    [Fact]
    public void TryExtractCmdInner_NoSubcommand_Should_Return_False() {
        var success = CmdIndirectCallGuard.TryExtractCmdInner("cmd".AsSpan(), out var inner);
        success.Should().BeFalse();
        inner.Should().BeNull();
    }

    [Fact]
    public void TryExtractCmdInner_NoSwitch_Should_Return_False() {
        var success = CmdIndirectCallGuard.TryExtractCmdInner("cmd echo hello".AsSpan(), out var inner);
        success.Should().BeFalse();
    }

    [Fact]
    public void TryExtractCmdInner_NonCmd_Should_Return_False() {
        var success = CmdIndirectCallGuard.TryExtractCmdInner("powershell -Command echo".AsSpan(), out var inner);
        success.Should().BeFalse();
    }

    [Fact]
    public void TryExtractCmdInner_EmptyInner_Should_Return_False() {
        var success = CmdIndirectCallGuard.TryExtractCmdInner("cmd /c".AsSpan(), out var inner);
        success.Should().BeFalse();
    }

    #endregion

    #region CmdIndirectCallGuard.TryExtractPwshInner

    [Fact]
    public void TryExtractPwshInner_PowershellCommand_Should_Extract() {
        var success = CmdIndirectCallGuard.TryExtractPwshInner("powershell -Command Get-Process".AsSpan(), out var inner);
        success.Should().BeTrue();
        inner.Should().Be("Get-Process");
    }

    [Fact]
    public void TryExtractPwshInner_PwshCommand_Should_Extract() {
        var success = CmdIndirectCallGuard.TryExtractPwshInner("pwsh -Command echo hi".AsSpan(), out var inner);
        success.Should().BeTrue();
        inner.Should().Be("echo hi");
    }

    [Fact]
    public void TryExtractPwshInner_QuotedInner_Should_Extract_Without_Quotes() {
        var success = CmdIndirectCallGuard.TryExtractPwshInner("powershell -Command \"Get-Process\"".AsSpan(), out var inner);
        success.Should().BeTrue();
        inner.Should().Be("Get-Process");
    }

    [Fact]
    public void TryExtractPwshInner_NoCommand_Should_Return_False() {
        var success = CmdIndirectCallGuard.TryExtractPwshInner("powershell".AsSpan(), out var inner);
        success.Should().BeFalse();
    }

    [Fact]
    public void TryExtractPwshInner_NoSwitch_Should_Return_False() {
        var success = CmdIndirectCallGuard.TryExtractPwshInner("powershell Get-Process".AsSpan(), out var inner);
        success.Should().BeFalse();
    }

    [Fact]
    public void TryExtractPwshInner_NonPwsh_Should_Return_False() {
        var success = CmdIndirectCallGuard.TryExtractPwshInner("cmd /c echo".AsSpan(), out var inner);
        success.Should().BeFalse();
    }

    #endregion

    #region CmdIndirectCallGuard.ExtractQuotedOrRaw

    [Fact]
    public void ExtractQuotedOrRaw_Quoted_Should_Extract_Without_Quotes() {
        var result = CmdIndirectCallGuard.ExtractQuotedOrRaw("\"hello world\"".AsSpan());
        result.Should().Be("hello world");
    }

    [Fact]
    public void ExtractQuotedOrRaw_Raw_Should_Return_As_Is() {
        var result = CmdIndirectCallGuard.ExtractQuotedOrRaw("hello world".AsSpan());
        result.Should().Be("hello world");
    }

    [Fact]
    public void ExtractQuotedOrRaw_UnclosedQuote_Should_Return_Content() {
        var result = CmdIndirectCallGuard.ExtractQuotedOrRaw("\"hello world".AsSpan());
        result.Should().Be("hello world");
    }

    [Fact]
    public void ExtractQuotedOrRaw_Empty_Should_Return_Empty() {
        var result = CmdIndirectCallGuard.ExtractQuotedOrRaw("".AsSpan());
        result.Should().BeEmpty();
    }

    #endregion

    #region HeredocGuard.EscapeForDoubleQuotedString

    [Fact]
    public void EscapeForDoubleQuotedString_NoSpecialChars_Should_Return_Trimmed() {
        HeredocGuard.EscapeForDoubleQuotedString("hello world").Should().Be("hello world");
    }

    [Fact]
    public void EscapeForDoubleQuotedString_Backslash_Should_Be_Escaped() {
        HeredocGuard.EscapeForDoubleQuotedString("path\\to\\file").Should().Be("path\\\\to\\\\file");
    }

    [Fact]
    public void EscapeForDoubleQuotedString_DoubleQuote_Should_Be_Escaped() {
        HeredocGuard.EscapeForDoubleQuotedString("say \"hi\"").Should().Be("say \\\"hi\\\"");
    }

    [Fact]
    public void EscapeForDoubleQuotedString_Crlf_Should_Be_Converted_To_Lf() {
        HeredocGuard.EscapeForDoubleQuotedString("line1\r\nline2").Should().Be("line1\nline2");
    }

    [Fact]
    public void EscapeForDoubleQuotedString_Should_Be_Trimmed() {
        HeredocGuard.EscapeForDoubleQuotedString("  hello  ").Should().Be("hello");
    }

    [Fact]
    public void EscapeForDoubleQuotedString_Empty_Should_Return_Empty() {
        HeredocGuard.EscapeForDoubleQuotedString("").Should().BeEmpty();
    }

    [Fact]
    public void EscapeForDoubleQuotedString_DollarSign_Should_Be_Escaped() {
        HeredocGuard.EscapeForDoubleQuotedString("$var").Should().Be("\\$var");
    }

    [Fact]
    public void EscapeForDoubleQuotedString_DollarInContent_Should_Be_Escaped() {
        HeredocGuard.EscapeForDoubleQuotedString("echo $HOME").Should().Be("echo \\$HOME");
    }

    #endregion

    #region GhPrBodyGuard.HasBodyParameter

    [Theory]
    [InlineData("gh pr create --body \"hello\"", true)]
    [InlineData("gh pr create -b \"hello\"", true)]
    [InlineData("gh pr create --body=hello", true)]
    [InlineData("gh pr create", false)]
    [InlineData("gh pr create --title test", false)]
    public void HasBodyParameter_VariousInputs(string command, bool expected) {
        GhPrBodyGuard.HasBodyParameter(command).Should().Be(expected);
    }

    [Fact]
    public void HasBodyParameter_CaseInsensitive_Should_Detect() {
        GhPrBodyGuard.HasBodyParameter("gh pr create --BODY hello").Should().BeTrue();
    }

    #endregion

    #region GhPrBodyGuard.EscapeBody

    [Fact]
    public void EscapeBody_NoSpecialChars_Should_Return_As_Is() {
        GhPrBodyGuard.EscapeBody("hello world").Should().Be("hello world");
    }

    [Fact]
    public void EscapeBody_Backslash_Should_Be_Escaped() {
        GhPrBodyGuard.EscapeBody("path\\file").Should().Be("path\\\\file");
    }

    [Fact]
    public void EscapeBody_DoubleQuote_Should_Be_Escaped() {
        GhPrBodyGuard.EscapeBody("say \"hi\"").Should().Be("say \\\"hi\\\"");
    }

    [Fact]
    public void EscapeBody_Newline_Should_Be_Escaped() {
        GhPrBodyGuard.EscapeBody("line1\nline2").Should().Be("line1\\nline2");
    }

    [Fact]
    public void EscapeBody_CarriageReturn_Should_Be_Escaped() {
        GhPrBodyGuard.EscapeBody("line1\rline2").Should().Be("line1\\rline2");
    }

    [Fact]
    public void EscapeBody_AllSpecial_Should_Be_Escaped() {
        GhPrBodyGuard.EscapeBody("\"\\\n\r").Should().Be("\\\"\\\\\\n\\r");
    }

    #endregion

    #region VpnRouteGuard.DetectVpn — 跳过

    // VpnRouteGuard.DetectVpn 跳过确定性测试 — 依赖 Process.GetProcessesByName/Environment.GetEnvironmentVariable,
    // 属于不可控外部状态(进程列表/环境变量随时变化),不适合确定性测试。
    // 如需测试 VPN 检测逻辑,应通过 INetworkConnectivityService 接口 mock 注入。

    #endregion
}
