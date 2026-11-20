namespace Core.Tests.Query.Parallel;

/// <summary>
/// ADR 0160 — 工具并发安全分类器：SafeToolNames 静态分类 + Bash 只读探测 + 交互工具防御性排除。
/// </summary>
public sealed class ToolConcurrencyClassifierTests {
    private static Dictionary<string, JsonElement> CommandArgs(string command) {
        return new Dictionary<string, JsonElement> {
            ["command"] = JsonSerializer.Deserialize<JsonElement>($"\"{command}\"")
        };
    }

    [Fact]
    public async Task SafeTool_InSafeSet_IsConcurrencySafe_CaseInsensitive() {
        await using var classifier = new ToolConcurrencyClassifier(
            FrozenSet.Create<string>(StringComparer.OrdinalIgnoreCase, ["read_file", "search_code"]));

        (await classifier.IsConcurrencySafeAsync("read_file", null)).Should().BeTrue();
        (await classifier.IsConcurrencySafeAsync("READ_FILE", null)).Should().BeTrue();
        (await classifier.IsConcurrencySafeAsync("search_code", null)).Should().BeTrue();
    }

    [Fact]
    public async Task UnknownTool_IsNotConcurrencySafe() {
        await using var classifier = new ToolConcurrencyClassifier(
            FrozenSet.Create<string>(StringComparer.OrdinalIgnoreCase, ["read_file"]));

        (await classifier.IsConcurrencySafeAsync("write_file", null)).Should().BeFalse();
    }

    [Fact]
    public async Task EmptySafeSet_DegradesToSequential() {
        await using var classifier = new ToolConcurrencyClassifier();

        (await classifier.IsConcurrencySafeAsync("read_file", null)).Should().BeFalse();
        (await classifier.IsConcurrencySafeAsync(ShellToolNameEnumConstants.Bash, CommandArgs("git status"))).Should().BeFalse();
    }

    [Fact]
    public async Task Bash_ReadOnlyCommand_IsConcurrencySafe() {
        await using var classifier = new ToolConcurrencyClassifier(
            FrozenSet<string>.Empty,
            readOnlyProbe: command => command.StartsWith("git status", StringComparison.Ordinal));

        (await classifier.IsConcurrencySafeAsync(ShellToolNameEnumConstants.Bash, CommandArgs("git status"))).Should().BeTrue();
    }

    [Fact]
    public async Task Bash_WriteCommand_IsNotConcurrencySafe() {
        await using var classifier = new ToolConcurrencyClassifier(
            FrozenSet<string>.Empty,
            readOnlyProbe: command => command.StartsWith("git status", StringComparison.Ordinal));

        (await classifier.IsConcurrencySafeAsync(ShellToolNameEnumConstants.Bash, CommandArgs("rm file.txt"))).Should().BeFalse();
    }

    [Fact]
    public async Task Bash_MissingCommandArgument_IsNotConcurrencySafe() {
        await using var classifier = new ToolConcurrencyClassifier(
            FrozenSet<string>.Empty,
            readOnlyProbe: _ => true);

        (await classifier.IsConcurrencySafeAsync(ShellToolNameEnumConstants.Bash, null)).Should().BeFalse();
        (await classifier.IsConcurrencySafeAsync(ShellToolNameEnumConstants.Bash, new Dictionary<string, JsonElement>())).Should().BeFalse();
    }

    [Fact]
    public async Task InteractionTool_EvenIfMarkedSafe_IsNotConcurrencySafe() {
        await using var classifier = new ToolConcurrencyClassifier(
            FrozenSet.Create<string>(StringComparer.OrdinalIgnoreCase, [UserInteractionToolNameEnumConstants.AskUserQuestion]));

        (await classifier.IsConcurrencySafeAsync(UserInteractionToolNameEnumConstants.AskUserQuestion, null)).Should().BeFalse(
            "交互工具并行会绕过独占调度导致多路等待用户输入");
        (await classifier.IsConcurrencySafeAsync(UserInteractionToolNameEnumConstants.ConfirmAction, null)).Should().BeFalse();
    }
}
