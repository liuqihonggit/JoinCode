namespace Core.Tests.Context;


public class DiagnosticLogRecorderTests
{
    [Fact]
    public async Task InvokeAsync_RecordsTurnStartAndEnd()
    {
        var fs = new IOFileSystem();
        var recorder = CreateRecorder(fs);
        var context = CreateContext();
        var events = new List<ChatStreamEvent>
        {
            ChatStreamEvent.Done(),
        };

        await foreach (var _ in recorder.InvokeAsync(context, (_, _) => EventsAsync(events), CancellationToken.None)) { }

        var logContent = await ReadLogContentAsync(fs);
        Assert.Contains("turn_start", logContent);
        Assert.Contains("turn_end", logContent);
    }

    [Fact]
    public async Task InvokeAsync_RecordsToolStartAndEnd()
    {
        var fs = new IOFileSystem();
        var recorder = CreateRecorder(fs);
        var context = CreateContext();
        var events = new List<ChatStreamEvent>
        {
            ChatStreamEvent.ToolStart("Read", "tc1"),
            ChatStreamEvent.ToolEnd("Read", "file content", "tc1"),
            ChatStreamEvent.Done(),
        };

        await foreach (var _ in recorder.InvokeAsync(context, (_, _) => EventsAsync(events), CancellationToken.None)) { }

        var logContent = await ReadLogContentAsync(fs);
        Assert.Contains("tool_start", logContent);
        Assert.Contains("tool_end", logContent);
        Assert.Contains("Read", logContent);
    }

    [Fact]
    public async Task InvokeAsync_RecordsToolErrorAsAnomaly()
    {
        var fs = new IOFileSystem();
        var recorder = CreateRecorder(fs);
        var context = CreateContext();
        var events = new List<ChatStreamEvent>
        {
            ChatStreamEvent.ToolStart("Bash", "tc1"),
            ChatStreamEvent.ToolEnd("Bash", "error", "tc1", isError: true),
            ChatStreamEvent.Done(),
        };

        await foreach (var _ in recorder.InvokeAsync(context, (_, _) => EventsAsync(events), CancellationToken.None)) { }

        var logContent = await ReadLogContentAsync(fs);
        Assert.Contains("tool_error", logContent);
        Assert.Contains("anomaly", logContent);
    }

    [Fact]
    public async Task InvokeAsync_RecordsLoopDetectedAsAnomaly()
    {
        var fs = new IOFileSystem();
        var recorder = CreateRecorder(fs);
        var context = CreateContext();
        var events = new List<ChatStreamEvent>
        {
            ChatStreamEvent.LoopDetected(3, 5),
            ChatStreamEvent.Done(),
        };

        await foreach (var _ in recorder.InvokeAsync(context, (_, _) => EventsAsync(events), CancellationToken.None)) { }

        var logContent = await ReadLogContentAsync(fs);
        Assert.Contains("loop_detected", logContent);
        Assert.Contains("anomaly", logContent);
    }

    [Fact]
    public async Task InvokeAsync_LoopDetected_ContainsRepeatedPattern()
    {
        var fs = new IOFileSystem();
        var recorder = CreateRecorder(fs);
        var context = CreateContext();
        var events = new List<ChatStreamEvent>
        {
            ChatStreamEvent.LoopDetected(3, 5, "重复的模式内容"),
            ChatStreamEvent.Done(),
        };

        await foreach (var _ in recorder.InvokeAsync(context, (_, _) => EventsAsync(events), CancellationToken.None)) { }

        var logContent = await ReadLogContentAsync(fs);
        Assert.Contains("repeated_pattern", logContent);
    }

    [Fact]
    public async Task InvokeAsync_EveryEntry_ContainsTraceField()
    {
        var fs = new IOFileSystem();
        var recorder = CreateRecorder(fs);
        var context = CreateContext();
        var events = new List<ChatStreamEvent>
        {
            ChatStreamEvent.ToolStart("Read", "tc1"),
            ChatStreamEvent.ToolEnd("Read", "ok", "tc1"),
            ChatStreamEvent.LoopDetected(2, 10, "pattern"),
            ChatStreamEvent.Done(),
        };

        await foreach (var _ in recorder.InvokeAsync(context, (_, _) => EventsAsync(events), CancellationToken.None)) { }

        var logContent = await ReadLogContentAsync(fs);
        Assert.Contains("\"trace\":", logContent);
    }

