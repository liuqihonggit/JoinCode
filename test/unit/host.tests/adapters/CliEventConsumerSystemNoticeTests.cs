namespace Host.Tests.Adapters;

/// <summary>
/// TASK136 T2 — <c>CliEventConsumer.OnSystemNotice</c> 的 NDJSON 投影：
/// 交互模式保持 dim 行；agentMode 输出 <c>system/hook_system_message</c>，字段与基准 attachment 同名。
/// </summary>
[Collection("ConsoleOutput")]
public sealed class CliEventConsumerSystemNoticeTests {
    /// <summary>agentMode 必须输出可辨识的 system 事件，而不是静默丢弃。</summary>
    [Fact]
    public void OnSystemNotice_AgentMode_WritesHookSystemMessageEvent() {
        var consumer = new JoinCode.Adapters.CliEventConsumer(agentMode: true, sessionId: "20261125-t136-t2");

        var captured = CaptureConsole(() => consumer.OnSystemNotice("T136 hook notice"));

        var jsonLine = captured.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Single(line => line.Contains("hook_system_message", StringComparison.Ordinal));
        using var doc = System.Text.Json.JsonDocument.Parse(jsonLine);
        var root = doc.RootElement;
        root.GetProperty("type").GetString().Should().Be("system");
        root.GetProperty("subtype").GetString().Should().Be("hook_system_message");
        root.GetProperty("content").GetString().Should().Be("T136 hook notice");
        root.GetProperty("session_id").GetString().Should().Be("20261125-t136-t2");
    }

    /// <summary>空旁白不得产出空事件。</summary>
    [Fact]
    public void OnSystemNotice_AgentMode_EmptyMessage_NoOutput() {
        var consumer = new JoinCode.Adapters.CliEventConsumer(agentMode: true);

        var captured = CaptureConsole(() => consumer.OnSystemNotice(string.Empty));

        captured.Should().BeEmpty();
    }

    /// <summary>交互模式不回归：仍然是 dim 文本行，不输出 NDJSON。</summary>
    [Fact]
    public void OnSystemNotice_InteractiveMode_PrintsDimLineWithoutJson() {
        var consumer = new JoinCode.Adapters.CliEventConsumer();

        var captured = CaptureConsole(() => consumer.OnSystemNotice("T136 dim line"));

        captured.Should().Contain("T136 dim line");
        captured.Should().NotContain("\"type\":\"system\"");
    }

    private static string CaptureConsole(Action action) {
        var originalOut = Console.Out;
        using var sw = new System.IO.StringWriter();
        Console.SetOut(sw);
        try {
            action();
        } finally {
            Console.SetOut(originalOut);
        }
        return sw.ToString();
    }
}
