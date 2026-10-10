namespace JoinCode.Gui.ViewModels;

/// <summary>文件、Git、模型及引擎管理工作台。</summary>
public sealed partial class WorkbenchViewModel : ObservableObject {
    private readonly Func<IJccChatSession> _session;
    private IReadOnlyList<WorkspaceFile> _files = [];
    private IReadOnlyList<ToolSummary> _tools = [];
    private int _schemaVersion;
    private CancellationTokenSource? _operationCancellation;
    private CancellationToken OperationToken => _operationCancellation?.Token ?? CancellationToken.None;

    /// <summary>创建当前会话驱动的工作台。</summary>
    public WorkbenchViewModel(MainViewModel main, Func<IJccChatSession> session) { Main = main; _session = session; }
    /// <summary>共享模型与外观配置。</summary>
    public MainViewModel Main { get; }
    /// <summary>文件候选。</summary>
    public ObservableCollection<WorkspaceFile> Files { get; } = [];
    /// <summary>当前搜索命中的工具。</summary>
    public ObservableCollection<ToolSummary> Tools { get; } = [];
    /// <summary>从可用目录派生的工具分组。</summary>
    public ObservableCollection<string> Groups { get; } = [];
    /// <summary>当前工具参数表单。</summary>
    public ObservableCollection<WorkbenchParameter> Parameters { get; } = [];
    /// <summary>引擎生成的插件命令用法。</summary>
    [ObservableProperty] private string _pluginUsage = "请刷新引擎目录。";
    /// <summary>插件动作输入。</summary>
    [ObservableProperty] private string _pluginArguments = "list";
    /// <summary>当前目录。</summary>
    [ObservableProperty] private string _directory = System.Environment.CurrentDirectory;
    /// <summary>文件筛选词。</summary>
    [ObservableProperty] private string _fileSearch = string.Empty;
    /// <summary>工具筛选词。</summary>
    [ObservableProperty] private string _toolSearch = string.Empty;
    /// <summary>分组选择。</summary>
    [ObservableProperty] private string? _selectedGroup;
    /// <summary>工具选择。</summary>
    [ObservableProperty] private ToolSummary? _selectedTool;
    /// <summary>文件选择。</summary>
    [ObservableProperty] private WorkspaceFile? _selectedFile;
    /// <summary>代码预览正文。</summary>
    [ObservableProperty] private string _fileContent = "选择文件查看代码，双击文件夹进入。";
    /// <summary>Git 结果。</summary>
    [ObservableProperty] private string _gitContent = "刷新状态查看变更，或查看工作区差异。";
    /// <summary>管理操作结果。</summary>
    [ObservableProperty] private string _operationResult = "选择工具查看说明和参数。";
    /// <summary>详细工具说明。</summary>
    [ObservableProperty] private string _toolDescription = string.Empty;
    /// <summary>所选工具参数是否已加载。</summary>
    [ObservableProperty] private bool _isSchemaReady;
    /// <summary>插件名称或安装文件路径。</summary>
    [ObservableProperty] private string _pluginTarget = string.Empty;
    /// <summary>忙碌状态。</summary>
    [ObservableProperty] private bool _isWorking;
    /// <summary>操作状态。</summary>
    [ObservableProperty] private string _status = "就绪";
    /// <summary>默认打开的标签。</summary>
    [ObservableProperty] private int _tabIndex;

    partial void OnFileSearchChanged(string value) => FilterFiles();
    partial void OnToolSearchChanged(string value) => FilterTools();
    partial void OnSelectedGroupChanged(string? value) => FilterTools();
    partial void OnSelectedToolChanged(ToolSummary? value) => _ = LoadSchemaAsync(value);
    partial void OnSelectedFileChanged(WorkspaceFile? value) {
        if (value is not null && !value.IsDirectory) _ = ReadFileAsync(value);
    }

    private void FilterFiles() {
        Files.Clear();
        foreach (var item in _files.Where(f => f.Name.Contains(FileSearch.Trim(), StringComparison.OrdinalIgnoreCase))) Files.Add(item);
    }

    private void FilterTools() {
        Tools.Clear();
        foreach (var item in _tools.Where(t => SelectedGroup is null || WorkbenchCatalog.Group(t.Name) == SelectedGroup)
            .Where(t => t.Name.Contains(ToolSearch.Trim(), StringComparison.OrdinalIgnoreCase) || t.Description.Contains(ToolSearch.Trim(), StringComparison.OrdinalIgnoreCase))) Tools.Add(item);
    }

