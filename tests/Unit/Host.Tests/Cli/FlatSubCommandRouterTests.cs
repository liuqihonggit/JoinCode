namespace Host.Tests.Cli;

/// <summary>
/// FlatSubCommandRouter 参数解析测试 — 验证布尔标志不误吞 key=value 参数
/// 场景: AI 常写 `jcc mcp_call tool --trust key=value --json`，--trust 不应吞掉 key=value
/// </summary>
public sealed class FlatSubCommandRouterTests
{
    /// <summary>
    /// 核心回归场景: --trust 后跟 key=value，key=value 不应被吞
    /// </summary>
    [Fact]
    public void GetAllPositional_TrustBeforeKeyValue_ShouldNotConsumeKeyValue()
    {
        var args = new[] { "mcp_call", "gh_pr_checks", "--trust", "pr_number=201", "--json" };

        var positional = FlatSubCommandRouter.GetAllPositional(args, 0);

        positional.Should().NotBeNull();
        positional!.Should().Contain("pr_number=201");
        positional.Should().Contain("gh_pr_checks");
    }

    /// <summary>
    /// --json 后跟 key=value，key=value 不应被吞
    /// </summary>
    [Fact]
    public void GetAllPositional_JsonBeforeKeyValue_ShouldNotConsumeKeyValue()
    {
        var args = new[] { "mcp_call", "gh_pr_checks", "--json", "pr_number=201" };

        var positional = FlatSubCommandRouter.GetAllPositional(args, 0);

        positional.Should().NotBeNull();
        positional!.Should().Contain("pr_number=201");
    }

    /// <summary>
    /// --args-stdin（布尔标志）后跟 key=value，key=value 不应被吞
    /// </summary>
    [Fact]
    public void GetAllPositional_ArgsStdinBeforeKeyValue_ShouldNotConsumeKeyValue()
    {
        var args = new[] { "mcp_call", "read_file", "--args-stdin", "path=/tmp/x" };

        var positional = FlatSubCommandRouter.GetAllPositional(args, 0);

        positional.Should().NotBeNull();
        positional!.Should().Contain("path=/tmp/x");
    }

    /// <summary>
    /// --debuglog（布尔标志）后跟 key=value，key=value 不应被吞
    /// </summary>
    [Fact]
    public void GetAllPositional_DebugLogBeforeKeyValue_ShouldNotConsumeKeyValue()
    {
        var args = new[] { "mcp_call", "read_file", "--debuglog", "path=/tmp/x" };

        var positional = FlatSubCommandRouter.GetAllPositional(args, 0);

        positional.Should().NotBeNull();
        positional!.Should().Contain("path=/tmp/x");
    }

    /// <summary>
    /// 带值选项 --model 后跟值，值应被正确吞掉，后续 key=value 保留
    /// </summary>
    [Fact]
    public void GetAllPositional_ModelWithValue_ShouldConsumeValueButKeepKeyValue()
    {
        var args = new[] { "mcp_call", "read_file", "--model", "gpt-4o", "path=/tmp/x" };

        var positional = FlatSubCommandRouter.GetAllPositional(args, 0);

        positional.Should().NotBeNull();
        positional!.Should().Contain("read_file");
        positional.Should().Contain("path=/tmp/x");
        positional.Should().NotContain("gpt-4o");
    }

    /// <summary>
    /// 带值选项 --args-file 后跟值，值应被正确吞掉
    /// </summary>
    [Fact]
    public void GetAllPositional_ArgsFileWithValue_ShouldConsumeValue()
    {
        var args = new[] { "mcp_call", "read_file", "--args-file", "args.json", "extra_kv=1" };

        var positional = FlatSubCommandRouter.GetAllPositional(args, 0);

        positional.Should().NotBeNull();
        positional!.Should().Contain("read_file");
        positional.Should().Contain("extra_kv=1");
        positional.Should().NotContain("args.json");
    }

