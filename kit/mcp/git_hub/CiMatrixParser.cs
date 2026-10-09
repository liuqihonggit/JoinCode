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

    /// <summary>
    /// 从 ci.yml 解析 reusable workflow 调用 — (job_id, workflow_path) 列表
    /// <para>格式: jobs → job_id: → uses: ./.github/workflows/xxx.yml</para>
    /// </summary>
    internal static List<(string JobId, string WorkflowPath)> ExtractWorkflowUses(string ymlContent) {
        var result = new List<(string, string)>();
        var inJobs = false;
        int? jobsIndent = null;
        string? currentJobId = null;
        int? jobIndent = null;
        foreach (var line in ymlContent.Split('\n')) {
            if (string.IsNullOrWhiteSpace(line)) continue;
            var trimmed = line.AsSpan().Trim();
            var indent = line.Length - line.TrimStart().Length;
            if (trimmed.StartsWith("jobs:")) { inJobs = true; jobsIndent = indent; continue; }
            if (!inJobs || jobsIndent is null) continue;
            if (indent == jobsIndent + 2 && trimmed.EndsWith(':')) {
                currentJobId = trimmed[..^1].ToString();
                jobIndent = indent;
                continue;
            }
            if (currentJobId is not null && jobIndent is not null &&
                indent == jobIndent + 2 && trimmed.StartsWith("uses:")) {
                var path = trimmed[5..].Trim().ToString();
                result.Add((currentJobId, path));
            }
        }
        return result;
    }

    /// <summary>
    /// 从 workflow yml 解析所有 job 的 (job_id, job_name) — 包括 matrix 和独立 job
    /// </summary>
    internal static List<(string JobId, string JobName)> ExtractAllJobNames(string ymlContent) {
        var result = new List<(string, string)>();
        var inJobs = false;
        int? jobsIndent = null;
        string? currentJobId = null;
        int? jobIndent = null;
        foreach (var line in ymlContent.Split('\n')) {
            if (string.IsNullOrWhiteSpace(line)) continue;
            var trimmed = line.AsSpan().Trim();
            var indent = line.Length - line.TrimStart().Length;
            if (trimmed.StartsWith("jobs:")) { inJobs = true; jobsIndent = indent; continue; }
            if (!inJobs || jobsIndent is null) continue;
            if (indent == jobsIndent + 2 && trimmed.EndsWith(':')) {
                currentJobId = trimmed[..^1].ToString();
                jobIndent = indent;
                continue;
            }
            if (currentJobId is not null && jobIndent is not null &&
                indent == jobIndent + 2 && trimmed.StartsWith("name:")) {
                var name = trimmed[5..].Trim().ToString();
                result.Add((currentJobId, name));
            }
        }
        return result;
    }

    /// <summary>
    /// 从 workflow yml 解析所有 check 名 — `{outerJobId} / {job_name}` 或 `{outerJobId} / {prefix}{matrix.name}`
    /// </summary>
    internal static List<string> ExtractAllCheckNames(string ymlContent, string outerJobId) {
        var checkNames = new List<string>();
        var matrixNames = ExtractMatrixJobNames(ymlContent);
        var allJobs = ExtractAllJobNames(ymlContent);
        foreach (var (_, jobName) in allJobs) {
            if (jobName.Contains("${{ matrix.name }}")) {
                var prefix = jobName.Replace("${{ matrix.name }}", "");
                foreach (var matrixName in matrixNames)
                    checkNames.Add($"{outerJobId} / {prefix}{matrixName}");
            } else {
                checkNames.Add($"{outerJobId} / {jobName}");
            }
        }
        return checkNames;
    }
}
