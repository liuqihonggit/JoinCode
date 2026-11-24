namespace Mcp.Tests;

/// <summary>
/// TASK102/TASK132 round_gate 工具测试 — 选项注入文本、自验结论校验、非法选项诱导式报错、参数回显与 TODO 注入。
/// </summary>
public sealed class RoundGateToolHandlersTests {
    private readonly RoundGateToolHandlers _handler = new();

    [Theory]
    [InlineData(0, "阻塞上报")]
    [InlineData(1, "直接继续")]
    [InlineData(2, "TODO 继续")]
    [InlineData(4, "用户验收")]
    [InlineData(5, "完成")]
    [InlineData(6, "等候后台")]
    [InlineData(7, "重新规划")]
    [InlineData(8, "回退")]
    [InlineData(9, "降级交付")]
    [InlineData(10, "求助")]
    public async Task RoundGateAsync_ValidChoice_ReturnsMatchingInjection(int choice, string expectedMarker) {
        var result = await _handler.RoundGateAsync(choice).ConfigureAwait(true);

        result.IsError.Should().BeFalse();
        result.GetFirstText().Should().Contain(expectedMarker);
    }

    [Fact]
    public async Task RoundGateAsync_SelfAccept_RequiresVerdictAndInjectsVerificationPrompt() {
        var withVerdict = await _handler.RoundGateAsync(3, verdict: "pass").ConfigureAwait(true);
        withVerdict.IsError.Should().BeFalse();
        withVerdict.GetFirstText().Should().Contain("自验");

        var withoutVerdict = await _handler.RoundGateAsync(3).ConfigureAwait(true);
        withoutVerdict.IsError.Should().BeTrue("choice=3 必须声明 verdict");
        withoutVerdict.GetFirstText().Should().Contain("verdict");
    }

    [Fact]
    public async Task RoundGateAsync_Done_InjectionRequiresTerminalVerification() {
        var result = await _handler.RoundGateAsync(5).ConfigureAwait(true);

        var text = result.GetFirstText();
        text.Should().Contain("终验");
        text.Should().Contain("3 自验");
        text.Should().Contain("○");
    }

    [Fact]
    public async Task RoundGateAsync_UserAccept_InjectionRequiresCircleMarkedHandoff() {
        var result = await _handler.RoundGateAsync(4).ConfigureAwait(true);

        var text = result.GetFirstText();
        text.Should().Contain("○");
        text.Should().Contain("待用户验收");
    }

    [Theory]
    [InlineData(12)]
    [InlineData(-3)]
    [InlineData(42)]
    public async Task RoundGateAsync_InvalidChoice_ReturnsGuidingError(int choice) {
        var result = await _handler.RoundGateAsync(choice).ConfigureAwait(true);

        result.IsError.Should().BeTrue();
        var text = result.GetFirstText();
        text.Should().Contain("不在允许范围");
        text.Should().Contain("1 直接继续");
        text.Should().Contain("round_gate(choice=1)");
    }

    [Fact]
    public async Task RoundGateAsync_InvalidVerdict_ReturnsGuidingError() {
        var result = await _handler.RoundGateAsync(3, verdict: "maybe").ConfigureAwait(true);

        result.IsError.Should().BeTrue();
        result.GetFirstText().Should().Contain("不是合法的自验结论");
    }

    [Fact]
    public async Task RoundGateAsync_Abandon_RequiresNoteAndInjectsFailureReportPrompt() {
        var withoutNote = await _handler.RoundGateAsync(11).ConfigureAwait(true);
        withoutNote.IsError.Should().BeTrue("choice=11 是失败终止，必须写失败报告");
        withoutNote.GetFirstText().Should().Contain("失败报告");

        var withNote = await _handler
            .RoundGateAsync(11, note: "失败证据=测试红；已试=重试 3 次；根因=信息缺失；残留=未提交；建议=补需求")
            .ConfigureAwait(true);
        withNote.IsError.Should().BeFalse();
        var text = withNote.GetFirstText();
        text.Should().Contain("【闸门 · 11 放弃");
        text.Should().Contain("已尝试");
    }

    [Fact]
    public async Task RoundGateAsync_Done_InjectsPullRequestFlow() {
        var result = await _handler.RoundGateAsync(5).ConfigureAwait(true);

        var text = result.GetFirstText();
        text.Should().Contain("PR");
        text.Should().Contain("凭证");
        text.Should().Contain("放弃创建 PR");
    }

    [Fact]
    public async Task RoundGateAsync_WithNoteAndEvidence_EchoesBoth() {
        var result = await _handler.RoundGateAsync(1, note: "继续 TASK132 测试", evidence: "dotnet test 全绿").ConfigureAwait(true);

        var text = result.GetFirstText();
        text.Should().Contain("继续 TASK132 测试");
        text.Should().Contain("dotnet test 全绿");
    }

    [Fact]
    public async Task RoundGateAsync_WithoutEvidence_InjectsIdleGuardrail() {
        var result = await _handler.RoundGateAsync(1).ConfigureAwait(true);

        result.GetFirstText().Should().Contain("每轮必答三问");
    }

    [Fact]
    public async Task RoundGateAsync_WithEvidence_DoesNotPreloadGuardrail() {
        var result = await _handler.RoundGateAsync(1, evidence: "dotnet test 全绿").ConfigureAwait(true);

        result.GetFirstText().Should().NotContain("每轮必答三问", "有证据时不再常驻三问说明，避免每轮重复注入");
    }

    [Fact]
    public async Task RoundGateAsync_InjectsOnlySelectedOptionPrompt() {
        var result = await _handler.RoundGateAsync(3, verdict: "pass", evidence: "dotnet test 全绿").ConfigureAwait(true);

        var text = result.GetFirstText();
        text.Should().Contain("【闸门 · 3 自验】");
        text.Should().NotContain("【闸门 · 1 直接继续】", "其它选项提示词必须按需注入，不得预载");
        text.Should().NotContain("【闸门 · 9 降级交付】");
    }

    [Fact]
    public async Task RoundGateAsync_NextTodoWithService_AppendsReadyTodos() {
        var todoService = new Mock<ITodoService>();
        todoService.Setup(t => t.GetReadyTodosAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                new TodoItem("t1", "实现 TASK132 状态机", "pending", "high"),
                new TodoItem("t2", "补真机验收", "pending", "medium")
            ]);
        var handler = new RoundGateToolHandlers(todoService.Object);

        var result = await handler.RoundGateAsync(2).ConfigureAwait(true);

        result.IsError.Should().BeFalse();
        var text = result.GetFirstText();
        text.Should().Contain("当前就绪 TODO");
        text.Should().Contain("实现 TASK132 状态机");
        text.Should().Contain("补真机验收");
    }

    [Fact]
    public async Task RoundGateAsync_NextTodoWithoutReadyItems_FallsBackToReplanHint() {
        var todoService = new Mock<ITodoService>();
        todoService.Setup(t => t.GetReadyTodosAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        var handler = new RoundGateToolHandlers(todoService.Object);

        var result = await handler.RoundGateAsync(2).ConfigureAwait(true);

        result.GetFirstText().Should().Contain("当前没有就绪 TODO");
        result.GetFirstText().Should().Contain("7 重新规划");
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
    public async Task RoundGateAsync_DoneChoice_DoesNotQueryTodoService() {
        var todoService = new Mock<ITodoService>();
        var handler = new RoundGateToolHandlers(todoService.Object);

        await handler.RoundGateAsync(5).ConfigureAwait(true);

        todoService.Verify(t => t.GetReadyTodosAsync(It.IsAny<CancellationToken>()), Times.Never);
    }
}
