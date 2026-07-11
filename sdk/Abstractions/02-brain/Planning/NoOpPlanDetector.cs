namespace JoinCode.Abstractions.Brain.Planning;

public static class NoOpPlanDetector
{
    private static readonly string[] NoOpPhrases =
    [
        "no changes needed",
        "no changes are needed",
        "no changes required",
        "no changes are required",
        "no action needed",
        "no action required",
        "nothing to change",
        "nothing to do",
        "already handled",
        "already implemented",
        "already resolved",
        "[no_changes]",
        "无需改动",
        "无需修改",
        "无需更改",
        "不需要修改",
        "不需要改",
        "不用改",
        "不用修改",
        "不必改动",
        "没有需要修改",
        "已经正确处理",
        "已经实现",
        "已经解决",
    ];

    private static readonly string[] ActionTerms =
    [
        " add ", " add docs", " add tests", " update ", " edit ", " write ",
        " create ", " delete ", " remove ", " patch ", " refactor ", " implement ",
        " run ", " test ", " build ", " fix ",
        "新增", "补充", "更新", "编辑", "写入", "创建", "删除", "移除",
        "运行", "测试", "构建", "修复", "实现", "重构",
    ];

    public static bool IsNoOpPlan(string plan)
    {
        if (string.IsNullOrWhiteSpace(plan)) return false;

        var lower = plan.ToLowerInvariant().Trim();

        if (ContainsActionTerm(lower)) return false;

        foreach (var phrase in NoOpPhrases)
        {
            if (lower.Contains(phrase))
            {
                var negationEn = $"not {phrase}";
                var negationZh = $"不是{phrase}";
                if (!lower.Contains(negationEn) && !lower.Contains(negationZh))
                    return true;
            }
        }

        return false;
    }

    private static bool ContainsActionTerm(string lower)
    {
        var padded = $" {lower} ";
        foreach (var term in ActionTerms)
        {
            if (padded.Contains(term)) return true;
        }
        return false;
    }
}
