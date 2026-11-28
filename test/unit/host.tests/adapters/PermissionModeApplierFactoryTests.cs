namespace Host.Tests.Adapters;

/// <summary>
/// TASK143 — 权限模式应用器工厂（bridge_serve 与 SDK 流式输入共用同一份解析/文案）。
/// <para>不构造真实 <c>PermissionChecker</c>：其构造会读 <c>JCC_PERMISSION_MODE</c> 环境变量与
/// settings.json，与其它测试并行时是共享进程态（guard_security_tests 为此专门串行化）。
/// 真实应用路径由真机 stdin 注入 <c>set_permission_mode</c> 验证。</para>
/// </summary>
public sealed class PermissionModeApplierFactoryTests {
    /// <summary>无权限系统（引擎未注册 IPermissionChecker）时必须返回 null，由调用方发 error 而不是崩。</summary>
    [Fact]
    public void Create_WithoutChecker_ReturnsNull() {
        PermissionModeApplierFactory.Create(null).Should().BeNull();
    }

    /// <summary>失败文案要给出合法值清单，客户端才有下一步动作；bypass 被闸拦截的语义也要说清。</summary>
    [Fact]
    public void DescribeFailure_ListsValidModesAndBypassGate() {
        var text = PermissionModeApplierFactory.DescribeFailure("bogus");

        text.Should().Contain("bogus");
        text.Should().Contain(PermissionModeApplierFactory.ValidPermissionModes);
        text.Should().Contain("disableBypassPermissionsMode", "被安全闸拦下时实际值不变，客户端要能理解");
    }

    /// <summary>
    /// 锁住「清单 ≡ 枚举」：清单是给客户端看的契约，枚举是真实能力，两者漂移就会出现
    /// 「提示里说能用、实际 FromValue 返回 null」。
    /// </summary>
    [Fact]
    public void ValidPermissionModes_AllParseToEnum() {
        var values = PermissionModeApplierFactory.ValidPermissionModes.Split(
            ", ", StringSplitOptions.RemoveEmptyEntries);

        values.Should().HaveCount(5, "PermissionMode 枚举当前 5 个核心模式");
        foreach (var value in values) {
            JoinCode.Abstractions.Security.PermissionModeExtensions.FromValue(value)
                .Should().NotBeNull($"清单里的 '{value}' 必须能被枚举解析");
        }
    }
}
