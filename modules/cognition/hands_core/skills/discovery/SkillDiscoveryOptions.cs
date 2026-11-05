namespace Core.Skills.Discovery;

/// <summary>
/// 技能发现服务配置选项
/// </summary>
[Register(typeof(SkillDiscoveryOptions), ServiceLifetime.Singleton)]
public sealed partial class SkillDiscoveryOptions : ServiceEntity {
    /// <summary>
    /// 技能目录路径
    /// </summary>
    public string SkillsDirectory { get; init; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        AppDataConstants.AppDataFolder,
        "skills");

    /// <summary>
    /// 是否启用文件监控
    /// </summary>
    public bool EnableFileWatching { get; init; } = true;

    /// <summary>
    /// 文件变更去抖动毫秒数
    /// </summary>
    public int WatchDebounceMs { get; init; } = 500;

    /// <summary>
    /// 加载时是否验证技能
    /// </summary>
    public bool ValidateOnLoad { get; init; } = true;

    /// <summary>
    /// 支持的文件扩展名列表
    /// </summary>
    public IReadOnlyList<string> SupportedExtensions { get; init; } = new List<string> { ".json", ".md" }.AsReadOnly();

    /// <summary>
    /// 创建默认配置
    /// </summary>
    public SkillDiscoveryOptions() { }

    /// <summary>
    /// 从工作流配置创建技能发现选项
    /// </summary>
    /// <param name="config">工作流配置；为 null 则使用默认值</param>
    public SkillDiscoveryOptions(WorkflowConfig? config) {
        if (config is not null && !string.IsNullOrEmpty(config.SkillsDirectory)) {
            SkillsDirectory = config.SkillsDirectory;
        }
    }
}