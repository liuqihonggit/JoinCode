namespace JoinCode.Guard.Configuration.ConfigRoot;

/// <summary>
/// 单层配置访问实现 — 从已加载的 SettingsJson DTO 按点分路径取值（TASK054）。
/// <para>桥接模式：不读物理文件，从启动时全量加载的 SettingsJson DTO 取值。</para>
/// <para>路径约定（点分）：searchProviders.{name}.apiKeyEnvVar / searchProviders.{name}.priority / searchProviders.{name}.enabled / searchProviders.{name}.endpointEnvVar</para>
/// <para>线程安全：SettingsJson 是 init-only 不可变 DTO，多线程读安全。</para>
/// <para>TryGet 模式：bool 成功时 out 必然非 null，强制调用方处理未命中。</para>
/// </summary>
public sealed class ConfigLayer : IConfigLayer {
    private readonly SettingsJson? _settings;

    /// <summary>
    /// 构造单层配置访问。
    /// </summary>
    /// <param name="settings">已加载的 SettingsJson DTO（null 表示该层配置不存在）</param>
    public ConfigLayer(SettingsJson? settings) {
        _settings = settings;
    }

    /// <inheritdoc />
    public bool Exists => _settings is not null;

    /// <inheritdoc />
    public bool TryGet<T>(string path, [NotNullWhen(true)] out T? value) {
        ArgumentException.ThrowIfNullOrEmpty(path);
        if (_settings is null) {
            value = default;
            return false;
        }

        var segments = path.Split('.');
        var found = segments[0] switch {
            "searchProviders" => ResolveSearchProviders<T>(_settings, segments, out value),
            _ => FalseWithDefault(out value),
        };
        return found;
    }

    private static bool FalseWithDefault<T>(out T? value) {
        value = default;
        return false;
    }

    private static bool ResolveSearchProviders<T>(SettingsJson settings, string[] segments, [NotNullWhen(true)] out T? value) {
        if (segments.Length < 3) {
            value = default;
            return false;
        }
        var providerName = segments[1];
        if (settings.SearchProviders is null || !settings.SearchProviders.TryGetValue(providerName, out var provider)) {
            value = default;
            return false;
        }

        var fieldValue = segments[2] switch {
            "apiKeyEnvVar" => provider.ApiKeyEnvVar,
            "endpointEnvVar" => provider.EndpointEnvVar,
            "priority" => provider.Priority.ToString(),
            "enabled" => provider.Enabled.ToString(),
            _ => null,
        };

        if (fieldValue is null) {
            value = default;
            return false;
        }
        if (fieldValue is T typed) {
            value = typed;
            return true;
        }
        value = default;
        return false;
    }
}
