namespace McpToolDispatch;

/// <summary>
/// CI yml matrix 解析器 — 从 ci-unit-tests.yml 的 matrix.include 列表提取测试 job 名
/// <para>单一职责:纯文本解析,无外部依赖,可独立测试</para>
/// <para>链式:ymlContent → ExtractMatrixJobNames → job 名列表</para>
/// </summary>
internal static class CiMatrixParser {

    /// <summary>
    /// 从 CI yml matrix.include 列表提取 name 字段值 — 用缩进级别精确限定 include 区域
    /// </summary>
    /// <param name="ymlContent">CI yml 文件内容</param>
    /// <returns>matrix include 中所有 - name: 的值(保序)</returns>
    internal static List<string> ExtractMatrixJobNames(string ymlContent) {
        var names = new List<string>();
        int? includeIndent = null;
        foreach (var line in ymlContent.Split('\n')) {
            if (string.IsNullOrWhiteSpace(line)) continue;
            var indent = line.Length - line.TrimStart().Length;
            var trimmed = line.AsSpan().Trim();
            if (trimmed.StartsWith("include:")) { includeIndent = indent; continue; }
            if (includeIndent is null) continue;
            if (trimmed.StartsWith("- name:")) {
                var value = trimmed[7..].Trim().ToString();
                if (!string.IsNullOrEmpty(value)) names.Add(value);
            } else if (indent <= includeIndent && !trimmed.StartsWith("-") && !trimmed.StartsWith("#")) {
                includeIndent = null;
            }
        }
        return names;
    }

    /// <summary>
    /// 从 CI yml 提取 jobs 下的第一个 job_id（如 unit-tests）— 用于筛选对应 workflow 的 check 名
    /// </summary>
    internal static string? ExtractJobId(string ymlContent) {
        var inJobs = false;
        foreach (var line in ymlContent.Split('\n')) {
            if (string.IsNullOrWhiteSpace(line)) continue;
            var trimmed = line.AsSpan().Trim();
            if (trimmed.StartsWith("jobs:")) { inJobs = true; continue; }
            if (!inJobs) continue;
            var indent = line.Length - line.TrimStart().Length;
            if (indent == 2 && trimmed.EndsWith(':')) {
                return trimmed[..^1].ToString();
            }
        }
        return null;
    }

    /// <summary>
    /// 从 check 名提取 matrix name — 格式 `{job_id} / {prefix} - {matrix.name}` → `matrix.name`
    /// <para>非对应 job_id 的 check 返回 null(其他 workflow, 不在审计范围)</para>
    /// </summary>
    internal static string? ExtractMatrixNameFromCheck(string checkName, string jobId) {
        var prefix = $"{jobId} / ";
        if (!checkName.StartsWith(prefix, StringComparison.Ordinal)) return null;
        var afterPrefix = checkName[prefix.Length..];
        var dashIndex = afterPrefix.LastIndexOf(" - ", StringComparison.Ordinal);
        return dashIndex >= 0 ? afterPrefix[(dashIndex + 3)..] : afterPrefix;
    }
}
