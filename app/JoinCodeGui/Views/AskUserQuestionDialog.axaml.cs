using Avalonia.Controls;
using JoinCode.Abstractions.Models.Interactive;

namespace JoinCode.Gui.Views;

/// <summary>
/// AskUserQuestion 弹窗 — 显示问题和选项列表，支持单选/多选/自由输入。
/// MCP AskUserQuestion 工具调用时由 AvaloniaInteractiveService 触发弹出。
/// </summary>
public sealed partial class AskUserQuestionDialog : Window
{
    private readonly List<QuestionOption>? _options;
    private readonly bool _multiSelect;
    private readonly List<int> _selectedIndices = [];

    public AskUserQuestionDialog()
    {
        InitializeComponent();
        CancelButton.Click += (_, _) => Close(null);
        ConfirmFreeInputButton.Click += (_, _) => OnConfirmFreeInput();
    }

    public AskUserQuestionDialog(QuestionItem question) : this()
    {
        _options = question.Options;
        _multiSelect = question.MultiSelect;

        HeaderBlock.Text = question.Header;
        QuestionBlock.Text = question.Question;

        for (int i = 0; i < question.Options.Count; i++)
        {
            var idx = i;
            var opt = question.Options[i];
            var display = string.IsNullOrWhiteSpace(opt.Description)
                ? opt.Label
                : $"{opt.Label} — {opt.Description}";

            var btn = new Button
            {
                Content = display,
                MinWidth = 360,
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch,
                HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Left,
                FontSize = 13,
                Padding = new Avalonia.Thickness(10, 8)
            };
            btn.Click += (_, _) => OnOptionSelected(idx);
            OptionsPanel.Children.Add(btn);
        }

        var freeInputBtn = new Button
        {
            Content = "用户输入内容（自由输入）",
            MinWidth = 360,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch,
            HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Left,
            FontSize = 13,
            Padding = new Avalonia.Thickness(10, 8)
        };
        freeInputBtn.Click += (_, _) => OnFreeInputSelected();
        OptionsPanel.Children.Add(freeInputBtn);
    }

    private void OnOptionSelected(int index)
    {
        if (_multiSelect)
        {
            if (_selectedIndices.Contains(index))
                _selectedIndices.Remove(index);
            else
                _selectedIndices.Add(index);

            if (_selectedIndices.Count > 0 && _options is not null)
            {
                var selected = _selectedIndices.Select(i => _options[i].Label).ToList();
                Close(AskUserQuestionResult.MultiSelectResult(selected));
            }
        }
        else
        {
            Close(AskUserQuestionResult.SuccessResult(_options?[index].Label ?? ""));
        }
    }

    private void OnFreeInputSelected()
    {
        FreeInputBox.IsVisible = true;
        ConfirmFreeInputButton.IsVisible = true;
        FreeInputBox.Focus();
    }

    private void OnConfirmFreeInput()
    {
        var text = FreeInputBox.Text;
        if (string.IsNullOrWhiteSpace(text))
        {
            Close(AskUserQuestionResult.CancelledResult());
            return;
        }
        Close(AskUserQuestionResult.SuccessResult(text));
    }
}