    /// <summary>刷新所选目录。</summary>
    [RelayCommand] private Task RefreshFilesAsync() => RunAsync(async () => {
        var path = System.IO.Path.GetFullPath(Directory);
        var entries = await Task.Run(() => WorkspaceReader.List(path));
        Directory = path; _files = entries; SelectedFile = null; FileContent = "选择文件查看代码，双击文件夹进入。"; FilterFiles();
        Status = $"{Files.Count} 个条目";
    });

    /// <summary>返回父目录。</summary>
    [RelayCommand] private async Task ParentDirectoryAsync() {
        try {
            var parent = System.IO.Directory.GetParent(System.IO.Path.GetFullPath(Directory));
            if (parent is null) return;
            Directory = parent.FullName;
            await RefreshFilesAsync();
        } catch (Exception ex) { Status = $"无法返回父目录：{ex.Message}"; }
    }

    /// <summary>打开当前目录条目。</summary>
    [RelayCommand] private async Task OpenEntryAsync() {
        if (SelectedFile is not { IsDirectory: true } entry) return;
        Directory = entry.Path;
        await RefreshFilesAsync();
    }

    private async Task ReadFileAsync(WorkspaceFile file) {
        try {
            var text = await WorkspaceReader.ReadAsync(file.Path);
            if (SelectedFile == file) FileContent = text;
        } catch (Exception ex) { Status = ex.Message; }
    }

    /// <summary>查看 Git 状态。</summary>
    [RelayCommand] private Task GitStatusAsync() => RunAsync(async () => GitContent = await WorkspaceReader.GitAsync(Directory, ["status", "--short"], OperationToken));
    /// <summary>查看工作区所有未暂存变更。</summary>
    [RelayCommand] private Task GitDiffAsync() => RunAsync(async () => GitContent = await WorkspaceReader.GitAsync(Directory, ["diff", "--no-ext-diff", "--no-textconv", "--no-color"], OperationToken));
    /// <summary>查看已暂存变更。</summary>
    [RelayCommand] private Task GitStagedAsync() => RunAsync(async () => GitContent = await WorkspaceReader.GitAsync(Directory, ["diff", "--cached", "--no-ext-diff", "--no-textconv", "--no-color"], OperationToken));
    /// <summary>查看选中文件相对 HEAD 的变更。</summary>
    [RelayCommand] private Task FileDiffAsync() => RunAsync(async () => {
        if (SelectedFile is not { IsDirectory: false } file) throw new InvalidOperationException("请先选择文件。");
        GitContent = await WorkspaceReader.GitAsync(Directory, ["diff", "HEAD", "--no-ext-diff", "--no-textconv", "--no-color", "--", file.Path], OperationToken);
        TabIndex = 1;
    });

    /// <summary>从活动引擎重新加载工具和插件入口。</summary>
    [RelayCommand] private Task RefreshCatalogAsync() => RunAsync(async () => {
        _tools = await _session().GetAvailableToolsAsync(OperationToken);
        Groups.Clear();
        foreach (var group in _tools.Select(t => WorkbenchCatalog.Group(t.Name)).Distinct().Order()) Groups.Add(group);
        SelectedGroup = Groups.FirstOrDefault(g => g == "MCP") ?? Groups.FirstOrDefault();
        FilterTools(); SelectedTool = null;
        PluginUsage = _session().GetAvailableSlashCommands().FirstOrDefault(c => c.Name.TrimStart('/') == "plugin")?.Usage
            ?? "当前引擎没有插件管理命令；请关闭 Mock 并连接真实引擎。";
        Status = _tools.Count > 0 ? $"已载入 {_tools.Count} 个工具" : "当前引擎无工具，请关闭 Mock 或等待引擎加载后刷新。";
    });

    private async Task LoadSchemaAsync(ToolSummary? tool) {
        var version = ++_schemaVersion;
        IsSchemaReady = false;
        Parameters.Clear(); ToolDescription = tool?.Description ?? string.Empty;
        if (tool is null) return;
        try {
            var info = await _session().GetToolInfoAsync(tool.Name);
            if (version != _schemaVersion) return;
            if (info is null) { ToolDescription += "\n无法读取参数说明，请刷新目录后重试。"; return; }
            var required = info.InputSchema.Required.ToHashSet(StringComparer.Ordinal);
            foreach (var pair in info.InputSchema.Properties) Parameters.Add(new WorkbenchParameter {
                Name = pair.Key, Schema = pair.Value,
                Hint = $"{pair.Value.Type}{(required.Contains(pair.Key) ? " · 必填" : " · 可选")} — {pair.Value.Description}" +
                    (pair.Value.Enum is { Count: > 0 } options ? $" [{string.Join(", ", options)}]" : string.Empty),
                Value = pair.Value.Default ?? string.Empty
            });
            IsSchemaReady = true;
        } catch (Exception ex) { Status = ex.Message; }
    }

