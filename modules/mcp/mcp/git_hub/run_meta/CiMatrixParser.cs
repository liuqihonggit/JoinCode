namespace McpToolDispatch;

/// <summary>
/// CI yml matrix 解析器 — 从 ci-unit-tests.yml 的 matrix.include 列表提取测试 job 名
/// <para>单一职责:纯文本解析,无外部依赖,可独立测试</para>
/// <para>通用提取:不硬编码字段名,自动检测 job name 中的 ${{ matrix.xxx }} 变量</para>
/// </summary>
internal static class CiMatrixParser {

    /// <summary>
    /// 从 CI yml matrix.include 列表提取所有 entry 的字段字典 — 通用提取,不硬编码字段名
    /// <para>支持 domain/name/csprojs 等任意字段,每条 include entry = 一个字段→值字典</para>
    /// </summary>
    internal static List<Dictionary<string, string>> ExtractMatrixIncludeEntries(string ymlContent) {
        var entries = new List<Dictionary<string, string>>();
        int? includeIndent = null;
        Dictionary<string, string>? currentEntry = null;
        int? entryDashIndent = null;
        foreach (var line in ymlContent.Split('\n')) {
            if (string.IsNullOrWhiteSpace(line)) continue;
            var indent = line.Length - line.TrimStart().Length;
            var trimmed = line.AsSpan().Trim();
            if (trimmed.StartsWith("include:")) { includeIndent = indent; continue; }
            if (includeIndent is null) continue;
            if (trimmed.StartsWith("- ") && indent == includeIndent + 2) {
                if (currentEntry is not null) entries.Add(currentEntry);
                currentEntry = new Dictionary<string, string>();
                entryDashIndent = indent;
                ParseField(trimmed[2..], currentEntry);
            } else if (currentEntry is not null && entryDashIndent is not null &&
                       indent > entryDashIndent && !trimmed.StartsWith("-") && !trimmed.StartsWith("#")) {
                ParseField(trimmed, currentEntry);
            } else if (indent <= includeIndent && !trimmed.StartsWith("-") && !trimmed.StartsWith("#")) {
                if (currentEntry is not null) entries.Add(currentEntry);
                currentEntry = null;
                includeIndent = null;
                entryDashIndent = null;
            }
        }
        if (currentEntry is not null) entries.Add(currentEntry);
        return entries;

        static void ParseField(ReadOnlySpan<char> content, Dictionary<string, string> entry) {
            var colonIdx = content.IndexOf(':');
            if (colonIdx < 0) return;
            var key = content[..colonIdx].Trim().ToString();
            var value = content[(colonIdx + 1)..].Trim().ToString();
            if (!string.IsNullOrEmpty(key) && !string.IsNullOrEmpty(value))
                entry[key] = value;
        }
    }

    /// <summary>
    /// 从 job name 提取所有 matrix 变量名 — `${{ matrix.domain }}` → ["domain"]
    /// </summary>
    internal static List<string> ExtractAllMatrixVariables(string jobName) {
        var vars = new List<string>();
        var searchFrom = 0;
        while (true) {
            var start = jobName.IndexOf("${{ matrix.", searchFrom, StringComparison.Ordinal);
            if (start < 0) break;
            var varStart = start + 11;
            var end = jobName.IndexOf(" }}", varStart, StringComparison.Ordinal);
            if (end < 0) break;
            vars.Add(jobName[varStart..end].Trim());
            searchFrom = end + 3;
        }
        return vars;
    }

    /// <summary>
    /// 从 CI yml 提取 matrix 展开后的所有值 — 自动检测 job name 中的 ${{ matrix.xxx }},从 include 提取对应字段
    /// <para>通用提取:不硬编码 name/domain,自动匹配 job name 中引用的 matrix 变量</para>
    /// </summary>
    /// <param name="ymlContent">CI yml 文件内容</param>
    /// <returns>matrix 展开后的所有值(保序),无 matrix 时返回空列表</returns>
    internal static List<string> ExtractMatrixJobNames(string ymlContent) {
        var entries = ExtractMatrixIncludeEntries(ymlContent);
        if (entries.Count == 0) return [];
        var allJobs = ExtractAllJobNames(ymlContent);
        foreach (var (_, jobName) in allJobs) {
            var matrixVars = ExtractAllMatrixVariables(jobName);
            if (matrixVars.Count == 0) continue;
            var primaryVar = matrixVars[0];
            if (entries[0].ContainsKey(primaryVar))
                return entries
                    .Select(e => e.GetValueOrDefault(primaryVar, ""))
                    .Where(v => !string.IsNullOrEmpty(v))
                    .ToList();
        }
        return [];
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
    /// 从 check 名提取 matrix name — 格式 `{job_id} / {prefix} - {matrix.value}` → `matrix.value`
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
    /// 从 workflow yml 解析所有 check 名 — 通用展开任意 ${{ matrix.xxx }} 变量
    /// <para>自动检测 job name 中的 matrix 变量,从 include entries 提取对应字段值展开</para>
    /// </summary>
    internal static List<string> ExtractAllCheckNames(string ymlContent, string outerJobId) {
        var entries = ExtractMatrixIncludeEntries(ymlContent);
        var allJobs = ExtractAllJobNames(ymlContent);
        var checkNames = new List<string>();
        foreach (var (_, jobName) in allJobs) {
            var matrixVars = ExtractAllMatrixVariables(jobName);
            if (matrixVars.Count > 0 && entries.Count > 0 && entries[0].ContainsKey(matrixVars[0])) {
                foreach (var entry in entries) {
                    var expanded = jobName;
                    foreach (var var in matrixVars) {
                        expanded = expanded.Replace($"${{{{ matrix.{var} }}}}", entry.GetValueOrDefault(var, ""));
                    }
                    checkNames.Add($"{outerJobId} / {expanded}");
                }
            } else {
                checkNames.Add($"{outerJobId} / {jobName}");
            }
        }
        return checkNames;
    }
}
