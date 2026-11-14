namespace Core.Context;

/// <summary>
/// 工具调用统一修复入口 — 协议层和 Fallback 层共用
/// 确保无论工具调用来源（协议层 AllToolCalls / XML 解析 / DSML 解析），
/// 都经过相同的修复管道：工具名归一化 + 参数 JSON 修复
/// </summary>
internal static class ToolCallRepairHelper {
    /// <summary>
    /// 修复单个工具调用：工具名归一化 + 参数 JSON 修复
    /// </summary>
    public static ToolCallEntry Repair(ToolCallEntry entry, ILogger? logger = null) {
        var repairedName = LlmJsonHelper.RepairToolName(entry.Name, logger);
        var jsonRepair = LlmJsonHelper.RepairJson(entry.Arguments, logger);
        if (!string.Equals(entry.Name, repairedName, StringComparison.Ordinal))
            Diag.WriteLine($"[XML-REPAIR] 工具名修复: '{entry.Name}' → '{repairedName}'");
        if (jsonRepair.Success && !string.Equals(entry.Arguments, jsonRepair.RepairedJson, StringComparison.Ordinal))
            Diag.WriteLine($"[XML-REPAIR] 参数JSON修复: {jsonRepair.RepairHint}");
        return entry with {
            Name = repairedName,
            Arguments = jsonRepair.Success ? jsonRepair.RepairedJson : entry.Arguments
        };
    }

    /// <summary>
    /// 批量修复工具调用列表
    /// </summary>
    public static IReadOnlyList<ToolCallEntry> RepairAll(IReadOnlyList<ToolCallEntry> entries, ILogger? logger = null) {
        if (entries.Count == 0) return entries;
        var result = new List<ToolCallEntry>(entries.Count);
        foreach (var entry in entries)
            result.Add(Repair(entry, logger));
        return result;
    }
}
