namespace Tools.Handlers;

/// <summary>
/// Config 工具处理器 — LLM 通过此工具读写配置设置。
/// 对齐 TS: ConfigTool.ts — 统一入口（省略 value = GET，提供 value = SET）
/// </summary>
[McpToolDispatch(ToolCategory.Config)]
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
    [McpTool(InteractionToolNameConstants.ConfigGet, "Get a configuration setting value", "config", ConcurrencySafe = true)]
    public async Task<ToolResult> ConfigGetAsync(
        [McpToolParameter("The setting key (e.g., \"theme\", \"model\", \"permissions.defaultMode\")")] string setting,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // 对齐 TS: isSupported — 未知 key 拒绝
            if (!SupportedSettings.IsSupported(setting))
            {
                var diagnostic = BuildUnknownSettingDiagnostic(setting);
                return ToolResultBuilder.Error()
                    .WithText(diagnostic.FormattedMessage)
                    .WithDiagnostic(diagnostic)
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

            return ToolResultBuilder.Success()
                .WithText($"{setting} = {FormatValue(displayValue)}")
                .Build();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return ToolExceptionDiagnosticHelper.BuildErrorResult("config_get", ex, _logger, "setting", setting);
        }
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
        try
        {
            // 对齐 TS: isSupported — 未知 key 拒绝
            if (!SupportedSettings.IsSupported(setting))
            {
                var diagnostic = BuildUnknownSettingDiagnostic(setting);
                return ToolResultBuilder.Error()
                    .WithText(diagnostic.FormattedMessage)
                    .WithDiagnostic(diagnostic)
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
                {
                    var boolDiag = BuildInvalidBooleanValueDiagnostic(setting, value);
                    return ToolResultBuilder.Error()
                        .WithText(boolDiag.FormattedMessage)
                        .WithDiagnostic(boolDiag)
                        .Build();
                }
            }

            // 对齐 TS: options 校验
            var options = SupportedSettings.GetOptionsForSetting(setting);
            if (options is not null && !options.Contains(finalValue))
            {
                var optionsDiag = BuildInvalidOptionValueDiagnostic(setting, value, options);
                return ToolResultBuilder.Error()
                    .WithText(optionsDiag.FormattedMessage)
                    .WithDiagnostic(optionsDiag)
                    .Build();
            }

            // 对齐 TS: validateOnWrite — 异步验证
            if (config.ValidateOnWrite is not null)
            {
                var (valid, error) = await config.ValidateOnWrite(finalValue).ConfigureAwait(false);
                if (!valid)
                {
                    var validateDiag = BuildValidateOnWriteFailedDiagnostic(setting, finalValue, error);
                    return ToolResultBuilder.Error()
                        .WithText(validateDiag.FormattedMessage)
                        .WithDiagnostic(validateDiag)
                        .Build();
                }
            }

            // 写入 — 对齐 TS: global → saveGlobalConfig, settings → updateSettingsForSource
            var source = config.Source == "global" ? SettingSource.GlobalConfig : SettingSource.UserSettings;
            var previousValue = await _configService.GetAsync(setting, source, cancellationToken).ConfigureAwait(false);

            var success = await _configService.SetAsync(setting, finalValue, source, config.AppStateKey, cancellationToken).ConfigureAwait(false);

            if (!success)
            {
                var setFailedDiag = BuildSetFailedDiagnostic(setting, finalValue);
                return ToolResultBuilder.Error()
                    .WithText(setFailedDiag.FormattedMessage)
                    .WithDiagnostic(setFailedDiag)
                    .Build();
            }

            _logger?.LogInformation("Config changed: {Setting} = {Value} (was {Previous})", setting, finalValue, previousValue);

            // 对齐 TS: logEvent('tengu_config_tool_changed', { setting, value })
            _telemetryService?.RecordCount("config.tool.changed", new Dictionary<string, string> { ["setting"] = setting, ["value"] = finalValue }, description: "Config tool setting changed");

            return ToolResultBuilder.Success()
                .WithText($"Set {setting} to {FormatValue(finalValue)}")
                .Build();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return ToolExceptionDiagnosticHelper.BuildErrorResult("config_set", ex, _logger, "setting", setting, new DiagnosticDetail("value", value));
        }
    }

    /// <summary>
    /// 列出所有可配置设置。
    /// 对齐 TS: prompt.ts — 动态生成设置列表
    /// </summary>
    [McpTool(InteractionToolNameConstants.ConfigList, "List all configurable settings", "config", ConcurrencySafe = true)]
    public Task<ToolResult> ConfigListAsync(CancellationToken cancellationToken = default)
    {
        try
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

            return Task.FromResult(ToolResultBuilder.Success()
                .WithText(sb.ToString())
                .Build());
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return Task.FromResult(ToolExceptionDiagnosticHelper.BuildErrorResult("config_list", ex, _logger));
        }
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

    /// <summary>
    /// 未知设置项的诊断消息 — 列出所有支持的设置 + 模糊匹配建议。
    /// 仅在失败路径调用，不影响正常操作性能。
    /// </summary>
    internal static string BuildUnknownSettingMessage(string setting)
    {
        return BuildUnknownSettingDiagnostic(setting).FormattedMessage;
    }

    /// <summary>
    /// 未知设置项的结构化诊断 — 列出所有支持的设置 + 模糊匹配建议。
    /// </summary>
    internal static ToolDiagnostic BuildUnknownSettingDiagnostic(string setting)
    {
        var sb = new StringBuilder(256);
        sb.Append($"Unknown setting: \"{setting}\"");

        var details = new List<DiagnosticDetail>(2) { new("setting", setting) };
        var suggestions = new List<string>(1);

        var candidates = new List<string>();
        foreach (var key in SupportedSettings.All.Keys)
        {
            if (key.Contains(setting, StringComparison.OrdinalIgnoreCase) ||
                setting.Contains(key, StringComparison.OrdinalIgnoreCase))
            {
                candidates.Add(key);
            }
        }

        if (candidates.Count > 0)
        {
            sb.Append($"\n[诊断] 你是不是想用: {string.Join(", ", candidates)}");
            details.Add(new DiagnosticDetail("candidates", string.Join(", ", candidates)));
            suggestions.Add($"你是不是想用: {string.Join(", ", candidates)}");
        }

        sb.Append($"\n[诊断] 支持的设置项 ({SupportedSettings.All.Count} 个):");
        foreach (var key in SupportedSettings.All.Keys)
        {
            sb.Append($"\n  - {key}");
        }

        return ToolDiagnostic.Create("UnknownSetting", sb.ToString(), details, suggestions);
    }

    /// <summary>
    /// boolean 类型设置项值无效的诊断。
    /// </summary>
    internal static ToolDiagnostic BuildInvalidBooleanValueDiagnostic(string setting, string value)
    {
        var sb = new StringBuilder(128);
        sb.Append($"{setting} requires true or false.");
        sb.Append($"\n[诊断] 提供的值: \"{value}\"");
        sb.Append($"\n[诊断] 设置项类型: boolean");

        var details = new List<DiagnosticDetail>(3)
        {
            new("setting", setting),
            new("providedValue", value),
            new("expectedType", "boolean"),
        };
        var suggestions = new List<string>(1) { "使用 \"true\" 或 \"false\"" };

        return ToolDiagnostic.Create("InvalidBooleanValue", sb.ToString(), details, suggestions);
    }

    /// <summary>
    /// 选项校验失败的诊断 — 值不在允许的选项列表中。
    /// </summary>
    internal static ToolDiagnostic BuildInvalidOptionValueDiagnostic(string setting, string value, string[] options)
    {
        var sb = new StringBuilder(128);
        sb.Append($"Invalid value \"{value}\". Options: {string.Join(", ", options)}");
        sb.Append($"\n[诊断] 设置项: {setting}");
        sb.Append($"\n[诊断] 提供的值: \"{value}\"");
        sb.Append($"\n[诊断] 允许的选项 ({options.Length} 个): {string.Join(", ", options)}");

        var details = new List<DiagnosticDetail>(3)
        {
            new("setting", setting),
            new("providedValue", value),
            new("allowedOptions", string.Join(", ", options)),
        };
        var suggestions = new List<string>(1) { $"从以下选项中选择: {string.Join(", ", options)}" };

        return ToolDiagnostic.Create("InvalidOptionValue", sb.ToString(), details, suggestions);
    }

    /// <summary>
    /// validateOnWrite 异步验证失败的诊断。
    /// </summary>
    internal static ToolDiagnostic BuildValidateOnWriteFailedDiagnostic(string setting, string value, string? error)
    {
        var errorMessage = error ?? "Validation failed";
        var sb = new StringBuilder(128);
        sb.Append(errorMessage);
        sb.Append($"\n[诊断] 设置项: {setting}");
        sb.Append($"\n[诊断] 提供的值: \"{value}\"");

        var details = new List<DiagnosticDetail>(3)
        {
            new("setting", setting),
            new("value", value),
            new("validationError", errorMessage),
        };
        var suggestions = new List<string>(1) { "检查值是否满足验证规则" };

        return ToolDiagnostic.Create("ValidateOnWriteFailed", sb.ToString(), details, suggestions);
    }

    /// <summary>
    /// 配置写入失败的诊断 — SetAsync 返回 false。
    /// </summary>
    internal static ToolDiagnostic BuildSetFailedDiagnostic(string setting, string value)
    {
        var sb = new StringBuilder(128);
        sb.Append($"Failed to set {setting}");
        sb.Append($"\n[诊断] 设置项: {setting}");
        sb.Append($"\n[诊断] 尝试写入的值: \"{value}\"");

        var details = new List<DiagnosticDetail>(2)
        {
            new("setting", setting),
            new("value", value),
        };
        var suggestions = new List<string>(2)
        {
            "检查配置文件权限",
            "确认设置项的来源（global/user）是否可写",
        };

        return ToolDiagnostic.Create("SetFailed", sb.ToString(), details, suggestions);
    }
}
