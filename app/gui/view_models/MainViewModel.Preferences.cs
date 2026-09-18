namespace JoinCode.Gui.ViewModels;

/// <summary>
/// MainViewModel partial — GUI 偏好持久化（加载/保存/属性变更路由/采样参数应用）。
/// 从 MainViewModel.cs 拆出以控制文件行数（关注点分离）。
/// </summary>
public sealed partial class MainViewModel
{
    private bool _isPreferencesLoaded;

    /// <summary>属性名→持久化操作映射 — OnPropertyChanged 自动路由统一持久化（新增可持久化属性只需 RegisterPersist 一行）</summary>
    private readonly Dictionary<string, Action> _persistActions = new(StringComparer.Ordinal);

    partial void OnMaxTokensChanged(int value)
    {
        OnPropertyChanged(nameof(IsInputTooLong));
        Task.Run(async () =>
        {
            try
            {
                await _session.SetMaxTokensAsync(value).WaitAsync(Timeout);
                StatusText = $"采样参数: 温度 {Temperature:0.00}, 最大 {value} tokens";
            }
            catch (Exception ex)
            {
                StatusText = $"设置采样参数失败: {ex.Message}";
            }
        });
    }

    partial void OnTemperatureChanged(double value)
    {
        Task.Run(async () =>
        {
            try
            {
                await _session.SetTemperatureAsync((float)value).WaitAsync(Timeout);
                StatusText = $"采样参数: 温度 {value:0.00}, 最大 {MaxTokens} tokens";
            }
            catch (Exception ex)
            {
                StatusText = $"设置采样参数失败: {ex.Message}";
            }
        });
    }

    /// <summary>系统提示词变更时应用到引擎（持久化由 OnPropertyChanged 自动路由处理）</summary>
    partial void OnSystemPromptChanged(string value)
    {
        if (_isPreferencesLoaded && !string.IsNullOrWhiteSpace(value))
        {
            // 需求10：向消息区推送系统提示词注入卡片（去重：与最后一条注入内容相同则跳过）
            if (Messages.Count == 0
                || Messages[^1].Kind != ChatUiMessageKind.SystemPromptInjection
                || Messages[^1].Content != value)
            {
                Messages.Add(new ChatUiMessage
                {
                    Role = MessageRole.System,
                    Content = value,
                    Timestamp = DateTime.Now,
                    Kind = ChatUiMessageKind.SystemPromptInjection
                });
            }
            _ = Task.Run(async () =>
            {
                try { await _session.SetSystemPromptAsync(value).WaitAsync(Timeout); }
                catch (Exception ex) { ViewModelDiagnosticsLogger.WriteError(ex); }
            });
        }
    }

    /// <summary>
    /// 加载 GUI 偏好并应用到 UI 属性 — 启动时恢复上次显示的内容。
    /// 加载期间置 _isPreferencesLoaded=false 防止 OnXxxChanged 回写磁盘。
    /// </summary>
    private void LoadPreferences()
    {
        try
        {
            var prefs = _preferencesStore.Load();
            _isPreferencesLoaded = false;
            Temperature = prefs.Temperature;
            MaxTokens = prefs.MaxTokens;
            SystemPrompt = prefs.SystemPrompt;
            FontSize = prefs.FontSize;
            StreamingEnabled = prefs.StreamingEnabled;
            EnterSends = prefs.EnterSends;
            DoubleEscStop = prefs.DoubleEscStop;
            // 需求3：加载快捷键面板项
            HotkeyItems.Clear();
            HotkeyItems.Add(new HotkeyItemVm("发送消息", "Send", prefs.HotkeySend));
            HotkeyItems.Add(new HotkeyItemVm("换行", "Newline", prefs.HotkeyNewline));
            HotkeyItems.Add(new HotkeyItemVm("终止对话", "Stop", prefs.HotkeyStop));
            HotkeyItems.Add(new HotkeyItemVm("新建会话", "NewSession", prefs.HotkeyNewSession));
            HotkeyItems.Add(new HotkeyItemVm("清空对话", "ClearHistory", prefs.HotkeyClearHistory));
            HotkeyItems.Add(new HotkeyItemVm("打开设置", "ToggleSettings", prefs.HotkeyToggleSettings));
            NetworkMode = prefs.NetworkMode;
            ProxyUrl = prefs.ProxyUrl;
            WindowShakeEnabled = prefs.WindowShakeEnabled;
            ChatRoomEnabled = prefs.ChatRoomEnabled;
            _isPreferencesLoaded = true;
        }
        catch (Exception ex)
        {
            _isPreferencesLoaded = true;
            ViewModelDiagnosticsLogger.WriteError(ex);
        }
    }

    /// <summary>把偏好里的采样参数应用到引擎（构造函数引擎就绪后 / AttachRealSession 热切换后调用）</summary>
    private void ApplyPreferencesToEngine()
    {
        _ = Task.Run(async () =>
        {
            try
            {
                await _session.SetTemperatureAsync((float)Temperature).WaitAsync(Timeout);
                await _session.SetMaxTokensAsync(MaxTokens).WaitAsync(Timeout);
                if (!string.IsNullOrWhiteSpace(SystemPrompt))
                    await _session.SetSystemPromptAsync(SystemPrompt).WaitAsync(Timeout);
            }
            catch (Exception ex)
            {
                ViewModelDiagnosticsLogger.WriteError(ex);
            }
        });
    }

