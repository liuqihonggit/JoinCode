using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;

using JoinCode.Gui.ViewModels;

namespace JoinCode.Gui.Views;

/// <summary>
/// 斜杠命令面板 — 输入 / 时弹出，展示过滤后的命令列表。
/// ↑↓ 导航、Enter 确认（返回命令名）、Esc 取消（返回 null）、双击确认。
/// </summary>
public sealed class CommandPalette : Window
{
    private readonly ListBox _listBox;
    private readonly IReadOnlyList<SlashCommandItem> _filtered;

    /// <param name="prefix">当前输入前缀（如 "/c"），据此过滤命令列表</param>
    /// <param name="commands">命令列表（从引擎获取）；为 null 时回退到 BuiltInCommands</param>
    public CommandPalette(string prefix, IReadOnlyList<SlashCommandItem>? commands = null)
    {
        Title = "斜杠命令";
        Width = 440;
        MinWidth = 380;
        MaxWidth = 520;
        MaxHeight = 420;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        CanResize = false;

        _filtered = SlashCommandItem.Filter(prefix, commands);

        _listBox = new ListBox
        {
            ItemsSource = _filtered,
            ItemTemplate = new FuncDataTemplate<SlashCommandItem>((item, _) =>
                new StackPanel
                {
                    Spacing = 2,
                    Margin = new Avalonia.Thickness(4, 3),
                    Children =
                    {
                        new TextBlock
                        {
                            Text = item?.Name ?? "",
                            FontSize = 13,
                            FontWeight = FontWeight.SemiBold,
                            Foreground = new SolidColorBrush(Color.Parse("#4da6ff"))
                        },
                        new TextBlock
                        {
                            Text = item?.Description ?? "",
                            FontSize = 11,
                            Foreground = new SolidColorBrush(Color.Parse("#979797"))
                        }
                    }
                })
        };

        _listBox.DoubleTapped += (_, _) => ConfirmSelection();

        Content = new StackPanel
        {
            Margin = new Avalonia.Thickness(12),
            Spacing = 8,
            Children =
            {
                new TextBlock
                {
                    Text = "选择命令（↑↓ 导航 · Enter 确认 · Esc 取消）",
                    FontSize = 11,
                    Foreground = new SolidColorBrush(Color.Parse("#979797"))
                },
                _listBox
            }
        };

        if (_filtered.Count > 0)
            _listBox.SelectedIndex = 0;
    }

    /// <summary>过滤后的命令数（0 时调用方可直接关闭）</summary>
    public int CommandCount => _filtered.Count;

    /// <inheritdoc />
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            ConfirmSelection();
        }
        else if (e.Key == Key.Escape)
        {
            e.Handled = true;
            Close(null);
        }
    }

    /// <summary>确认当前选中项，关闭并返回命令名</summary>
    private void ConfirmSelection()
    {
        if (_listBox.SelectedItem is SlashCommandItem item)
            Close(item.Name);
        else if (_filtered.Count > 0)
            Close(_filtered[0].Name);
        else
            Close(null);
    }
}
