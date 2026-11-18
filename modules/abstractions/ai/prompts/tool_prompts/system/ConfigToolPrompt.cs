// JCC1017 抑制: 提示词正文内嵌 JSON 示例, 非序列化用途
#pragma warning disable JCC1017
namespace JoinCode.Abstractions.Prompts.ToolPrompts;

/// <summary>
/// ConfigTool 提示词
/// <para><b>为什么把列表交给这里生成</b>：工具描述原本只有一句「获取或设置配置设置」，
/// 模型并不知道有哪些键可设、每个键接受什么值，只能猜（猜 <c>permissions.mode</c>、
/// 猜 <c>model: opus</c>）。而 <c>config_list</c> 是个要额外花一轮调用的工具 —— 模型不会先问再设。
/// 列表直接从唯一清单 <see cref="ConfigKeyRegistry"/> 生成，所以这里永远和
/// <c>config_get</c>/<c>config_set</c> 认可的值一致；新增键不会漏在提示词外。</para>
/// <para><b>可执行性</b>：本类的 <see cref="GetDescription"/> 由源码生成器注册进
/// <c>ToolPromptRegistration</c>，模型请求工具时会用它替换 <c>[McpTool]</c> 上的短描述。</para>
/// </summary>
[ToolPrompt(ToolName = ConfigToolName.Config, Category = ToolPromptCategory.System)]
public static class ConfigToolPrompt {
    /// <summary>工具短描述（一行）。</summary>
    public const string Description = "获取或设置配置设置。";

    /// <summary>
    /// 生成工具详细描述 —— 含分组后的全部可配置键、可选值与含义。
    /// <para>无参数：列表来自契约层唯一清单，不需要调用方再传一份（原来那个带参重载零调用方，
    /// 于是这段内容从来没到达过模型）。</para>
    /// </summary>
    /// <returns>工具详细描述文本。</returns>
    public static string GetDescription() {
        var globalLines = new List<string>();
        var projectLines = new List<string>();

        foreach (var (key, setting) in ConfigKeyRegistry.All.OrderBy(x => x.Key, StringComparer.Ordinal)) {
            var line = DescribeSetting(key, setting);
            if (setting.Source == SettingSource.GlobalConfig)
                globalLines.Add(line);
            else
                projectLines.Add(line);
        }

        var globalFile = $"{AppDataConstants.AppDataFolder}/{AppDataConstants.GlobalConfigFileName}";

        return $@"获取或设置配置设置。查看或更改设置。当用户请求配置更改、询问当前设置或调整设置对他们有益时使用。

## 用法
- **获取当前值：** 省略 ""value"" 参数
- **设置新值：** 包含 ""value"" 参数

## 可配置设置

### 全局设置（存储在 ~/{globalFile}）
{string.Join("\n", globalLines)}

### 项目设置（存储在项目 settings.json）
{string.Join("\n", projectLines)}

## 说明
- 布尔设置只接受 true/false；其余设置只接受列出的选项，写错会被拒绝并返回允许值。
- model 的候选模型用 model_search 查询；写入即对后续请求生效。
- 只有列出的键可改；不在列表里的键一律视为未知设置。

## 示例
- 获取主题：{{ ""setting"": ""theme"" }}
- 设置深色主题：{{ ""setting"": ""theme"", ""value"": ""dark"" }}
- 启用 vim 模式：{{ ""setting"": ""editorMode"", ""value"": ""vim"" }}
- 启用调试日志：{{ ""setting"": ""debuglog"", ""value"": true }}
- 更改模型：{{ ""setting"": ""model"", ""value"": ""deepseek-v3"" }}
- 更改权限模式：{{ ""setting"": ""permissions.defaultMode"", ""value"": ""plan"" }}
";
    }

    private static string DescribeSetting(string key, ConfigSetting setting) {
        var sb = new StringBuilder("- ");
        sb.Append(key);

        if (setting.Options is { Length: > 0 } options) {
            sb.Append(": ");
            sb.Append(string.Join(", ", options.Select(o => $"\"{o}\"")));
        } else if (setting.Type == "boolean") {
            sb.Append(": true/false");
        }

        sb.Append(" - ");
        sb.Append(setting.Description);
        return sb.ToString();
    }
}
