namespace Infrastructure.Localization;

/// <summary>
/// 本地化系统初始化器 - 加载 .resx 资源并初始化 L 类
/// Phase 2 源码生成器就绪后，DefaultStrings/ZhStrings 将自动生成
/// </summary>
public static partial class LocalizerInitializer
{
    /// <summary>
    /// 初始化本地化系统
    /// </summary>
    /// <param name="language">目标语言代码（"en", "zh" 等）</param>
    public static void Initialize(string language = "zh")
    {
        var defaultEntries = new Dictionary<string, string>();
        var zhEntries = new Dictionary<string, string>();

        // 各模块注册自己的字符串
        RegisterCoreEntries(defaultEntries, zhEntries);
        RegisterSkillEntries(defaultEntries, zhEntries);
        RegisterDevEntries(defaultEntries, zhEntries);
        RegisterWorkflowEntries(defaultEntries, zhEntries);
        RegisterUserEntries(defaultEntries, zhEntries);
        RegisterTerminalEntries(defaultEntries, zhEntries);
        RegisterTaskEntries(defaultEntries, zhEntries);
        RegisterCommunicationEntries(defaultEntries, zhEntries);
        RegisterHandsEntries(defaultEntries, zhEntries);
        RegisterSyncEntries(defaultEntries, zhEntries);
        RegisterVaultEntries(defaultEntries, zhEntries);
        RegisterGuardEntries(defaultEntries, zhEntries);
        RegisterBrainEntries(defaultEntries, zhEntries);
        RegisterHostEntries(defaultEntries, zhEntries);
        RegisterInfrastructureEntries(defaultEntries, zhEntries);
        RegisterClockEntries(defaultEntries, zhEntries);
        RegisterEyesEntries(defaultEntries, zhEntries);

        // 中文为基底，非中文时用英文覆盖
        var entries = new Dictionary<string, string>(zhEntries);
        if (language is not "zh")
        {
            foreach (var (key, value) in defaultEntries)
                entries[key] = value;
        }

        L.Initialize(language, entries.ToFrozenDictionary());
    }

    /// <summary>
    /// 确保本地化系统已初始化（使用默认中文）
    /// </summary>
    public static void EnsureInitialized() => Initialize("zh");
}
