
namespace Core.Prompts.Sections;

/// <summary>
/// 额外工作目录部分 - 关于多个工作目录的说明
/// </summary>
[PromptSection(Name = "additional_workdirs", Order = 73, IsDynamic = true)]
public static class AdditionalWorkdirsSection {
    /// <summary>
    /// 获取额外工作目录部分的内容。
    /// </summary>
    /// <returns>额外工作目录说明文本；无额外目录时返回 <c>null</c>。</returns>
    public static string? GetContent() {
        var dirs = PromptConfigSnapshot.Current.AdditionalWorkdirs.ToList();
        if (dirs.Count == 0) {
            return null;
        }

        var result = new System.Text.StringBuilder();
        result.AppendLine("# 额外工作目录");
        result.AppendLine("以下额外工作目录也可用：");
        foreach (var dir in dirs) {
            result.AppendLine($" - {dir}");
        }

        return result.ToString().TrimEnd();
    }

    /// <summary>
    /// 创建额外工作目录提示词部分。
    /// </summary>
    /// <returns>动态系统提示词部分。</returns>
    public static SystemPromptSection Create() =>
        SystemPromptSection.Dynamic("additional_workdirs", GetContent);
}