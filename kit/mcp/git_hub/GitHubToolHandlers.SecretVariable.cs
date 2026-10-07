namespace McpToolDispatch;

/// <summary>
/// GitHub Secret/Variable 工具 — 管理 Actions secrets 和 variables
/// <para>secret set 涉及加密(需获取仓库公钥+加密 secret 值)，简化提示用系统 gh CLI</para>
/// </summary>
public partial class GitHubToolHandlers {
    /// <summary>
    /// 列出仓库 Secret — 调 GET /actions/secrets
    /// </summary>
    [McpTool(GitHubToolNameEnumConstants.GhSecretList, "列出仓库 Secret", "github", ConcurrencySafe = true)]
    public async Task<ToolResult> GhSecretListAsync(
        [McpToolParameter("仓库(可选,默认当前仓库)", Required = false)] string? repo = null,
        [McpToolParameter("JSON 字段过滤(可选,逗号分隔,如 name,created_at)", Required = false)] string? json_fields = null,
        [McpToolParameter("工作目录(可选)", Required = false)] string? working_dir = null,
        CancellationToken cancellationToken = default)
        => await ExecuteGhAsync(repo, working_dir, cancellationToken, async (client, owner, repoName) => {
            var result = await client.SendAsync(HttpMethod.Get, $"repos/{owner}/{repoName}/actions/secrets", ct: cancellationToken).ConfigureAwait(false);
            if (!result.Success) return Fail(result.Error);
            return Ok(!string.IsNullOrEmpty(json_fields) ? FilterJsonFields(result.Body, json_fields) : SummarizeSecretList(result.Body));
        }).ConfigureAwait(false);

    /// <summary>
    /// 精简 Secret 列表 — 表格格式(name, created_at)
    /// </summary>
    private static string SummarizeSecretList(string json) {
        try {
            using var doc = JsonDocument.Parse(json);
            var sb = new StringBuilder(128);
            var totalCount = doc.RootElement.TryGetProperty("total_count", out var tc) ? tc.GetInt32() : 0;
            sb.AppendLine($"共 {totalCount} 个 secret");
            if (doc.RootElement.TryGetProperty("secrets", out var secrets) && secrets.ValueKind == JsonValueKind.Array) {
                sb.AppendLine("名称\t创建时间");
                foreach (var s in secrets.EnumerateArray()) {
                    var name = s.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
                    var created = s.TryGetProperty("created_at", out var c) ? c.GetString() ?? "" : "";
                    sb.AppendLine($"{name}\t{created}");
                }
            }
            return sb.ToString();
        } catch { return json; }
    }

    /// <summary>
    /// 设置 Secret — 获取仓库公钥 + libsodium sealed box 加密 + PUT API
    /// </summary>
    [McpTool(GitHubToolNameEnumConstants.GhSecretSet, "设置 Secret(加密后 PUT API)", "github")]
    public async Task<ToolResult> GhSecretSetAsync(
        [McpToolParameter("Secret 名称", Required = true)] string name,
        [McpToolParameter("Secret 值", Required = false)] string? body = null,
        [McpToolParameter("仓库(可选,默认当前仓库)", Required = false)] string? repo = null,
        [McpToolParameter("环境名(可选,设置环境 Secret)", Required = false)] string? env = null,
        [McpToolParameter("工作目录(可选)", Required = false)] string? working_dir = null,
        CancellationToken cancellationToken = default)
        => await ExecuteGhAsync(repo, working_dir, cancellationToken, async (client, owner, repoName) => {
            if (string.IsNullOrEmpty(body)) return Fail("Secret 值不能为空");
            var publicKeyPath = string.IsNullOrEmpty(env)
                ? $"repos/{owner}/{repoName}/actions/secrets/public-key"
                : $"repos/{owner}/{repoName}/environments/{env}/secrets/public-key";
            var keyResponse = await client.SendAsync(HttpMethod.Get, publicKeyPath, ct: cancellationToken).ConfigureAwait(false);
            if (!keyResponse.Success) return Fail($"获取公钥失败: {keyResponse.Error}");
            string keyId;
            byte[] publicKeyBytes;
            try {
                using var doc = JsonDocument.Parse(keyResponse.Body);
                keyId = doc.RootElement.TryGetProperty("key_id", out var k) ? k.GetString() ?? "" : "";
                var keyB64 = doc.RootElement.TryGetProperty("key", out var kk) ? kk.GetString() ?? "" : "";
                publicKeyBytes = Convert.FromBase64String(keyB64);
            } catch { return Fail($"公钥响应解析失败: {keyResponse.Body}"); }
            var plaintext = Encoding.UTF8.GetBytes(body);
            var sealedBox = GitHubSecretEncryptor.Seal(publicKeyBytes, plaintext);
            var encryptedValue = Convert.ToBase64String(sealedBox);
            var secretPath = string.IsNullOrEmpty(env)
                ? $"repos/{owner}/{repoName}/actions/secrets/{name}"
                : $"repos/{owner}/{repoName}/environments/{env}/secrets/{name}";
            var secretBody = JsonSerializer.Serialize(new SecretSetRequest {
                EncryptedValue = encryptedValue,
                KeyId = keyId
            }, GitHubApiJsonContext.Safe.SecretSetRequest);
            var result = await client.SendAsync(HttpMethod.Put, secretPath, secretBody, ct: cancellationToken).ConfigureAwait(false);
            return result.Success ? Ok($"已设置 Secret {name}") : Fail(result.Error);
        }).ConfigureAwait(false);

