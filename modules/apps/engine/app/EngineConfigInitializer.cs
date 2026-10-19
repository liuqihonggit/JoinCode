// JCC1017 抑制: 存量手写 JSON 模板, 后续改为 DTO+JsonContext
#pragma warning disable JCC1017
namespace JoinCode.App.Builder;

/// <summary>
/// 引擎配置初始化器 — 确保全局配置文件存在（ADR 0143 从 cli StartupWorkflow 提取的引擎部分）。
/// <para>首次启动自动创建带注释的模板，不覆盖已有文件；必须在 LoadConfigAsync 之前调用。</para>
/// <para>交互式 Onboarding/信任确认留在宿主侧 StartupWorkflow（CLI 表现层）。</para>
/// </summary>
public static class EngineConfigInitializer {
    /// <summary>
    /// 确保全局配置文件存在 — 首次启动时自动创建带注释的模板，不覆盖已有文件
    /// </summary>
    /// <param name="fs">文件系统抽象</param>
    /// <param name="modelConfigLoader">模型配置加载器，用于生成默认配置模板的模型 ID；null 时使用回退默认值</param>
    public static async Task EnsureConfigFilesExistAsync(IFileSystem fs, IModelConfigLoader? modelConfigLoader = null) {
        var appDataPath = AppDataConstants.Paths.JccDirectory;
        var settingsPath = System.IO.Path.Combine(appDataPath, AppDataConstants.SettingsFileName);
        var authPath = System.IO.Path.Combine(appDataPath, AppDataConstants.AuthFileName);

        if (!fs.DirectoryExists(appDataPath)) {
            DirectoryHelper.EnsureDirectoryExists(fs, appDataPath);
        }

        if (!fs.FileExists(settingsPath)) {
            await fs.WriteAllTextAsync(settingsPath, BuildDefaultSettingsTemplate(modelConfigLoader)).ConfigureAwait(false);
        }

        if (!fs.FileExists(authPath)) {
            await fs.WriteAllTextAsync(authPath, BuildDefaultAuthTemplate()).ConfigureAwait(false);
        }
    }

    private static string BuildDefaultSettingsTemplate(IModelConfigLoader? modelConfigLoader) {
        var defaultModel = modelConfigLoader?.GetDefaultModelId(VendorKind.DeepSeek.ToValue()) ?? DefaultModelCatalog.GetDefaultModel(VendorKind.DeepSeek); // P1-⑤⑥ 委托枚举+统一数据源
        var sb = new StringBuilder();
        sb.AppendLine("{");
        sb.AppendLine("  // LLM Provider: deepseek | openai | anthropic | azure | agnes");
        sb.AppendLine($"  \"provider\": \"{VendorKind.DeepSeek.ToValue()}\","); // P1-⑤ 委托枚举
        sb.AppendLine();
        sb.AppendLine("  // 模型 ID，可用 /model 命令切换");
        sb.AppendLine($"  \"model\": \"{defaultModel}\",");
        sb.AppendLine();
        sb.AppendLine("  // API 端点（默认使用 Provider 内置地址，无需修改）");
        sb.AppendLine("  // \"endpoint\": \"https://api.deepseek.com\",");
        sb.AppendLine();
        sb.AppendLine("  // 推理努力级别: low | medium | high");
        sb.AppendLine("  // \"effortLevel\": \"high\",");
        sb.AppendLine();
        sb.AppendLine("  // 快速模式：使用轻量模型处理简单任务");
        sb.AppendLine("  // \"fastMode\": false,");
        sb.AppendLine();
        sb.AppendLine("  // 默认 Shell: powershell | bash | cmd");
        sb.AppendLine("  // \"defaultShell\": \"powershell\",");
        sb.AppendLine();
        sb.AppendLine("  // 语言: zh-CN | en-US");
        sb.AppendLine("  // \"language\": \"zh-CN\",");
        sb.AppendLine();
        sb.AppendLine("  // 自动记忆：自动将重要信息保存到记忆文件");
        sb.AppendLine("  // \"autoMemoryEnabled\": true");
        sb.Append('}');
        return sb.ToString();
    }

    private static string BuildDefaultAuthTemplate() {
        var sb = new StringBuilder();
        sb.AppendLine("{");
        sb.AppendLine("  // API Key 存储 — 键名为 Provider 名称，值为对应的 API Key");
        sb.AppendLine("  // 支持的键名: deepseek | openai | anthropic | azure | agnes");
        sb.AppendLine("  //");
        sb.AppendLine("  // 示例:");
        sb.AppendLine("  // \"deepseek\": \"sk-your-deepseek-api-key\",");
        sb.AppendLine("  // \"openai\": \"sk-your-openai-api-key\",");
        sb.AppendLine("  // \"anthropic\": \"sk-ant-your-anthropic-api-key\"");
        sb.AppendLine("  //");
        sb.AppendLine("  // 也可通过环境变量设置（优先级更高）:");
        sb.AppendLine("  //   DEEPSEEK_API_KEY / OPENAI_API_KEY / ANTHROPIC_API_KEY / AZURE_OPENAI_API_KEY");
        sb.Append('}');
        return sb.ToString();
    }
}
