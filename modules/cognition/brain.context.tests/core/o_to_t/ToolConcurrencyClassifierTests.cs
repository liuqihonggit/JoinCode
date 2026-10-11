namespace Core.Context;

public sealed class ToolConcurrencyClassifierTests {
    [Fact]
    public async Task IsConcurrencySafeAsync_SafeTool_ReturnsTrue() {
        await using var classifier = new ToolConcurrencyClassifier(
            FrozenSet.Create<string>(StringComparer.OrdinalIgnoreCase, ["Read", "Grep"]));

        (await classifier.IsConcurrencySafeAsync("Read", null)).Should().BeTrue();
        (await classifier.IsConcurrencySafeAsync("Grep", null)).Should().BeTrue();
        (await classifier.IsConcurrencySafeAsync("read", null)).Should().BeTrue();
    }

    [Fact]
    public async Task IsConcurrencySafeAsync_UnsafeTool_ReturnsFalse() {
        await using var classifier = new ToolConcurrencyClassifier(
            FrozenSet.Create<string>(StringComparer.OrdinalIgnoreCase, ["Read"]));

        (await classifier.IsConcurrencySafeAsync("Write", null)).Should().BeFalse();
    }

    [Fact]
    public async Task IsConcurrencySafeAsync_EmptySafeSet_ReturnsFalseForAll() {
        await using var classifier = new ToolConcurrencyClassifier();

        (await classifier.IsConcurrencySafeAsync("Read", null)).Should().BeFalse();
        (await classifier.IsConcurrencySafeAsync("Grep", null)).Should().BeFalse();
    }

    [Fact]
    public async Task IsConcurrencySafeAsync_BashWithReadOnlyCommand_ReturnsTrue() {
        await using var classifier = new ToolConcurrencyClassifier(
            FrozenSet<string>.Empty,
            isCommandReadOnly: cmd => cmd.StartsWith("git status") || cmd.StartsWith("ls"));

        var args = new Dictionary<string, JsonElement> {
            ["command"] = JsonSerializer.Deserialize<JsonElement>("\"git status\"")
        };

        (await classifier.IsConcurrencySafeAsync("Bash", args)).Should().BeTrue();
    }

    [Fact]
    public async Task IsConcurrencySafeAsync_BashWithWriteCommand_ReturnsFalse() {
        await using var classifier = new ToolConcurrencyClassifier(
            FrozenSet<string>.Empty,
            isCommandReadOnly: cmd => cmd.StartsWith("ls"));

        var args = new Dictionary<string, JsonElement> {
            ["command"] = JsonSerializer.Deserialize<JsonElement>("\"rm file.txt\"")
        };

        (await classifier.IsConcurrencySafeAsync("Bash", args)).Should().BeFalse();
    }

    [Fact]
    public async Task IsConcurrencySafeAsync_BashWithoutCommandArg_ReturnsFalse() {
        await using var classifier = new ToolConcurrencyClassifier(
            FrozenSet<string>.Empty,
            isCommandReadOnly: _ => true);

        (await classifier.IsConcurrencySafeAsync("Bash", null)).Should().BeFalse();
        (await classifier.IsConcurrencySafeAsync("Bash", new Dictionary<string, JsonElement>())).Should().BeFalse();
    }

    [Fact]
    public async Task IsConcurrencySafeAsync_PowershellWithReadOnlyCommand_ReturnsTrue() {
        await using var classifier = new ToolConcurrencyClassifier(
            FrozenSet<string>.Empty,
            isCommandReadOnly: cmd => cmd.StartsWith("Get-ChildItem"));

        var args = new Dictionary<string, JsonElement> {
            ["command"] = JsonSerializer.Deserialize<JsonElement>("\"Get-ChildItem .\"")
        };

        (await classifier.IsConcurrencySafeAsync("Powershell", args)).Should().BeTrue();
    }

    [Fact]
    public async Task AskUserQuestion_NotInSafeSet_ReturnsFalse() {
        await using var classifier = new ToolConcurrencyClassifier(FrozenSet<string>.Empty);

        (await classifier.IsConcurrencySafeAsync(UserInteractionToolNameEnumConstants.AskUserQuestion, null))
            .Should().BeFalse();
    }

    [Fact]
    public async Task AskUserQuestion_ErroneouslyInSafeSet_ReturnsTrueButWarns() {
        var logger = new CaptureLogger<ToolConcurrencyClassifier>();
        await using var classifier = new ToolConcurrencyClassifier(
            FrozenSet.Create<string>(StringComparer.OrdinalIgnoreCase, [UserInteractionToolNameEnumConstants.AskUserQuestion]),
            logger: logger);

        (await classifier.IsConcurrencySafeAsync(UserInteractionToolNameEnumConstants.AskUserQuestion, null))
            .Should().BeTrue();

        logger.Entries.Should().ContainSingle(e => e.Level == LogLevel.Warning && e.Message.Contains("ask_user_question"));
    }

    [Fact]
    public async Task ConfirmAction_ErroneouslyInSafeSet_ReturnsTrueButWarns() {
        var logger = new CaptureLogger<ToolConcurrencyClassifier>();
        await using var classifier = new ToolConcurrencyClassifier(
            FrozenSet.Create<string>(StringComparer.OrdinalIgnoreCase, [UserInteractionToolNameEnumConstants.ConfirmAction]),
            logger: logger);

        (await classifier.IsConcurrencySafeAsync(UserInteractionToolNameEnumConstants.ConfirmAction, null))
            .Should().BeTrue();

        logger.Entries.Should().ContainSingle(e => e.Level == LogLevel.Warning && e.Message.Contains("confirm_action"));
    }

    [Fact]
    public async Task NonInteractionToolInSafeSet_DoesNotWarn() {
        var logger = new CaptureLogger<ToolConcurrencyClassifier>();
        await using var classifier = new ToolConcurrencyClassifier(
            FrozenSet.Create<string>(StringComparer.OrdinalIgnoreCase, ["Read", "Grep"]),
            logger: logger);

        (await classifier.IsConcurrencySafeAsync("Read", null)).Should().BeTrue();
        (await classifier.IsConcurrencySafeAsync("Grep", null)).Should().BeTrue();

        logger.Entries.Should().BeEmpty();
    }

    private sealed class CaptureLogger<T> : ILogger<T> {
        public List<(LogLevel Level, string Message)> Entries { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) {
            Entries.Add((logLevel, formatter(state, exception)));
        }
    }
}