
namespace Core.Prompts.Sections;

[PromptSection(Name = "external_rules", Order = 8)]
public static class ExternalRulesSection
{
    public static string? GetContent()
    {
        var externalRules = PromptConfigSnapshot.Current.ExternalRules;
        if (externalRules is null || externalRules.Count == 0) return null;

        var alwaysApplyRules = new List<ExternalRuleEntry>();
        foreach (var rule in externalRules)
        {
            if (rule.AlwaysApply) alwaysApplyRules.Add(rule);
        }

        if (alwaysApplyRules.Count == 0) return null;

        var sb = new StringBuilder();
        sb.AppendLine("# 外部规则");
        sb.AppendLine();
        sb.AppendLine("以下规则来自外部规则目录（.trae/rules/、.claude/rules/、.codex/rules/ 等），标记为始终应用：");
        sb.AppendLine();

        foreach (var rule in alwaysApplyRules)
        {
            sb.AppendLine($"## {rule.Name}");
            sb.AppendLine();
            sb.AppendLine(rule.Content);
            sb.AppendLine();
        }

        sb.AppendLine("这些规则标记为始终应用，在协助用户时请始终遵循。");

        return sb.ToString();
    }

    public static SystemPromptSection Create() =>
        SystemPromptSection.Cached("external_rules", GetContent);
}
