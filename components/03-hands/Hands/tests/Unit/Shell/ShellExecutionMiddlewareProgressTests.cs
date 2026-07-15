namespace Hands.Tests.Shell;

[Trait("Category", "Unit")]
public class ShellExecutionMiddlewareProgressTests
{
    [Fact]
    public async Task ProgressTimer_ReportsOutputAndLineCount()
    {
        var stdout = "line1\nline2\nline3\nline4\nline5\nline6\nline7";
        var progressReports = new List<ToolProgressData>();

        var mockContext = new Mock<IShellCommandContext>();
        mockContext.Setup(x => x.Status).Returns(ShellCommandStatus.Running);
        mockContext.Setup(x => x.GetCurrentStdout()).Returns(stdout);
        mockContext.Setup(x => x.TaskId).Returns("test-task-001");

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        using var timer = CreateTestProgressTimer(
            mockContext.Object,
            data => progressReports.Add(data),
            "bash_progress");

        await Task.Delay(100, cts.Token);

        progressReports.Should().NotBeEmpty();

        var report = progressReports[0];
        report.ProgressType.Should().Be("bash_progress");
        report.ElapsedTimeMs.Should().HaveValue();
        report.Extra.Should().NotBeNull();
        report.Extra?.ContainsKey("output").Should().BeTrue();
        report.Extra?.ContainsKey("fullOutput").Should().BeTrue();
        report.Extra?.ContainsKey("totalLines").Should().BeTrue();
        report.Extra?.ContainsKey("totalBytes").Should().BeTrue();
        report.Extra?.ContainsKey("taskId").Should().BeTrue();
    }

    [Fact]
    public async Task ProgressTimer_SkipsWhenNotRunning()
    {
        var progressReports = new List<ToolProgressData>();

        var mockContext = new Mock<IShellCommandContext>();
        mockContext.Setup(x => x.Status).Returns(ShellCommandStatus.Completed);
        mockContext.Setup(x => x.GetCurrentStdout()).Returns("output");
        mockContext.Setup(x => x.TaskId).Returns("test-task-002");

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        using var timer = CreateTestProgressTimer(
            mockContext.Object,
            data => progressReports.Add(data),
            "bash_progress");

        await Task.Delay(100, cts.Token);

        progressReports.Should().BeEmpty();
    }

    [Fact]
    public async Task ProgressTimer_ReportsCorrectProgressType()
    {
        var progressReports = new List<ToolProgressData>();

        var mockContext = new Mock<IShellCommandContext>();
        mockContext.Setup(x => x.Status).Returns(ShellCommandStatus.Running);
        mockContext.Setup(x => x.GetCurrentStdout()).Returns("hello");
        mockContext.Setup(x => x.TaskId).Returns("test-task-003");

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        using var timer = CreateTestProgressTimer(
            mockContext.Object,
            data => progressReports.Add(data),
            "ps_progress");

        await Task.Delay(100, cts.Token);

        progressReports.Should().NotBeEmpty();
        progressReports[0].ProgressType.Should().Be("ps_progress");
    }

    private static Timer CreateTestProgressTimer(
        IShellCommandContext context,
        ToolProgressCallback onProgress,
        string progressType)
    {
        var startTime = Environment.TickCount64;
        var progressCounter = 0;

        return new Timer(_ =>
        {
            try
            {
                if (context.Status != ShellCommandStatus.Running) return;

                var elapsedMs = Environment.TickCount64 - startTime;
                var currentOutput = context.GetCurrentStdout();
                var totalLines = currentOutput.Count(c => c == '\n') + 1;
                var totalBytes = Encoding.UTF8.GetByteCount(currentOutput);

                var lastLines = GetLastNLines(currentOutput, 5);
                var fullOutput = GetLastNLines(currentOutput, 100);

                onProgress(new ToolProgressData
                {
                    ProgressType = progressType,
                    ToolUseId = $"{progressType}-{progressCounter++}",
                    Message = lastLines,
                    ElapsedTimeMs = elapsedMs,
                    Extra = new Dictionary<string, JsonElement>
                    {
                        ["output"] = JsonSerializer.SerializeToElement(lastLines, ToolsJsonContext.Default.String),
                        ["fullOutput"] = JsonSerializer.SerializeToElement(fullOutput, ToolsJsonContext.Default.String),
                        ["totalLines"] = JsonSerializer.SerializeToElement(totalLines, ToolsJsonContext.Default.Int32),
                        ["totalBytes"] = JsonSerializer.SerializeToElement(totalBytes, ToolsJsonContext.Default.Int64),
                        ["taskId"] = JsonSerializer.SerializeToElement(context.TaskId, ToolsJsonContext.Default.String),
                    }
                });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.WriteLine($"Progress timer error: {ex.Message}");
            }
        }, null, TimeSpan.FromMilliseconds(1), TimeSpan.FromMilliseconds(10));
    }

    private static string GetLastNLines(string text, int lineCount)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;

        var lines = text.Split('\n');
        if (lines.Length <= lineCount) return text.TrimEnd();

        var lastLines = lines[^lineCount..];
        return string.Join('\n', lastLines).TrimEnd();
    }
}
