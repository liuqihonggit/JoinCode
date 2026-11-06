namespace Testing.Common.Process;

/// <summary>
/// jcc 测试环境变量构造器 — 统一 E2E/单元测试中 jcc.exe 的环境变量构造
/// 工厂方法 <see cref="Create"/> 承载默认值，链式方法覆盖特定字段，<see cref="Build"/> 返回最终字典
/// </summary>
public sealed class JccEnvBuilder {
    private readonly Dictionary<string, string> _env = new(StringComparer.OrdinalIgnoreCase);

    private JccEnvBuilder() { }

    /// <summary>
    /// 创建带默认值的 JccEnvBuilder
    /// 默认值: JCC_ENDPOINT/JCC_VENDOR/JCC_MODEL_ID/OPENAI_API_KEY/JCC_PERMISSION_MODE=bypass/JCC_API_TIMEOUT_MS=30000/JCC_LANGUAGE=zh
    /// </summary>
    /// <param name="vendor">LLM 供应商</param>
    /// <param name="mockServerPort">MockServer 监听端口</param>
    /// <param name="stateFilePath">workflow_state.json 路径</param>
    /// <param name="appDataFolder">隔离 AppData 目录</param>
    /// <returns>带默认值的 JccEnvBuilder 实例</returns>
    public static JccEnvBuilder Create(VendorKind vendor, int mockServerPort, string stateFilePath, string appDataFolder) {
        var builder = new JccEnvBuilder();
        builder.SetDefaults(vendor, mockServerPort, stateFilePath, appDataFolder);
        return builder;
    }

    private void SetDefaults(VendorKind vendor, int mockServerPort, string stateFilePath, string appDataFolder) {
        var (providerValue, modelId, apiKeyEnvVar) = ResolveVendor(vendor);
        _env["JCC_ENDPOINT"] = $"http://localhost:{mockServerPort}";
        _env["OPENAI_API_KEY"] = "sk-test-1234567890";
        _env["JCC_VENDOR"] = providerValue;
        _env["JCC_MODEL_ID"] = modelId;
        _env[apiKeyEnvVar] = "sk-test-1234567890";
        _env["JCC_STATE_FILE_PATH"] = stateFilePath;
        _env["JCC_PERMISSION_MODE"] = "bypass";
        _env["JCC_APP_DATA_FOLDER"] = appDataFolder;
        _env["JCC_API_TIMEOUT_MS"] = "30000";
        _env["JCC_LANGUAGE"] = "zh";
    }

    private static (string ProviderValue, string ModelId, string ApiKeyEnvVar) ResolveVendor(VendorKind vendor) {
        return vendor switch {
            VendorKind.OpenAi => (VendorKind.OpenAi.ToValue(), "gpt-4o", "OPENAI_API_KEY"),
            VendorKind.Anthropic => (VendorKind.Anthropic.ToValue(), "claude-sonnet-4-20250514", "ANTHROPIC_API_KEY"),
            VendorKind.DeepSeek => (VendorKind.DeepSeek.ToValue(), "deepseek-v4-flash", "DEEPSEEK_API_KEY"),
            _ => (VendorKind.OpenAi.ToValue(), "gpt-4o", "OPENAI_API_KEY")
        };
    }

    /// <summary>覆盖单个环境变量</summary>
    /// <param name="key">环境变量名</param>
    /// <param name="value">环境变量值</param>
    /// <returns>this（链式调用）</returns>
    public JccEnvBuilder With(string key, string value) {
        _env[key] = value;
        return this;
    }

    /// <summary>批量覆盖环境变量（来自 ConversationScript.ExtraEnvVars）</summary>
    /// <param name="extra">额外环境变量键值对</param>
    /// <returns>this（链式调用）</returns>
    public JccEnvBuilder WithExtraEnvVars(IEnumerable<KeyValuePair<string, string>>? extra) {
        if (extra is not null) {
            foreach (var (key, value) in extra) {
                _env[key] = value;
            }
        }
        return this;
    }

    /// <summary>移除指定环境变量（如重试时不设 JCC_API_TIMEOUT_MS）</summary>
    /// <param name="key">要移除的环境变量名</param>
    /// <returns>this（链式调用）</returns>
    public JccEnvBuilder Without(string key) {
        _env.Remove(key);
        return this;
    }

    /// <summary>构建最终环境变量字典（副本，修改不影响 builder 状态）</summary>
    /// <returns>带 OrdinalIgnoreCase 比较器的环境变量字典</returns>
    public Dictionary<string, string> Build() => new(_env, StringComparer.OrdinalIgnoreCase);

    /// <summary>将环境变量直接应用到 ProcessStartInfo（用于独立 E2E 测试）</summary>
    /// <param name="psi">进程启动信息</param>
    public void ApplyTo(ProcessStartInfo psi) {
        foreach (var (key, value) in _env) {
            psi.EnvironmentVariables[key] = value;
        }
    }
}
