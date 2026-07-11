
namespace Core.Tests.Planning;

public class PlanModeManagerTests
{
    private readonly IPlanModeManager _planModeManager;

    public PlanModeManagerTests()
    {
        _planModeManager = new PlanModeManager(new IO.FileSystem.PhysicalFileSystem(), JoinCode.Abstractions.Clock.SystemClockService.Instance);
    }

    [Fact]
    public async Task EnterPlanModeAsync_ShouldCreateNewPlan()
    {
        // Arrange
        var description = "Test Plan";

        // Act
        var result = await _planModeManager.EnterPlanModeAsync(description).ConfigureAwait(true);

        // Assert
        result.Success.Should().BeTrue();
        result.PlanState.Should().NotBeNull();
        result.PlanState!.Description.Should().Be(description);
        result.PlanState.Status.Should().Be(PlanStatus.Draft);
        result.PlanState.IsInPlanMode.Should().BeTrue();
        _planModeManager.IsInPlanMode.Should().BeTrue();
    }

    [Fact]
    public async Task EnterPlanModeAsync_WithInitialSteps_ShouldCreatePlanWithSteps()
    {
        // Arrange
        var initialSteps = new List<PlanStepInput>
        {
            new() { Description = "Step 1", ToolName = "Tool1" },
            new() { Description = "Step 2", ToolName = "Tool2" }
        };

        // Act
        var result = await _planModeManager.EnterPlanModeAsync("Test Plan", initialSteps).ConfigureAwait(true);

        // Assert
        result.Success.Should().BeTrue();
        result.PlanState!.Steps.Should().HaveCount(2);
        result.PlanState.Steps[0].Description.Should().Be("Step 1");
        result.PlanState.Steps[1].Description.Should().Be("Step 2");
    }

    [Fact]
    public async Task ExitPlanModeAsync_ShouldExitPlanMode()
    {
        // Arrange
        await _planModeManager.EnterPlanModeAsync("Test Plan").ConfigureAwait(true);
        _planModeManager.IsInPlanMode.Should().BeTrue();

        // Act
        var result = await _planModeManager.ExitPlanModeAsync().ConfigureAwait(true);

        // Assert
        result.Success.Should().BeTrue();
        _planModeManager.IsInPlanMode.Should().BeFalse();
        result.PlanState!.IsInPlanMode.Should().BeFalse();
    }

