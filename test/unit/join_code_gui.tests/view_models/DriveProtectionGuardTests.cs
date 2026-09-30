namespace JoinCode.Gui.Tests.ViewModels;

/// <summary>
/// DriveProtectionGuard 单元测试 — 验证磁盘根保护守卫的拦截逻辑（ADR 0123）。
/// 命令参数为保护盘根路径（如 C:\）→ Deny；子目录（如 C:\Users）→ Allow。
/// </summary>
public class DriveProtectionGuardTests {
    private static GuardContext Ctx() => new(SystemActuatorKind.Bash, @"D:\project");

    [Fact]
    public void CanHandle_NoProtectedDrives_ReturnsFalse() {
        var store = new ProtectedDriveStore();
        var guard = new DriveProtectionGuard(store);

        guard.CanHandle("rg C:\\", Ctx()).Should().BeFalse("无保护盘时不拦截");
    }

    [Fact]
    public void CanHandle_CommandTargetsProtectedDriveRoot_ReturnsTrue() {
        var store = new ProtectedDriveStore();
        store.Update(FrozenSet.Create(StringComparer.OrdinalIgnoreCase, "C:"));
        var guard = new DriveProtectionGuard(store);

        guard.CanHandle("rg C:\\", Ctx()).Should().BeTrue("命令参数为保护盘根路径应拦截");
    }

    [Fact]
    public void CanHandle_CommandTargetsSubdirectory_ReturnsFalse() {
        var store = new ProtectedDriveStore();
        store.Update(FrozenSet.Create(StringComparer.OrdinalIgnoreCase, "C:"));
        var guard = new DriveProtectionGuard(store);

        guard.CanHandle("rg C:\\Users\\foo", Ctx()).Should().BeFalse("子目录操作不拦截");
    }

    [Fact]
    public void CanHandle_CommandTargetsUnprotectedDrive_ReturnsFalse() {
        var store = new ProtectedDriveStore();
        store.Update(FrozenSet.Create(StringComparer.OrdinalIgnoreCase, "C:"));
        var guard = new DriveProtectionGuard(store);

        guard.CanHandle("rg D:\\", Ctx()).Should().BeFalse("非保护盘不拦截");
    }

    [Fact]
    public void Evaluate_ProtectedDriveRoot_ReturnsDeny() {
        var store = new ProtectedDriveStore();
        store.Update(FrozenSet.Create(StringComparer.OrdinalIgnoreCase, "C:"));
        var guard = new DriveProtectionGuard(store);

        var decision = guard.Evaluate("rm -rf C:\\", Ctx());
        decision.Should().BeOfType<CommandDecision.Deny>("删除保护盘根目录应拒绝");
        var deny = (CommandDecision.Deny)decision;
        deny.Diagnostic.Reason.Should().Be("DriveRootProtected");
        deny.Diagnostic.FormattedMessage.Should().Contain("C:");
    }

    [Fact]
    public void Evaluate_ScanProtectedDriveRoot_ReturnsDeny() {
        var store = new ProtectedDriveStore();
        store.Update(FrozenSet.Create(StringComparer.OrdinalIgnoreCase, "D:"));
        var guard = new DriveProtectionGuard(store);

        var decision = guard.Evaluate("rg D:\\", Ctx());
        decision.Should().BeOfType<CommandDecision.Deny>("扫盘保护盘根目录应拒绝");
    }

    [Fact]
    public void Evaluate_NoMatch_ReturnsAllow() {
        var store = new ProtectedDriveStore();
        store.Update(FrozenSet.Create(StringComparer.OrdinalIgnoreCase, "C:"));
        var guard = new DriveProtectionGuard(store);

        var decision = guard.Evaluate("rg D:\\", Ctx());
        decision.Should().BeOfType<CommandDecision.Allow>("非保护盘应放行");
    }

    [Fact]
    public void Update_EmptySet_DisablesProtection() {
        var store = new ProtectedDriveStore();
        store.Update(FrozenSet.Create(StringComparer.OrdinalIgnoreCase, "C:"));
        store.Update(FrozenSet<string>.Empty);
        var guard = new DriveProtectionGuard(store);

        guard.CanHandle("rg C:\\", Ctx()).Should().BeFalse("清空保护盘后不拦截");
    }
}
