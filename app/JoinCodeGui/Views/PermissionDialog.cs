using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

using JoinCode.Gui.Hosting;

namespace JoinCode.Gui.Views;

/// <summary>
/// 权限确认弹窗 — 引擎权限待确认时由 MainWindow 注入回调弹出。
/// 三个决策按钮：拒绝 / 允许本次 / 始终允许；关闭窗口等价于拒绝。
/// </summary>
public sealed class PermissionDialog : Window
{
    private readonly PermissionConfirmationRequest _request;

    public PermissionDialog(PermissionConfirmationRequest request)
    {
        _request = request;
        Title = "权限确认";
        Width = 460;
        MinWidth = 400;
        MaxWidth = 560;
        SizeToContent = Avalonia.Controls.SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        CanResize = false;
        Content = BuildContent();
    }

    /// <summary>搭建弹窗内容（代码构建，规避 XAML 静态资源/附加属性坑）</summary>
    private Control BuildContent()
    {
        var stack = new StackPanel { Margin = new Avalonia.Thickness(20), Spacing = 14 };

        var title = new TextBlock
        {
            Text = $"工具「{_request.ToolName}」请求执行权限",
            FontSize = 15,
            FontWeight = FontWeight.SemiBold,
            Foreground = Brushes.DarkSlateGray,
            TextWrapping = TextWrapping.Wrap
        };

        var prompt = new TextBlock
        {
            Text = _request.ConfirmationPrompt,
            FontSize = 13,
            Foreground = Brushes.DimGray,
            TextWrapping = TextWrapping.Wrap
        };

        if (!string.IsNullOrWhiteSpace(_request.RuleContent))
        {
            var rule = new TextBlock
            {
                Text = _request.RuleContent,
                FontSize = 11,
                Foreground = Brushes.Gray,
                TextWrapping = TextWrapping.Wrap,
                FontFamily = new FontFamily("Consolas")
            };
            stack.Children.Add(rule);
        }

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 10,
            HorizontalAlignment = HorizontalAlignment.Right
        };

        buttons.Children.Add(BuildButton("拒绝", PermissionConfirmationDecision.Deny));
        buttons.Children.Add(BuildButton("允许本次", PermissionConfirmationDecision.Allow));
        buttons.Children.Add(BuildButton("始终允许", PermissionConfirmationDecision.AlwaysAllow));

        stack.Children.Add(title);
        stack.Children.Add(prompt);
        stack.Children.Add(buttons);
        return stack;
    }

    /// <summary>构建决策按钮并绑定点击关闭</summary>
    private Button BuildButton(string text, PermissionConfirmationDecision decision)
    {
        var button = new Button { Content = text, MinWidth = 88 };
        button.Click += (_, _) => Close(decision);
        return button;
    }
}
