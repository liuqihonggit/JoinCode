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

    [Fact]
    public async Task RoundGateAsync_NextTodoWithService_AppendsReadyTodos() {
        var todoService = new Mock<ITodoService>();
        todoService.Setup(t => t.GetReadyTodosAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                new TodoItem("t1", "实现 TASK102 收尾", "pending", "high"),
                new TodoItem("t2", "补真机验收", "pending", "medium")
            ]);
        var handler = new RoundGateToolHandlers(todoService.Object);

        var result = await handler.RoundGateAsync(2).ConfigureAwait(true);

        result.IsError.Should().BeFalse();
        var text = result.GetFirstText();
        text.Should().Contain("当前就绪 TODO");
        text.Should().Contain("实现 TASK102 收尾");
        text.Should().Contain("补真机验收");
    }

    [Fact]
    public async Task RoundGateAsync_NextTodoWithoutReadyItems_FallsBackToPlanHint() {
        var todoService = new Mock<ITodoService>();
        todoService.Setup(t => t.GetReadyTodosAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        var handler = new RoundGateToolHandlers(todoService.Object);

        var result = await handler.RoundGateAsync(2).ConfigureAwait(true);

        result.GetFirstText().Should().Contain("当前没有就绪 TODO");
        result.GetFirstText().Should().Contain("3 规划任务");
    }

    [Fact]
    public async Task RoundGateAsync_TodoServiceThrows_FallsBackToGenericPrompt() {
        var todoService = new Mock<ITodoService>();
        todoService.Setup(t => t.GetReadyTodosAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("todo store offline"));
        var handler = new RoundGateToolHandlers(todoService.Object);

        var result = await handler.RoundGateAsync(2).ConfigureAwait(true);

        result.IsError.Should().BeFalse("TODO 读取失败不应阻断闸门");
        result.GetFirstText().Should().Contain("TODO 继续");
    }

    [Fact]
    public async Task RoundGateAsync_EndChoice_DoesNotQueryTodoService() {
        var todoService = new Mock<ITodoService>();
        var handler = new RoundGateToolHandlers(todoService.Object);

        await handler.RoundGateAsync(5).ConfigureAwait(true);

        todoService.Verify(t => t.GetReadyTodosAsync(It.IsAny<CancellationToken>()), Times.Never);
    }
}
