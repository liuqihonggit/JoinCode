
namespace Core.Prompts.Sections;

/// <summary>
/// 版本信息部分 - 应用版本和构建信息
/// </summary>
[PromptSection(Name = "version_info", Order = 68, IsDynamic = true)]
public static class VersionInfoSection
{
    /// <summary>
    /// 获取版本信息部分的内容。
    /// </summary>
    /// <returns>版本信息文本；未设置版本时返回 <c>null</c>。</returns>
    public static string? GetContent()
    {
        var version = PromptConfigSnapshot.Current.Version;
        var buildTime = PromptConfigSnapshot.Current.BuildTime;
        if (string.IsNullOrWhiteSpace(version))
        {
            return null;
        }

        var result = new System.Text.StringBuilder();
        result.AppendLine("# 版本信息");
        result.AppendLine($"JoinCode 版本: {version}");

        if (!string.IsNullOrWhiteSpace(buildTime))
        {
            result.AppendLine($"构建时间: {buildTime}");
        }

        return result.ToString().TrimEnd();
    }

    /// <summary>
    /// 创建版本信息提示词部分。
    /// </summary>
    /// <returns>动态系统提示词部分。</returns>
    public static SystemPromptSection Create() =>
        SystemPromptSection.Dynamic("version_info", GetContent);
}
