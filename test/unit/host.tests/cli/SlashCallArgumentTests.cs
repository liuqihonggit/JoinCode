namespace Host.Tests.Cli;

/// <summary>
/// slash_call 参数拆分契约 — JSON 对象参数必须拆成命令能识别的独立参数。
/// <para>此前 `slash_call /debuglog '{"flags":"-p"}'` 会把整段 JSON 当一个参数，
/// ParseFlags 匹配不到任何标志，结果永远打印默认的错误段。</para>
/// </summary>
public sealed class SlashCallArgumentTests {
    [Theory]
    [InlineData("{\"flags\":\"-p\"}", new[] { "-p" })]
    [InlineData("{\"flags\":\"--prompt\"}", new[] { "--prompt" })]
    [InlineData("{\"level\":2}", new[] { "2" })]
    public void GetSplitArgs_JsonObject_ExtractsValuesInDeclarationOrder(string raw, string[] expected) {
        var actual = ChatCommandBase.GetSplitArgs(CreateContext(raw));

        actual.Should().Equal(expected);
    }

    [Fact]
    public void GetSplitArgs_PositionalAndMalformedJson_FallBackToWhitespaceSplit() {
        ChatCommandBase.GetSplitArgs(CreateContext("a b c")).Should().Equal("a", "b", "c");
        ChatCommandBase.GetSplitArgs(CreateContext("{oops")).Should().Equal("{oops");
    }

    [Theory]
    [InlineData("{\"vendor\":\"openai\"}", "openai")]
    [InlineData("{\"count\":2}", "2")]
    [InlineData("{\"action\":\"add\",\"args\":\"PreToolUse echo\"}", "add PreToolUse echo")]
    [InlineData("{\"enable\":true}", "True")]
    public void GetNormalizedArgs_JsonObject_RestoresValuesAsText(string raw, string expected) {
        ChatCommandBase.GetNormalizedArgs(CreateContext(raw)).Should().Be(expected);
    }

    [Theory]
    [InlineData("openai")]
    [InlineData("add PreToolUse echo")]
    [InlineData("   spaced   text  ", "spaced   text")]
    public void GetNormalizedArgs_NonJson_KeepsOriginalText(string raw, string? expected = null) {
        ChatCommandBase.GetNormalizedArgs(CreateContext(raw)).Should().Be(expected ?? raw);
    }

    private static ChatCommandContext CreateContext(string raw) => new() {
        Arguments = raw,
        CancellationToken = CancellationToken.None,
        Services = new EmptyServices(),
        SessionId = ""
    };

    private sealed class EmptyServices : IServiceProvider {
        public object? GetService(Type serviceType) => null;
    }
}
