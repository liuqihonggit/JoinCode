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

    /// <summary>用户切换模型下拉项时回写共享配置 + 联动更新 MaxTokens（任务3双向联动）</summary>
    partial void OnSelectedModelOptionChanged(ModelOptionItem? value) {
        if (value is not null && value.Id != _session.CurrentModelId) {
            SelectedModel = value.Id;
        }
        // 联动：切换模型时按 ContextWindow 自动更新 MaxTokens（模型上限）
        if (value is not null && value.ContextWindow > 0) {
            var suggested = EstimateMaxOutputTokens(value.ContextWindow);
            if (MaxTokens != suggested) {
                MaxTokens = suggested;
            }
        }
        OnPropertyChanged(nameof(MaxInputChars));
        OnPropertyChanged(nameof(CurrentModelDisplay));
    }

    /// <summary>
    /// 估算合理输出 token 上限 — ContextWindow 的 1/4（留 75% 给输入+系统提示），
    /// 下限 1024（保证可用），上限 32768（避免过大）。0 或负值返回 4096 默认值。
    /// </summary>
    internal static int EstimateMaxOutputTokens(int contextWindow)
        => contextWindow > 0
            ? Math.Clamp(contextWindow / 4, 1024, 32768)
            : 4096;

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
        OnPropertyChanged(nameof(MockToggleToolTip));
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
            RefreshProviderModelGroups();

            // 恢复连接选择（RebuildConnectionOptions 重建了对象引用），用门控 scope 绕过 OnSelectedConnectionChanged 持久化副作用避免循环
            using var _ = _gate.EnterRefreshingConfigScope();
            SelectedConnection = GetConnectionById(previousConnectionId)
                ?? GetConnectionById(_session.CurrentVendor)
                ?? _connectionDropdown.ConnectionOptions.FirstOrDefault();
            OnPropertyChanged(nameof(IsMockConnection));
        OnPropertyChanged(nameof(MockToggleToolTip));

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

    // ===== 模型选择器 Popup（发送区域集成 — DSG031）=====

    /// <summary>模型选择器 Popup 是否展开</summary>
    [ObservableProperty]
    private bool _isModelPickerOpen;

    /// <summary>所有供应商分组的模型列表 — 模型选择器 Popup 数据源,按供应商分组展示</summary>
    [ObservableProperty]
    private IReadOnlyList<ProviderModelGroupVm> _providerModelGroups = [];

    /// <summary>当前选中模型的显示文本(用于模型选择器按钮,如 "glm-5.2")</summary>
    public string CurrentModelDisplay => SelectedModelOption?.Id ?? _session.CurrentModelId ?? "选择模型";

    /// <summary>切换模型选择器 Popup 显隐</summary>
    [RelayCommand]
    private void ToggleModelPicker() {
        if (!IsModelPickerOpen)
            RefreshProviderModelGroups();
        IsModelPickerOpen = !IsModelPickerOpen;
    }

    /// <summary>从 Popup 选择模型 — 切换供应商+模型并关闭 Popup</summary>
    /// <param name="parameter">格式 "providerId|modelId" 的选择键</param>
    [RelayCommand]
    private async Task SelectModelFromPopupAsync(string? parameter) {
        IsModelPickerOpen = false;
        if (string.IsNullOrEmpty(parameter))
            return;
        var pipe = parameter.IndexOf('|');
        if (pipe <= 0 || pipe >= parameter.Length - 1)
            return;
        var providerId = parameter[..pipe];
        var modelId = parameter[(pipe + 1)..];

        // 切换供应商(若不同) → 切换模型
        if (!string.Equals(providerId, _session.CurrentVendor, StringComparison.OrdinalIgnoreCase)) {
            var conn = GetConnectionById(providerId);
            if (conn is not null) {
                using var _ = _gate.EnterRefreshingConfigScope();
                SelectedConnection = conn;
                try { await _session.SetVendorAsync(providerId).WaitAsync(Timeout); } catch (Exception ex) { ViewModelDiagnosticsLogger.WriteError(ex); }
                RefreshModelOptions();
            }
        }
        var modelItem = GetModelById(modelId) ?? ModelOptions.FirstOrDefault(m => string.Equals(m.Id, modelId, StringComparison.OrdinalIgnoreCase));
        if (modelItem is not null) {
            SelectedModelOption = modelItem;
            SelectedModel = modelItem.Id;
            try { await _session.SetModelAsync(modelId).WaitAsync(Timeout); } catch (Exception ex) { ViewModelDiagnosticsLogger.WriteError(ex); }
        }
        OnPropertyChanged(nameof(CurrentModelDisplay));
    }

    /// <summary>刷新供应商模型分组 — 从 VendorModelMap 和 ModelConfigLoader 构建 Popup 数据源</summary>
    private void RefreshProviderModelGroups() {
        var map = _session.VendorModelMap;
        // Fallback: VendorModelMap 为空时（ModelConfigLoader 未被 ApplyProviders 注入），直接从 settings.json 读
        if (map.Count == 0)
            map = LoadVendorMapFromSettingsFile();
        var groups = new List<ProviderModelGroupVm>(map.Count);
        foreach (var (providerId, models) in map) {
            if (models is null || models.Count == 0)
                continue;
            var (name, initial, color) = GetProviderBranding(providerId);
            var entries = new List<ProviderModelEntryVm>(models.Count);
            foreach (var modelId in models) {
                var model = _modelConfigLoader.FindModel(providerId, modelId);
                entries.Add(new ProviderModelEntryVm {
                    ModelId = modelId,
                    DisplayName = model?.DisplayName ?? modelId,
                    ContextWindow = model?.ContextWindow ?? 0,
                    SelectionKey = $"{providerId}|{modelId}"
                });
            }
            groups.Add(new ProviderModelGroupVm {
                ProviderId = providerId,
                ProviderName = name,
                Initial = initial,
                BrandColor = color,
                Models = entries
            });
        }
        ProviderModelGroups = groups;
    }

    /// <summary>从 settings.json 直接读 vendor→models 构建 VendorModelMap（Fallback：ModelConfigLoader 未被注入时用）</summary>
    private IReadOnlyDictionary<string, IReadOnlyList<string>> LoadVendorMapFromSettingsFile() {
        var result = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);
        try {
            var settingsPath = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".jcc", "settings.json");
            if (!System.IO.File.Exists(settingsPath)) return result;
            using var doc = System.Text.Json.JsonDocument.Parse(System.IO.File.ReadAllText(settingsPath));
            if (!doc.RootElement.TryGetProperty("vendor", out var vendorEl)) return result;
            foreach (var vendor in vendorEl.EnumerateObject()) {
                if (!vendor.Value.TryGetProperty("models", out var modelsEl)) continue;
                var ids = new List<string>();
                foreach (var m in modelsEl.EnumerateArray()) {
                    if (m.TryGetProperty("id", out var idEl))
                        ids.Add(idEl.GetString() ?? "");
                }
                if (ids.Count > 0)
                    result[vendor.Name] = ids.ToArray();
            }
        } catch (Exception ex) {
            ViewModelDiagnosticsLogger.WriteError(ex);
        }
        return result;
    }

    /// <summary>供应商品牌信息(显示名+首字母+品牌色) — 唯一数据源,对齐 DSG031</summary>
    private static (string Name, string Initial, string Color) GetProviderBranding(string providerId)
        => providerId.ToLowerInvariant() switch {
        "openai" => ("OpenAI", "O", "#10A37F"),
        "deepseek" => ("DeepSeek", "D", "#4D6BFE"),
        "anthropic" => ("Anthropic", "A", "#D97757"),
        "zhipu" => ("Zhipu", "Z", "#4D6BFE"),
        "sensenova" => ("SenseNova", "S", "#E1251B"),
        "agnes" => ("Agnes", "A", "#9B59B6"),
        var other => (other, other[..1].ToUpperInvariant(), "#6B7280")
    };
}