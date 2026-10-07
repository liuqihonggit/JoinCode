namespace McpToolDispatch;

/// <summary>
/// GitHub Org/SSH Key/GPG Key 工具 — 直调 GitHub REST API
/// </summary>
public partial class GitHubToolHandlers {
    /// <summary>
    /// 列出当前用户的组织 — 调 GET /user/orgs
    /// </summary>
    [McpTool(GitHubToolNameEnumConstants.GhOrgList, "列出当前用户的组织", "github", ConcurrencySafe = true)]
    public async Task<ToolResult> GhOrgListAsync(
        [McpToolParameter("数量限制(默认 30)", Required = false)] int? limit = null,
        [McpToolParameter("JSON 字段过滤(可选,逗号分隔,如 login,description)", Required = false)] string? json_fields = null,
        [McpToolParameter("输出档位(0=gh风格[默认] 1=精简JSON 2=完整JSON)", Required = false)] int? verbosity = null,
        CancellationToken cancellationToken = default) {
        if (_apiClient is null) return ApiClientNotConfigured();
        var query = new Dictionary<string, string> { ["per_page"] = (limit ?? 30).ToString() };
        var result = await _apiClient.SendAsync(HttpMethod.Get, "user/orgs", query: query, ct: cancellationToken).ConfigureAwait(false);
        if (!result.Success) return Fail(result.Error);
        return Ok(FormatGhOutput(result.Body, verbosity, json_fields, SummarizeOrgList, "login,description"));
    }

