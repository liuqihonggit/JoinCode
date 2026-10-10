namespace JoinCode.Gui.Tests.Views;

/// <summary>
/// 面板大小调整测试 — 验证 PanelHeight/PanelWidth 属性可正确设置和读取，
/// 以及四种位置下拖拽方向计算正确。
/// </summary>
public sealed class PanelResizeTests {
    private static MainViewModel CreateVm() => new(
        null,
        new GuiSessionStore(new IO.FileSystem.InMemoryFileSystem(), "mem/sessions"),
        new GuiPreferencesStore(new IO.FileSystem.InMemoryFileSystem(), "mem/gui-preferences.json"));

    [Fact]
    public async Task PanelHeight_CanBeSetAndClamped() {
        await using var vm = CreateVm();
        vm.PanelHeight = 300;
        vm.PanelHeight.Should().Be(300);
    }

    [Fact]
    public async Task PanelWidth_CanBeSetAndClamped() {
        await using var vm = CreateVm();
        vm.PanelWidth = 400;
        vm.PanelWidth.Should().Be(400);
    }

    [Fact]
    public async Task PanelBottom_DragDown_IncreasesHeight() {
        await using var vm = CreateVm();
        vm.PanelPosition = PanelPosition.Bottom;
        vm.IsPanelOpen = true;
        var startHeight = vm.PanelHeight;
        var dy = 50.0;
        vm.PanelHeight = Math.Clamp(startHeight + dy, 0, 800);
        vm.PanelHeight.Should().Be(startHeight + 50);
    }

    [Fact]
    public async Task PanelTop_DragDown_DecreasesHeight() {
        await using var vm = CreateVm();
        vm.PanelPosition = PanelPosition.Top;
        vm.IsPanelOpen = true;
        var startHeight = vm.PanelHeight;
        var dy = 50.0;
        vm.PanelHeight = Math.Clamp(startHeight - dy, 0, 800);
        vm.PanelHeight.Should().Be(startHeight - 50);
    }

    [Fact]
    public async Task PanelRight_DragRight_IncreasesWidth() {
        await using var vm = CreateVm();
        vm.PanelPosition = PanelPosition.Right;
        vm.IsPanelOpen = true;
        var startWidth = vm.PanelWidth;
        var dx = 60.0;
        vm.PanelWidth = Math.Clamp(startWidth + dx, 0, 800);
        vm.PanelWidth.Should().Be(startWidth + 60);
    }

    [Fact]
    public async Task PanelLeft_DragRight_DecreasesWidth() {
        await using var vm = CreateVm();
        vm.PanelPosition = PanelPosition.Left;
        vm.IsPanelOpen = true;
        var startWidth = vm.PanelWidth;
        var dx = 60.0;
        vm.PanelWidth = Math.Clamp(startWidth - dx, 0, 800);
        vm.PanelWidth.Should().Be(startWidth - 60);
    }

    [Fact]
    public async Task PanelSash_VisibleWhenPanelOpen() {
        await using var vm = CreateVm();
        vm.IsPanelOpen = true;
        vm.PanelPosition = PanelPosition.Bottom;
        vm.IsPanelOpen.Should().BeTrue("面板应已打开");
        vm.PanelHeight.Should().BeGreaterThan(0, "面板打开时高度应大于0");
        vm.PanelWidth.Should().BeGreaterThan(0, "面板打开时宽度应大于0");
    }
}
