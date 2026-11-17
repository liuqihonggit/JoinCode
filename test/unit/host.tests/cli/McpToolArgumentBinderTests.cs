namespace Host.Tests.Cli;

/// <summary>
/// 工具参数绑定契约 — 命令行上的工具参数必须能被正确抽出，并按工具参数表校验。
/// <para>真机证据见 docs/implemented/task/TASK086-工具参数契约统一.md：
/// 修复前 <c>jcc tool call search search_symbol --query X</c> 直接报「未知选项 '--query'」，
/// 245 个 host 工具全部无法用 <c>--参数 值</c> 形式调用；这里锁的是解析与校验规则。</para>
/// </summary>
public sealed class McpToolArgumentBinderTests {
    private static readonly FrozenSet<string> NoReserved = FrozenSet<string>.Empty;
    private static readonly FrozenSet<string> NoBooleanFlags = FrozenSet<string>.Empty;

    [Fact]
    public void ExtractFlagArguments_ReadsSpaceSeparatedValue() {
        var (flagArgs, _) = Extract("mcp_call", "glob", "--pattern", "*.cs");

        flagArgs.Should().ContainKey("pattern");
        flagArgs["pattern"].Should().Be("*.cs");
    }

    [Fact]
    public void ExtractFlagArguments_ReadsEqualsForm() {
        var (flagArgs, _) = Extract("mcp_call", "glob", "--pattern=*.cs");

        flagArgs["pattern"].Should().Be("*.cs");
    }

    [Fact]
    public void ExtractFlagArguments_TreatsValuelessFlagAsTrue() {
        var (flagArgs, _) = Extract("mcp_call", "grep", "--recursive");

        flagArgs["recursive"].Should().Be("true");
    }

    [Fact]
    public void ExtractFlagArguments_KeepsReservedOptionWithItsValue() {
        var reserved = FrozenSet.Create(StringComparer.Ordinal, "--args-file");
        var (flagArgs, remaining) = ExtractWithReserved(
            ["mcp_call", "glob", "--args-file", "args.json", "--pattern", "*.cs"], reserved);

        flagArgs.Should().ContainKey("pattern").And.NotContainKey("args-file");
        // 保留选项与它的值必须保持相邻 —— 位置参数收集靠这个关系排除选项值
        remaining.Should().ContainInOrder("--args-file", "args.json");
    }

    [Fact]
    public void ExtractFlagArguments_LeavesPlainValuesInRemaining() {
        var (flagArgs, remaining) = Extract("mcp_call", "glob", "pattern=*.cs");

        flagArgs.Should().BeEmpty();
        remaining.Should().Contain("pattern=*.cs");
    }

    [Fact]
    public void ValidateAgainstSchema_AcceptsKnownParameter() {
        var schema = BuildSchema(required: "pattern");

        var error = McpToolArgumentBinder.ValidateAgainstSchema(
            BuildArgs(("pattern", "*.cs")), schema, "glob");

        error.Should().BeNull();
    }

    [Fact]
    public void ValidateAgainstSchema_RejectsUnknownParameterWithRealNamesAndSuggestion() {
        var schema = BuildSchema(required: "pattern", extra: "path");

        var error = McpToolArgumentBinder.ValidateAgainstSchema(
            BuildArgs(("patern", "*.cs")), schema, "glob");

        error.Should().NotBeNull();
        error.Should().Contain("patern");
        error.Should().Contain("--pattern").And.Contain("--path");
        error.Should().Contain("mcp_schema glob", "必须告诉调用方去哪查参数表");
    }

    [Fact]
    public void ValidateAgainstSchema_ReportsMissingRequiredParameter() {
        var schema = BuildSchema(required: "pattern");

        var error = McpToolArgumentBinder.ValidateAgainstSchema(
            new Dictionary<string, JsonElement>(StringComparer.Ordinal), schema, "glob");

        error.Should().NotBeNull();
        error.Should().Contain("pattern");
    }

    [Fact]
    public void ValidateAgainstSchema_AcceptsAliasBecauseRepairNormalizesFirst() {
        // filePath 是 file_path 的合法别名 —— 中间件链的 ArgumentRepairMiddleware 会归一化，
        // CLI 若先判未知就会把合法写法拦在门外。
        var schema = BuildSchema(required: "file_path");

        var error = McpToolArgumentBinder.ValidateAgainstSchema(
            BuildArgs(("filePath", "a.cs")), schema, "read_file");

        error.Should().BeNull();
    }

    private static (Dictionary<string, string> FlagArgs, string[] Remaining) Extract(
        params string[] args) =>
        McpToolArgumentBinder.ExtractFlagArguments(args, 1, NoReserved, NoBooleanFlags);

    private static (Dictionary<string, string> FlagArgs, string[] Remaining) ExtractWithReserved(
        string[] args, FrozenSet<string> reserved) =>
        McpToolArgumentBinder.ExtractFlagArguments(args, 1, reserved, NoBooleanFlags);

    private static ToolSchema BuildSchema(string required, string? extra = null) {
        var properties = new Dictionary<string, ToolSchemaProperty>(StringComparer.Ordinal) {
            [required] = new() { Type = "string", Description = "primary" }
        };
        if (extra is not null)
            properties[extra] = new() { Type = "string", Description = "extra" };
        return new ToolSchema { Properties = properties, Required = [required] };
    }

    private static Dictionary<string, JsonElement> BuildArgs(params (string Key, string Value)[] pairs) {
        var dict = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var (key, value) in pairs)
            dict[key] = JsonSerializer.SerializeToElement(value);
        return dict;
    }
}