    /// <summary>执行 schema 驱动的工具参数表单。</summary>
    [RelayCommand] private Task RunToolAsync() => RunAsync(async () => {
        var tool = SelectedTool ?? throw new InvalidOperationException("请先选择工具。");
        if (!IsSchemaReady) throw new InvalidOperationException("参数仍在加载，请稍后执行。");
        var info = await _session().GetToolInfoAsync(tool.Name, OperationToken) ?? throw new InvalidOperationException("工具参数不可用，请刷新目录。");
        var required = info.InputSchema.Required.ToHashSet(StringComparer.Ordinal);
        var args = new Dictionary<string, JsonElement>();
        var allowedValues = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        foreach (var f in Parameters) {
            if (f.Schema.Enum is { Count: > 0 } enumValues)
                allowedValues[f.Name] = enumValues.ToHashSet(StringComparer.Ordinal);
        }
        foreach (var field in Parameters) {
            if (string.IsNullOrWhiteSpace(field.Value)) {
                if (required.Contains(field.Name)) throw new InvalidOperationException($"参数 {field.Name} 必填，请填写后执行。");
                continue;
            }
            var literal = field.Schema.Type == "string" ? JsonSerializer.Serialize(field.Value, Persistence.GuiJsonContext.Default.String) : field.Value;
            using var doc = JsonDocument.Parse(literal);
            var valid = field.Schema.Type switch {
                "boolean" => doc.RootElement.ValueKind is JsonValueKind.True or JsonValueKind.False,
                "integer" => doc.RootElement.ValueKind == JsonValueKind.Number && doc.RootElement.TryGetInt64(out _),
                "number" => doc.RootElement.ValueKind == JsonValueKind.Number,
                "array" => doc.RootElement.ValueKind == JsonValueKind.Array,
                "object" => doc.RootElement.ValueKind == JsonValueKind.Object,
                _ => doc.RootElement.ValueKind == JsonValueKind.String
            };
            if (!valid) throw new InvalidOperationException($"参数 {field.Name} 必须是 {field.Schema.Type}，请按参数提示填写。");
            if (allowedValues.TryGetValue(field.Name, out var options) && !options.Contains(field.Value))
                throw new InvalidOperationException($"参数 {field.Name} 请从 {string.Join(", ", options)} 中选择。");
            args.Add(field.Name, doc.RootElement.Clone());
        }
        var result = await _session().ExecuteToolAsync(tool.Name, args, OperationToken);
        OperationResult = MainViewModel.ExtractToolResultText(result);
        Status = result.IsError ? "操作失败，请查看结果中的原因和用法。" : "操作完成";
    });

    /// <summary>通过现有命令执行插件管理。</summary>
    [RelayCommand] private Task RunPluginAsync() => RunAsync(async () => {
        if (!_session().GetAvailableSlashCommands().Any(c => c.IsEnabled && c.Name.TrimStart('/') == "plugin"))
            throw new InvalidOperationException("当前引擎不支持插件管理，请连接真实引擎并刷新。");
        OperationResult = await _session().ExecuteSlashCommandAsync($"/plugin {PluginArguments.Trim()}", OperationToken);
    });

    /// <summary>按钮驱动插件管理。</summary>
    [RelayCommand] private async Task PluginActionAsync(string? action) {
        if (action is null) return;
        if (action != "list" && string.IsNullOrWhiteSpace(PluginTarget)) { Status = "请填写插件名称或安装文件路径。"; return; }
        PluginArguments = action == "list" ? "list" : $"{action} {PluginTarget.Trim()}";
        await RunPluginAsync();
    }

    /// <summary>依次加载本地目录和当前引擎目录。</summary>
    public async Task InitializeAsync() { await RefreshFilesAsync(); await RefreshCatalogAsync(); }

    /// <summary>取消当前读取或引擎管理操作。</summary>
    [RelayCommand] private void CancelOperation() => _operationCancellation?.Cancel();

    private async Task RunAsync(Func<Task> operation) {
        if (IsWorking) return;
        using var cancellation = new CancellationTokenSource();
        _operationCancellation = cancellation;
        IsWorking = true; Status = "正在执行…";
        try { await operation(); if (Status == "正在执行…") Status = "完成"; }
        catch (OperationCanceledException) { Status = "已取消操作"; }
        catch (Exception ex) { Status = $"操作失败：{ex.Message}"; }
        finally { _operationCancellation = null; IsWorking = false; }
    }
}
