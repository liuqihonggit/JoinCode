namespace JoinCode.Abstractions.Prompts.ToolPrompts;

/// <summary>
/// ConfigTool 提示词
/// </summary>
[ToolPrompt(ToolName = "Config", Category = ToolPromptCategory.System)]
public static class ConfigToolPrompt
{
    public const string Description = "获取或设置配置设置。";

    /// <summary>
    /// 生成提示词文档
    /// </summary>
    public static string GeneratePrompt(Dictionary<string, ConfigSetting> supportedSettings, List<ModelOption> modelOptions)
    {
        var globalSettings = new List<string>();
        var projectSettings = new List<string>();

        foreach (var (key, config) in supportedSettings)
        {
            if (key == "model") continue;

            var options = GetOptionsForSetting(key, config);
            var lineBuilder = new System.Text.StringBuilder();
            lineBuilder.Append("- ");
            lineBuilder.Append(key);

            if (!string.IsNullOrEmpty(options))
            {
                lineBuilder.Append(": ");
                lineBuilder.Append(options);
            }
            else if (config.Type == "boolean")
            {
                lineBuilder.Append(": true/false");
            }

            lineBuilder.Append(" - ");
            lineBuilder.Append(config.Description);

            var line = lineBuilder.ToString();

            if (config.Source == "global")
            {
                globalSettings.Add(line);
            }
            else
            {
                projectSettings.Add(line);
            }
        }

        var modelSection = GenerateModelSection(modelOptions);

        return $@"获取或设置配置设置。

  查看或更改设置。当用户请求配置更改、询问当前设置或调整设置对他们有益时使用。


## 用法
- **获取当前值：** 省略 ""value"" 参数
- **设置新值：** 包含 ""value"" 参数

## 可配置设置列表
以下设置可供你更改：

### 全局设置（存储在 ~/{AppDataConstants.AppDataFolder}.json）
{string.Join("\n", globalSettings)}

### 项目设置（存储在 settings.json）
{string.Join("\n", projectSettings)}

{modelSection}
## 示例
- 获取主题：{{ ""setting"": ""theme"" }}
- 设置深色主题：{{ ""setting"": ""theme"", ""value"": ""dark"" }}
- 启用 vim 模式：{{ ""setting"": ""editorMode"", ""value"": ""vim"" }}
- 启用详细模式：{{ ""setting"": ""verbose"", ""value"": true }}
- 更改模型：{{ ""setting"": ""model"", ""value"": ""opus"" }}
- 更改权限模式：{{ ""setting"": ""permissions.defaultMode"", ""value"": ""plan"" }}
";
    }

    private static string GetOptionsForSetting(string key, ConfigSetting config)
    {
        // 优先使用动态选项，其次静态选项
        var options = config.GetOptions != null ? config.GetOptions() : config.Options;
        if (options is { Length: > 0 })
        {
            return string.Join(", ", options.Select(o => $"\"{o}\"").ToArray());
        }

        return "";
    }

    private static string GenerateModelSection(List<ModelOption> modelOptions)
    {
        if (modelOptions.Count > 0)
        {
            var lines = modelOptions.Select(o =>
            {
                var value = o.Value == null ? "null/\"default\"" : $"\"{o.Value}\"";
                return $"  - {value}: {o.DescriptionForModel ?? o.Description}";
            }).ToArray();
            return $@"## 模型
- model - 覆盖默认模型。可用选项：
{string.Join("\n", lines)}
";
        }

        return $@"## 模型
- model - 覆盖默认模型（sonnet、opus、haiku、best 或完整模型 ID）
";
    }
}
