namespace Mcp.Tests;

/// <summary>
/// TASK102 round_gate 工具测试 — 选项注入文本、非法选项诱导式报错、note 回显。
/// </summary>
public sealed class RoundGateToolHandlersTests {
    private readonly RoundGateToolHandlers _handler = new();

    [Theory]
    [InlineData(1, "直接继续")]
    [InlineData(2, "TODO 继续")]
    [InlineData(3, "规划任务")]
    [InlineData(4, "验收")]
    [InlineData(5, "结束")]
    public async Task RoundGateAsync_ValidChoice_ReturnsMatchingInjection(int choice, string expectedMarker) {
        var result = await _handler.RoundGateAsync(choice).ConfigureAwait(true);

        result.IsError.Should().BeFalse();
        result.GetFirstText().Should().Contain(expectedMarker);
    }

    [Fact]
    public async Task RoundGateAsync_EndChoice_InjectionMentionsStopPermission() {
        var result = await _handler.RoundGateAsync(5).ConfigureAwait(true);

        result.GetFirstText().Should().Contain("允许停止");
    }

    [Fact]
    public async Task RoundGateAsync_Acceptance_InjectionRequiresRealExeAndCircleMark() {
        var result = await _handler.RoundGateAsync(4).ConfigureAwait(true);

        var text = result.GetFirstText();
        text.Should().Contain("exe");
        text.Should().Contain("○");
        text.Should().Contain("待用户验收");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(6)]
    [InlineData(-3)]
    public async Task RoundGateAsync_InvalidChoice_ReturnsGuidingError(int choice) {
        var result = await _handler.RoundGateAsync(choice).ConfigureAwait(true);

        result.IsError.Should().BeTrue();
        var text = result.GetFirstText();
        text.Should().Contain("不在允许范围");
        text.Should().Contain("1 直接继续");
        text.Should().Contain("round_gate(choice=1)");
    }

    [Fact]
    public async Task RoundGateAsync_WithNote_EchoesNote() {
        var result = await _handler.RoundGateAsync(2, "继续 TASK102 测试").ConfigureAwait(true);

        result.GetFirstText().Should().Contain("继续 TASK102 测试");
    }
}
