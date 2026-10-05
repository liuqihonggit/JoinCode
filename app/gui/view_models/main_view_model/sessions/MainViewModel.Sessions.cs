namespace JoinCode.Gui.ViewModels;

/// <summary>
/// MainViewModel 会话管理 partial — 侧边栏会话列表、新建/选中/删除/清空、重命名、持久化加载/保存。
/// </summary>
public sealed partial class MainViewModel {
    /// <summary>侧边栏会话列表（占位阶段）</summary>
    public ObservableCollection<SessionItem> Sessions { get; } = [];

    private int _sessionCounter;

    /// <summary>当前活动会话（首条用户消息后自动设为新标题）</summary>
    private SessionItem? _activeSession;

    /// <summary>当前选中的会话（供视图侧边栏选中/删除定位）</summary>
    public SessionItem? SelectedSession => Sessions.FirstOrDefault(s => s.IsSelected);

    /// <summary>启动时从同一 sessions 目录恢复历史会话到侧边栏（CLI 与 GUI 共享会话文件）</summary>
    private async Task LoadPersistedSessionsAsync() {
        var summaries = await _sessionStore.ListSessionsAsync();
        ViewModelDiagnosticsLogger.WriteDebug($"LoadPersistedSessions: 读取到 {summaries.Count} 个会话: [{string.Join(", ", summaries.Select(s => $"{s.Id}({s.Title})"))}]");
        Avalonia.Threading.Dispatcher.UIThread.Post(() => {
            foreach (var summary in summaries) {
                var item = new SessionItem {
                    Id = summary.Id,
                    Title = summary.Title
                };
                LoadSubSessionsInto(item);
                Sessions.Add(item);
            }
            ViewModelDiagnosticsLogger.WriteDebug($"LoadPersistedSessions: Sessions 现有 {Sessions.Count} 个, 激活第一个 tab");
            if (Sessions.Count > 0)
                _dockFactory?.ActivateSession(Sessions[0].Id);
        });
    }

    /// <summary>新建一个会话（加入侧边栏并选中）</summary>
    [RelayCommand]
    private void NewConversation() {
        _sessionCounter++;
        var item = new SessionItem {
            Title = $"会话 {_sessionCounter}",
            IsSelected = true
        }; foreach (var s in Sessions)
            s.IsSelected = false;
        Sessions.Add(item);
        _activeSession = item;
        Messages.Clear();
        _session.SwitchSession(item.Id);
        _dockFactory?.ActivateSession(item.Id);
        OnPropertyChanged(nameof(Sessions));
    }

    /// <summary>从磁盘枚举子会话填充 session.Children</summary>
    private void LoadSubSessionsInto(SessionItem session) {
        session.Children.Clear();
        foreach (var (id, title) in _sessionStore.ListSubSessions(session.Id)) {
            session.Children.Add(new SessionItem {
                Id = id,
                Title = title,
                ParentId = session.Id
            });
        }
        ViewModelDiagnosticsLogger.WriteDebug($"LoadSubSessionsInto: 会话 {session.Id} 有 {session.Children.Count} 个子会话");
    }

    /// <summary>将当前会话消息持久化到 ~/.jcc/sessions/{Id}.json（含自动命名标题）</summary>
    private async Task SaveActiveSessionAsync() {
        if (_activeSession is null)
            return;

        var data = new Persistence.GuiSessionData {
            Id = _activeSession.Id,
            CustomTitle = _activeSession.Title,
            CreatedAt = DateTime.UtcNow,
            Messages = Messages
                .Where(m => m.Role is MessageRole.User or MessageRole.Assistant && !string.IsNullOrWhiteSpace(m.Content))
                .Select(m => new Persistence.GuiSessionMessage {
                    Role = m.Role.ToValue(),
                    Content = m.Content,
                    Timestamp = m.Timestamp
                })
                .ToList()
        };

        try {
            await _sessionStore.SaveAsync(data);
        } catch (Exception ex) {
            System.Diagnostics.Debug.WriteLine($"[MainViewModel] 会话持久化失败: {ex.Message}");
        }
    }

    /// <summary>重置界面布局到初始状态 — 恢复 Dock 面板位置、底部面板、编辑器视图</summary>
    [RelayCommand]
    private void ClearAllSessions() {
        InitDockLayout();
        IsPanelOpen = true;
        ActivePanelTab = PanelTabKind.Log;
        PanelPosition = PanelPosition.Bottom;
        PanelHeight = PanelDefaultHeight;
        PanelWidth = PanelDefaultWidth;
        ActiveMainArea = MainAreaKind.Messages;
    }

