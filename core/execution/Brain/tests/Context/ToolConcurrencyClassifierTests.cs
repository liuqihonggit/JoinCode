namespace Core.Context;

public sealed class ToolConcurrencyClassifierTests
{
    [Fact]
    public async Task IsConcurrencySafeAsync_SafeTool_ReturnsTrue()
    {
        var classifier = new ToolConcurrencyClassifier(
            FrozenSet.Create<string>(StringComparer.OrdinalIgnoreCase, ["Read", "Grep"]));

        (await classifier.IsConcurrencySafeAsync("Read", null)).Should().BeTrue();
        (await classifier.IsConcurrencySafeAsync("Grep", null)).Should().BeTrue();
        (await classifier.IsConcurrencySafeAsync("read", null)).Should().BeTrue();
    }

    [Fact]
    public async Task IsConcurrencySafeAsync_UnsafeTool_ReturnsFalse()
    {
        var classifier = new ToolConcurrencyClassifier(
            FrozenSet.Create<string>(StringComparer.OrdinalIgnoreCase, ["Read"]));

        (await classifier.IsConcurrencySafeAsync("Write", null)).Should().BeFalse();
    }

    [Fact]
    public async Task IsConcurrencySafeAsync_EmptySafeSet_ReturnsFalseForAll()
    {
        var classifier = new ToolConcurrencyClassifier();

        (await classifier.IsConcurrencySafeAsync("Read", null)).Should().BeFalse();
        (await classifier.IsConcurrencySafeAsync("Grep", null)).Should().BeFalse();
    }

    [Fact]
    public async Task IsConcurrencySafeAsync_BashWithReadOnlyCommand_ReturnsTrue()
    {
        var classifier = new ToolConcurrencyClassifier(
            FrozenSet<string>.Empty,
            isCommandReadOnly: cmd => cmd.StartsWith("git status") || cmd.StartsWith("ls"));

        var args = new Dictionary<string, JsonElement>
        {
            ["command"] = JsonSerializer.Deserialize<JsonElement>("\"git status\"")
        };

        (await classifier.IsConcurrencySafeAsync("Bash", args)).Should().BeTrue();
    }

    [Fact]
    public async Task IsConcurrencySafeAsync_BashWithWriteCommand_ReturnsFalse()
    {
        var classifier = new ToolConcurrencyClassifier(
            FrozenSet<string>.Empty,
            isCommandReadOnly: cmd => cmd.StartsWith("ls"));

        var args = new Dictionary<string, JsonElement>
        {
            ["command"] = JsonSerializer.Deserialize<JsonElement>("\"rm file.txt\"")
        };

        (await classifier.IsConcurrencySafeAsync("Bash", args)).Should().BeFalse();
    }

    [Fact]
    public async Task IsConcurrencySafeAsync_BashWithoutCommandArg_ReturnsFalse()
    {
        var classifier = new ToolConcurrencyClassifier(
            FrozenSet<string>.Empty,
            isCommandReadOnly: _ => true);

        (await classifier.IsConcurrencySafeAsync("Bash", null)).Should().BeFalse();
        (await classifier.IsConcurrencySafeAsync("Bash", new Dictionary<string, JsonElement>())).Should().BeFalse();
    }

    [Fact]
    public async Task IsConcurrencySafeAsync_PowershellWithReadOnlyCommand_ReturnsTrue()
    {
        var classifier = new ToolConcurrencyClassifier(
            FrozenSet<string>.Empty,
            isCommandReadOnly: cmd => cmd.StartsWith("Get-ChildItem"));

        var args = new Dictionary<string, JsonElement>
        {
            ["command"] = JsonSerializer.Deserialize<JsonElement>("\"Get-ChildItem .\"")
        };

        (await classifier.IsConcurrencySafeAsync("Powershell", args)).Should().BeTrue();
    }
}
