namespace JoinCode.Gui.Views;

/// <summary>
/// 极简确认对话框（T9）— 供 /exit、/commit 等斜杠命令的 context.Confirm 回调使用。
/// ShowDialog&lt;bool&gt;(owner)：true=确定，false/null=取消。
/// 命名字段（MessageText/OkButton/CancelButton）由 AvaloniaNameSourceGenerator 从 x:Name 生成。
/// </summary>
public partial class ConfirmDialogWindow : Window
{
    public ConfirmDialogWindow()
    {
        InitializeComponent();
    }

    public ConfirmDialogWindow(string message) : this()
    {
        MessageText.Text = message;
    }

    private void OnOkClick(object? sender, RoutedEventArgs e) => Close(true);

    private void OnCancelClick(object? sender, RoutedEventArgs e) => Close(false);
}
