namespace Guard.Security.Tests;

/// <summary>
/// PermissionMode autonomous 别名测试 — 用户显式选择“完全访问 + AI 监控”时映射到 Unattended 语义
/// </summary>
public sealed class PermissionModeAliasTests {
    [Fact]
    public void FromValue_Autonomous_ResolvesToUnattended() {
        PermissionModeExtensions.FromValue("autonomous").Should().Be(PermissionMode.Unattended);
    }

    [Fact]
    public void ToValue_Unattended_KeepsPrimaryValue() {
        PermissionMode.Unattended.ToValue().Should().Be("unattended");
    }
}
