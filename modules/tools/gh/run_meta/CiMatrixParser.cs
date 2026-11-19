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

            if (IsIncludeHeader(trimmed)) {
                includeIndent = indent;
                continue;
            }
            if (includeIndent is null) continue;

            if (IsNewEntryDash(trimmed, indent, includeIndent.Value)) {
                FlushEntry(entries, ref currentEntry);
                currentEntry = new Dictionary<string, string>();
                entryDashIndent = indent;
                ParseField(trimmed[2..], currentEntry);
            } else if (currentEntry is not null && entryDashIndent is not null && IsEntryContinuation(trimmed, indent, entryDashIndent.Value)) {
                ParseField(trimmed, currentEntry);
            } else if (IsIncludeBlockEnd(trimmed, indent, includeIndent.Value)) {
                FlushEntry(entries, ref currentEntry);
                includeIndent = null;
                entryDashIndent = null;
            }
        }
        FlushEntry(entries, ref currentEntry);
        return entries;

        static bool IsIncludeHeader(ReadOnlySpan<char> trimmed) => trimmed.StartsWith("include:");
        static bool IsNewEntryDash(ReadOnlySpan<char> trimmed, int indent, int includeIndent)
            => trimmed.StartsWith("- ") && indent == includeIndent + 2;
        static bool IsEntryContinuation(ReadOnlySpan<char> trimmed, int indent, int entryDashIndent)
            => indent > entryDashIndent && !trimmed.StartsWith("-") && !trimmed.StartsWith("#");
        static bool IsIncludeBlockEnd(ReadOnlySpan<char> trimmed, int indent, int includeIndent)
            => indent <= includeIndent && !trimmed.StartsWith("-") && !trimmed.StartsWith("#");
        static void FlushEntry(List<Dictionary<string, string>> entries, ref Dictionary<string, string>? current) {
            if (current is not null) entries.Add(current);
            current = null;
        }

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
    /// 从 workflow yml 解析所有 job 的 (job_id, job_name) — 包括 matrix 和独立 job。
    /// <para>无 <c>name:</c> 的 job 用 job_id 兜底（GitHub check 上下文此时就是 job_id，如 ci.yml 的 <c>detect</c>）。</para>
    /// </summary>
    internal static List<(string JobId, string JobName)> ExtractAllJobNames(string ymlContent) {
        var result = new List<(string, string)>();
        var inJobs = false;
        int? jobsIndent = null;
        string? currentJobId = null;
        int? jobIndent = null;
        string? currentName = null;
        foreach (var line in ymlContent.Split('\n')) {
            if (string.IsNullOrWhiteSpace(line)) continue;
            var trimmed = line.AsSpan().Trim();
            var indent = line.Length - line.TrimStart().Length;
            if (trimmed.StartsWith("jobs:")) {
                FlushJob(result, ref currentJobId, ref currentName);
                inJobs = true; jobsIndent = indent; continue;
            }
            if (!inJobs || jobsIndent is null) continue;
            if (indent == jobsIndent + 2 && trimmed.EndsWith(':')) {
                FlushJob(result, ref currentJobId, ref currentName);
                currentJobId = trimmed[..^1].ToString();
                jobIndent = indent;
                continue;
            }
            if (currentJobId is not null && jobIndent is not null && currentName is null &&
                indent == jobIndent + 2 && trimmed.StartsWith("name:")) {
                currentName = trimmed[5..].Trim().ToString();
            }
        }
        FlushJob(result, ref currentJobId, ref currentName);
        return result;

        static void FlushJob(List<(string, string)> jobs, ref string? jobId, ref string? jobName) {
            if (jobId is not null) jobs.Add((jobId, jobName ?? jobId));
            jobId = null;
            jobName = null;
        }
    }

    /// <summary>
    /// 从 workflow yml 解析所有 check 名 — 通用展开任意 ${{ matrix.xxx }} 变量。
    /// <para><paramref name="outerJobId"/> 为空(reusable workflow 自身的 cis.yml / 本地 job)时 check 名为裸名，
    /// 否则为 <c>{outerJobId} / {展开名}</c>。</para>
    /// <para><paramref name="excludeJobIds"/> 用于排除 ci.yml 里 <c>uses:</c> 调用 reusable workflow 的 caller job
    /// （那些 check 由被调 workflow 展开，前缀为 caller job id）。</para>
    /// </summary>
    internal static List<string> ExtractAllCheckNames(string ymlContent, string? outerJobId, IReadOnlySet<string>? excludeJobIds = null) {
        var entries = ExtractMatrixIncludeEntries(ymlContent);
        var allJobs = ExtractAllJobNames(ymlContent);
        var prefix = string.IsNullOrEmpty(outerJobId) ? "" : $"{outerJobId} / ";
        var checkNames = new List<string>();
        foreach (var (jobId, jobName) in allJobs) {
            if (excludeJobIds?.Contains(jobId) == true) continue;
            var matrixVars = ExtractAllMatrixVariables(jobName);
            if (matrixVars.Count > 0 && entries.Count > 0 && entries[0].ContainsKey(matrixVars[0])) {
                foreach (var entry in entries) {
                    var expanded = jobName;
                    foreach (var var in matrixVars) {
                        expanded = expanded.Replace($"${{{{ matrix.{var} }}}}", entry.GetValueOrDefault(var, ""));
                    }
                    checkNames.Add($"{prefix}{expanded}");
                }
            } else {
                checkNames.Add($"{prefix}{jobName}");
            }
        }
        return checkNames;
    }
}