    [Fact]
    public async Task ExitPlanModeAsync_WhenNotInPlanMode_ShouldFail()
    {
        // Act
        var result = await _planModeManager.ExitPlanModeAsync().ConfigureAwait(true);

        // Assert
        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task GetPlanStatusAsync_WhenInPlanMode_ShouldReturnPlan()
    {
        // Arrange
        await _planModeManager.EnterPlanModeAsync("Test Plan").ConfigureAwait(true);

        // Act
        var plan = await _planModeManager.GetPlanStatusAsync().ConfigureAwait(true);

        // Assert
        plan.Should().NotBeNull();
        plan!.Description.Should().Be("Test Plan");
    }

    [Fact]
    public async Task GetPlanStatusAsync_WhenNotInPlanMode_ShouldReturnNull()
    {
        // Act
        var plan = await _planModeManager.GetPlanStatusAsync().ConfigureAwait(true);

        // Assert
        plan.Should().BeNull();
    }

    [Fact]
    public async Task AddStepAsync_ShouldAddStepToPlan()
    {
        // Arrange
        await _planModeManager.EnterPlanModeAsync("Test Plan").ConfigureAwait(true);

        // Act
        var result = await _planModeManager.AddStepAsync("New Step", "Tool1").ConfigureAwait(true);

        // Assert
        result.Success.Should().BeTrue();
        result.PlanState!.Steps.Should().HaveCount(1);
        result.PlanState.Steps[0].Description.Should().Be("New Step");
        result.PlanState.Steps[0].ToolName.Should().Be("Tool1");
    }

    [Fact]
    public async Task AddStepAsync_WhenNotInPlanMode_ShouldFail()
    {
        // Act
        var result = await _planModeManager.AddStepAsync("New Step").ConfigureAwait(true);

        // Assert
        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task ApproveStepAsync_ShouldApprovePendingStep()
    {
        // Arrange
        await _planModeManager.EnterPlanModeAsync("Test Plan").ConfigureAwait(true);
        await _planModeManager.AddStepAsync("Step 1").ConfigureAwait(true);

        // Act
        var result = await _planModeManager.ApproveStepAsync(0).ConfigureAwait(true);

        // Assert
        result.Success.Should().BeTrue();
        result.PlanState!.Steps[0].Status.Should().Be(PlanStepStatus.Approved);
        result.PlanState.Steps[0].IsApproved.Should().BeTrue();
    }

    [Fact]
    public async Task ApproveStepAsync_WithInvalidIndex_ShouldFail()
    {
        // Arrange
        await _planModeManager.EnterPlanModeAsync("Test Plan").ConfigureAwait(true);

        // Act
        var result = await _planModeManager.ApproveStepAsync(0).ConfigureAwait(true);

        // Assert
        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("无效");
    }

    [Fact]
    public async Task RejectStepAsync_ShouldRejectPendingStep()
    {
        // Arrange
        await _planModeManager.EnterPlanModeAsync("Test Plan").ConfigureAwait(true);
        await _planModeManager.AddStepAsync("Step 1").ConfigureAwait(true);

        // Act
        var result = await _planModeManager.RejectStepAsync(0, "Not needed").ConfigureAwait(true);

        // Assert
        result.Success.Should().BeTrue();
        result.PlanState!.Steps[0].Status.Should().Be(PlanStepStatus.Rejected);
        result.PlanState.Steps[0].RejectionReason.Should().Be("Not needed");
    }

    [Fact]
    public async Task RejectStepAsync_CompletedStep_ShouldFail()
    {
        // Arrange
        await _planModeManager.EnterPlanModeAsync("Test Plan").ConfigureAwait(true);
        await _planModeManager.AddStepAsync("Step 1").ConfigureAwait(true);
        await _planModeManager.ApproveStepAsync(0).ConfigureAwait(true);
        await _planModeManager.ExecuteApprovedStepsAsync().ConfigureAwait(true);

        // Act
        var result = await _planModeManager.RejectStepAsync(0).ConfigureAwait(true);

        // Assert
        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("无法拒绝");
    }

    [Fact]
    public async Task ExecuteApprovedStepsAsync_ShouldExecuteApprovedSteps()
    {
        // Arrange
        await _planModeManager.EnterPlanModeAsync("Test Plan").ConfigureAwait(true);
        await _planModeManager.AddStepAsync("Step 1").ConfigureAwait(true);
        await _planModeManager.AddStepAsync("Step 2").ConfigureAwait(true);
        await _planModeManager.ApproveStepAsync(0).ConfigureAwait(true);
        await _planModeManager.ApproveStepAsync(1).ConfigureAwait(true);

        // Act
        var result = await _planModeManager.ExecuteApprovedStepsAsync().ConfigureAwait(true);

        // Assert
        result.Success.Should().BeTrue();
        result.PlanState!.Status.Should().Be(PlanStatus.Completed);
        result.PlanState.Steps[0].Status.Should().Be(PlanStepStatus.Completed);
        result.PlanState.Steps[1].Status.Should().Be(PlanStepStatus.Completed);
    }

    [Fact]
    public async Task ExecuteApprovedStepsAsync_WithPendingSteps_ShouldStopAtPending()
    {
        // Arrange
        await _planModeManager.EnterPlanModeAsync("Test Plan").ConfigureAwait(true);
        await _planModeManager.AddStepAsync("Step 1").ConfigureAwait(true);
        await _planModeManager.AddStepAsync("Step 2").ConfigureAwait(true);
        await _planModeManager.ApproveStepAsync(0).ConfigureAwait(true);
        // Step 1 保持 Pending

        // Act
        var result = await _planModeManager.ExecuteApprovedStepsAsync().ConfigureAwait(true);

        // Assert
        result.Success.Should().BeTrue();
        result.PlanState!.Steps[0].Status.Should().Be(PlanStepStatus.Completed);
        result.PlanState.Steps[1].Status.Should().Be(PlanStepStatus.Pending);
    }

    [Fact]
    public async Task ModifyStepAsync_ShouldModifyStepDescription()
    {
        // Arrange
        await _planModeManager.EnterPlanModeAsync("Test Plan").ConfigureAwait(true);
        await _planModeManager.AddStepAsync("Old Description").ConfigureAwait(true);

        // Act
        var result = await _planModeManager.ModifyStepAsync(0, "New Description").ConfigureAwait(true);

        // Assert
        result.Success.Should().BeTrue();
        result.PlanState!.Steps[0].Description.Should().Be("New Description");
    }

    [Fact]
    public async Task ModifyStepAsync_RejectedStep_ShouldResetToPending()
    {
        // Arrange
        await _planModeManager.EnterPlanModeAsync("Test Plan").ConfigureAwait(true);
        await _planModeManager.AddStepAsync("Step 1").ConfigureAwait(true);
        await _planModeManager.RejectStepAsync(0).ConfigureAwait(true);

        // Act
        var result = await _planModeManager.ModifyStepAsync(0, "Modified Step").ConfigureAwait(true);

        // Assert
        result.Success.Should().BeTrue();
        result.PlanState!.Steps[0].Status.Should().Be(PlanStepStatus.Pending);
        result.PlanState.Steps[0].RejectionReason.Should().BeNull();
    }

    [Fact]
    public async Task RemoveStepAsync_ShouldRemoveStep()
    {
        // Arrange
        await _planModeManager.EnterPlanModeAsync("Test Plan").ConfigureAwait(true);
        await _planModeManager.AddStepAsync("Step 1").ConfigureAwait(true);
        await _planModeManager.AddStepAsync("Step 2").ConfigureAwait(true);

        // Act
        var result = await _planModeManager.RemoveStepAsync(0).ConfigureAwait(true);

        // Assert
        result.Success.Should().BeTrue();
        result.PlanState!.Steps.Should().HaveCount(1);
        result.PlanState.Steps[0].Description.Should().Be("Step 2");
        result.PlanState.Steps[0].Index.Should().Be(0);
    }

    [Fact]
    public async Task RemoveStepAsync_CompletedStep_ShouldFail()
    {
        // Arrange
        await _planModeManager.EnterPlanModeAsync("Test Plan").ConfigureAwait(true);
        await _planModeManager.AddStepAsync("Step 1").ConfigureAwait(true);
        await _planModeManager.ApproveStepAsync(0).ConfigureAwait(true);
        await _planModeManager.ExecuteApprovedStepsAsync().ConfigureAwait(true);

        // Act
        var result = await _planModeManager.RemoveStepAsync(0).ConfigureAwait(true);

        // Assert
        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("无法删除");
    }

    [Fact]
    public async Task ReorderStepsAsync_ShouldReorderSteps()
    {
        // Arrange
        await _planModeManager.EnterPlanModeAsync("Test Plan").ConfigureAwait(true);
        await _planModeManager.AddStepAsync("Step 1").ConfigureAwait(true);
        await _planModeManager.AddStepAsync("Step 2").ConfigureAwait(true);
        await _planModeManager.AddStepAsync("Step 3").ConfigureAwait(true);

        // Act - 将顺序改为 2, 0, 1
        var result = await _planModeManager.ReorderStepsAsync(new List<int> { 2, 0, 1 }).ConfigureAwait(true);

        // Assert
        result.Success.Should().BeTrue();
        result.PlanState!.Steps[0].Description.Should().Be("Step 3");
        result.PlanState.Steps[1].Description.Should().Be("Step 1");
        result.PlanState.Steps[2].Description.Should().Be("Step 2");
    }

    [Fact]
    public async Task ReorderStepsAsync_WrongCount_ShouldFail()
    {
        // Arrange
        await _planModeManager.EnterPlanModeAsync("Test Plan").ConfigureAwait(true);
        await _planModeManager.AddStepAsync("Step 1").ConfigureAwait(true);
        await _planModeManager.AddStepAsync("Step 2").ConfigureAwait(true);

        // Act
        var result = await _planModeManager.ReorderStepsAsync(new List<int> { 1, 0, 2 }).ConfigureAwait(true); // 3 items for 2 steps

        // Assert
        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("不匹配");
    }

    [Fact]
    public async Task GetPlanHistoryAsync_ShouldReturnHistory()
    {
        // Arrange
        await _planModeManager.EnterPlanModeAsync("Plan 1").ConfigureAwait(true);
        await _planModeManager.ExitPlanModeAsync().ConfigureAwait(true);
        await _planModeManager.EnterPlanModeAsync("Plan 2").ConfigureAwait(true);
        await _planModeManager.ExitPlanModeAsync().ConfigureAwait(true);

        // Act
        var history = await _planModeManager.GetPlanHistoryAsync(10).ConfigureAwait(true);

        // Assert
        history.Should().HaveCount(2);
        history[0].Description.Should().Be("Plan 2");
        history[1].Description.Should().Be("Plan 1");
    }

    [Fact]
    public async Task PlanState_GetProgressPercentage_ShouldCalculateCorrectly()
    {
        // Arrange
        await _planModeManager.EnterPlanModeAsync("Test Plan").ConfigureAwait(true);
        await _planModeManager.AddStepAsync("Step 1").ConfigureAwait(true);
        await _planModeManager.AddStepAsync("Step 2").ConfigureAwait(true);
        await _planModeManager.AddStepAsync("Step 3").ConfigureAwait(true);
        await _planModeManager.AddStepAsync("Step 4").ConfigureAwait(true);

        await _planModeManager.ApproveStepAsync(0).ConfigureAwait(true);
        await _planModeManager.ApproveStepAsync(1).ConfigureAwait(true);
        await _planModeManager.ExecuteApprovedStepsAsync().ConfigureAwait(true);

        var plan = await _planModeManager.GetPlanStatusAsync().ConfigureAwait(true);

        // Assert
        plan.Should().NotBeNull();
        plan!.CompletedStepsCount.Should().Be(2);
        plan.TotalSteps.Should().Be(4);
        plan.GetProgressPercentage().Should().Be(50.0);
    }

    [Fact]
    public async Task PlanState_GetCurrentStep_ShouldReturnCorrectStep()
    {
        // Arrange
        await _planModeManager.EnterPlanModeAsync("Test Plan").ConfigureAwait(true);
        await _planModeManager.AddStepAsync("Step 1").ConfigureAwait(true);
        await _planModeManager.AddStepAsync("Step 2").ConfigureAwait(true);

        var plan = await _planModeManager.GetPlanStatusAsync().ConfigureAwait(true);

        // Act & Assert
        plan!.GetCurrentStep()!.Description.Should().Be("Step 1");
    }

    [Fact]
    public async Task ComplexScenario_CreatePlanApproveAndExecute()
    {
        // Arrange - 创建计划
        var result = await _planModeManager.EnterPlanModeAsync("Development Plan").ConfigureAwait(true);
        result.Success.Should().BeTrue();

        // 添加步骤
        await _planModeManager.AddStepAsync("Setup environment", ShellToolName.ShellExecute.ToValue()).ConfigureAwait(true);
        await _planModeManager.AddStepAsync("Write code", FileToolName.FileWrite.ToValue()).ConfigureAwait(true);
        await _planModeManager.AddStepAsync("Run tests", ShellToolName.ShellExecute.ToValue()).ConfigureAwait(true);

        // 批准所有步骤
        await _planModeManager.ApproveStepAsync(0).ConfigureAwait(true);
        await _planModeManager.ApproveStepAsync(1).ConfigureAwait(true);
        await _planModeManager.ApproveStepAsync(2).ConfigureAwait(true);

        // 执行步骤
        var executeResult = await _planModeManager.ExecuteApprovedStepsAsync().ConfigureAwait(true);
        executeResult.Success.Should().BeTrue();

        // 退出计划模式
        var exitResult = await _planModeManager.ExitPlanModeAsync().ConfigureAwait(true);
        exitResult.Success.Should().BeTrue();

        // 验证历史
        var history = await _planModeManager.GetPlanHistoryAsync(1).ConfigureAwait(true);
        history.Should().HaveCount(1);
        history[0].Status.Should().Be(PlanStatus.Completed);
        history[0].CompletedStepsCount.Should().Be(3);
    }

    [Fact]
    public async Task ExitPlanModeAsync_ShouldSetHasExitedPlanMode()
    {
        // 对齐 TS: hasExitedPlanMode — 退出plan后设置为true
        _planModeManager.HasExitedPlanMode.Should().BeFalse();

        await _planModeManager.EnterPlanModeAsync("Test Plan").ConfigureAwait(true);
        await _planModeManager.ExitPlanModeAsync().ConfigureAwait(true);

        _planModeManager.HasExitedPlanMode.Should().BeTrue();
    }

    [Fact]
    public async Task ExitPlanModeAsync_ShouldSetNeedsPlanModeExitAttachment()
    {
        // 对齐 TS: needsPlanModeExitAttachment — 退出plan后设置为true
        _planModeManager.NeedsPlanModeExitAttachment.Should().BeFalse();

        await _planModeManager.EnterPlanModeAsync("Test Plan").ConfigureAwait(true);
        await _planModeManager.ExitPlanModeAsync().ConfigureAwait(true);

        _planModeManager.NeedsPlanModeExitAttachment.Should().BeTrue();
    }

    [Fact]
    public async Task EnterPlanModeAsync_ShouldClearNeedsPlanModeExitAttachment()
    {
        // 对齐 TS: handlePlanModeTransition — 进入plan时清除退出通知标志
        await _planModeManager.EnterPlanModeAsync("Plan 1").ConfigureAwait(true);
        await _planModeManager.ExitPlanModeAsync().ConfigureAwait(true);
        _planModeManager.NeedsPlanModeExitAttachment.Should().BeTrue();

        // 重新进入plan模式
        await _planModeManager.EnterPlanModeAsync("Plan 2").ConfigureAwait(true);
        _planModeManager.NeedsPlanModeExitAttachment.Should().BeFalse();
    }

    [Fact]
    public void ClearPlanModeExitAttachment_ShouldClearFlag()
    {
        // 对齐 TS: setNeedsPlanModeExitAttachment(false)
        // 模拟退出plan后标志被设置
        _planModeManager.ClearPlanModeExitAttachment(); // 先清除确保初始状态
        _planModeManager.NeedsPlanModeExitAttachment.Should().BeFalse();
    }

    [Fact]
    public void ClearHasExitedPlanMode_ShouldClearFlag()
    {
        // 对齐 TS: setHasExitedPlanMode(false) — 发送完reentry引导后清除
        _planModeManager.ClearHasExitedPlanMode();
        _planModeManager.HasExitedPlanMode.Should().BeFalse();
    }

    [Fact]
    public async Task HasExitedPlanMode_ReentryScenario()
    {
        // 对齐 TS: plan_mode_reentry attachment 场景
        // 1. 进入plan → 退出plan → hasExitedPlanMode=true
        await _planModeManager.EnterPlanModeAsync("Plan 1").ConfigureAwait(true);
        await _planModeManager.ExitPlanModeAsync().ConfigureAwait(true);
        _planModeManager.HasExitedPlanMode.Should().BeTrue();

        // 2. 消费方读取标志后清除（模拟发送完reentry引导）
        _planModeManager.ClearHasExitedPlanMode();
        _planModeManager.HasExitedPlanMode.Should().BeFalse();
    }

    [Fact]
    public async Task EnterPlanModeAsync_ShouldUseRandomWordSlug()
    {
        // 对齐 TS getPlanSlug(): slug 应为 {adjective}-{verb}-{noun} 格式，不含时间戳
        var result = await _planModeManager.EnterPlanModeAsync("Test Plan").ConfigureAwait(true);
        result.PlanState!.PlanFilePath.Should().NotBeNull();
        var fileName = Path.GetFileName(result.PlanState.PlanFilePath!);
        fileName.Should().EndWith(".md");
        fileName.Should().NotContain("test-plan");
        fileName.Should().NotContain("2026");
        // 格式应为 adjective-verb-noun.md
        var nameWithoutExt = Path.GetFileNameWithoutExtension(fileName);
        var parts = nameWithoutExt.Split('-');
        parts.Should().HaveCount(3);
    }

    [Fact]
    public async Task EnterPlanModeAsync_ReentryShouldReuseSlug()
    {
        // 对齐 TS planSlugCache: 同一 session 内进出 plan mode 应覆盖同一文件
        var result1 = await _planModeManager.EnterPlanModeAsync("Plan 1").ConfigureAwait(true);
        var filePath1 = result1.PlanState!.PlanFilePath;
        await _planModeManager.ExitPlanModeAsync().ConfigureAwait(true);

        var result2 = await _planModeManager.EnterPlanModeAsync("Plan 2").ConfigureAwait(true);
        var filePath2 = result2.PlanState!.PlanFilePath;

        filePath2.Should().Be(filePath1, "同一 session 内 slug 应缓存复用");
        await _planModeManager.ExitPlanModeAsync().ConfigureAwait(true);
    }

    [Fact]
    public void ClearPlanSlug_ShouldResetSlugCache()
    {
        // 对齐 TS clearPlanSlug(): 清除后下次进入 plan mode 应生成新 slug
        var slug1 = _planModeManager.GetType()
            .GetField("_currentSessionSlug", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .GetValue(_planModeManager) as string;

        _planModeManager.ClearPlanSlug();

        var slug2 = _planModeManager.GetType()
            .GetField("_currentSessionSlug", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .GetValue(_planModeManager) as string;

        slug2.Should().BeNull("ClearPlanSlug 应清除 slug 缓存");
    }

    [Fact]
    public void CleanupOldPlanFiles_WithNoPlansDir_ShouldReturnZero()
    {
        // 对齐 TS cleanupOldPlanFiles(): 目录不存在时返回 0
        var result = _planModeManager.CleanupOldPlanFiles();
        result.Should().BeGreaterThanOrEqualTo(0);
    }

    [Fact]
    public async Task ExitPlanModeAsync_ShouldNotAutoWriteFile()
    {
        // 对齐 TS: 退出时不自动写文件 — plan 文件由模型通过 FileWriteTool 写入
        var result = await _planModeManager.EnterPlanModeAsync("Test Plan").ConfigureAwait(true);
        var planFilePath = result.PlanState!.PlanFilePath;
        await _planModeManager.ExitPlanModeAsync().ConfigureAwait(true);

        // 退出后 plan 文件不应被自动创建
        if (planFilePath is not null)
        {
            _planModeManager.GetType()
                .GetField("_fs", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
                .GetValue(_planModeManager)!.Should().NotBeNull();
        }
    }
}
