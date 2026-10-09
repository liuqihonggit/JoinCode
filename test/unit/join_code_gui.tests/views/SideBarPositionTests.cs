namespace JoinCode.Gui.Tests.Views;

/// <summary>
/// Side Bar 位置切换测试 — 验证 PrimarySideBarPosition 在左↔右之间切换,
/// 以及 IsPrimarySideBarLeft/IsPrimarySideBarRight 辅助属性正确反映位置。
/// </summary>
public sealed class SideBarPositionTests {
    private static MainViewModel CreateVm() => new(
        null,
        new GuiSessionStore(new IO.FileSystem.InMemoryFileSystem(), "mem/sessions"),
        new GuiPreferencesStore(new IO.FileSystem.InMemoryFileSystem(), "mem/gui-preferences.json"));

    /// <summary>初始位置应为 Left</summary>
    [Fact]
    public async Task InitialPosition_IsLeft() {
        await using var vm = CreateVm();
        vm.PrimarySideBarPosition.Should().Be(SideBarPosition.Left);
        vm.IsPrimarySideBarLeft.Should().BeTrue();
        vm.IsPrimarySideBarRight.Should().BeFalse();
    }

    /// <summary>切换一次 → Right</summary>
    [Fact]
    public async Task ToggleOnce_GoesRight() {
        await using var vm = CreateVm();
        vm.ToggleSideBarPositionCommand.Execute(null);
        vm.PrimarySideBarPosition.Should().Be(SideBarPosition.Right);
        vm.IsPrimarySideBarLeft.Should().BeFalse();
        vm.IsPrimarySideBarRight.Should().BeTrue();
    }

    /// <summary>切换两次 → 回到 Left</summary>
    [Fact]
    public async Task ToggleTwice_ReturnsLeft() {
        await using var vm = CreateVm();
        vm.ToggleSideBarPositionCommand.Execute(null);
        vm.ToggleSideBarPositionCommand.Execute(null);
        vm.PrimarySideBarPosition.Should().Be(SideBarPosition.Left);
    }
}
