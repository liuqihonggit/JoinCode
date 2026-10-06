namespace Core.Tests.Permission;

/// <summary>
/// BashPermissionChecker 边界测试 — MaxSubcommandsForSecurityCheck=50 边界
/// <para>构造恰好 50 个子命令(通过)和 51 个子命令(触发 Ask)验证边界判断。</para>
/// </summary>
public sealed class BashPermissionCheckerBoundaryTests {

    #region 辅助构造

    private static BashPermissionChecker CreateChecker(
        Mock<IBashSecurityValidator>? securityMock = null,
        Mock<IPathConstraintValidator>? pathMock = null,
        Mock<IReadOnlyCommandDetector>? readOnlyMock = null) {
        securityMock ??= CreateSafeSecurityMock();
        pathMock ??= CreateAllowPathMock();
        readOnlyMock ??= CreateAllowReadOnlyMock();
        return new BashPermissionChecker(
            securityMock.Object,
            pathMock.Object,
            readOnlyMock.Object);
    }

    private static Mock<IBashSecurityValidator> CreateSafeSecurityMock() {
        var mock = new Mock<IBashSecurityValidator>();
        mock.Setup(v => v.Validate(It.IsAny<string>()))
            .Returns(new BashSecurityResult(IsSafe: true));
        return mock;
    }

    private static Mock<IPathConstraintValidator> CreateAllowPathMock() {
        var mock = new Mock<IPathConstraintValidator>();
        mock.Setup(v => v.CheckPathConstraints(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>()))
            .Returns(new PathConstraintResult(PermissionBehavior.Allow));
        return mock;
    }

    private static Mock<IReadOnlyCommandDetector> CreateAllowReadOnlyMock() {
        var mock = new Mock<IReadOnlyCommandDetector>();
        mock.Setup(v => v.CheckReadOnlyConstraints(It.IsAny<string>(), It.IsAny<bool>()))
            .Returns(new ShellPermissionCheckResult(PermissionBehavior.Allow));
        return mock;
    }

    private static string BuildCommand(int subcommandCount) {
        var parts = Enumerable.Range(1, subcommandCount).Select(i => $"echo {i}");
        return string.Join("; ", parts);
    }

    #endregion

    #region MaxSubcommandsForSecurityCheck 边界

    [Fact]
    public void CheckPermission_恰好50个子命令_不触发过多子命令拦截() {
        using var checker = CreateChecker();
        var command = BuildCommand(50);

        var result = checker.CheckPermission(command, @"D:\test");

        result.Behavior.Should().NotBe(PermissionBehavior.Ask);
        result.Message.Should().NotContain("too many subcommands");
    }

    [Fact]
    public void CheckPermission_51个子命令_触发Ask() {
        using var checker = CreateChecker();
        var command = BuildCommand(51);

        var result = checker.CheckPermission(command, @"D:\test");

        result.Behavior.Should().Be(PermissionBehavior.Ask);
        result.Message.Should().Contain("too many subcommands");
        result.Message.Should().Contain("51");
        result.Message.Should().Contain("50");
    }

    [Fact]
    public void CheckPermission_远超50个子命令_触发Ask() {
        using var checker = CreateChecker();
        var command = BuildCommand(100);

        var result = checker.CheckPermission(command, @"D:\test");

        result.Behavior.Should().Be(PermissionBehavior.Ask);
        result.Message.Should().Contain("too many subcommands");
    }

    [Fact]
    public void CheckPermission_少于50个子命令_不触发过多子命令拦截() {
        using var checker = CreateChecker();
        var command = BuildCommand(10);

        var result = checker.CheckPermission(command, @"D:\test");

        result.Behavior.Should().NotBe(PermissionBehavior.Ask);
        result.Message.Should().NotContain("too many subcommands");
    }

    [Fact]
    public void CheckPermission_单个子命令_不触发过多子命令拦截() {
        using var checker = CreateChecker();

        var result = checker.CheckPermission("echo hello", @"D:\test");

        result.Message.Should().NotContain("too many subcommands");
    }

    #endregion

    #region 空命令守卫

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void CheckPermission_空命令_返回Passthrough(string? command) {
        using var checker = CreateChecker();

        var result = checker.CheckPermission(command!, @"D:\test");

        result.Behavior.Should().Be(PermissionBehavior.Passthrough);
    }

    #endregion

    #region 安全检查失败 — 优先于子命令计数

    [Fact]
    public void CheckPermission_安全检查失败_优先返回Ask_不检查子命令数() {
        var securityMock = new Mock<IBashSecurityValidator>();
        securityMock.Setup(v => v.Validate(It.IsAny<string>()))
            .Returns(new BashSecurityResult(IsSafe: false, CheckId: null, Message: "danger"));
        using var checker = CreateChecker(securityMock: securityMock);
        var command = BuildCommand(100);

        var result = checker.CheckPermission(command, @"D:\test");

        result.Behavior.Should().Be(PermissionBehavior.Ask);
        result.Message.Should().NotContain("too many subcommands");
        result.Message.Should().Contain("danger");
    }

    #endregion

    #region 构造函数 null 守卫

    [Fact]
    public void Constructor_NullSecurityValidator_ThrowsArgumentNullException() {
        var act = () => new BashPermissionChecker(
            null!,
            Mock.Of<IPathConstraintValidator>(),
            Mock.Of<IReadOnlyCommandDetector>());
        act.Should().Throw<ArgumentNullException>()
            .Which.ParamName.Should().Be("securityValidator");
    }

    [Fact]
    public void Constructor_NullPathConstraintValidator_ThrowsArgumentNullException() {
        var act = () => new BashPermissionChecker(
            Mock.Of<IBashSecurityValidator>(),
            null!,
            Mock.Of<IReadOnlyCommandDetector>());
        act.Should().Throw<ArgumentNullException>()
            .Which.ParamName.Should().Be("pathConstraintValidator");
    }

    [Fact]
    public void Constructor_NullReadOnlyDetector_ThrowsArgumentNullException() {
        var act = () => new BashPermissionChecker(
            Mock.Of<IBashSecurityValidator>(),
            Mock.Of<IPathConstraintValidator>(),
            null!);
        act.Should().Throw<ArgumentNullException>()
            .Which.ParamName.Should().Be("readOnlyDetector");
    }

    #endregion

    #region CheckPermission workingDirectory null/空 守卫

    [Fact]
    public void CheckPermission_NullWorkingDirectory_ThrowsArgumentNullException() {
        using var checker = CreateChecker();
        var act = () => checker.CheckPermission("echo hello", null!);
        act.Should().Throw<ArgumentNullException>()
            .Which.ParamName.Should().Be("workingDirectory");
    }

    [Fact]
    public void CheckPermission_EmptyWorkingDirectory_ThrowsArgumentException() {
        using var checker = CreateChecker();
        var act = () => checker.CheckPermission("echo hello", "");
        act.Should().Throw<ArgumentException>();
    }

    #endregion
}
