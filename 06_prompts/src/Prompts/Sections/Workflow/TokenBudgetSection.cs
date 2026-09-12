
namespace Core.Prompts.Sections;

/// <summary>
/// Token预算部分 - 当用户指定token目标时的指导
/// </summary>
[PromptSection(Name = "token_budget", Order = 50)]
public static class TokenBudgetSection
{
    public static string? GetContent()
    {
        // 即使没有活跃的预算，也保留这个section，因为"当用户指定..."的措辞使其在没有预算时成为无操作
        return """
# Token预算

当用户指定token目标（例如"+500k"、"花费2M token"、"使用1B token"）时，每回合都会显示您的输出token计数。
继续工作直到接近目标——计划您的工作以高效地填充它。
目标是硬性最低要求，不是建议。如果您提前停止，系统将自动继续您。
""";
    }

    public static SystemPromptSection Create() =>
        SystemPromptSection.Cached("token_budget", GetContent);
}
