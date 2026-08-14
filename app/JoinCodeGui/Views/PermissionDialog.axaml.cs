using Avalonia.Controls;
using CommunityToolkit.Mvvm.Input;

using JoinCode.Gui.Hosting;

namespace JoinCode.Gui.Views;

/// <summary>
/// 权限确认弹窗 — 引擎权限待确认时由 MainWindow 注入回调弹出。
/// 三个决策按钮：拒绝 / 允许本次 / 始终允许；关闭窗口等价于拒绝。
/// UI 由 PermissionDialog.axaml 定义，按钮通过 Command 绑定关闭返回决策。
/// </summary>
public sealed partial class PermissionDialog : Window
{
    /// <summary>拒绝命令（关闭窗口返回 Deny）</summary>
    public System.Windows.Input.ICommand DenyCommand { get; }

    /// <summary>允许本次命令（关闭窗口返回 Allow）</summary>
    public System.Windows.Input.ICommand AllowCommand { get; }

    /// <summary>始终允许命令（关闭窗口返回 AlwaysAllow）</summary>
    public System.Windows.Input.ICommand AlwaysAllowCommand { get; }

    public PermissionDialog()
    {
        InitializeComponent();
        DenyCommand = new RelayCommand(() => Close(PermissionConfirmationDecision.Deny));
        AllowCommand = new RelayCommand(() => Close(PermissionConfirmationDecision.Allow));
        AlwaysAllowCommand = new RelayCommand(() => Close(PermissionConfirmationDecision.AlwaysAllow));
    }

    /// <summary>以权限请求为 DataContext 构建弹窗</summary>
    public PermissionDialog(PermissionConfirmationRequest request) : this()
    {
        DataContext = request;
    }
}
