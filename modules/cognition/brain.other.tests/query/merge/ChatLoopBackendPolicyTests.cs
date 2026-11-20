namespace Core.Tests.Query.Merge;

/// <summary>
/// TASK103 循环合并 — 聊天循环后端灰度开关的解析与默认值契约。
/// </summary>
public sealed class ChatLoopBackendPolicyTests {
    [Fact]
    public void Read_DefaultsToLegacyWhenUnset() {
        using var scope = EnvVarScope.Set(JccEnvVar.ChatLoopBackend.ToValue(), null);

        ChatLoopBackendPolicy.Read().Should().Be(ChatLoopBackend.Legacy);
        ChatLoopBackendPolicy.UseQueryEngine.Should().BeFalse();
    }

    [Theory]
    [InlineData("query")]
    [InlineData("QUERY")]
    [InlineData("1")]
    [InlineData("true")]
    [InlineData("on")]
    public void Read_AcceptsQueryAliases(string raw) {
        using var scope = EnvVarScope.Set(JccEnvVar.ChatLoopBackend.ToValue(), raw);

        ChatLoopBackendPolicy.Read().Should().Be(ChatLoopBackend.Query);
        ChatLoopBackendPolicy.UseQueryEngine.Should().BeTrue();
    }

    [Theory]
    [InlineData("legacy")]
    [InlineData("0")]
    [InlineData("false")]
    [InlineData("off")]
    public void Read_AcceptsLegacyAliases(string raw) {
        using var scope = EnvVarScope.Set(JccEnvVar.ChatLoopBackend.ToValue(), raw);

        ChatLoopBackendPolicy.Read().Should().Be(ChatLoopBackend.Legacy);
    }

    [Fact]
    public void Read_InvalidValueFallsBackToLegacy() {
        using var scope = EnvVarScope.Set(JccEnvVar.ChatLoopBackend.ToValue(), "garbage");

        ChatLoopBackendPolicy.Read().Should().Be(ChatLoopBackend.Legacy);
    }
}
