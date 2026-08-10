namespace Brain.Other.Tests;

/// <summary>
/// PlanModeToolHandlers 诊断方法单元测试
/// </summary>
public class PlanModeToolHandlersDiagnosticTests
{
    [Fact]
    public void BuildChannelsDisabledDiagnostic_Enter_ReturnsCorrectStructure()
    {
        var diag = PlanModeToolHandlers.BuildChannelsDisabledDiagnostic("enter");
        diag.Reason.Should().Be("PlanModeChannelsDisabled_enter");
        diag.FormattedMessage.Should().Contain("Plan mode is disabled");
        diag.Details.Should().Contain(d => d.Key == "Operation" && d.Value == "enter");
    }

    [Fact]
    public void BuildChannelsDisabledDiagnostic_Exit_ReturnsCorrectStructure()
    {
        var diag = PlanModeToolHandlers.BuildChannelsDisabledDiagnostic("exit");
        diag.Reason.Should().Be("PlanModeChannelsDisabled_exit");
        diag.FormattedMessage.Should().Contain("Plan mode exit is disabled");
    }

    [Fact]
    public void BuildEnterPlanModeFailedDiagnostic_WithError_ReturnsCorrectStructure()
    {
        var diag = PlanModeToolHandlers.BuildEnterPlanModeFailedDiagnostic("Already in plan mode");
        diag.Reason.Should().Be("EnterPlanModeFailed");
        diag.FormattedMessage.Should().Be("Already in plan mode");
    }

    [Fact]
    public void BuildEnterPlanModeFailedDiagnostic_WithNull_ReturnsDefault()
    {
        var diag = PlanModeToolHandlers.BuildEnterPlanModeFailedDiagnostic(null);
        diag.FormattedMessage.Should().Be("Failed to enter plan mode");
    }

    [Fact]
    public void BuildExitPlanModeFailedDiagnostic_WithError_ReturnsCorrectStructure()
    {
        var diag = PlanModeToolHandlers.BuildExitPlanModeFailedDiagnostic("No plan exists");
        diag.Reason.Should().Be("ExitPlanModeFailed");
        diag.FormattedMessage.Should().Be("No plan exists");
    }

    [Fact]
    public void BuildExitPlanModeFailedDiagnostic_WithNull_ReturnsDefault()
    {
        var diag = PlanModeToolHandlers.BuildExitPlanModeFailedDiagnostic(null);
        diag.FormattedMessage.Should().Be("Failed to exit plan mode");
    }

    [Fact]
    public void BuildValidationErrorDiagnostic_ReturnsCorrectStructure()
    {
        var diag = PlanModeToolHandlers.BuildValidationErrorDiagnostic("add_step", "description 不能为空");
        diag.Reason.Should().Be("PlanValidation_add_step");
        diag.FormattedMessage.Should().Be("description 不能为空");
        diag.Details.Should().Contain(d => d.Key == "Operation" && d.Value == "add_step");
    }

    [Fact]
    public void BuildOperationFailedDiagnostic_WithError_ReturnsCorrectStructure()
    {
        var diag = PlanModeToolHandlers.BuildOperationFailedDiagnostic("add_step", "添加步骤失败", "Step limit reached");
        diag.Reason.Should().Be("PlanOperationFailed_add_step");
        diag.FormattedMessage.Should().Be("Step limit reached");
    }

    [Fact]
    public void BuildOperationFailedDiagnostic_WithNull_ReturnsDefault()
    {
        var diag = PlanModeToolHandlers.BuildOperationFailedDiagnostic("remove_step", "删除步骤失败", null);
        diag.FormattedMessage.Should().Be("删除步骤失败");
    }
}
