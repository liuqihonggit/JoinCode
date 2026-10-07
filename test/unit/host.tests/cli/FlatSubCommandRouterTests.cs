namespace Host.Tests.Cli;

/// <summary>
/// FlatSubCommandRouter 参数解析测试 — 验证布尔标志不误吞 key=value 参数
/// 场景: AI 常写 `jcc mcp_call tool --trust key=value --json`，--trust 不应吞掉 key=value
/// </summary>
public sealed class FlatSubCommandRouterTests {
    /// <summary>
    /// 核心回归场景: --trust 后跟 key=value，key=value 不应被吞
    /// </summary>
    [Fact]
    public void GetAllPositional_TrustBeforeKeyValue_ShouldNotConsumeKeyValue() {
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
    public void GetAllPositional_JsonBeforeKeyValue_ShouldNotConsumeKeyValue() {
        var args = new[] { "mcp_call", "gh_pr_checks", "--json", "pr_number=201" };

        var positional = FlatSubCommandRouter.GetAllPositional(args, 0);

        positional.Should().NotBeNull();
        positional!.Should().Contain("pr_number=201");
    }

    /// <summary>
    /// --args-stdin（布尔标志）后跟 key=value，key=value 不应被吞
    /// </summary>
    [Fact]
    public void GetAllPositional_ArgsStdinBeforeKeyValue_ShouldNotConsumeKeyValue() {
        var args = new[] { "mcp_call", "read_file", "--args-stdin", "path=/tmp/x" };

        var positional = FlatSubCommandRouter.GetAllPositional(args, 0);

        positional.Should().NotBeNull();
        positional!.Should().Contain("path=/tmp/x");
    }

    /// <summary>
    /// --debuglog（布尔标志）后跟 key=value，key=value 不应被吞
    /// </summary>
    [Fact]
    public void GetAllPositional_DebugLogBeforeKeyValue_ShouldNotConsumeKeyValue() {
        var args = new[] { "mcp_call", "read_file", "--debuglog", "path=/tmp/x" };

        var positional = FlatSubCommandRouter.GetAllPositional(args, 0);

        positional.Should().NotBeNull();
        positional!.Should().Contain("path=/tmp/x");
    }

    /// <summary>
    /// 带值选项 --model 后跟值，值应被正确吞掉，后续 key=value 保留
    /// </summary>
    [Fact]
    public void GetAllPositional_ModelWithValue_ShouldConsumeValueButKeepKeyValue() {
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
    public void GetAllPositional_ArgsFileWithValue_ShouldConsumeValue() {
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
    public void GetAllPositional_MultipleBooleanFlags_ShouldNotConsumeAnything() {
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
    public void GetAllPositional_KeyValueBeforeBooleanFlag_ShouldWork() {
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
    public void GetPositional_ToolNameWithTrustBeforeKeyValue_ShouldReturnToolName() {
        var args = new[] { "mcp_call", "gh_pr_checks", "--trust", "pr_number=201", "--json" };

        var toolName = FlatSubCommandRouter.GetPositional(args, 0);

        toolName.Should().Be("gh_pr_checks");
    }

    /// <summary>
    /// 用户原始失败场景: `jcc mcp_call gh_pr_checks --trust pr_number=201 --json`
    /// GetAllPositional 应返回 ["gh_pr_checks", "pr_number=201"]，pr_number=201 不丢失
    /// </summary>
    [Fact]
    public void GetAllPositional_OriginalFailingScenario_ShouldPreservePrNumber() {
        var args = new[] { "mcp_call", "gh_pr_checks", "--trust", "pr_number=201", "--json" };

        var positional = FlatSubCommandRouter.GetAllPositional(args, 0);

        positional.Should().NotBeNull();
        positional!.Should().HaveCount(2);
        positional[0].Should().Be("gh_pr_checks");
        positional[1].Should().Be("pr_number=201");
    }

    /// <summary>
    /// CliArgCliOptionConstants.BooleanFlags 应包含 --trust/--json/--debuglog 等全局布尔标志
    /// ToolCallArgCliOptionConstants.BooleanFlags 应包含 --args-stdin 子命令布尔标志
    /// </summary>
    [Fact]
    public void CliArgConstants_BooleanFlags_ShouldContainKnownBooleanFlags() {
        CliArgCliOptionConstants.BooleanFlags.Contains("--trust").Should().BeTrue();
        CliArgCliOptionConstants.BooleanFlags.Contains("--json").Should().BeTrue();
        CliArgCliOptionConstants.BooleanFlags.Contains("--debuglog").Should().BeTrue();
        CliArgCliOptionConstants.BooleanFlags.Contains("-d").Should().BeTrue();
        ToolCallArgCliOptionConstants.BooleanFlags.Contains("--args-stdin").Should().BeTrue();
    }

    /// <summary>
    /// CliArgCliOptionConstants.BooleanFlags 不应包含带值选项
    /// </summary>
    [Fact]
    public void CliArgConstants_BooleanFlags_ShouldNotContainValueOptions() {
        CliArgCliOptionConstants.BooleanFlags.Contains("--model").Should().BeFalse();
        CliArgCliOptionConstants.BooleanFlags.Contains("--vendor").Should().BeFalse();
        CliArgCliOptionConstants.BooleanFlags.Contains("--args-file").Should().BeFalse();
        CliArgCliOptionConstants.BooleanFlags.Contains("--port").Should().BeFalse();
    }

    /// <summary>
    /// 大小写不敏感 — PowerShell 可能传入不同大小写
    /// </summary>
    [Fact]
    public void CliArgConstants_BooleanFlags_ShouldBeCaseInsensitive() {
        CliArgCliOptionConstants.BooleanFlags.Contains("--TRUST").Should().BeTrue();
        CliArgCliOptionConstants.BooleanFlags.Contains("--Json").Should().BeTrue();
    }

    /// <summary>
    /// 未知 --flag 应被检测并返回错误信息，不静默吞掉
    /// </summary>
    [Fact]
    public void DetectUnknownOptions_UnknownFlag_ShouldReturnError() {
        var args = new[] { "mcp_call", "gh_pr_checks", "--unknown-flag", "pr_number=201" };

        var error = FlatSubCommandRouter.DetectUnknownOptions(args, CliArgCliOptionConstants.AllOptionNames, ToolCallArgCliOptionConstants.AllOptionNames);

        error.Should().NotBeNull();
        error!.Should().Contain("error:");
        error.Should().Contain("--unknown-flag");
        error.Should().Contain("未知选项");
    }

    /// <summary>
    /// 已知 --flag 不应报错
    /// </summary>
    [Fact]
    public void DetectUnknownOptions_KnownFlag_ShouldReturnNull() {
        var args = new[] { "mcp_call", "gh_pr_checks", "--trust", "pr_number=201", "--json" };

        var error = FlatSubCommandRouter.DetectUnknownOptions(args, CliArgCliOptionConstants.AllOptionNames, ToolCallArgCliOptionConstants.AllOptionNames);

        error.Should().BeNull();
    }

    /// <summary>
    /// Rust 风格错误格式应包含位置指示线(^)和 hint
    /// </summary>
    [Fact]
    public void DetectUnknownOptions_ErrorFormat_ShouldHaveRustStyleIndicator() {
        var args = new[] { "mcp_call", "tool", "--bad-flag" };

        var error = FlatSubCommandRouter.DetectUnknownOptions(args, CliArgCliOptionConstants.AllOptionNames);

        error.Should().NotBeNull();
        error!.Should().Contain("  |");
        error.Should().Contain("^");
        error.Should().Contain("hint:");
    }

    /// <summary>
    /// CliErrorCatalog.ArgInvalidKeyValueFormat + ToRustStyleString — key= (空值) 应报 '=' 后面不能为空
    /// </summary>
    [Fact]
    public void CliErrorCatalog_KeyValueEmptyValue_ShouldShowRustStyleError() {
        var error = CliErrorCatalog.ArgInvalidKeyValueFormat("pr_number=", "'=' 后面不能为空").ToRustStyleString("pr_number=");

        error.Should().Contain("error:");
        error.Should().Contain("  |");
        error.Should().Contain("pr_number=");
        error.Should().Contain("^");
        error.Should().Contain("'=' 后面不能为空");
        error.Should().Contain("hint:");
    }

    /// <summary>
    /// CliErrorCatalog.ArgUnknownOption + ToRustStyleString — 完整命令行 + 位置指示
    /// </summary>
    [Fact]
    public void CliErrorCatalog_FormatError_ShouldShowCommandLineAndArrow() {
        var args = new[] { "mcp_call", "tool", "--bad-flag", "key=value" };

        var error = CliErrorCatalog.ArgUnknownOption("--bad-flag").ToRustStyleString(args, 2);

        error.Should().Contain("error:");
        error.Should().Contain("mcp_call tool --bad-flag key=value");
        error.Should().Contain("^^^^^^^^^^");
        error.Should().Contain("hint:");
    }

    /// <summary>
    /// AllOptionNames 应包含所有已知全局选项(布尔+带值)
    /// </summary>
    [Fact]
    public void CliArgConstants_AllOptionNames_ShouldContainAllKnownOptions() {
        CliArgCliOptionConstants.AllOptionNames.Contains("--trust").Should().BeTrue();
        CliArgCliOptionConstants.AllOptionNames.Contains("--json").Should().BeTrue();
        CliArgCliOptionConstants.AllOptionNames.Contains("--model").Should().BeTrue();
        CliArgCliOptionConstants.AllOptionNames.Contains("--vendor").Should().BeTrue();
        CliArgCliOptionConstants.AllOptionNames.Contains("-m").Should().BeTrue();
        CliArgCliOptionConstants.AllOptionNames.Contains("-d").Should().BeTrue();
    }

    /// <summary>
    /// McpList 子命令的 --category 是 [CliOption] 合法选项，传入 McpListArg 白名单不应报错
    /// </summary>
    [Fact]
    public void DetectUnknownOptions_McpListCategory_ShouldNotFlagAsUnknown() {
        var args = new[] { "mcp_list", "--category", "github" };

        var error = FlatSubCommandRouter.DetectUnknownOptions(args, CliArgCliOptionConstants.AllOptionNames, McpListArgCliOptionConstants.AllOptionNames);

        error.Should().BeNull();
    }

    /// <summary>
    /// McpCall 子命令的 --args-file/--args-stdin 是 [CliOption] 合法选项，传入 ToolCallArg 白名单不应报错
    /// </summary>
    [Fact]
    public void DetectUnknownOptions_McpCallToolCallOptions_ShouldNotFlagAsUnknown() {
        var args = new[] { "mcp_call", "read_file", "--args-file", "args.json" };

        var error = FlatSubCommandRouter.DetectUnknownOptions(args, CliArgCliOptionConstants.AllOptionNames, ToolCallArgCliOptionConstants.AllOptionNames);

        error.Should().BeNull();
    }

    /// <summary>
    /// McpServe 子命令的 --port/--host/--transport 合法，传入 McpServeArg 白名单不应报错
    /// </summary>
    [Fact]
    public void DetectUnknownOptions_McpServeOptions_ShouldNotFlagAsUnknown() {
        var args = new[] { "mcp_serve", "--port", "9903", "--host", "localhost", "--transport", "http" };

        var error = FlatSubCommandRouter.DetectUnknownOptions(args, CliArgCliOptionConstants.AllOptionNames, McpServeArgCliOptionConstants.AllOptionNames);

        error.Should().BeNull();
    }

    // ===== ShouldOutputJson: 全局默认输出格式（JCC_OUTPUT_FORMAT 环境变量）=====

    /// <summary>JCC_OUTPUT_FORMAT=text 时默认纯文本输出(降 AI token,优化C)</summary>
    [Fact]
    public void ShouldOutputJson_EnvTextDefault_ReturnsFalse() {
        using var env = EnvVarScope.Set("JCC_OUTPUT_FORMAT", "text");
        FlatSubCommandRouter.ShouldOutputJson(new[] { "gh", "pr", "checks", "390" }).Should().BeFalse();
    }

    /// <summary>显式 --format json 覆盖环境变量 text</summary>
    [Fact]
    public void ShouldOutputJson_ExplicitJson_OverridesEnvText() {
        using var env = EnvVarScope.Set("JCC_OUTPUT_FORMAT", "text");
        FlatSubCommandRouter.ShouldOutputJson(new[] { "gh", "pr", "checks", "390", "--format", "json" }).Should().BeTrue();
    }

    /// <summary>显式 --format text 覆盖默认 JSON(回归)</summary>
    [Fact]
    public void ShouldOutputJson_ExplicitText_OverridesDefault() {
        FlatSubCommandRouter.ShouldOutputJson(new[] { "gh", "pr", "checks", "390", "--format", "text" }).Should().BeFalse();
    }

    /// <summary>无环境变量无 --format → 默认 JSON(回归,ADR 0069)</summary>
    [Fact]
    public void ShouldOutputJson_NoEnvNoFormat_DefaultsJson() {
        using var env = EnvVarScope.Set("JCC_OUTPUT_FORMAT", null);
        FlatSubCommandRouter.ShouldOutputJson(new[] { "gh", "pr", "checks", "390" }).Should().BeTrue();
    }
}