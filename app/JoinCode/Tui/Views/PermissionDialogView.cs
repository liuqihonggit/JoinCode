namespace JoinCode.Tui.Views;

/// <summary>
/// 权限确认弹窗组件 — 工具执行前的权限确认对话框。
/// 对齐 claude code 的 PermissionDialog 设计，显示工具名+描述+允许/拒绝按钮。
/// </summary>
public sealed class PermissionDialogView : ITuiComponent
{
    private readonly View _container;
    private readonly Label _titleLabel;
    private readonly Label _toolLabel;
    private readonly Label _descriptionLabel;
    private readonly Button _allowButton;
    private readonly Button _denyButton;
    private TaskCompletionSource<bool>? _pendingResponse;

    /// <summary>
    /// 创建 PermissionDialogView。
    /// </summary>
    public PermissionDialogView()
    {
        _container = new View
        {
            Width = Dim.Fill(),
            Height = Dim.Auto(),
            Visible = false,
        };

        _titleLabel = new Label
        {
            Text = "权限确认",
            X = 0,
            Y = 0,
            Width = Dim.Fill(),
        };

        _toolLabel = new Label
        {
            Text = "",
            X = 0,
            Y = Pos.Bottom(_titleLabel),
            Width = Dim.Fill(),
        };

        _descriptionLabel = new Label
        {
            Text = "",
            X = 0,
            Y = Pos.Bottom(_toolLabel),
            Width = Dim.Fill(),
        };

        _allowButton = new Button
        {
            Text = "允许 (y)",
            X = 0,
            Y = Pos.Bottom(_descriptionLabel) + 1,
        };
        _allowButton.Accepting += OnAllow;

        _denyButton = new Button
        {
            Text = "拒绝 (n)",
            X = Pos.Right(_allowButton),
            Y = Pos.Bottom(_descriptionLabel) + 1,
        };
        _denyButton.Accepting += OnDeny;

        _container.Add(_titleLabel, _toolLabel, _descriptionLabel, _allowButton, _denyButton);
    }

    /// <inheritdoc />
    public View TerminalView => _container;

    /// <summary>显示权限确认对话框并等待用户响应。</summary>
    /// <param name="toolName">工具名称。</param>
    /// <param name="description">工具描述。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>true 表示允许，false 表示拒绝。</returns>
    public Task<bool> ShowAsync(string toolName, string description, CancellationToken cancellationToken = default)
    {
        _pendingResponse = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        _container.Visible = true;
        _toolLabel.Text = $"工具: {toolName}";
        _descriptionLabel.Text = description;

        cancellationToken.Register(() => _pendingResponse.TrySetResult(false));

#pragma warning disable VSTHRD003 // TaskCompletionSource 任务由按钮事件启动，RunContinuationsAsynchronously 避免死锁
        return _pendingResponse.Task;
#pragma warning restore VSTHRD003
    }

    /// <summary>隐藏对话框。</summary>
    public void Hide()
    {
        _container.Visible = false;
    }

    /// <inheritdoc />
    public void OnQueueChanged(QueueSnapshot snapshot)
    {
    }

    /// <inheritdoc />
    public void OnResize(int cols, int rows)
    {
        _container.Width = Dim.Fill();
    }

    private void OnAllow(object? sender, EventArgs e)
    {
        _pendingResponse?.TrySetResult(true);
        Hide();
    }

    private void OnDeny(object? sender, EventArgs e)
    {
        _pendingResponse?.TrySetResult(false);
        Hide();
    }
}
