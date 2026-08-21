namespace JoinCode.Tui.Views;

/// <summary>
/// 权限确认弹窗组件 — 工具执行前的权限确认对话框。
/// 对齐 claude code 的 PermissionDialog 设计，显示工具名+描述+三档决策按钮（T3 对齐 GUI/CLI）：
/// 允许一次(临时5分钟) / 始终允许(24小时会话级) / 拒绝。
/// </summary>
public sealed class PermissionDialogView : ITuiComponent
{
    private readonly View _container;
    private readonly Label _titleLabel;
    private readonly Label _toolLabel;
    private readonly Label _descriptionLabel;
    private readonly Button _allowButton;
    private readonly Button _alwaysAllowButton;
    private readonly Button _denyButton;
    private TaskCompletionSource<bool>? _pendingResponse;
    private TaskCompletionSource<PermissionConfirmAction>? _pendingDecision;

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
            Text = "允许一次 (y)",
            X = 0,
            Y = Pos.Bottom(_descriptionLabel) + 1,
        };
        _allowButton.Accepting += OnAllow;

        _alwaysAllowButton = new Button
        {
            Text = "始终允许 (a)",
            X = Pos.Right(_allowButton),
            Y = Pos.Bottom(_descriptionLabel) + 1,
        };
        _alwaysAllowButton.Accepting += OnAlwaysAllow;

        _denyButton = new Button
        {
            Text = "拒绝 (n)",
            X = Pos.Right(_alwaysAllowButton),
            Y = Pos.Bottom(_descriptionLabel) + 1,
        };
        _denyButton.Accepting += OnDeny;

        _container.Add(_titleLabel, _toolLabel, _descriptionLabel, _allowButton, _alwaysAllowButton, _denyButton);
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

    /// <summary>
    /// 显示三档权限决策对话框并等待用户选择（T3 对齐 GUI PermissionConfirmationHandler 语义）。
    /// </summary>
    /// <param name="toolName">工具名称。</param>
    /// <param name="description">工具描述。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>用户决策：Deny / Allow(临时) / AlwaysAllow(会话级)。</returns>
    public Task<PermissionConfirmAction> ShowWithDecisionAsync(string toolName, string description, CancellationToken cancellationToken = default)
    {
        _pendingDecision = new TaskCompletionSource<PermissionConfirmAction>(TaskCreationOptions.RunContinuationsAsynchronously);

        _container.Visible = true;
        _toolLabel.Text = $"工具: {toolName}";
        _descriptionLabel.Text = description;

        cancellationToken.Register(() => _pendingDecision.TrySetResult(PermissionConfirmAction.Deny));

#pragma warning disable VSTHRD003 // TaskCompletionSource 任务由按钮事件启动，RunContinuationsAsynchronously 避免死锁
        return _pendingDecision.Task;
#pragma warning restore VSTHRD003
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
        _pendingDecision?.TrySetResult(PermissionConfirmAction.Allow);
        Hide();
    }

    /// <summary>始终允许 — 24 小时会话级批准窗口（由调用方经 GetApprovalDuration 映射）</summary>
    private void OnAlwaysAllow(object? sender, EventArgs e)
    {
        _pendingDecision?.TrySetResult(PermissionConfirmAction.AlwaysAllow);
        Hide();
    }

    private void OnDeny(object? sender, EventArgs e)
    {
        _pendingResponse?.TrySetResult(false);
        _pendingDecision?.TrySetResult(PermissionConfirmAction.Deny);
        Hide();
    }
}