    /// <summary>
    /// 删除 Secret — 调 DELETE /actions/secrets/{name}
    /// </summary>
    [McpTool(GitHubToolNameEnumConstants.GhSecretDelete, "删除 Secret", "github")]
    public async Task<ToolResult> GhSecretDeleteAsync(
        [McpToolParameter("Secret 名称", Required = true)] string name,
        [McpToolParameter("仓库(可选,默认当前仓库)", Required = false)] string? repo = null,
        [McpToolParameter("工作目录(可选)", Required = false)] string? working_dir = null,
        CancellationToken cancellationToken = default)
        => await ExecuteGhAsync(repo, working_dir, cancellationToken, async (client, owner, repoName) => {
            var result = await client.SendAsync(HttpMethod.Delete, $"repos/{owner}/{repoName}/actions/secrets/{name}", ct: cancellationToken).ConfigureAwait(false);
            return result.Success ? Ok($"已删除 Secret {name}") : Fail(result.Error);
        }).ConfigureAwait(false);

    // === Variable ===

    /// <summary>
    /// 列出仓库 Variable — 调 GET /actions/variables
    /// </summary>
    [McpTool(GitHubToolNameEnumConstants.GhVariableList, "列出仓库 Variable", "github", ConcurrencySafe = true)]
    public async Task<ToolResult> GhVariableListAsync(
        [McpToolParameter("仓库(可选,默认当前仓库)", Required = false)] string? repo = null,
        [McpToolParameter("JSON 字段过滤(可选,逗号分隔,如 name,value)", Required = false)] string? json_fields = null,
        [McpToolParameter("工作目录(可选)", Required = false)] string? working_dir = null,
        CancellationToken cancellationToken = default)
        => await ExecuteGhAsync(repo, working_dir, cancellationToken, async (client, owner, repoName) => {
            var result = await client.SendAsync(HttpMethod.Get, $"repos/{owner}/{repoName}/actions/variables", ct: cancellationToken).ConfigureAwait(false);
            if (!result.Success) return Fail(result.Error);
            return Ok(!string.IsNullOrEmpty(json_fields) ? FilterJsonFields(result.Body, json_fields) : SummarizeVariableList(result.Body));
        }).ConfigureAwait(false);

    /// <summary>
    /// 获取单个 Variable — 调 GET /actions/variables/{name}
    /// </summary>
    [McpTool(GitHubToolNameEnumConstants.GhVariableGet, "获取 Variable 值", "github", ConcurrencySafe = true)]
    public async Task<ToolResult> GhVariableGetAsync(
        [McpToolParameter("Variable 名称", Required = true)] string name,
        [McpToolParameter("仓库(可选,默认当前仓库)", Required = false)] string? repo = null,
        [McpToolParameter("工作目录(可选)", Required = false)] string? working_dir = null,
        CancellationToken cancellationToken = default)
        => await ExecuteGhAsync(repo, working_dir, cancellationToken, async (client, owner, repoName) => {
            var result = await client.SendAsync(HttpMethod.Get, $"repos/{owner}/{repoName}/actions/variables/{name}", ct: cancellationToken).ConfigureAwait(false);
            if (!result.Success) return Fail(result.Error);
            try {
                using var doc = JsonDocument.Parse(result.Body);
                var value = doc.RootElement.TryGetProperty("value", out var v) ? v.GetString() ?? "" : "";
                return Ok(value);
            } catch { return Ok(result.Body); }
        }).ConfigureAwait(false);