    /// <summary>
    /// 精简组织列表 — 表格格式(login, description)
    /// </summary>
    private static string SummarizeOrgList(string json) {
        try {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Array) return json;
            var sb = new StringBuilder(128);
            sb.AppendLine("组织\t描述");
            foreach (var org in doc.RootElement.EnumerateArray()) {
                var login = org.TryGetProperty(GitHubJsonFields.Login, out var l) ? l.GetString() ?? "" : "";
                var desc = org.TryGetProperty(GitHubJsonFields.Description, out var d) ? (d.ValueKind == JsonValueKind.Null ? "" : d.GetString() ?? "") : "";
                sb.AppendLine($"{login}\t{desc}");
            }
            return sb.ToString();
        } catch { return json; }
    }

    // === SSH Key ===

    /// <summary>
    /// 列出 SSH Key — 调 GET /user/keys
    /// </summary>
    [McpTool(GitHubToolNameEnumConstants.GhSshKeyList, "列出 SSH Key", "github", ConcurrencySafe = true)]
    public async Task<ToolResult> GhSshKeyListAsync(
        [McpToolParameter("JSON 字段过滤(可选,逗号分隔,如 id,title)", Required = false)] string? json_fields = null,
        [McpToolParameter("输出档位(0=gh风格[默认] 1=精简JSON 2=完整JSON)", Required = false)] int? verbosity = null,
        CancellationToken cancellationToken = default) {
        if (_apiClient is null) return ApiClientNotConfigured();
        var result = await _apiClient.SendAsync(HttpMethod.Get, "user/keys", ct: cancellationToken).ConfigureAwait(false);
        if (!result.Success) return Fail(result.Error);
        return Ok(FormatGhOutput(result.Body, verbosity, json_fields, SummarizeSshKeyList, "id,title"));
    }

    /// <summary>
    /// 添加 SSH Key — 调 POST /user/keys
    /// </summary>
    [McpTool(GitHubToolNameEnumConstants.GhSshKeyAdd, "添加 SSH Key", "github")]
    public async Task<ToolResult> GhSshKeyAddAsync(
        [McpToolParameter("Key 标题", Required = true)] string title,
        [McpToolParameter("SSH public key 内容", Required = true)] string key,
        CancellationToken cancellationToken = default) {
        if (_apiClient is null) return ApiClientNotConfigured();
        var jsonBody = JsonSerializer.Serialize(new SshKeyAddRequest { Title = title, Key = key }, GitHubApiJsonContext.Safe.SshKeyAddRequest);
        var result = await _apiClient.SendAsync(HttpMethod.Post, "user/keys", jsonBody, ct: cancellationToken).ConfigureAwait(false);
        return result.Success ? OkBrief(result.Body, "SSH Key 添加成功") : Fail(result.Error);
    }

    /// <summary>
    /// 删除 SSH Key — 调 DELETE /user/keys/{id}
    /// </summary>
    [McpTool(GitHubToolNameEnumConstants.GhSshKeyDelete, "删除 SSH Key", "github")]
    public async Task<ToolResult> GhSshKeyDeleteAsync(
        [McpToolParameter("Key ID", Required = true)] int key_id,
        CancellationToken cancellationToken = default) {
        if (_apiClient is null) return ApiClientNotConfigured();
        var result = await _apiClient.SendAsync(HttpMethod.Delete, $"user/keys/{key_id}", ct: cancellationToken).ConfigureAwait(false);
        return result.Success ? Ok($"已删除 SSH Key {key_id}") : Fail(result.Error);
    }

    /// <summary>
    /// 精简 SSH Key 列表 — 表格格式(id, title)
    /// </summary>
    private static string SummarizeSshKeyList(string json) {
        try {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Array) return json;
            var sb = new StringBuilder(128);
            sb.AppendLine("ID\t标题");
            foreach (var key in doc.RootElement.EnumerateArray()) {
                var id = key.TryGetProperty(GitHubJsonFields.Id, out var i) ? i.GetInt32() : 0;
                var title = key.TryGetProperty(GitHubJsonFields.Title, out var t) ? t.GetString() ?? "" : "";
                sb.AppendLine($"{id}\t{title}");
            }
            return sb.ToString();
        } catch { return json; }
    }

    // === GPG Key ===

    /// <summary>
    /// 列出 GPG Key — 调 GET /user/gpg_keys
    /// </summary>
    [McpTool(GitHubToolNameEnumConstants.GhGpgKeyList, "列出 GPG Key", "github", ConcurrencySafe = true)]
    public async Task<ToolResult> GhGpgKeyListAsync(
        [McpToolParameter("JSON 字段过滤(可选,逗号分隔,如 id,key_id)", Required = false)] string? json_fields = null,
        [McpToolParameter("输出档位(0=gh风格[默认] 1=精简JSON 2=完整JSON)", Required = false)] int? verbosity = null,
        CancellationToken cancellationToken = default) {
        if (_apiClient is null) return ApiClientNotConfigured();
        var result = await _apiClient.SendAsync(HttpMethod.Get, "user/gpg_keys", ct: cancellationToken).ConfigureAwait(false);
        if (!result.Success) return Fail(result.Error);
        return Ok(FormatGhOutput(result.Body, verbosity, json_fields, SummarizeGpgKeyList, "id,key_id,public_key"));
    }

    /// <summary>
    /// 添加 GPG Key — 调 POST /user/gpg_keys
    /// </summary>
    [McpTool(GitHubToolNameEnumConstants.GhGpgKeyAdd, "添加 GPG Key", "github")]
    public async Task<ToolResult> GhGpgKeyAddAsync(
        [McpToolParameter("ASCII armored GPG key 内容", Required = true)] string key,
        CancellationToken cancellationToken = default) {
        if (_apiClient is null) return ApiClientNotConfigured();
        var jsonBody = JsonSerializer.Serialize(new GpgKeyAddRequest { ArmoredPublicKey = key }, GitHubApiJsonContext.Safe.GpgKeyAddRequest);
        var result = await _apiClient.SendAsync(HttpMethod.Post, "user/gpg_keys", jsonBody, ct: cancellationToken).ConfigureAwait(false);
        return result.Success ? OkBrief(result.Body, "GPG Key 添加成功") : Fail(result.Error);
    }

    /// <summary>
    /// 删除 GPG Key — 调 DELETE /user/gpg_keys/{id}
    /// </summary>
    [McpTool(GitHubToolNameEnumConstants.GhGpgKeyDelete, "删除 GPG Key", "github")]
    public async Task<ToolResult> GhGpgKeyDeleteAsync(
        [McpToolParameter("Key ID", Required = true)] int key_id,
        CancellationToken cancellationToken = default) {
        if (_apiClient is null) return ApiClientNotConfigured();
        var result = await _apiClient.SendAsync(HttpMethod.Delete, $"user/gpg_keys/{key_id}", ct: cancellationToken).ConfigureAwait(false);
        return result.Success ? Ok($"已删除 GPG Key {key_id}") : Fail(result.Error);
    }

    /// <summary>
    /// 精简 GPG Key 列表 — 表格格式(id, key_id, can_sign)
    /// </summary>
    private static string SummarizeGpgKeyList(string json) {
        try {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Array) return json;
            var sb = new StringBuilder(128);
            sb.AppendLine("ID\tKey ID\t可签名");
            foreach (var key in doc.RootElement.EnumerateArray()) {
                var id = key.TryGetProperty(GitHubJsonFields.Id, out var i) ? i.GetInt32() : 0;
                var keyId = key.TryGetProperty("key_id", out var k) ? k.GetString() ?? "" : "";
                var canSign = key.TryGetProperty("can_sign", out var cs) && cs.GetBoolean();
                sb.AppendLine($"{id}\t{keyId}\t{canSign}");
            }
            return sb.ToString();
        } catch { return json; }
    }
}