    /// <summary>
    /// 多个布尔标志连续出现，不互相干扰
    /// </summary>
    [Fact]
    public void GetAllPositional_MultipleBooleanFlags_ShouldNotConsumeAnything()
    {
        var args = new[] { "mcp_call", "tool", "--trust", "--json", "--debuglog", "key=value" };

        var positional = FlatSubCommandRouter.GetAllPositional(args, 0);

        positional.Should().NotBeNull();
        positional!.Should().Contain("tool");
        positional.Should().Contain("key=value");
    }

    /// <summary>
    /// key=value 在布尔标志前面，正常识别
    /// </summary>
    [Fact]
    public void GetAllPositional_KeyValueBeforeBooleanFlag_ShouldWork()
    {
        var args = new[] { "mcp_call", "tool", "key=value", "--trust", "--json" };

        var positional = FlatSubCommandRouter.GetAllPositional(args, 0);

        positional.Should().NotBeNull();
        positional!.Should().Contain("tool");
        positional.Should().Contain("key=value");
    }

    /// <summary>
    /// GetPositional 取第一个位置参数（工具名），布尔标志不干扰
    /// </summary>
    [Fact]
    public void GetPositional_ToolNameWithTrustBeforeKeyValue_ShouldReturnToolName()
    {
        var args = new[] { "mcp_call", "gh_pr_checks", "--trust", "pr_number=201", "--json" };

        var toolName = FlatSubCommandRouter.GetPositional(args, 0);

        toolName.Should().Be("gh_pr_checks");
    }

    /// <summary>
    /// 用户原始失败场景: `jcc mcp_call gh_pr_checks --trust pr_number=201 --json`
    /// GetAllPositional 应返回 ["gh_pr_checks", "pr_number=201"]，pr_number=201 不丢失
    /// </summary>
    [Fact]
    public void GetAllPositional_OriginalFailingScenario_ShouldPreservePrNumber()
    {
        var args = new[] { "mcp_call", "gh_pr_checks", "--trust", "pr_number=201", "--json" };

        var positional = FlatSubCommandRouter.GetAllPositional(args, 0);

        positional.Should().NotBeNull();
        positional!.Should().HaveCount(2);
        positional[0].Should().Be("gh_pr_checks");
        positional[1].Should().Be("pr_number=201");
    }

    /// <summary>
    /// CliArgConstants.BooleanFlags 应包含 --trust/--json/--args-stdin/--debuglog 等布尔标志
    /// </summary>
    [Fact]
    public void CliArgConstants_BooleanFlags_ShouldContainKnownBooleanFlags()
    {
        CliArgConstants.BooleanFlags.Contains("--trust").Should().BeTrue();
        CliArgConstants.BooleanFlags.Contains("--json").Should().BeTrue();
        CliArgConstants.BooleanFlags.Contains("--args-stdin").Should().BeTrue();
        CliArgConstants.BooleanFlags.Contains("--debuglog").Should().BeTrue();
        CliArgConstants.BooleanFlags.Contains("-d").Should().BeTrue();
    }

    /// <summary>
    /// CliArgConstants.BooleanFlags 不应包含带值选项
    /// </summary>
    [Fact]
    public void CliArgConstants_BooleanFlags_ShouldNotContainValueOptions()
    {
        CliArgConstants.BooleanFlags.Contains("--model").Should().BeFalse();
        CliArgConstants.BooleanFlags.Contains("--vendor").Should().BeFalse();
        CliArgConstants.BooleanFlags.Contains("--args-file").Should().BeFalse();
        CliArgConstants.BooleanFlags.Contains("--port").Should().BeFalse();
    }

    /// <summary>
    /// 大小写不敏感 — PowerShell 可能传入不同大小写
    /// </summary>
    [Fact]
    public void CliArgConstants_BooleanFlags_ShouldBeCaseInsensitive()
    {
        CliArgConstants.BooleanFlags.Contains("--TRUST").Should().BeTrue();
        CliArgConstants.BooleanFlags.Contains("--Json").Should().BeTrue();
    }

