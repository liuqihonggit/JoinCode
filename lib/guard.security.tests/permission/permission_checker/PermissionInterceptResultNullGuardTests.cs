namespace Core.Tests.Permission;

/// <summary>
/// PermissionInterceptResult 工厂方法 null 参数守卫测试
/// <para>Denied/ConfirmationRequired 对 null/空/空白 prompt 抛 ArgumentException。</para>
/// </summary>
public sealed class PermissionInterceptResultNullGuardTests {

    #region Denied — reason 守卫

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Denied_空reason_抛ArgumentException(string? reason) {
        var act = () => PermissionInterceptResult.Denied(reason!);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Denied_有效reason_返回Denied结果() {
        var result = PermissionInterceptResult.Denied("危险操作");

        result.IsDenied.Should().BeTrue();
        result.IsAllowed.Should().BeFalse();
        result.RequiresConfirmation.Should().BeFalse();
        result.DenyReason.Should().Be("危险操作");
    }

    #endregion

    #region ConfirmationRequired — prompt 守卫

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ConfirmationRequired_空prompt_抛ArgumentException(string? prompt) {
        var act = () => PermissionInterceptResult.ConfirmationRequired(prompt!);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void ConfirmationRequired_有效prompt_返回Confirmation结果() {
        var result = PermissionInterceptResult.ConfirmationRequired("请确认");

        result.RequiresConfirmation.Should().BeTrue();
        result.IsAllowed.Should().BeFalse();
        result.IsDenied.Should().BeFalse();
        result.ConfirmationPrompt.Should().Be("请确认");
    }

    #endregion

    #region Allowed — 无参数

    [Fact]
    public void Allowed_返回Allowed结果() {
        var result = PermissionInterceptResult.Allowed();

        result.IsAllowed.Should().BeTrue();
        result.IsDenied.Should().BeFalse();
        result.RequiresConfirmation.Should().BeFalse();
    }

    #endregion
}
