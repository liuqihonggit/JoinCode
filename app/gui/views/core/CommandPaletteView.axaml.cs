namespace JoinCode.Gui.Views;

/// <summary>
/// 命令面板 UserControl — Ctrl+Shift+P 打开,Fuzzy 搜索命令名称,回车执行。
/// </summary>
public sealed partial class CommandPaletteView : UserControl {
    /// <summary>初始化 CommandPaletteView 实例</summary>
    public CommandPaletteView() {
        InitializeComponent();
    }

    /// <summary>输入框键盘处理 — Esc 关闭,回车执行第一条匹配命令</summary>
    private void OnCommandInputKeyDown(object? sender, KeyEventArgs e) {
        if (e.Key == Key.Escape) {
            if (DataContext is ViewModels.MainViewModel vm)
                vm.IsCommandPaletteOpen = false;
            e.Handled = true;
        } else if (e.Key == Key.Enter) {
            if (DataContext is ViewModels.MainViewModel vm && vm.FilteredCommands.Count > 0) {
                vm.ExecuteCommandCommand.Execute(vm.FilteredCommands[0]);
                e.Handled = true;
            }
        }
    }
}
