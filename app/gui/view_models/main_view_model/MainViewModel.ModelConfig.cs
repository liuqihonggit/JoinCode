namespace JoinCode.Gui.ViewModels;

/// <summary>
/// MainViewModel 模型/连接配置 partial — 供应商连接下拉、模型下拉、settings.json 热重载监控。
/// </summary>
public sealed partial class MainViewModel {
    /// <summary>模型下拉选项 — 委托给 ConnectionDropdownManager，供应商切换时清空重填</summary>
    public ObservableCollection<ModelOptionItem> ModelOptions => _connectionDropdown.ModelOptions;

    /// <summary>按 Id O(1) 查找模型项（委托给 ConnectionDropdownManager；null 返回 null）</summary>
    private ModelOptionItem? GetModelById(string? id) => _connectionDropdown.GetModelById(id);

    /// <summary>刷新模型下拉 — 委托给 ConnectionDropdownManager</summary>
    private void RefreshModelOptions()
        => _connectionDropdown.RefreshModelOptions(_session, _modelConfigLoader, SelectedConnection?.Id);

    /// <summary>当前选中的模型下拉项（View 层绑定 ComboBox.SelectedItem）</summary>
    [ObservableProperty]
    private ModelOptionItem? _selectedModelOption;

    /// <summary>用户切换模型下拉项时回写共享配置（绑定同一个配置源，下次请求引擎生效）</summary>
    partial void OnSelectedModelOptionChanged(ModelOptionItem? value) {
        if (value is not null && value.Id != _session.CurrentModelId) {
            SelectedModel = value.Id;
        }
    }

    /// <summary>用户切换模型时持久化由 OnPropertyChanged 自动路由处理（SelectedModel 映射 SetModelAsync）</summary>

    /// <summary>连接下拉候选 — 委托给 ConnectionDropdownManager</summary>
    public IReadOnlyList<ConnectionOptionItem> ConnectionOptions => _connectionDropdown.ConnectionOptions;

    /// <summary>按 Id O(1) 查找连接项（委托给 ConnectionDropdownManager；null 返回 null）</summary>
    private ConnectionOptionItem? GetConnectionById(string? id) => _connectionDropdown.GetConnectionById(id);

    /// <summary>重建连接选项 — 委托给 ConnectionDropdownManager</summary>
    private void RebuildConnectionOptions() => _connectionDropdown.RebuildConnectionOptions(_session);

    /// <summary>当前选中的连接项（切换时替换活动会话，不销毁任何会话）</summary>
    [ObservableProperty]
    private ConnectionOptionItem? _selectedConnection;

    /// <summary>当前是否连接 Mock 引擎（驱动状态提示与 Mock 徽标显隐）</summary>
    public bool IsMockConnection => _session is PlaceholderChatSession;

    partial void OnSelectedConnectionChanged(ConnectionOptionItem? value)
        => _ = OnSelectedConnectionChangedAsync(value);

    private async Task OnSelectedConnectionChangedAsync(ConnectionOptionItem? value) {
        ViewModelDiagnosticsLogger.WriteDebug($"OnSelectedConnectionChanged: id={value?.Id} refresh={_gate.RefreshingConfig} realSession={_realSession is not null} session={_session.GetType().Name} currentVendor={_session.CurrentVendor}");
        if (value is null || _gate.RefreshingConfig)
            return;

        // 无论引擎是否就绪,都持久化供应商切换到 settings.json(PlaceholderChatSession 也能写)
        // 并刷新模型列表供预览
        StatusText = _realSession is not null
            ? $"已连接真实引擎 {value.DisplayText}"
            : $"已选择供应商 {value.DisplayText}（引擎加载中…）";
        try { await _session.SetVendorAsync(value.Id).WaitAsync(Timeout); ViewModelDiagnosticsLogger.WriteDebug($"SetVendorAsync ok: id={value.Id}"); } catch (Exception ex) { ViewModelDiagnosticsLogger.WriteError(ex); ViewModelDiagnosticsLogger.WriteDebug($"SetVendorAsync FAIL: {ex.Message}"); }

        RefreshModelOptions();
        OnPropertyChanged(nameof(IsMockConnection));
        // 供应商切换后 SetVendorAsync 已把 CurrentModelId 重置为新供应商默认模型，优先匹配它；找不到才取第一个
        SelectedModelOption = GetModelById(_session.CurrentModelId)
            ?? ModelOptions.FirstOrDefault();
        SelectedModel = SelectedModelOption?.Id;
        SelectedEffort = _session.EffortLevel.ToValue();
    }

    /// <summary>启动 settings.json 文件监控（热重载）— 文件变更时自动刷新供应商/模型列表</summary>
    private void StartModelConfigWatch() {
        var path = AppDataConstants.Paths.SettingsFilePath;
        var dir = System.IO.Path.GetDirectoryName(path);
        if (string.IsNullOrEmpty(dir) || !_fileSystem.DirectoryExists(dir))
            return;

        _ = _modelConfigWatcher?.DisposeAsync();
        _modelConfigWatcher = _fileSystem.Watch(dir, System.IO.Path.GetFileName(path));
        _modelConfigWatcher.NotifyFilter = System.IO.NotifyFilters.FileName | System.IO.NotifyFilters.LastWrite;
        _modelConfigWatcher.DebounceInterval = TimeSpan.FromSeconds(1);
        _modelConfigWatcher.EnableRaisingEvents = true;
        _modelConfigWatcher.DebouncedChanged += OnModelConfigChanged;
        _modelConfigWatcher.DebouncedCreated += OnModelConfigChanged;
    }

    /// <summary>settings.json 变更事件 — 在 UI 线程刷新配置（防抖由 IFileSystemWatcher.DebouncedChanged 接管）</summary>
    private void OnModelConfigChanged(object? sender, FileChangedEventArgs e) {
        Avalonia.Threading.Dispatcher.UIThread.Post(RefreshModelOptionsFromConfig);
    }

    /// <summary>从 settings.json 重新加载配置并刷新连接/模型列表</summary>
    private void RefreshModelOptionsFromConfig() {
        try {
            // 记住热重载前的选择，重载后尽量保留（避免下拉跳回第一项）
            var previousModelId = SelectedModelOption?.Id;
            var previousConnectionId = SelectedConnection?.Id;

            _session.RefreshVendorModelMap();
            RebuildConnectionOptions();
            RefreshModelOptions();

            // 恢复连接选择（RebuildConnectionOptions 重建了对象引用），用门控 scope 绕过 OnSelectedConnectionChanged 持久化副作用避免循环
            using var _ = _gate.EnterRefreshingConfigScope();
            SelectedConnection = GetConnectionById(previousConnectionId)
                ?? GetConnectionById(_session.CurrentVendor)
                ?? _connectionDropdown.ConnectionOptions.FirstOrDefault();
            OnPropertyChanged(nameof(IsMockConnection));

            // 保留当前模型选择（若仍属于当前供应商模型列表），否则取引擎当前模型，再否则取第一个
            SelectedModelOption = GetModelById(previousModelId)
                ?? GetModelById(_session.CurrentModelId)
                ?? ModelOptions.FirstOrDefault();
            SelectedModel = SelectedModelOption?.Id;
            StatusText = "配置已热重载";
        } catch (Exception ex) {
            ViewModelDiagnosticsLogger.WriteError(ex);
        }
    }
}