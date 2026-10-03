namespace JoinCode.Gui.Tests.Views;

/// <summary>
/// 面板拖拽停靠位置计算测试 — 验证 ComputePanelDropPosition 根据拖拽终点
/// 相对于窗口边缘的距离，返回正确的 PanelPosition 或 null(不停靠)。
/// </summary>
public sealed class PanelDragDockTests {
    private static MainViewModel CreateVm() => new(
        null,
        new GuiSessionStore(new IO.FileSystem.InMemoryFileSystem(), "mem/sessions"),
        new GuiPreferencesStore(new IO.FileSystem.InMemoryFileSystem(), "mem/gui-preferences.json"));

    /// <summary>拖拽到窗口顶部边缘 → 停靠到顶部</summary>
    [Fact]
    public void DropNearTop_ReturnsTop() {
        var vm = CreateVm();
        var result = vm.ComputePanelDropPosition(400, 10, 800, 600);
        result.Should().Be(PanelPosition.Top);
    }

    /// <summary>拖拽到窗口底部边缘 → 停靠到底部</summary>
    [Fact]
    public void DropNearBottom_ReturnsBottom() {
        var vm = CreateVm();
        var result = vm.ComputePanelDropPosition(400, 590, 800, 600);
        result.Should().Be(PanelPosition.Bottom);
    }

    /// <summary>拖拽到窗口左侧边缘 → 停靠到左侧</summary>
    [Fact]
    public void DropNearLeft_ReturnsLeft() {
        var vm = CreateVm();
        var result = vm.ComputePanelDropPosition(10, 300, 800, 600);
        result.Should().Be(PanelPosition.Left);
    }

    /// <summary>拖拽到窗口右侧边缘 → 停靠到右侧</summary>
    [Fact]
    public void DropNearRight_ReturnsRight() {
        var vm = CreateVm();
        var result = vm.ComputePanelDropPosition(790, 300, 800, 600);
        result.Should().Be(PanelPosition.Right);
    }

    /// <summary>拖拽到窗口中心(远离所有边缘) → 不停靠(null)</summary>
    [Fact]
    public void DropAtCenter_ReturnsNull() {
        var vm = CreateVm();
        var result = vm.ComputePanelDropPosition(400, 300, 800, 600);
        result.Should().BeNull();
    }

    /// <summary>拖拽到角落 → 取最近的边缘(左上角→top更近)</summary>
    [Fact]
    public void DropAtCorner_PicksNearestEdge() {
        var vm = CreateVm();
        // (5,5) 在 800x600 窗口中，top=5, left=5，取 top（优先级 top > left）
        var result = vm.ComputePanelDropPosition(5, 5, 800, 600);
        result.Should().Be(PanelPosition.Top);
    }

    /// <summary>拖拽距离不足阈值(在边缘但移动很小) → 不停靠</summary>
    [Fact]
    public void DropWithSmallDrag_ReturnsNull() {
        var vm = CreateVm();
        // 拖拽到顶部边缘但总移动距离只有 5px（小于阈值 20px）
        var result = vm.ComputePanelDropPosition(400, 5, 800, 600, dragDistance: 5);
        result.Should().BeNull();
    }
}
