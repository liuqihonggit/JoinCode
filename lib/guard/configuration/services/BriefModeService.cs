
namespace Core.Configuration;

/// <summary>
/// 简要模式服务实现 — 支持文件持久化（CLI 跨进程状态保持）
/// <para>文件路径: {workspaceRoot}/.jcc/mode/brief.json</para>
/// <para>文件格式: {"isEnabled":true,"enabledAt":"2026-09-09T03:00:00"}</para>
/// </summary>
[Register(typeof(IBriefModeService), ServiceLifetime.Singleton)]
public partial class BriefModeService : ServiceEntity, IBriefModeService
{

    /// <summary>
    /// 构造简要模式服务 — 初始化时钟、文件系统与日志,并从文件加载已持久化的状态
    /// </summary>
    /// <param name="clock">时钟服务,用于获取启用时间</param>
    /// <param name="fs">文件系统抽象(可选),为 null 时不进行文件持久化</param>
    /// <param name="logger">日志记录器(可选)</param>
    public BriefModeService(IClockService clock, IFileSystem? fs = null, ILogger<BriefModeService>? logger = null)
    {
        _clock = clock;
        _fs = fs;
        _logger = logger;
        LoadFromFile();
    }
    private bool _isEnabled;
    private DateTime? _enabledAt;
    private bool _userMsgOptIn;
    private readonly IClockService _clock;
    private readonly IFileSystem? _fs;
    private readonly ILogger<BriefModeService>? _logger;
    private static readonly string ModeSubDir = Path.Combine(AppDataConstants.AppDataFolder, "mode");
    private const string ModeFileName = "brief.json";

    /// <summary>是否已启用简要模式</summary>
    public bool IsEnabled => _isEnabled;

    /// <summary>启用时间(本地时区);未启用时为 null</summary>
    public DateTime? EnabledAt => _enabledAt;

    /// <summary>
    /// 用户显式 opt-in — 对齐 TS userMsgOptIn
    /// </summary>
    public bool UserMsgOptIn
    {
        get => _userMsgOptIn;
        set => _userMsgOptIn = value;
    }

    /// <inheritdoc/>
    public void Enable()
    {
        _isEnabled = true;
        _enabledAt = _clock.GetLocalNow();
        _userMsgOptIn = true; // 对齐 TS: setUserMsgOptIn(true)
        SaveToFile();
    }

    /// <inheritdoc/>
    public void Disable()
    {
        _isEnabled = false;
        _enabledAt = null;
        _userMsgOptIn = false; // 对齐 TS: setUserMsgOptIn(false)
        SaveToFile();
    }

    /// <inheritdoc/>
    public bool Toggle()
    {
        if (_isEnabled)
        {
            Disable();
        }
        else
        {
            Enable();
        }
        return _isEnabled;
    }

    /// <inheritdoc/>
    public BriefModeStatus GetStatus()
    {
        return _isEnabled
            ? BriefModeStatus.Enabled(_enabledAt ?? _clock.GetLocalNow())
            : BriefModeStatus.Disabled();
    }

    /// <summary>
    /// 从文件加载 brief mode 状态 — 跨进程持久化
    /// </summary>
    private void LoadFromFile()
    {
        if (_fs is null) return;
        try
        {
            var root = GitWorkspaceResolver.FindGitWorkspaceDir(null, _fs);
            if (root is null) return;
            var path = Path.Combine(Path.Combine(root, ModeSubDir), ModeFileName);
            if (!_fs.FileExists(path)) return;
            var json = _fs.ReadAllText(path);
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            _isEnabled = doc.RootElement.TryGetProperty("isEnabled", out var enabledProp) && enabledProp.GetBoolean();
            if (doc.RootElement.TryGetProperty("enabledAt", out var atProp) && atProp.ValueKind == System.Text.Json.JsonValueKind.String)
                _enabledAt = atProp.GetDateTime();
        }
        catch (Exception ex)
        {
            _logger?.LogWarning("Brief mode 状态加载失败: {Message}", ex.Message);
        }
    }

    /// <summary>
    /// 保存 brief mode 状态到文件 — 跨进程持久化
    /// </summary>
    private void SaveToFile()
    {
        if (_fs is null) return;
        try
        {
            var root = GitWorkspaceResolver.FindGitWorkspaceDir(null, _fs);
            if (root is null) return;
            var dir = Path.Combine(root, ModeSubDir);
            if (!_fs.DirectoryExists(dir)) _fs.CreateDirectory(dir);
            var path = Path.Combine(dir, ModeFileName);
            var enabledAtStr = _enabledAt.HasValue ? $"\"{_enabledAt.Value:O}\"" : "null";
            var json = $$"""{"isEnabled":{{_isEnabled.ToString().ToLowerInvariant()}},"enabledAt":{{enabledAtStr}}}""";
            _fs.WriteAllText(path, json);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning("Brief mode 状态保存失败: {Message}", ex.Message);
        }
    }
}
