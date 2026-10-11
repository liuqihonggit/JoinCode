namespace Guard.Security.Tests;

/// <summary>
/// PathConstraintValidator 保留设备名重定向拦截测试 — P0-③ 单数据源改造
/// <para>
/// 验证 CheckPathConstraints 对全部22个保留设备名(nul/con/prn/aux/com1-9/lpt1-9)的重定向拦截
/// 改造前:仅拦截 NUL,漏检 con/prn/aux/com1-9/lpt1-9 共21个设备名(可能被路径检查误拦但消息不对)
/// 改造后:委托 RetainedDeviceNames.IsMatch 统一拦截全部22个,消息含"保留设备名"
/// </para>
/// </summary>
public class PathConstraintValidatorRetainedDeviceTests {
    private readonly PathConstraintValidator _validator = new(new StubPathValidator());

    #region 全部22个保留设备名重定向应拦截(消息含"保留设备名")

    [Theory]
    [InlineData("echo test >nul")]
    [InlineData("echo test >con")]
    [InlineData("echo test >prn")]
    [InlineData("echo test >aux")]
    [InlineData("echo test >com1")]
    [InlineData("echo test >com2")]
    [InlineData("echo test >com3")]
    [InlineData("echo test >com4")]
    [InlineData("echo test >com5")]
    [InlineData("echo test >com6")]
    [InlineData("echo test >com7")]
    [InlineData("echo test >com8")]
    [InlineData("echo test >com9")]
    [InlineData("echo test >lpt1")]
    [InlineData("echo test >lpt2")]
    [InlineData("echo test >lpt3")]
    [InlineData("echo test >lpt4")]
    [InlineData("echo test >lpt5")]
    [InlineData("echo test >lpt6")]
    [InlineData("echo test >lpt7")]
    [InlineData("echo test >lpt8")]
    [InlineData("echo test >lpt9")]
    public void CheckPathConstraints_RetainedDeviceRedirect_ShouldAskWithDeviceNameMessage(string command) {
        var result = _validator.CheckPathConstraints(command, "/tmp");
        result.Behavior.Should().Be(PermissionBehavior.Ask);
        result.Message.Should().Contain("保留设备名");
    }

    #endregion

    #region 大小写不敏感

    [Theory]
    [InlineData("echo test >NUL")]
    [InlineData("echo test >Con")]
    [InlineData("echo test >PRN")]
    [InlineData("echo test >AUX")]
    [InlineData("echo test >COM1")]
    [InlineData("echo test >LPT9")]
    public void CheckPathConstraints_RetainedDeviceRedirect_CaseInsensitive_ShouldAskWithDeviceNameMessage(string command) {
        var result = _validator.CheckPathConstraints(command, "/tmp");
        result.Behavior.Should().Be(PermissionBehavior.Ask);
        result.Message.Should().Contain("保留设备名");
    }

    #endregion

    #region /dev/null 始终安全

    [Theory]
    [InlineData("echo test >/dev/null")]
    [InlineData("echo test 2>/dev/null")]
    [InlineData("echo test >/dev/null 2>&1")]
    public void CheckPathConstraints_DevNull_ShouldNotAsk(string command) {
        var result = _validator.CheckPathConstraints(command, "/tmp");
        result.Behavior.Should().NotBe(PermissionBehavior.Ask);
    }

    #endregion

    #region 普通文件重定向不拦截

    [Theory]
    [InlineData("echo test > output.txt")]
    [InlineData("echo test 2> error.log")]
    public void CheckPathConstraints_NormalFileRedirect_ShouldNotAsk(string command) {
        var result = _validator.CheckPathConstraints(command, "/tmp");
        result.Behavior.Should().NotBe(PermissionBehavior.Ask);
    }

    #endregion

    private sealed class StubPathValidator : IPathValidator {
        public ValidationResult ValidatePaths(ShellCommand command, string workingDirectory)
            => ValidationResult.Valid();
        public bool IsPathWithinWorkspace(string path, string workingDirectory) => true;
    }
}
