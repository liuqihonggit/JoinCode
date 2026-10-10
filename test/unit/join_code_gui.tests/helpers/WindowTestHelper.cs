namespace JoinCode.Gui.Tests;

/// <summary>
/// MainWindow 测试辅助 — 强制 Dock 布局渲染，确保控件可在可视树中查找。
/// </summary>
public static class WindowTestHelper {
    /// <summary>创建并显示 MainWindow，强制 Dock 布局渲染</summary>
    public static MainWindow ShowWindow(MainViewModel vm) {
        var win = new MainWindow { DataContext = vm, Width = 1080, Height = 720 };
        win.Show();
        Dispatcher.UIThread.RunJobs();
        win.ApplyTemplate();
        Dispatcher.UIThread.RunJobs();
        var dockControl = win.GetVisualDescendants().OfType<DockControl>().FirstOrDefault();
        if (dockControl is not null) {
            if (dockControl.Layout is null && vm.DockLayout is not null)
                dockControl.Layout = vm.DockLayout;
            dockControl.ApplyTemplate();
            Dispatcher.UIThread.RunJobs();
        }
        var size = new Size(1080, 720);
        win.Measure(size);
        win.Arrange(new Rect(0, 0, size.Width, size.Height));
        Dispatcher.UIThread.RunJobs();
        for (var i = 0; i < 3; i++) {
            foreach (var d in win.GetVisualDescendants().ToList()) {
                if (d is Avalonia.Controls.Primitives.TemplatedControl tc)
                    tc.ApplyTemplate();
            }
            Dispatcher.UIThread.RunJobs();
            win.Measure(size);
            win.Arrange(new Rect(0, 0, size.Width, size.Height));
            Dispatcher.UIThread.RunJobs();
        }
        return win;
    }
}
