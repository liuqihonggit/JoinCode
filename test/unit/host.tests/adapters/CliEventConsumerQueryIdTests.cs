namespace Host.Tests.Adapters;

/// <summary>
/// TASK137（ADR 0165 阶段 2）— NDJSON/SDK 协议的查询身份：
/// 一条进程可承载多次查询（SDK 流式输入、bridge_serve），消费方按 <c>query_id</c> 归因；
/// 作用域外发出的握手类消息不带该字段（WhenWritingNull），旧消费方零影响。
/// </summary>
[Collection("ConsoleOutput")]
public sealed class CliEventConsumerQueryIdTests {
    /// <summary>捕获 writer 的宿主引用 — 不 Dispose（见 <see cref="CaptureConsole"/> 注释），
    /// 留引用同时避免「局部变量未释放」的静态检查误判。</summary>
    private static System.IO.StringWriter? _retainedCaptureWriter;

    [Fact]
    public void OnText_InsideQueryScope_StampsQueryId() {
        var consumer = new JoinCode.Adapters.CliEventConsumer(agentMode: true, sessionId: "s-ndjson");
        string captured;
        using (Core.Context.ChatQueryScope.Enter("q-ndjson-1")) {
            captured = CaptureConsole(() => {
                consumer.OnText("你好");
                consumer.OnDone(null, null);
            });
        }

        captured.Should().Contain("\"query_id\":\"q-ndjson-1\"");
        captured.Should().Contain("\"session_id\":\"s-ndjson\"");
    }

    [Fact]
    public void OnText_OutsideQueryScope_OmitsQueryIdField() {
        var consumer = new JoinCode.Adapters.CliEventConsumer(agentMode: true, sessionId: "s-ndjson");

        var captured = CaptureConsole(() => {
            consumer.OnText("你好");
            consumer.OnDone(null, null);
        });

        captured.Should().Contain("\"type\":\"assistant\"");
        captured.Should().NotContain("query_id", "作用域外没有查询身份，不得写出空字段污染协议");
    }

    [Fact]
    public void SdkResultMessage_WithQueryId_WritesSnakeCaseField() {
        var message = JoinCode.Cli.Output.SdkResultMessage.Create("s-1") with { QueryId = "q-result-1" };

        var json = System.Text.Json.JsonSerializer.Serialize(
            message, JoinCode.Cli.Output.CliOutputJsonContext.Default.SdkResultMessage);

        json.Should().Contain("\"query_id\":\"q-result-1\"");
    }

    [Fact]
    public void SdkResultMessage_WithoutQueryId_OmitsField() {
        var message = JoinCode.Cli.Output.SdkResultMessage.Create("s-1");

        var json = System.Text.Json.JsonSerializer.Serialize(
            message, JoinCode.Cli.Output.CliOutputJsonContext.Default.SdkResultMessage);

        json.Should().NotContain("query_id");
    }

    private static string CaptureConsole(Action action) {
        var originalOut = Console.Out;
        // 捕获 writer 不 Dispose：Console.Out 是进程全局，并行类可能晚到写入已关闭的 writer
        var sw = new System.IO.StringWriter();
        _retainedCaptureWriter = sw;
        Console.SetOut(sw);
        try {
            action();
        } finally {
            Console.SetOut(originalOut);
        }
        return sw.ToString();
    }
}
