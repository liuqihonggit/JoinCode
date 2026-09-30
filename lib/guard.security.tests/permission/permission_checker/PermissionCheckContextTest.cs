namespace Guard.Security.Tests;

/// <summary>
/// PermissionCheckContext 静态方法守卫测试 — 验证 null/空参数抛 ArgumentNullException/ArgumentException
/// <para>确定性测试:给定 null/空输入 → 断言抛指定异常,不依赖时序/IO。</para>
/// </summary>
[Trait("Category", "Deterministic")]
public class PermissionCheckContextTest {

    #region ExtractPathFromArguments — arguments null 守卫

    [Fact]
    public void ExtractPathFromArguments_NullArguments_ThrowsArgumentNullException() {
        var act = () => PermissionCheckContext.ExtractPathFromArguments(null!);
        act.Should().Throw<ArgumentNullException>()
            .Which.ParamName.Should().Be("arguments");
    }

    #endregion

    #region IsSensitivePath — path null/空 守卫

    [Fact]
    public void IsSensitivePath_NullPath_ThrowsArgumentNullException() {
        var act = () => PermissionCheckContext.IsSensitivePath(null!, []);
        act.Should().Throw<ArgumentNullException>()
            .Which.ParamName.Should().Be("path");
    }

    [Fact]
    public void IsSensitivePath_EmptyPath_ThrowsArgumentException() {
        var act = () => PermissionCheckContext.IsSensitivePath("", []);
        act.Should().Throw<ArgumentException>();
    }

    #endregion

    #region IsDangerousCommand — command null 守卫

    [Fact]
    public void IsDangerousCommand_NullCommand_ThrowsArgumentNullException() {
        var act = () => PermissionCheckContext.IsDangerousCommand(null!, []);
        act.Should().Throw<ArgumentNullException>()
            .Which.ParamName.Should().Be("command");
    }

    #endregion

    #region MatchesPattern — pattern null 守卫

    [Fact]
    public void MatchesPattern_NullPattern_ThrowsArgumentNullException() {
        var act = () => PermissionCheckContext.MatchesPattern("input", null!, PatternType.Contains);
        act.Should().Throw<ArgumentNullException>()
            .Which.ParamName.Should().Be("pattern");
    }

    #endregion
}
