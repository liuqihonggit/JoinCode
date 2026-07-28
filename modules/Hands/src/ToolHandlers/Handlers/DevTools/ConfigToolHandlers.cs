namespace Tools.Handlers;

/// <summary>
/// Config 工具处理器 — LLM 通过此工具读写配置设置。
/// 对齐 TS: ConfigTool.ts — 统一入口（省略 value = GET，提供 value = SET）
/// </summary>
[McpToolHandler(ToolCategory.Config)]
public sealed partial class ConfigToolHandlers
{
    private readonly IConfigurationService _configService;
    private readonly ITelemetryService? _telemetryService;
    [Inject] private readonly ILogger<ConfigToolHandlers>? _logger;

    public ConfigToolHandlers(IConfigurationService configService, ITelemetryService? telemetryService = null, ILogger<ConfigToolHandlers>? logger = null)
    {
        _configService = configService ?? throw new ArgumentNullException(nameof(configService));
        _telemetryService = telemetryService;
        _logger = logger;
    }

    /// <summary>
    /// 获取配置设置值。
    /// 对齐 TS: ConfigTool.call — value === undefined → GET
    /// </summary>
    [McpTool(InteractionToolNameConstants.ConfigGet, "Get a configuration setting value", "config")]
    public async Task<ToolResult> ConfigGetAsync(
        [McpToolParameter("The setting key (e.g., \"theme\", \"model\", \"permissions.defaultMode\")")] string setting,
        CancellationToken cancellationToken = default)
    {
        // 对齐 TS: isSupported — 未知 key 拒绝
        if (!SupportedSettings.IsSupported(setting))
        {
            return ResultBuilder.Error()
                .WithText($"Unknown setting: \"{setting}\"")
                .Build();
        }

        var config = SupportedSettings.GetConfig(setting)!;
        // 对齐 TS: source 分流 — global 走 ~/.jcc/global.json, settings 走 ~/.jcc/settings.json
        var source = config.Source == "global" ? SettingSource.GlobalConfig : SettingSource.UserSettings;
        var currentValue = await _configService.GetAsync(setting, source, cancellationToken).ConfigureAwait(false);

        // 对齐 TS: formatOnRead — 读取时格式化
        var displayValue = config.FormatOnRead is not null
            ? config.FormatOnRead(currentValue)
            : currentValue;

        return ResultBuilder.Success()
            .WithText($"{setting} = {FormatValue(displayValue)}")
            .Build();
    }

    /// <summary>
    /// 设置配置设置值。
    /// 对齐 TS: ConfigTool.call — value provided → SET
    /// 包含：boolean 强转 → options 校验 → validateOnWrite → 写入
    /// </summary>
    [McpTool(InteractionToolNameConstants.ConfigSet, "Set a configuration setting value", "config")]
    public async Task<ToolResult> ConfigSetAsync(
        [McpToolParameter("The setting key")] string setting,
        [McpToolParameter("The new value")] string value,
        CancellationToken cancellationToken = default)
    {
        // 对齐 TS: isSupported — 未知 key 拒绝
        if (!SupportedSettings.IsSupported(setting))
        {
            return ResultBuilder.Error()
                .WithText($"Unknown setting: \"{setting}\"")
                .Build();
        }

        var config = SupportedSettings.GetConfig(setting)!;
        var finalValue = value;

        // 对齐 TS: boolean 强转
        if (config.Type == "boolean")
        {
            var lower = value.ToLowerInvariant().Trim();
            if (lower is "true")
                finalValue = "true";
            else if (lower is "false")
                finalValue = "false";
            else
                return ResultBuilder.Error()
                    .WithText($"{setting} requires true or false.")
                    .Build();
        }

        // 对齐 TS: options 校验
        var options = SupportedSettings.GetOptionsForSetting(setting);
        if (options is not null && !options.Contains(finalValue))
        {
            return ResultBuilder.Error()
                .WithText($"Invalid value \"{value}\". Options: {string.Join(", ", options)}")
                .Build();
        }

        // 对齐 TS: validateOnWrite — 异步验证
        if (config.ValidateOnWrite is not null)
        {
            var (valid, error) = await config.ValidateOnWrite(finalValue).ConfigureAwait(false);
            if (!valid)
            {
                return ResultBuilder.Error()
                    .WithText(error ?? "Validation failed")
                    .Build();
            }
        }

        // 写入 — 对齐 TS: global → saveGlobalConfig, settings → updateSettingsForSource
        var source = config.Source == "global" ? SettingSource.GlobalConfig : SettingSource.UserSettings;
        var previousValue = await _configService.GetAsync(setting, source, cancellationToken).ConfigureAwait(false);

        var success = await _configService.SetAsync(setting, finalValue, source, config.AppStateKey, cancellationToken).ConfigureAwait(false);

        if (!success)
        {
            return ResultBuilder.Error()
                .WithText($"Failed to set {setting}")
                .Build();
        }

        _logger?.LogInformation("Config changed: {Setting} = {Value} (was {Previous})", setting, finalValue, previousValue);

        // 对齐 TS: logEvent('tengu_config_tool_changed', { setting, value })
        _telemetryService?.RecordCount("config.tool.changed", new Dictionary<string, string> { ["setting"] = setting, ["value"] = finalValue }, description: "Config tool setting changed");

        return ResultBuilder.Success()
            .WithText($"Set {setting} to {FormatValue(finalValue)}")
            .Build();
    }

    /// <summary>
    /// 列出所有可配置设置。
    /// 对齐 TS: prompt.ts — 动态生成设置列表
    /// </summary>
    [McpTool(InteractionToolNameConstants.ConfigList, "List all configurable settings", "config")]
    public Task<ToolResult> ConfigListAsync(CancellationToken cancellationToken = default)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Available settings:");
        sb.AppendLine();

        var globalSettings = new List<(string Key, ConfigSetting Config)>();
        var projectSettings = new List<(string Key, ConfigSetting Config)>();

        foreach (var (key, config) in SupportedSettings.All)
        {
            if (config.Source == "global")
                globalSettings.Add((key, config));
            else
                projectSettings.Add((key, config));
        }

        if (globalSettings.Count > 0)
        {
            sb.AppendLine("Global settings:");
            foreach (var (key, config) in globalSettings)
            {
                AppendSettingLine(sb, key, config);
            }
            sb.AppendLine();
        }

        if (projectSettings.Count > 0)
        {
            sb.AppendLine("Project settings:");
            foreach (var (key, config) in projectSettings)
            {
                AppendSettingLine(sb, key, config);
            }
        }

        return Task.FromResult(ResultBuilder.Success()
            .WithText(sb.ToString())
            .Build());
    }

    private static void AppendSettingLine(StringBuilder sb, string key, ConfigSetting config)
    {
        sb.Append("  - ");
        sb.Append(key);

        var options = config.GetOptions is not null ? config.GetOptions() : config.Options;
        if (options is { Length: > 0 })
        {
            sb.Append(": ");
            sb.Append(string.Join(", ", options.Select(o => $"\"{o}\"")));
        }
        else if (config.Type == "boolean")
        {
            sb.Append(": true/false");
        }

        sb.Append(" - ");
        sb.AppendLine(config.Description);
    }

    private static string FormatValue(string? value)
    {
        if (value is null)
            return "null";
        if (bool.TryParse(value, out var b))
            return b ? "true" : "false";
        return value;
    }
}
