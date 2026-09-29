namespace JoinCode.Gui.Tests.ViewModels;

/// <summary>
/// 无人值守模式 GUI 开关传导测试 — 验证 IsUnattendedMode 属性变更传导到引擎权限系统。
/// 修复 ADR 0012 Bug1：GUI 开关断裂（切换只写 settings.json 不激活权限模式）。
/// </summary>
public class MainViewModelUnattendedTests {
    /// <summary>创建注入 PlaceholderChatSession 的 ViewModel — PlaceholderChatSession 记录 SetPermissionModeAsync 调用</summary>
    private static (MainViewModel vm, JoinCode.Gui.Hosting.PlaceholderChatSession session) Create() {
        var session = new JoinCode.Gui.Hosting.PlaceholderChatSession();
        var vm = new MainViewModel(
            session,
            new GuiSessionStore(new InMemoryFileSystem(), "mem/sessions"),
            new GuiPreferencesStore(new InMemoryFileSystem(), "mem/gui-preferences.json"));
        return (vm, session);
    }

    [Fact]
    public void IsUnattendedMode_WhenToggledOn_CallsSetPermissionModeWithUnattended() {
        var (vm, session) = Create();
        vm.IsUnattendedMode = true;
        session.LastSetPermissionMode.Should().Be(PermissionMode.Unattended);
    }

    [Fact]
    public void IsUnattendedMode_WhenToggledOff_CallsSetPermissionModeWithAuto() {
        var (vm, session) = Create();
        vm.IsUnattendedMode = true;
        vm.IsUnattendedMode = false;
        session.LastSetPermissionMode.Should().Be(PermissionMode.Auto);
    }
}
