namespace JoinCode.Gui.Persistence;

/// <summary>
/// GUI 偏好持久化存储 — 读写 ~/.jcc/gui-preferences.json，
/// 通过 IFileSystem 抽象注入，生产用 PhysicalFileSystem，测试用 InMemoryFileSystem。
/// 文件不存在或损坏时返回默认 <see cref="GuiPreferences"/>，不阻塞 UI 启动。
/// </summary>
public sealed class GuiPreferencesStore
{
    private readonly IFileSystem _fs;
    private readonly string _filePath;

    public GuiPreferencesStore(IFileSystem fs, string? filePath = null)
    {
        _fs = fs;
        _filePath = filePath ?? _fs.CombinePath(WorkflowConstants.Paths.JccDirectory, "gui-preferences.json");
    }

    /// <summary>偏好文件完整路径</summary>
    public string FilePath => _filePath;

    /// <summary>底层文件系统抽象 — 生产 PhysicalFileSystem，测试 InMemoryFileSystem。
    /// 供 MainViewModel 派生 ConfigurationService，保证测试不读写真实 ~/.jcc/settings.json。</summary>
    public IFileSystem FileSystem => _fs;

    /// <summary>加载偏好；文件不存在或损坏返回默认值，不抛异常（不阻塞 UI 启动）</summary>
    public GuiPreferences Load()
    {
        try
        {
            if (!_fs.FileExists(_filePath))
                return new GuiPreferences();

            var json = _fs.ReadAllText(_filePath);
            return JsonSerializer.Deserialize(json, GuiJsonContext.Default.GuiPreferences)
                ?? new GuiPreferences();
        }
        catch (Exception)
        {
            return new GuiPreferences();
        }
    }

    /// <summary>保存偏好到磁盘（目录不存在则创建）</summary>
    public void Save(GuiPreferences preferences)
    {
        ArgumentNullException.ThrowIfNull(preferences);

        var dir = _fs.GetParentPath(_filePath);
        if (!string.IsNullOrEmpty(dir) && !_fs.DirectoryExists(dir))
            _fs.CreateDirectory(dir);

        var json = JsonSerializer.Serialize(preferences, GuiJsonContext.Default.GuiPreferences);
        _fs.WriteAllText(_filePath, json);
    }
}
