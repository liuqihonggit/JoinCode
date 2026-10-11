namespace JoinCode.Gui.ViewModels;

/// <summary>按标题筛选原始会话对象，保留选中态和重命名实时更新。</summary>
public sealed partial class MainViewModel {
    private readonly HashSet<SessionItem> _observedSessions = [];
    /// <summary>会话查询，与消息正文搜索分开。</summary>
    [ObservableProperty]
    private string _sessionSearchText = "";
    /// <summary>筛选后的侧栏列表。</summary>
    public IReadOnlyList<SessionItem> VisibleSessions => Sessions.Where(MatchesSessionQuery).ToArray();
    /// <summary>空查询结果提示。</summary>
    public bool HasNoMatchingSessions => VisibleSessions.Count == 0;
    /// <summary>侧栏列表计数。</summary>
    public string SessionCountText => $"{VisibleSessions.Count} / {Sessions.Count} 个会话";
    partial void OnSessionSearchTextChanged(string value) => NotifySessionFilter();
    /// <summary>大小写无关，忽略首尾空白。</summary>
    private bool MatchesSessionQuery(SessionItem item) => item.Title.Contains(SessionSearchText.Trim(), StringComparison.OrdinalIgnoreCase);
    /// <summary>监控列表变更并更新标题监听；Reset 时也释放旧订阅。</summary>
    private void OnSessionListChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e) {
        var current = Sessions.ToHashSet();
        foreach (var removed in _observedSessions.Where(s => !current.Contains(s)).ToArray()) {
            removed.PropertyChanged -= OnSessionTitleChanged;
            _observedSessions.Remove(removed);
        }
        foreach (var added in Sessions.Where(s => !_observedSessions.Contains(s))) {
            added.PropertyChanged += OnSessionTitleChanged;
            _observedSessions.Add(added);
        }
        NotifySessionFilter();
    }
    /// <summary>标题编辑后立即刷新筛选。</summary>
    private void OnSessionTitleChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e) {
        if (e.PropertyName == nameof(SessionItem.Title)) NotifySessionFilter();
    }
    /// <summary>刷新会话列表和计数绑定。</summary>
    private void NotifySessionFilter() {
        OnPropertyChanged(nameof(VisibleSessions));
        OnPropertyChanged(nameof(HasNoMatchingSessions));
        OnPropertyChanged(nameof(SessionCountText));
    }
    /// <summary>释放列表和标题订阅。</summary>
    private void DetachSessionFilter() {
        Sessions.CollectionChanged -= OnSessionListChanged;
        foreach (var item in _observedSessions) item.PropertyChanged -= OnSessionTitleChanged;
        _observedSessions.Clear();
    }
}
