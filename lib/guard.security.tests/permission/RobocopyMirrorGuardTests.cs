namespace Guard.Security.Tests;

using Core.Hooks.Execution.Interception;
using Core.Hooks.Execution.Interception.Guards;

/// <summary>
/// RobocopyMirrorGuard 单元测试 — ADR 0012 阶段3
/// 验证保留名清理场景白名单放行条件
/// </summary>
public class RobocopyMirrorGuardTests {
    private readonly RobocopyMirrorGuard _guard = new();

    private static GuardContext CreateContext(string workingDir = "D:\\project\\w2") =>
        new(SystemActuatorKind.Bash, workingDir);

    #region CanHandle 测试

    [Theory]
    [InlineData("robocopy src dst /MIR")]
    [InlineData("robocopy src dst /PURGE")]
    [InlineData("robocopy src dst /mir")]
    [InlineData("robocopy src dst /purge")]
    public void CanHandle_Robocopy_With_Mirror_Purge_Should_Return_True(string command) {
        _guard.CanHandle(command, CreateContext()).Should().BeTrue();
    }

    [Theory]
    [InlineData("robocopy src dst /E")]
    [InlineData("robocopy src dst")]
    [InlineData("del file")]
    [InlineData("rm -rf /")]
    public void CanHandle_Other_Commands_Should_Return_False(string command) {
        _guard.CanHandle(command, CreateContext()).Should().BeFalse();
    }

    #endregion

    #region 保留名清理场景放行测试

    [Theory]
    [InlineData("robocopy empty D:\\project\\w2\\.xxx\\nul.20260912.del /MIR")]
    [InlineData("robocopy empty D:\\project\\w2\\.xxx\\con.20260912.del /MIR")]
    [InlineData("robocopy empty D:\\project\\w2\\.xxx\\prn.20260912.del /MIR")]
    [InlineData("robocopy empty D:\\project\\w2\\.xxx\\aux.20260912.del /MIR")]
    [InlineData("robocopy empty D:\\project\\w2\\.xxx\\nul.20260912.del /PURGE")]
    public void Evaluate_Retained_Name_Cleanup_Should_Allow(string command) {
        _guard.CanHandle(command, CreateContext()).Should().BeTrue();
        var decision = _guard.Evaluate(command, CreateContext());
        decision.Should().BeOfType<CommandDecision.Allow>();
    }

    #endregion

    #region 非保留名场景不放行测试

    [Theory]
    [InlineData("robocopy src D:\\project\\w2\\src /MIR")]
    [InlineData("robocopy src D:\\project\\JoinCode /MIR")]
    [InlineData("robocopy src dst /MIR")]
    [InlineData("robocopy src D:\\project\\w2\\normal.txt /MIR")]
    public void Evaluate_Non_Retained_Name_Should_Handoff(string command) {
        _guard.CanHandle(command, CreateContext()).Should().BeTrue();
        var decision = _guard.Evaluate(command, CreateContext());
        decision.Should().BeOfType<CommandDecision.Handoff>();
    }

    #endregion

    #region IsRetainedNameCleanupScenario 辅助方法测试

    [Theory]
    [InlineData("D:\\project\\w2\\.xxx\\nul.20260912.del", true)]
    [InlineData("D:\\project\\w2\\.xxx\\con.20260912.del", true)]
    [InlineData("D:\\project\\w2\\.xxx\\prn.20260912.del", true)]
    [InlineData("D:\\project\\w2\\.xxx\\aux.20260912.del", true)]
    [InlineData("D:\\project\\w2\\.xxx\\com1.20260912.del", true)]
    [InlineData("D:\\project\\w2\\.xxx\\lpt1.20260912.del", true)]
    [InlineData("D:\\project\\w2\\src", false)]
    [InlineData("D:\\project\\JoinCode", false)]
    [InlineData("D:\\project\\w2\\normal.txt", false)]
    [InlineData("nul", true)]
    [InlineData("NUL", true)]
    [InlineData("normal.txt", false)]
    public void IsRetainedNameCleanupScenario_Should_Detect(string targetPath, bool expected) {
        RobocopyMirrorGuard.IsRetainedNameCleanupScenario(targetPath).Should().Be(expected);
    }

    #endregion
}