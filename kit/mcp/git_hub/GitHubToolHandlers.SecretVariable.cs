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
        [McpToolParameter("输出档位(0=gh风格[默认] 1=精简JSON 2=完整JSON)", Required = false)] int? verbosity = null,
        [McpToolParameter("工作目录(可选)", Required = false)] string? working_dir = null,
        CancellationToken cancellationToken = default)
        => await ExecuteGhAsync(repo, working_dir, cancellationToken, async (client, owner, repoName) => {
            var result = await client.SendAsync(HttpMethod.Get, $"repos/{owner}/{repoName}/actions/secrets", ct: cancellationToken).ConfigureAwait(false);
            if (!result.Success) return Fail(result.Error);
            return Ok(FormatGhOutput(result.Body, verbosity, json_fields, SummarizeSecretList, "name,created_at"));
        }).ConfigureAwait(false);

    /// <summary>
    /// 精简 Secret 列表 — 表格格式(name, created_at)
    /// </summary>
    private static string SummarizeSecretList(string json) {
        try {
            var resp = JsonSerializer.Deserialize(json, GitHubApiJsonContext.Safe.SecretListResponse);
            if (resp is null) return json;
            var sb = new StringBuilder(128);
            sb.AppendLine($"共 {resp.TotalCount} 个 secret");
            sb.AppendLine("名称\t创建时间");
            foreach (var s in resp.Secrets) sb.AppendLine($"{s.Name}\t{s.CreatedAt}");
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
                var pubKey = JsonSerializer.Deserialize(keyResponse.Body, GitHubApiJsonContext.Safe.PublicKeyResponse);
                if (pubKey is null) return Fail($"公钥响应解析失败: {keyResponse.Body}");
                keyId = pubKey.KeyId;
                publicKeyBytes = Convert.FromBase64String(pubKey.Key);
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
        [McpToolParameter("输出档位(0=gh风格[默认] 1=精简JSON 2=完整JSON)", Required = false)] int? verbosity = null,
        [McpToolParameter("工作目录(可选)", Required = false)] string? working_dir = null,
        CancellationToken cancellationToken = default)
        => await ExecuteGhAsync(repo, working_dir, cancellationToken, async (client, owner, repoName) => {
            var result = await client.SendAsync(HttpMethod.Get, $"repos/{owner}/{repoName}/actions/variables", ct: cancellationToken).ConfigureAwait(false);
            if (!result.Success) return Fail(result.Error);
            return Ok(FormatGhOutput(result.Body, verbosity, json_fields, SummarizeVariableList, "name,value,created_at,updated_at"));
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
                var variable = JsonSerializer.Deserialize(result.Body, GitHubApiJsonContext.Safe.VariableItemResponse);
                return Ok(variable?.Value ?? "");
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
            var resp = JsonSerializer.Deserialize(json, GitHubApiJsonContext.Safe.VariableListResponse);
            if (resp is null) return json;
            var sb = new StringBuilder(128);
            sb.AppendLine($"共 {resp.TotalCount} 个 variable");
            sb.AppendLine("名称\t值\t更新时间");
            foreach (var v in resp.Variables) sb.AppendLine($"{v.Name}\t{v.Value}\t{v.UpdatedAt}");
            return sb.ToString();
        } catch { return json; }
    }
}