    /// <summary>
    /// 同步阻塞 UI 线程至异步持久化操作完成 — Task.Run 避免 UI 线程 SynchronizationContext 死锁，
    /// Wait(Timeout) 确保点击时立即落盘，关闭 GUI 不丢失。失败写错误日志不抛出。
    /// </summary>
    private void PersistSync(Func<Task> action)
    {
        try
        {
            Task.Run(action).Wait(Timeout);
            ViewModelDiagnosticsLogger.WriteDebug($"PersistSync ok");
        }
        catch (Exception ex) { ViewModelDiagnosticsLogger.WriteError(ex); ViewModelDiagnosticsLogger.WriteDebug($"PersistSync FAIL: {ex.Message}"); }
    }

    /// <summary>
    /// 注册属性名→持久化操作映射 — 构造函数调用一次。简单属性全走路由，
    /// 复杂属性（SelectedConnection/SelectedEffort）保留 OnXxxChanged 手动调 PersistSync。
    /// </summary>
    private void RegisterPersistActions()
    {
        _persistActions[nameof(IsDarkTheme)] = () =>
            PersistSync(() => _session.SetThemeAsync(ThemeConverter.FromIsDark(IsDarkTheme)));
        _persistActions[nameof(SelectedModel)] = () =>
        {
            var m = SelectedModel;
            if (!string.IsNullOrWhiteSpace(m) && !string.Equals(m, _session.CurrentModelId, StringComparison.Ordinal))
                PersistSync(() => _session.SetModelAsync(m!));
        };
        _persistActions[nameof(Temperature)] = SavePreferences;
        _persistActions[nameof(MaxTokens)] = SavePreferences;
        _persistActions[nameof(SystemPrompt)] = SavePreferences;
        _persistActions[nameof(FontSize)] = SavePreferences;
        _persistActions[nameof(StreamingEnabled)] = SavePreferences;
        _persistActions[nameof(EnterSends)] = SavePreferences;
        _persistActions[nameof(DoubleEscStop)] = SavePreferences;
        _persistActions[nameof(IsUnattendedMode)] = SavePreferences;
        _persistActions[nameof(IsAntiCharLossConfirm)] = SavePreferences;
        _persistActions[nameof(WindowShakeEnabled)] = SavePreferences;
        _persistActions[nameof(ChatRoomEnabled)] = SavePreferences;
    }

    /// <summary>
    /// PropertyChanged 自动路由 — 拦截所有属性变更，查 _persistActions 字典统一持久化。
    /// 标志位过滤：_isPreferencesLoaded（加载期不回写）、_isRefreshingConfig（热重载期不回写）、
    /// _isApplyingExternalTheme（外部主题变更不回写避免循环）。
    /// </summary>
    protected override void OnPropertyChanged(System.ComponentModel.PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        var propertyName = e.PropertyName;
        if (propertyName is null)
            return;
        if (_persistActions.ContainsKey(propertyName))
            ViewModelDiagnosticsLogger.WriteDebug($"OnPropertyChanged: {propertyName} | loaded={_isPreferencesLoaded} refresh={_isRefreshingConfig} extTheme={_isApplyingExternalTheme} | session={_session.GetType().Name}");
        if (!_isPreferencesLoaded || _isRefreshingConfig || _isApplyingExternalTheme)
            return;
        if (_persistActions.TryGetValue(propertyName, out var action))
            action();
    }

    /// <summary>保存当前 UI 偏好到磁盘 — 各 OnXxxChanged 调用，_isPreferencesLoaded 防止初始化时回写</summary>
    private void SavePreferences()
    {
        if (!_isPreferencesLoaded)
            return;
        try
        {
            _preferencesStore.Save(new Persistence.GuiPreferences
            {
                Temperature = Temperature,
                MaxTokens = MaxTokens,
                SystemPrompt = SystemPrompt,
                FontSize = FontSize,
                StreamingEnabled = StreamingEnabled,
                EnterSends = EnterSends,
                DoubleEscStop = DoubleEscStop,
                HotkeySend = GetHotkeyGesture("Send"),
                HotkeyNewline = GetHotkeyGesture("Newline"),
                HotkeyStop = GetHotkeyGesture("Stop"),
                HotkeyNewSession = GetHotkeyGesture("NewSession"),
                HotkeyClearHistory = GetHotkeyGesture("ClearHistory"),
                HotkeyToggleSettings = GetHotkeyGesture("ToggleSettings"),
                NetworkMode = NetworkMode,
                ProxyUrl = ProxyUrl,
                WindowShakeEnabled = WindowShakeEnabled,
                ChatRoomEnabled = ChatRoomEnabled
            });
        }
        catch (Exception ex)
        {
            ViewModelDiagnosticsLogger.WriteError(ex);
        }
    }
}
