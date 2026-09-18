namespace JoinCode.Cli;

// ─── Dialog ───

/// <summary>
/// 对话框 — CLI 简化版
/// </summary>
public sealed class Dialog
{
    private readonly string _title;
    private readonly string _content;
    private readonly string[] _buttons;

    /// <summary>
    /// 构造对话框实例
    /// </summary>
    /// <param name="title">对话框标题</param>
    /// <param name="content">对话框正文内容</param>
    /// <param name="buttons">按钮标签数组</param>
    public Dialog(string title, string content, string[] buttons)
    {
        _title = title;
        _content = content;
        _buttons = buttons;
    }

    /// <summary>
    /// 异步显示对话框并等待用户选择按钮
    /// </summary>
    /// <param name="ct">取消令牌</param>
    /// <returns>对话框结果，包含是否取消和选中按钮索引</returns>
    public async Task<DialogResult> ShowAsync(CancellationToken ct = default)
    {
        await Task.CompletedTask.ConfigureAwait(false);

        TerminalHelper.WriteLine();
        TerminalHelper.WriteLine($"{AnsiStyleEnumConstants.Bold}{_title}{AnsiStyleEnumConstants.Reset}");
        TerminalHelper.NewLine();
        TerminalHelper.WriteLine(_content);
        TerminalHelper.NewLine();

        for (var i = 0; i < _buttons.Length; i++)
        {
            TerminalHelper.WriteLine($"  {TerminalColors.Muted}{i + 1}.{AnsiStyleEnumConstants.Reset} {_buttons[i]}");
        }

        TerminalHelper.NewLine();

        if (Core.Utils.TestEnvironmentDetector.IsNonInteractive)
        {
            return new DialogResult { Cancelled = true, SelectedIndex = -1 };
        }

        try
        {
            TerminalHelper.WriteRaw($"请选择 (1-{_buttons.Length}, Esc 取消): ");
            var input = TerminalHelper.ReadLine();
            if (string.IsNullOrWhiteSpace(input))
            {
                return new DialogResult { Cancelled = true, SelectedIndex = -1 };
            }

            if (int.TryParse(input.Trim(), out var index) && index >= 1 && index <= _buttons.Length)
            {
                return new DialogResult { Cancelled = false, SelectedIndex = index - 1 };
            }

            return new DialogResult { Cancelled = true, SelectedIndex = -1 };
        }
        catch
        {
            return new DialogResult { Cancelled = true, SelectedIndex = -1 };
        }
    }

    /// <summary>
    /// 异步显示确认提示并等待用户输入 y/N
    /// </summary>
    /// <param name="message">确认提示消息</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>用户输入 y 返回 true，否则返回 false</returns>
    public static async Task<bool> ConfirmAsync(string message, CancellationToken ct = default)
    {
        await Task.CompletedTask.ConfigureAwait(false);

        if (Core.Utils.TestEnvironmentDetector.IsNonInteractive)
        {
            TerminalHelper.WriteLine($"{message} (y/N): ");
            return false;
        }

        TerminalHelper.WriteLineReal();
        TerminalHelper.WriteRawReal($"{message} (y/N): ");

        try
        {
            var response = TerminalHelper.ReadLine();
            return response?.ToLowerInvariant() == "y";
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// 异步显示输入提示并等待用户输入文本
    /// </summary>
    /// <param name="message">输入提示消息</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>用户输入的文本，取消或非交互环境返回 null</returns>
    public static async Task<string?> PromptAsync(string message, CancellationToken ct = default)
    {
        await Task.CompletedTask.ConfigureAwait(false);

        TerminalHelper.WriteLineReal();
        TerminalHelper.WriteRawReal($"{message}: ");

        if (Core.Utils.TestEnvironmentDetector.IsNonInteractive) return null;

        try
        {
            return TerminalHelper.ReadLine();
        }
        catch
        {
            return null;
        }
    }
}

/// <summary>
/// 对话框结果
/// </summary>
public sealed class DialogResult
{
    /// <summary>
    /// 是否已取消对话框
    /// </summary>
    public required bool Cancelled { get; init; }

    /// <summary>
    /// 选中按钮的从零开始的索引，取消时为 -1
    /// </summary>
    public required int SelectedIndex { get; init; }
}

// ─── Confirmation ───

/// <summary>
/// 确认对话框 — CLI 简化版
/// </summary>
public static class Confirmation
{
    /// <summary>
    /// 异步显示确认提示，委托给 Dialog.ConfirmAsync
    /// </summary>
    /// <param name="message">确认提示消息</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>用户确认返回 true，否则返回 false</returns>
    public static Task<bool> ConfirmAsync(string message, CancellationToken ct = default)
    {
        return Dialog.ConfirmAsync(message, ct);
    }

    /// <summary>
    /// 异步显示确认提示，语义同 ConfirmAsync
    /// </summary>
    /// <param name="message">确认提示消息</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>用户确认返回 true，否则返回 false</returns>
    public static Task<bool> ShowAsync(string message, CancellationToken ct = default)
    {
        return Dialog.ConfirmAsync(message, ct);
    }
}

