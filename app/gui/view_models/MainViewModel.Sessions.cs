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
    private void LoadPersistedSessions() {
        foreach (var summary in _sessionStore.ListSessions()) {
            Sessions.Add(new SessionItem {
                Id = summary.Id,
                Title = summary.Title
            });
        }
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
        OnPropertyChanged(nameof(Sessions));
    }

    /// <summary>将当前会话消息持久化到 ~/.jcc/sessions/{Id}.json（含自动命名标题）</summary>
    private void SaveActiveSession() {
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
            _sessionStore.Save(data);
        } catch (Exception ex) {
            System.Diagnostics.Debug.WriteLine($"[MainViewModel] 会话持久化失败: {ex.Message}");
        }
    }

    /// <summary>清空全部会话（会话列表与消息一并重置，持久化文件同步删除）</summary>
    [RelayCommand]
    private void ClearAllSessions() {
        foreach (var s in Sessions.ToList()) {
            try {
                _sessionStore.Delete(s.Id);
            } catch (Exception ex) {
                System.Diagnostics.Debug.WriteLine($"[MainViewModel] 会话删除失败: {ex.Message}");
            }
        }
        Sessions.Clear();
        Messages.Clear();
        _sessionCounter = 0;
        NewConversation();
    }

    /// <summary>从会话列表删除指定会话（同步删除持久化文件）</summary>
    [RelayCommand]
    private void RemoveSession(SessionItem? session) {
        if (session is null)
            return;
        Sessions.Remove(session);
        try {
            _sessionStore.Delete(session.Id);
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

        // 需求11：子会话点击展示内容（SubSessionMessages 缓存或引擎加载）
        if (session.IsSubSession) {
            await LoadSubSessionContentAsync(session).ConfigureAwait(false);
            return;
        }

        // 切换会话时从持久化恢复该会话消息到消息区（空会话则清空）
        var data = _sessionStore.Load(session.Id);
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
        }

        // 把持久化历史灌入底层引擎上下文 — GUI 新进程 StateService 内存为空，
        // SwitchSession 仅切换 sessionId 不加载历史，需显式灌入否则发送时 LLM 收不到历史
        await _session.LoadHistoryAsync(historyForEngine).ConfigureAwait(false);
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