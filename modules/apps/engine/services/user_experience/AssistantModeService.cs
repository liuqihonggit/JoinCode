namespace IO.Services;

/// <summary>助手模式服务 — 通过环境变量判定是否启用助手模式，单例服务</summary>
[Register(typeof(IAssistantModeService), ServiceLifetime.Singleton)]
public sealed partial class AssistantModeService : ServiceEntity, IAssistantModeService {
    /// <summary>是否处于助手模式 — 等价于 IsAssistantModeEnabled</summary>
    public bool IsAssistantMode => IsAssistantModeEnabled;

    /// <summary>助手模式是否已启用 — 读取环境变量 JccEnvVar.AssistantMode，值为 "1" 或 "true" 时返回 true</summary>
    public bool IsAssistantModeEnabled {
        get {
            var value = Environment.GetEnvironmentVariable(JccEnvVar.AssistantMode.ToValue());
            return string.Equals(value, "1", StringComparison.OrdinalIgnoreCase)
                || string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
        }
    }
}