namespace Core.Tests.Query;

/// <summary>
/// 结构化输出重试上限 env 解析（TASK103 #45a）：注释承诺 MAX_STRUCTURED_OUTPUT_RETRIES 可覆盖，
/// 必须有真实消费点；非法值回退默认 5，保证不会退化成无限重试或 0 次直接失败。
/// </summary>
public class StructuredOutputRetryLimitTests {
    [Theory]
    [InlineData("7", 7)]
    [InlineData("1", 1)]
    [InlineData("5", 5)]
    public void ResolveMaxRetries_ValidValue_UsesIt(string envValue, int expected) {
        QueryMiddlewareContext.ResolveMaxStructuredOutputRetries(envValue).Should().Be(expected);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("abc")]
    [InlineData("0")]
    [InlineData("-3")]
    public void ResolveMaxRetries_InvalidValue_FallsBackToFive(string? envValue) {
        QueryMiddlewareContext.ResolveMaxStructuredOutputRetries(envValue).Should().Be(5);
    }
}
