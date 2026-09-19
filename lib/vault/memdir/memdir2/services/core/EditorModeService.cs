namespace Core.Memdir;

/// <summary>
/// 编辑器模式服务 — 管理当前编辑器模式（Normal/Vim），持久化到配置文件
/// </summary>
[Register(typeof(ConfigPersistentServiceBase<EditorMode>), ServiceLifetime.Singleton)]
[Register(typeof(IEditorModeService), ServiceLifetime.Singleton)]
public sealed partial class EditorModeService : ConfigPersistentServiceBase<EditorMode>, IEditorModeService {
    /// <summary>
    /// 构造编辑器模式服务
    /// </summary>
    /// <param name="configService">配置服务（可选，用于持久化）</param>
    public EditorModeService(IConfigurationService? configService = null)
        : base(EditorMode.Normal, configService) { }

    /// <summary>
    /// 配置键名 — "editor.mode"
    /// </summary>
    protected override string ConfigKey => "editor.mode";
    /// <summary>
    /// 尝试将原始配置字符串解析为编辑器模式
    /// </summary>
    /// <param name="raw">原始配置值</param>
    /// <param name="result">解析结果</param>
    /// <returns>解析是否成功</returns>
    protected override bool TryParseConfigValue(string? raw, out EditorMode result) {
        if (raw is not null && EditorModeExtensions.FromValue(raw) is { } mode) {
            result = mode;
            return true;
        }
        result = default;
        return false;
    }
    /// <summary>
    /// 将编辑器模式格式化为配置字符串（小写）
    /// </summary>
    /// <param name="value">编辑器模式</param>
    /// <returns>格式化后的配置字符串</returns>
    protected override string FormatConfigValue(EditorMode value)
        => value.ToString().ToLowerInvariant();

    /// <summary>
    /// 当前编辑器模式
    /// </summary>
    public EditorMode CurrentMode => Value;

    /// <summary>
    /// 设置编辑器模式
    /// </summary>
    /// <param name="mode">目标模式</param>
    public void SetMode(EditorMode mode) => SetValue(mode);

    /// <summary>
    /// 在 Normal 与 Vim 模式之间切换
    /// </summary>
    /// <returns>切换后的新模式</returns>
    public EditorMode Toggle() {
        var newMode = Value == EditorMode.Normal ? EditorMode.Vim : EditorMode.Normal;
        SetValue(newMode);
        return newMode;
    }
}