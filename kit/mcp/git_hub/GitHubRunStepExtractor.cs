namespace McpToolDispatch;

/// <summary>
/// GitHub Run 步骤名提取与日志累积工具 — 从日志行/zip entry 名提取步骤名,累积日志到摘要
/// </summary>
internal static class GitHubRunStepExtractor {
    /// <summary>
    /// 从日志行提取步骤名 — REST API 格式 [entry.Name] line 优先,回退 gh CLI TSV 格式
    /// </summary>
    public static string? TryExtractStepName(string line) {
        // REST API 格式: [entry.Name] logLine — entry.Name 是 zip 文件名(如 0_Checkout.txt)
        if (line.StartsWith('[')) {
            var closeIdx = line.IndexOf(']');
            if (closeIdx > 1) {
                return ExtractStepNameFromEntryName(line[1..closeIdx]);
            }
        }

        // gh CLI TSV 格式: 列1\t步骤名\t...
        var tabIdx = line.IndexOf('\t');
        if (tabIdx < 0) return null;
        var secondTabIdx = line.IndexOf('\t', tabIdx + 1);
        return secondTabIdx > tabIdx ? line[(tabIdx + 1)..secondTabIdx] : line[(tabIdx + 1)..];
    }

    /// <summary>
    /// 从 zip entry 名提取步骤名 — "0_Checkout.txt" → "Checkout", "Build.txt" → "Build", "0_build/1_Test.txt" → "Test"
    /// </summary>
    public static string ExtractStepNameFromEntryName(string entryName) {
        var name = entryName;
        var slashIdx = name.LastIndexOf('/');
        if (slashIdx >= 0) name = name[(slashIdx + 1)..];
        var dotIdx = name.LastIndexOf('.');
        if (dotIdx > 0) name = name[..dotIdx];
        var underscoreIdx = name.IndexOf('_');
        if (underscoreIdx > 0 && int.TryParse(name.AsSpan(0, underscoreIdx), out _))
            name = name[(underscoreIdx + 1)..];
        return name.Length == 0 ? entryName : name;
    }

    /// <summary>
    /// 累积一行日志到 summary 和 sectionContents — 用指定 stepName
    /// </summary>
    public static void Accumulate(string line, string stepName, RunLogSummary summary, Dictionary<string, Dictionary<string, List<string>>> sectionContents) {
        var sectionType = RunLogCache.ParseSectionType(line);

        summary.StepLineCounts[stepName] = summary.StepLineCounts.GetValueOrDefault(stepName) + 1;

        if (!summary.SectionCounts.TryGetValue(stepName, out var secCounts)) {
            secCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            summary.SectionCounts[stepName] = secCounts;
        }
        secCounts[sectionType] = secCounts.GetValueOrDefault(sectionType) + 1;

        if (!sectionContents.TryGetValue(stepName, out var stepSecs)) {
            stepSecs = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            sectionContents[stepName] = stepSecs;
        }
        if (!stepSecs.TryGetValue(sectionType, out var secLines)) {
            secLines = new List<string>();
            stepSecs[sectionType] = secLines;
        }
        secLines.Add(GitHubRunLogText.StripLogTimestamp(line));
    }
}