    /// <summary>从会话列表删除指定会话（同步删除持久化文件）</summary>
    [RelayCommand]
    private async Task RemoveSessionAsync(SessionItem? session) {
        if (session is null)
            return;
        Sessions.Remove(session);
        try {
            await _sessionStore.DeleteAsync(session.Id);
        } catch (Exception ex) {
            System.Diagnostics.Debug.WriteLine($"[MainViewModel] 会话删除失败: {ex.Message}");
        }
        if (session.IsSelected && Sessions.Count > 0)
            Sessions[^1].IsSelected = true;
    }

    /// <summary>选中指定会话（单击切换当前会话，同一时刻仅一个选中；未选中态用作未可选区分）</summary>
    [RelayCommand]
    private async Task SelectSession(SessionItem? session) {
        if (session is null)
            return;
        if (session == _activeSession)
            return;

        foreach (var s in Sessions)
            s.IsSelected = s == session;
        _activeSession = session;
        _session.SwitchSession(session.Id);
        _dockFactory?.ActivateSession(session.Id);
        LoadSubSessionsInto(session);
        OnPropertyChanged(nameof(SelectedSession));

        // 需求11：子会话点击展示内容（SubSessionMessages 缓存或引擎加载）
        if (session.IsSubSession) {
            await LoadSubSessionContentAsync(session);
            return;
        }

        // 切换会话时从持久化恢复该会话消息到消息区（空会话则清空）
        var data = await _sessionStore.LoadAsync(session.Id);
        Messages.Clear();
        var historyForEngine = new List<(MessageRole Role, string Content)>();
        if (data is not null) {
            foreach (var msg in data.Messages) {
                if (string.IsNullOrWhiteSpace(msg.Content))
                    continue;
                var role = MessageRoleExtensions.FromValue(msg.Role) ?? MessageRole.User;
                Messages.Add(new ChatUiMessage {
                    Role = role,
                    Content = msg.Content,
                    Timestamp = msg.Timestamp
                });
                historyForEngine.Add((role, msg.Content));
            }
        } else {
            // 回退：transcript.json 不存在时从引擎内存获取当前会话消息
            try {
                var records = await _session.GetMessagesAsync(CancellationToken.None);
                foreach (var r in records) {
                    if (string.IsNullOrWhiteSpace(r.Content))
                        continue;
                    var role = MessageRoleExtensions.FromValue(r.Role) ?? MessageRole.User;
                    Messages.Add(new ChatUiMessage {
                        Role = role,
                        Content = r.Content,
                        Timestamp = r.Timestamp
                    });
                    historyForEngine.Add((role, r.Content));
                }
                ViewModelDiagnosticsLogger.WriteDebug($"SelectSession: transcript.json 不存在, 从引擎获取 {Messages.Count} 条消息");
            } catch (Exception ex) {
                ViewModelDiagnosticsLogger.WriteError(ex);
            }
        }

        // 把持久化历史灌入底层引擎上下文 — GUI 新进程 StateService 内存为空，
        // SwitchSession 仅切换 sessionId 不加载历史，需显式灌入否则发送时 LLM 收不到历史
        await _session.LoadHistoryAsync(historyForEngine);
    }

    /// <summary>重命名指定会话（标题由视图双击触发，空标题忽略）</summary>
    [RelayCommand]
    private void RenameSession(string? title) {
        if (string.IsNullOrWhiteSpace(title))
            return;
        var active = SelectedSession;
        if (active is not null)
            active.Title = title.Trim();
    }

    /// <summary>进入重命名编辑态（双击会话条目）</summary>
    [RelayCommand]
    private void BeginRenameSession(SessionItem? session) {
        if (session is null)
            return;
        foreach (var s in Sessions)
            s.IsSelected = s == session;
        session.IsRenaming = true;
        session.RenameDraft = session.Title;
    }

    /// <summary>提交重命名（Enter 触发），空标题则保留原名</summary>
    [RelayCommand]
    private void CommitRenameSession(SessionItem? session) {
        if (session is null)
            return;
        session.IsRenaming = false;
        if (!string.IsNullOrWhiteSpace(session.RenameDraft))
            session.Title = session.RenameDraft.Trim();
    }

    /// <summary>取消重命名（Esc 触发），恢复原标题</summary>
    [RelayCommand]
    private void CancelRenameSession(SessionItem? session) {
        if (session is not null)
            session.IsRenaming = false;
    }

    /// <summary>用首条用户消息为会话自动命名（截断避免过长）</summary>
    private void RenameActiveSessionTo(string message) {
        if (_activeSession is null)
            return;
        var title = message.Trim().Length > 18
            ? message.Trim()[..18] + "…"
            : message.Trim();
        if (title.Length > 0)
            _activeSession.Title = title;
    }
}