    /// <summary>
    /// 未知 --flag 应被检测并返回错误信息，不静默吞掉
    /// </summary>
    [Fact]
    public void DetectUnknownOptions_UnknownFlag_ShouldReturnError()
    {
        var args = new[] { "mcp_call", "gh_pr_checks", "--unknown-flag", "pr_number=201" };

        var error = FlatSubCommandRouter.DetectUnknownOptions(args);

        error.Should().NotBeNull();
        error!.Should().Contain("error:");
        error.Should().Contain("--unknown-flag");
        error.Should().Contain("未知选项");
    }

    /// <summary>
    /// 已知 --flag 不应报错
    /// </summary>
    [Fact]
    public void DetectUnknownOptions_KnownFlag_ShouldReturnNull()
    {
        var args = new[] { "mcp_call", "gh_pr_checks", "--trust", "pr_number=201", "--json" };

        var error = FlatSubCommandRouter.DetectUnknownOptions(args);

        error.Should().BeNull();
    }

    /// <summary>
    /// Rust 风格错误格式应包含位置指示线(^)和 hint
    /// </summary>
    [Fact]
    public void DetectUnknownOptions_ErrorFormat_ShouldHaveRustStyleIndicator()
    {
        var args = new[] { "mcp_call", "tool", "--bad-flag" };

        var error = FlatSubCommandRouter.DetectUnknownOptions(args);

        error.Should().NotBeNull();
        error!.Should().Contain("  |");
        error.Should().Contain("^");
        error.Should().Contain("hint:");
    }

    /// <summary>
    /// CliErrorFormatter.FormatKeyValueError — key= (空值) 应报 '=' 后面不能为空
    /// </summary>
    [Fact]
    public void CliErrorFormatter_KeyValueEmptyValue_ShouldShowRustStyleError()
    {
        var error = CliErrorFormatter.FormatKeyValueError("pr_number=", "'=' 后面不能为空", "使用 key=value 传递工具参数，如 pr_number=201");

        error.Should().Contain("error: 参数格式错误");
        error.Should().Contain("  |");
        error.Should().Contain("pr_number=");
        error.Should().Contain("^");
        error.Should().Contain("'=' 后面不能为空");
        error.Should().Contain("hint:");
    }

    /// <summary>
    /// CliErrorFormatter.FormatError — 完整命令行 + 位置指示
    /// </summary>
    [Fact]
    public void CliErrorFormatter_FormatError_ShouldShowCommandLineAndArrow()
    {
        var args = new[] { "mcp_call", "tool", "--bad-flag", "key=value" };

        var error = CliErrorFormatter.FormatError(args, 2, "未知选项", "未知选项", "可用选项见 jcc --help");

        error.Should().Contain("error: 未知选项");
        error.Should().Contain("mcp_call tool --bad-flag key=value");
        error.Should().Contain("^^^^^^^^^^");
        error.Should().Contain("hint: 可用选项见 jcc --help");
    }

    /// <summary>
    /// AllOptionNames 应包含所有已知选项(布尔+带值)
    /// </summary>
    [Fact]
    public void CliArgConstants_AllOptionNames_ShouldContainAllKnownOptions()
    {
        CliArgConstants.AllOptionNames.Contains("--trust").Should().BeTrue();
        CliArgConstants.AllOptionNames.Contains("--json").Should().BeTrue();
        CliArgConstants.AllOptionNames.Contains("--model").Should().BeTrue();
        CliArgConstants.AllOptionNames.Contains("--args-file").Should().BeTrue();
        CliArgConstants.AllOptionNames.Contains("--vendor").Should().BeTrue();
        CliArgConstants.AllOptionNames.Contains("-m").Should().BeTrue();
        CliArgConstants.AllOptionNames.Contains("-d").Should().BeTrue();
    }
}
