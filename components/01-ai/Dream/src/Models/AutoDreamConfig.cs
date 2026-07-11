namespace JoinCode.Dream;

/// <summary>
/// 自动做梦配置
/// </summary>
public sealed class AutoDreamConfig
{
    /// <summary>
    /// 最小间隔小时数（默认24小时）
    /// </summary>
    public int MinHours { get; set; } = 24;

    /// <summary>
    /// 最小会话数量（默认5个）
    /// </summary>
    public int MinSessions { get; set; } = 5;

    /// <summary>
    /// 会话扫描间隔（毫秒，默认10分钟）
    /// </summary>
    public int SessionScanIntervalMs { get; set; } = 10 * 60 * 1000;

    /// <summary>
    /// 是否启用自动做梦
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// 是否启用自动记忆
    /// </summary>
    public bool AutoMemoryEnabled { get; set; } = true;

    /// <summary>
    /// 自动记忆目录路径
    /// </summary>
    public string? AutoMemoryPath { get; set; }

    /// <summary>
    /// 项目目录（用于扫描会话）
    /// </summary>
    public string? ProjectDir { get; set; }
}

/// <summary>
/// 门控检查结果
/// </summary>
public readonly record struct GateCheckResult(bool Passed, string Reason);

/// <summary>
/// 会话扫描结果
/// </summary>
public readonly record struct SessionScanResult(
    IReadOnlyList<string> SessionIds,
    int Count,
    long LastScanTime);

/// <summary>
/// 自动做梦配置构建器 - 支持链式配置
/// </summary>
public sealed class AutoDreamConfigBuilder
{
    private int _minHours = 24;
    private int _minSessions = 5;
    private int _sessionScanIntervalMs = 10 * 60 * 1000;
    private bool _enabled = true;
    private bool _autoMemoryEnabled = true;
    private string? _autoMemoryPath;
    private string? _projectDir;

    private AutoDreamConfigBuilder()
    {
    }

    /// <summary>
    /// 创建新的构建器
    /// </summary>
    public static AutoDreamConfigBuilder Create() => new();

    /// <summary>
    /// 设置最小间隔小时数
    /// </summary>
    public AutoDreamConfigBuilder WithMinHours(int hours)
    {
        _minHours = hours;
        return this;
    }

    /// <summary>
    /// 设置最小会话数量
    /// </summary>
    public AutoDreamConfigBuilder WithMinSessions(int sessions)
    {
        _minSessions = sessions;
        return this;
    }

    /// <summary>
    /// 设置会话扫描间隔（毫秒）
    /// </summary>
    public AutoDreamConfigBuilder WithSessionScanInterval(int milliseconds)
    {
        _sessionScanIntervalMs = milliseconds;
        return this;
    }

    /// <summary>
    /// 设置会话扫描间隔（分钟）
    /// </summary>
    public AutoDreamConfigBuilder WithSessionScanIntervalMinutes(int minutes)
    {
        _sessionScanIntervalMs = minutes * 60 * 1000;
        return this;
    }

    /// <summary>
    /// 启用自动做梦
    /// </summary>
    public AutoDreamConfigBuilder Enable()
    {
        _enabled = true;
        return this;
    }

    /// <summary>
    /// 禁用自动做梦
    /// </summary>
    public AutoDreamConfigBuilder Disable()
    {
        _enabled = false;
        return this;
    }

    /// <summary>
    /// 设置是否启用自动做梦
    /// </summary>
    public AutoDreamConfigBuilder WithEnabled(bool enabled)
    {
        _enabled = enabled;
        return this;
    }

    /// <summary>
    /// 启用自动记忆
    /// </summary>
    public AutoDreamConfigBuilder EnableAutoMemory()
    {
        _autoMemoryEnabled = true;
        return this;
    }

    /// <summary>
    /// 禁用自动记忆
    /// </summary>
    public AutoDreamConfigBuilder DisableAutoMemory()
    {
        _autoMemoryEnabled = false;
        return this;
    }

    /// <summary>
    /// 设置是否启用自动记忆
    /// </summary>
    public AutoDreamConfigBuilder WithAutoMemoryEnabled(bool enabled)
    {
        _autoMemoryEnabled = enabled;
        return this;
    }

    /// <summary>
    /// 设置自动记忆目录路径
    /// </summary>
    public AutoDreamConfigBuilder WithAutoMemoryPath(string path)
    {
        _autoMemoryPath = path;
        return this;
    }

    /// <summary>
    /// 设置项目目录
    /// </summary>
    public AutoDreamConfigBuilder WithProjectDir(string projectDir)
    {
        _projectDir = projectDir;
        return this;
    }

    /// <summary>
    /// 使用高频扫描模式（适合开发环境）
    /// </summary>
    public AutoDreamConfigBuilder UseHighFrequencyMode()
    {
        _minHours = 1;
        _minSessions = 2;
        _sessionScanIntervalMs = 60 * 1000; // 1分钟
        return this;
    }

    /// <summary>
    /// 使用低频扫描模式（适合生产环境）
    /// </summary>
    public AutoDreamConfigBuilder UseLowFrequencyMode()
    {
        _minHours = 48;
        _minSessions = 10;
        _sessionScanIntervalMs = 60 * 60 * 1000; // 1小时
        return this;
    }

    /// <summary>
    /// 使用默认平衡模式
    /// </summary>
    public AutoDreamConfigBuilder UseBalancedMode()
    {
        _minHours = 24;
        _minSessions = 5;
        _sessionScanIntervalMs = 10 * 60 * 1000; // 10分钟
        return this;
    }

    /// <summary>
    /// 构建自动做梦配置
    /// </summary>
    public AutoDreamConfig Build()
    {
        return new AutoDreamConfig
        {
            MinHours = _minHours,
            MinSessions = _minSessions,
            SessionScanIntervalMs = _sessionScanIntervalMs,
            Enabled = _enabled,
            AutoMemoryEnabled = _autoMemoryEnabled,
            AutoMemoryPath = _autoMemoryPath,
            ProjectDir = _projectDir
        };
    }
}