    [Fact]
    public async Task InvokeAsync_LoopDetected_ContainsTriggerCountAndStartIndex()
    {
        var fs = new IOFileSystem();
        var recorder = CreateRecorder(fs);
        var context = CreateContext();
        var events = new List<ChatStreamEvent>
        {
            ChatStreamEvent.LoopDetected(5, 20, "abc"),
            ChatStreamEvent.Done(),
        };

        await foreach (var _ in recorder.InvokeAsync(context, (_, _) => EventsAsync(events), CancellationToken.None)) { }

        var logContent = await ReadLogContentAsync(fs);
        Assert.Contains("trigger_count", logContent);
        Assert.Contains("loop_start_index", logContent);
    }

    [Fact]
    public async Task InvokeAsync_RecordsApiCompleteWithUsage()
    {
        var fs = new IOFileSystem();
        var recorder = CreateRecorder(fs);
        var context = CreateContext();
        var usage = new TokenUsage(100, 50) { CacheCreationInputTokens = 10, CacheReadInputTokens = 20 };
        var events = new List<ChatStreamEvent>
        {
            ChatStreamEvent.Done(usage, "gpt-4o"),
        };

        await foreach (var _ in recorder.InvokeAsync(context, (_, _) => EventsAsync(events), CancellationToken.None)) { }

        var logContent = await ReadLogContentAsync(fs);
        Assert.Contains("api_complete", logContent);
        Assert.Contains("gpt-4o", logContent);
    }

    [Fact]
    public async Task InvokeAsync_PassesThroughAllEvents()
    {
        var fs = new IOFileSystem();
        var recorder = CreateRecorder(fs);
        var context = CreateContext();
        var events = new List<ChatStreamEvent>
        {
            ChatStreamEvent.Text("hello"),
            ChatStreamEvent.Thinking("thinking..."),
            ChatStreamEvent.ToolStart("Read", "tc1"),
            ChatStreamEvent.ToolEnd("Read", "content", "tc1"),
            ChatStreamEvent.Done(),
        };

        var output = new List<ChatStreamEvent>();
        await foreach (var evt in recorder.InvokeAsync(context, (_, _) => EventsAsync(events), CancellationToken.None))
        {
            output.Add(evt);
        }

        Assert.Equal(5, output.Count);
        Assert.Equal(ChatStreamEventType.Content, output[0].Type);
        Assert.Equal(ChatStreamEventType.Thinking, output[1].Type);
        Assert.Equal(ChatStreamEventType.ToolCallStart, output[2].Type);
        Assert.Equal(ChatStreamEventType.ToolCallEnd, output[3].Type);
        Assert.Equal(ChatStreamEventType.Complete, output[4].Type);
    }

    [Fact]
    public async Task InvokeAsync_CreatesDiagDirectory()
    {
        var fs = new IOFileSystem();
        var recorder = CreateRecorder(fs);
        var context = CreateContext();
        var events = new List<ChatStreamEvent> { ChatStreamEvent.Done() };

        await foreach (var _ in recorder.InvokeAsync(context, (_, _) => EventsAsync(events), CancellationToken.None)) { }

        Assert.True(true);
    }

    private static DiagnosticLogRecorder CreateRecorder(IFileSystem fs)
    {
        return new DiagnosticLogRecorder(fs, NullLogger<DiagnosticLogRecorder>.Instance);
    }

    private static ChatMiddlewareContext CreateContext()
    {
        return new ChatMiddlewareContext
        {
            Message = "test message",
            ToolUseContext = new ToolUseContext(),
        };
    }

    private static async IAsyncEnumerable<ChatStreamEvent> EventsAsync(IReadOnlyList<ChatStreamEvent> events)
    {
        foreach (var evt in events)
        {
            yield return evt;
            await Task.Yield();
        }
    }

    private static async Task<string> ReadLogContentAsync(IFileSystem fs)
    {
        var sessionsDir = WorkflowConstants.Paths.SessionsDirectory;

        if (!fs.DirectoryExists(sessionsDir))
            return string.Empty;

        var files = fs.GetFiles(sessionsDir, "*.jsonl", SearchOption.AllDirectories);
        var sb = new StringBuilder();
        foreach (var file in files)
        {
            try
            {
                sb.Append(await fs.ReadAllTextAsync(file, CancellationToken.None));
            }
            catch (FileNotFoundException) { Assert.Fail("不应到达此处"); }
        }
        return sb.ToString();
    }
}
