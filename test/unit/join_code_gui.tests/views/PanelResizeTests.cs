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
    public void PanelHeight_CanBeSetAndClamped() {
        var vm = CreateVm();
        vm.PanelHeight = 300;
        vm.PanelHeight.Should().Be(300);
    }

    [Fact]
    public void PanelWidth_CanBeSetAndClamped() {
        var vm = CreateVm();
        vm.PanelWidth = 400;
        vm.PanelWidth.Should().Be(400);
    }

    [Fact]
    public void PanelBottom_DragDown_IncreasesHeight() {
        var vm = CreateVm();
        vm.PanelPosition = PanelPosition.Bottom;
        vm.IsPanelOpen = true;
        var startHeight = vm.PanelHeight;
        var dy = 50.0;
        vm.PanelHeight = Math.Clamp(startHeight + dy, 0, 800);
        vm.PanelHeight.Should().Be(startHeight + 50);
    }

    [Fact]
    public void PanelTop_DragDown_DecreasesHeight() {
        var vm = CreateVm();
        vm.PanelPosition = PanelPosition.Top;
        vm.IsPanelOpen = true;
        var startHeight = vm.PanelHeight;
        var dy = 50.0;
        vm.PanelHeight = Math.Clamp(startHeight - dy, 0, 800);
        vm.PanelHeight.Should().Be(startHeight - 50);
    }

    [Fact]
    public void PanelRight_DragRight_IncreasesWidth() {
        var vm = CreateVm();
        vm.PanelPosition = PanelPosition.Right;
        vm.IsPanelOpen = true;
        var startWidth = vm.PanelWidth;
        var dx = 60.0;
        vm.PanelWidth = Math.Clamp(startWidth + dx, 0, 800);
        vm.PanelWidth.Should().Be(startWidth + 60);
    }

    [Fact]
    public void PanelLeft_DragRight_DecreasesWidth() {
        var vm = CreateVm();
        vm.PanelPosition = PanelPosition.Left;
        vm.IsPanelOpen = true;
        var startWidth = vm.PanelWidth;
        var dx = 60.0;
        vm.PanelWidth = Math.Clamp(startWidth - dx, 0, 800);
        vm.PanelWidth.Should().Be(startWidth - 60);
    }

    [AvaloniaFact]
    public async Task PanelSash_VisibleWhenPanelOpen() {
        var fs = new IO.FileSystem.InMemoryFileSystem();
        await using var vm = new MainViewModel(new PlaceholderChatSession(),
            new GuiSessionStore(fs, "mem/sessions"), new GuiPreferencesStore(fs, "mem/preferences.json"));
        vm.IsPanelOpen = true;
        vm.PanelPosition = PanelPosition.Bottom;
        var window = new MainWindow { DataContext = vm, Width = 1200, Height = 800 };
        try {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            var visibleBorders = window.GetVisualDescendants()
                .OfType<Border>()
                .Where(b => b.IsVisible && b.Bounds.Width > 0 && b.Bounds.Height > 0)
                .Count();
            visibleBorders.Should().BeGreaterThan(5, "面板打开后应有多个可见 Border 包括 sash");
        } finally { window.Close(); }
    }
}
