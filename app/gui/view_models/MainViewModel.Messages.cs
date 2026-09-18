namespace JoinCode.Gui.ViewModels;

/// <summary>
/// MainViewModel 消息管理 partial — 消息集合、搜索过滤、复制/删除/重新生成、导出、token 估算。
/// </summary>
public sealed partial class MainViewModel
{
    /// <summary>UI 对话消息集合（角色化气泡）</summary>
    public ObservableCollection<ChatUiMessage> Messages { get; } = [];

    /// <summary>Assistant 消息计数器（CanRegenerate O(1) 查找，由 OnMessagesChanged 维护）</summary>
    private int _assistantMessageCount;

    /// <summary>
    /// UI 消息列表硬上限（G4 内存防护）— 长会话/批量恢复历史时防止集合无限增长。
    /// 仅裁剪显示层，引擎上下文由 /compact 机制管理；TUI 端对应 OutputView 的 2048 行环形缓冲。
    /// </summary>
    public const int MaxVisibleMessages = 500;

    /// <summary>测试可见的助手消息计数（与 _assistantMessageCount 同源，供一致性断言）</summary>
    internal int AssistantMessageCountForTest => _assistantMessageCount;

    /// <summary>消息条数（随集合变化更新，驱动 UI 计数显示）</summary>
    public int MessageCount => Messages.Count;

    /// <summary>是否有消息（驱动空状态引导与清空按钮）</summary>
    public bool HasMessages => Messages.Count > 0;

    /// <summary>会话累计字符数（含输入与回复，粗略 token 估算用）</summary>
    public int TotalChars => Messages.Sum(m => m.Content.Length);

    /// <summary>估算 token 数（中文约 1.6 字符/token，英文约 4 字符/token，取保守下限 4）</summary>
    public int EstimatedTokens => TotalChars / 4;

    /// <summary>
    /// 本轮真实 token 用量文案（如 "Token:1,234"）— 来自引擎 Complete 事件上报的 Usage，
    /// 对齐 TUI statusBar.SetTokenCount；空串表示引擎未上报。
    /// </summary>
    [ObservableProperty]
    private string _tokenUsageText = string.Empty;

    /// <summary>消息搜索关键词（非空时仅显示匹配消息）</summary>
    [ObservableProperty]
    private string _searchText = string.Empty;

    /// <summary>是否处于搜索状态（驱动搜索框样式与计数显示）</summary>
    public bool IsSearching => !string.IsNullOrWhiteSpace(SearchText);

    /// <summary>已过滤消息集合（按 SearchText 关键词；空搜索返回全部）</summary>
    public IEnumerable<ChatUiMessage> FilteredMessages => IsSearching
        ? Messages.Where(m => (m.Content ?? string.Empty).Contains(SearchText, StringComparison.OrdinalIgnoreCase)
            || (m.ToolResultText ?? string.Empty).Contains(SearchText, StringComparison.OrdinalIgnoreCase))
        : Messages;

    /// <summary>全部消息的终端式纯文本（角色标签+时间戳+内容），供 TextBox 跨行选择</summary>
    public string AllMessagesText
    {
        get
        {
            if (Messages.Count == 0)
                return string.Empty;
            var sb = new System.Text.StringBuilder();
            foreach (var msg in FilteredMessages)
            {
                var text = msg.CopyAllText;
                if (text.Length > 0)
                {
                    sb.AppendLine(text);
                    sb.AppendLine();
                }
            }
            return sb.Length == 0 ? string.Empty : sb.ToString(0, sb.Length - 2);
        }
    }

    partial void OnSearchTextChanged(string value)
    {
        OnPropertyChanged(nameof(FilteredMessages));
        OnPropertyChanged(nameof(AllMessagesText));
    }

    private void OnMessagesChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        if (e.Action == System.Collections.Specialized.NotifyCollectionChangedAction.Reset)
            _assistantMessageCount = Messages.Count(m => m.Role == MessageRole.Assistant);
        else
        {
            if (e.OldItems is not null)
                foreach (ChatUiMessage m in e.OldItems)
                    if (m.Role == MessageRole.Assistant) _assistantMessageCount--;
            if (e.NewItems is not null)
                foreach (ChatUiMessage m in e.NewItems)
                    if (m.Role == MessageRole.Assistant) _assistantMessageCount++;
        }

        // G4 内存防护：超出上限裁剪最旧消息。RemoveAt 触发的 Remove 事件同步重入本处理器，
        // 计数器随 OldItems 递减，此处无需重复扣减
        while (Messages.Count > MaxVisibleMessages)
            Messages.RemoveAt(0);