    /// <summary>
    /// 设置 Variable — 调 PUT /actions/variables/{name}（已存在）或 POST /actions/variables（新建）
    /// </summary>
    [McpTool(GitHubToolNameEnumConstants.GhVariableSet, "设置 Variable", "github")]
    public async Task<ToolResult> GhVariableSetAsync(
        [McpToolParameter("Variable 名称", Required = true)] string name,
        [McpToolParameter("Variable 值", Required = true)] string body,
        [McpToolParameter("仓库(可选,默认当前仓库)", Required = false)] string? repo = null,
        [McpToolParameter("工作目录(可选)", Required = false)] string? working_dir = null,
        CancellationToken cancellationToken = default)
        => await ExecuteGhAsync(repo, working_dir, cancellationToken, async (client, owner, repoName) => {
            var jsonBody = JsonSerializer.Serialize(new VariableSetRequest { Name = name, Value = body }, GitHubApiJsonContext.Safe.VariableSetRequest);
            var putResult = await client.SendAsync(HttpMethod.Put, $"repos/{owner}/{repoName}/actions/variables/{name}", jsonBody, ct: cancellationToken).ConfigureAwait(false);
            if (putResult.Success) return Ok($"已更新 Variable {name}");
            var postResult = await client.SendAsync(HttpMethod.Post, $"repos/{owner}/{repoName}/actions/variables", jsonBody, ct: cancellationToken).ConfigureAwait(false);
            return postResult.Success ? Ok($"已创建 Variable {name}") : Fail(postResult.Error);
        }).ConfigureAwait(false);

    /// <summary>
    /// 删除 Variable — 调 DELETE /actions/variables/{name}
    /// </summary>
    [McpTool(GitHubToolNameEnumConstants.GhVariableDelete, "删除 Variable", "github")]
    public async Task<ToolResult> GhVariableDeleteAsync(
        [McpToolParameter("Variable 名称", Required = true)] string name,
        [McpToolParameter("仓库(可选,默认当前仓库)", Required = false)] string? repo = null,
        [McpToolParameter("工作目录(可选)", Required = false)] string? working_dir = null,
        CancellationToken cancellationToken = default)
        => await ExecuteGhAsync(repo, working_dir, cancellationToken, async (client, owner, repoName) => {
            var result = await client.SendAsync(HttpMethod.Delete, $"repos/{owner}/{repoName}/actions/variables/{name}", ct: cancellationToken).ConfigureAwait(false);
            return result.Success ? Ok($"已删除 Variable {name}") : Fail(result.Error);
        }).ConfigureAwait(false);

    /// <summary>
    /// 精简 Variable 列表 — 表格格式(name, value, updated_at)
    /// </summary>
    private static string SummarizeVariableList(string json) {
        try {
            using var doc = JsonDocument.Parse(json);
            var sb = new StringBuilder(128);
            var totalCount = doc.RootElement.TryGetProperty("total_count", out var tc) ? tc.GetInt32() : 0;
            sb.AppendLine($"共 {totalCount} 个 variable");
            if (doc.RootElement.TryGetProperty("variables", out var variables) && variables.ValueKind == JsonValueKind.Array) {
                sb.AppendLine("名称\t值\t更新时间");
                foreach (var v in variables.EnumerateArray()) {
                    var name = v.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
                    var value = v.TryGetProperty("value", out var val) ? val.GetString() ?? "" : "";
                    var updated = v.TryGetProperty("updated_at", out var u) ? u.GetString() ?? "" : "";
                    sb.AppendLine($"{name}\t{value}\t{updated}");
                }
            }
            return sb.ToString();
        } catch { return json; }
    }
}
