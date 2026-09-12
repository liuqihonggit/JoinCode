namespace Sync.Tests.ToolHandlers;

public class VerifyPlanExecutionToolHandlersTests
{
    private readonly Mock<IPlanService> _planService = new();
    private readonly VerifyPlanExecutionToolHandlers _handler;

    public VerifyPlanExecutionToolHandlersTests()
    {
        _handler = new VerifyPlanExecutionToolHandlers(_planService.Object, NullLogger<VerifyPlanExecutionToolHandlers>.Instance);
    }

    [Fact]
    public async Task VerifyPlanExecutionAsync_Success_ReturnsSuccess()
    {
        _planService.Setup(x => x.ExecutePlanWithResultAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PlanExecutionResult { Success = true, Result = "ok" });

        var result = await _handler.VerifyPlanExecutionAsync(cancellationToken: CancellationToken.None).ConfigureAwait(true);

        Assert.False(result.IsError);
        Assert.Contains("计划执行成功", result.GetTextContent());
    }

    [Fact]
    public async Task VerifyPlanExecutionAsync_Failure_ReturnsError()
    {
        _planService.Setup(x => x.ExecutePlanWithResultAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PlanExecutionResult { Success = false, Error = "bad plan" });

        var result = await _handler.VerifyPlanExecutionAsync(cancellationToken: CancellationToken.None).ConfigureAwait(true);

        Assert.True(result.IsError);
        Assert.Contains("计划执行存在问题", result.GetTextContent());
    }

    [Fact]
    public async Task VerifyPlanExecutionAsync_WithCriteria_ReturnsSuccess()
    {
        _planService.Setup(x => x.ExecutePlanWithResultAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PlanExecutionResult { Success = true, Result = "ok" });

        var result = await _handler.VerifyPlanExecutionAsync(criteria: "all tests pass", cancellationToken: CancellationToken.None).ConfigureAwait(true);

        Assert.False(result.IsError);
        Assert.Contains("验证标准", result.GetTextContent());
        Assert.Contains("all tests pass", result.GetTextContent());
    }

    [Fact]
    public async Task VerifyPlanExecutionAsync_ServiceThrows_ReturnsError()
    {
        _planService.Setup(x => x.ExecutePlanWithResultAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("boom"));

        var result = await _handler.VerifyPlanExecutionAsync(cancellationToken: CancellationToken.None).ConfigureAwait(true);

        Assert.True(result.IsError);
        Assert.Contains("验证计划执行失败", result.GetTextContent());
    }
}
