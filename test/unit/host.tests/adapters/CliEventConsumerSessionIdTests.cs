namespace Host.Tests.Adapters;

/// <summary>
/// CliEventConsumer session_id 一致性测试 — 验证 NDJSON 输出的 session_id 与外部传入的 sessionId 一致，
/// 而非 CliEventConsumer 自己生成的 UUID。根因：CliEventConsumer.EnsureSystemInit 行 204 自生成 Guid，
/// 与 SessionIdGenerator.Generate() 格式（transcript 目录名）不一致，导致 -c 恢复会话找不到 transcript。
/// </summary>
public sealed class CliEventConsumerSessionIdTests {
    [Fact]
    public void OnText_WithExplicitSessionId_OutputUsesThatSessionId() {
        var expectedSessionId = "20261105-1317-w1-w1-parent-2";
        var consumer = new JoinCode.Adapters.CliEventConsumer(agentMode: true, sessionId: expectedSessionId);

        var captured = CaptureConsole(() => consumer.OnText("Hello"));

        captured.Should().Contain($"\"session_id\":\"{expectedSessionId}\"");
    }

    [Fact]
    public void OnText_WithoutSessionId_OutputUsesGeneratedGuidSessionId() {
        var consumer = new JoinCode.Adapters.CliEventConsumer(agentMode: true);

        var captured = CaptureConsole(() => consumer.OnText("Hello"));

        captured.Should().Contain("session_id");
        captured.Should().NotContain("session_id\":null");
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