        OnPropertyChanged(nameof(MessageCount));
        OnPropertyChanged(nameof(HasMessages));
        OnPropertyChanged(nameof(CanRegenerate));
        OnPropertyChanged(nameof(TotalChars));
        OnPropertyChanged(nameof(EstimatedTokens));
        OnPropertyChanged(nameof(FilteredMessages));
        OnPropertyChanged(nameof(AllMessagesText));
        if (e.NewItems is not null)
            foreach (ChatUiMessage m in e.NewItems)
                m.PropertyChanged += OnMessagePropertyChanged;
        if (e.OldItems is not null)
            foreach (ChatUiMessage m in e.OldItems)
                m.PropertyChanged -= OnMessagePropertyChanged;
    }

    /// <summary>单条消息属性变化（流式输出 Content 变化）时刷新 AllMessagesText</summary>
    private void OnMessagePropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ChatUiMessage.Content) or nameof(ChatUiMessage.ToolResultText))
            OnPropertyChanged(nameof(AllMessagesText));
    }

    /// <summary>
    /// 从引擎上下文重读历史并重建消息列表（T1）— 斜杠命令可能改变引擎会话状态
    /// （/resume 装入历史、/clear 清空、/compact 压缩摘要），UI 列表需与引擎保持一致。
    /// 刷新失败仅记日志，不影响命令本身的回显。
    /// </summary>
    /// <param name="echoToKeep">保留在列表末尾的命令回显消息</param>
    private async Task ReloadMessagesFromEngineAsync(ChatUiMessage echoToKeep)
    {
        try
        {
            var records = await _session.GetMessagesAsync(_sendCts?.Token ?? CancellationToken.None);
            Messages.Clear();
            foreach (var record in records)
            {
                if (string.IsNullOrWhiteSpace(record.Content))
                    continue;
                Messages.Add(new ChatUiMessage
                {
                    Role = MessageRoleExtensions.FromValue(record.Role) ?? MessageRole.User,
                    Content = record.Content,
                    Timestamp = record.Timestamp.ToLocalTime()
                });
            }
            Messages.Add(echoToKeep);
        }
        catch (Exception ex)
        {
            ViewModelDiagnosticsLogger.WriteError(ex);
        }
    }

    /// <summary>刚复制的消息实例号（非 null 即显示"已复制"提示）</summary>
    [ObservableProperty]
    private int? _copiedMessage;

    /// <summary>是否刚复制了一条消息（驱动"已复制" toast 显隐）</summary>
    public bool HasCopied => CopiedMessage is not null;

    partial void OnCopiedMessageChanged(int? value)
        => OnPropertyChanged(nameof(HasCopied));

    /// <summary>清空复制反馈状态（toast 自动隐藏用）</summary>
    public void ClearCopiedState() => CopiedMessage = null;

    /// <summary>复制指定消息的完整终端式文本（含思考/工具/diff，穿透容器标签）到剪贴板并标记反馈状态</summary>
    [RelayCommand]
    private void CopyMessage(ChatUiMessage? message)
    {
        if (message is null || string.IsNullOrEmpty(message.CopyAllText))
            return;
        CopiedMessageCopy = message.CopyAllText;
        CopiedMessage = message.Timestamp.GetHashCode();
        // 剪贴板实际写入由 View 层完成（消费 CopiedMessageCopy），此处仅驱动状态提示
    }

    /// <summary>最近一次待复制消息的完整文本（View 层读取后写剪贴板，随后清除）</summary>
    [ObservableProperty]
    private string? _copiedMessageCopy;

    /// <summary>清除待复制消息文本（View 写入剪贴板后调用）</summary>
    public void ClearCopiedMessageCopy() => CopiedMessageCopy = null;

    /// <summary>删除单条消息</summary>
    [RelayCommand]
    private void RemoveMessage(ChatUiMessage? message)
    {
        if (message is not null)
            Messages.Remove(message);
    }

    /// <summary>撤回上一轮回复并重新生成（基于最后一条用户消息）</summary>
    [RelayCommand]
    private async Task RegenerateLastReplyAsync()
    {
        if (IsBusy)
            return;

        var lastUser = Messages.LastOrDefault(m => m.Role == MessageRole.User && !string.IsNullOrWhiteSpace(m.Content));
        if (lastUser is null)
            return;
        var lastUserIndex = Messages.IndexOf(lastUser);

        await _session.RewindLastTurnAsync();
        while (Messages.Count > lastUserIndex)
            Messages.RemoveAt(Messages.Count - 1);

        InputText = lastUser.Content;
        await SendAsync();
    }

    /// <summary>是否有可重新生成的上一轮回复（O(1) 计数器查找）</summary>
    public bool CanRegenerate => _assistantMessageCount > 0;

    [RelayCommand]
    private Task ClearHistoryAsync()
        => ClearHistoryInternalAsync();

    private async Task ClearHistoryInternalAsync()
    {
        Messages.Clear();
        await _session.ClearHistoryAsync();
    }

    /// <summary>会话导出为文本（`角色 时间: 内容` 格式，供复制/下载）</summary>
    public string ExportSessionText
    {
        get
        {
            var sb = new System.Text.StringBuilder();
            foreach (var m in Messages)
            {
                sb.Append('[').Append(m.RoleLabel).Append(" · ")
                  .Append(m.Timestamp.ToString("yyyy-MM-dd HH:mm:ss")).AppendLine("]");
                sb.AppendLine(m.Content);
                sb.AppendLine();
            }
            return sb.ToString();
        }
    }

    /// <summary>复制当前会话为文本到剪贴板（实际写入由 View 层完成）</summary>
    [RelayCommand]
    private void CopySessionExport() => ExportedSessionCopy = ExportSessionText;

    /// <summary>最近一次会话导出文本（View 层读取后写剪贴板，随后清除）</summary>
    [ObservableProperty]
    private string? _exportedSessionCopy;

    /// <summary>清除会话导出副本（View 写入剪贴板后调用）</summary>
    public void ClearSessionExport() => ExportedSessionCopy = null;
}
