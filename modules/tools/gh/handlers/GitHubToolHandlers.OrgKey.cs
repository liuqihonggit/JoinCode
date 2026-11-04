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
        [McpToolParameter(WellKnownParam.Limit)] int? limit = null,
        [McpToolOptions] GitHubCommonOptions? common = null,
        CancellationToken cancellationToken = default) {
        if (_apiClient is null) return ApiClientNotConfigured();
        var query = new Dictionary<string, string> { ["per_page"] = (limit ?? 30).ToString() };
        var result = await _apiClient.SendAsync(HttpMethod.Get, "user/orgs", query: query, ct: cancellationToken).ConfigureAwait(false);
        if (!result.Success) return Fail(result.Error);
        return Ok(FormatGhOutput(result.Body, common?.Verbosity, common?.JsonFields, SummarizeOrgList, "login,description", common?.Jq, common?.SkipLines, common?.StartLine, "gh_org_list"));
    }

    /// <summary>
    /// 精简组织列表 — 表格格式(login, description)
    /// </summary>
    private static string SummarizeOrgList(string json) {
        try {
            var orgs = JsonSerializer.Deserialize(json, GitHubApiJsonContext.Safe.ListOrgResponse);
            if (orgs is null) return json;
            var sb = new StringBuilder(128);
            sb.AppendLine("组织\t描述");
            foreach (var org in orgs) sb.AppendLine($"{org.Login}\t{org.Description}");
            return sb.ToString();
        } catch { return json; }
    }

    /// <summary>
    /// 查看组织详情 — 调 GET /orgs/{org}
    /// </summary>
    [McpTool(GitHubToolNameEnumConstants.GhOrgView, "查看组织详情", "github", ConcurrencySafe = true)]
    public async Task<ToolResult> GhOrgViewAsync(
        [McpToolParameter("组织名(如 JoinCode)", Required = true)] string org,
        [McpToolOptions] GitHubCommonOptions? common = null,
        CancellationToken cancellationToken = default) {
        if (_apiClient is null) return ApiClientNotConfigured();
        var result = await _apiClient.SendAsync(HttpMethod.Get, $"orgs/{org}", ct: cancellationToken).ConfigureAwait(false);
        if (!result.Success) return Fail(EnrichApiError(result.Error, "gh_org_view", $"orgs/{org}"));
        return Ok(FormatGhOutput(result.Body, common?.Verbosity, common?.JsonFields, SummarizeOrgView, "login,name,description,public_repos,followers", common?.Jq, common?.SkipLines, common?.StartLine, "gh_org_view"));
    }

    /// <summary>
    /// 组织详情 — 人类可读格式
    /// </summary>
    private static string SummarizeOrgView(string json) {
        try {
            var org = JsonSerializer.Deserialize(json, GitHubApiJsonContext.Safe.OrgResponse);
            if (org is null) return json;
            var sb = new StringBuilder(256);
            sb.AppendLine($"组织: {org.Login}");
            if (!string.IsNullOrEmpty(org.Name)) sb.AppendLine($"全称: {org.Name}");
            if (!string.IsNullOrEmpty(org.Description)) sb.AppendLine($"描述: {org.Description}");
            if (!string.IsNullOrEmpty(org.Company)) sb.AppendLine($"公司: {org.Company}");
            if (!string.IsNullOrEmpty(org.Blog)) sb.AppendLine($"博客: {org.Blog}");
            if (!string.IsNullOrEmpty(org.Location)) sb.AppendLine($"位置: {org.Location}");
            if (!string.IsNullOrEmpty(org.Email)) sb.AppendLine($"邮箱: {org.Email}");
            if (!string.IsNullOrEmpty(org.TwitterUsername)) sb.AppendLine($"Twitter: @{org.TwitterUsername}");
            sb.AppendLine($"公开仓库: {org.PublicRepos}  公开 Gist: {org.PublicGists}");
            sb.AppendLine($"关注者: {org.Followers}  正在关注: {org.Following}");
            if (!string.IsNullOrEmpty(org.CreatedAt)) sb.AppendLine($"创建于: {org.CreatedAt}");
            if (!string.IsNullOrEmpty(org.HtmlUrl)) sb.AppendLine($"主页: {org.HtmlUrl}");
            return sb.ToString();
        } catch { return json; }
    }

    // === SSH Key ===

    /// <summary>
    /// 列出 SSH Key — 调 GET /user/keys
    /// </summary>
    [McpTool(GitHubToolNameEnumConstants.GhSshKeyList, "列出 SSH Key", "github", ConcurrencySafe = true)]
    public async Task<ToolResult> GhSshKeyListAsync(
        [McpToolOptions] GitHubCommonOptions? common = null,
        CancellationToken cancellationToken = default) {
        if (_apiClient is null) return ApiClientNotConfigured();
        var result = await _apiClient.SendAsync(HttpMethod.Get, "user/keys", ct: cancellationToken).ConfigureAwait(false);
        if (!result.Success) return Fail(result.Error);
        return Ok(FormatGhOutput(result.Body, common?.Verbosity, common?.JsonFields, SummarizeSshKeyList, "id,title", common?.Jq, common?.SkipLines, common?.StartLine, "gh_ssh_key_list"));
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
            var keys = JsonSerializer.Deserialize(json, GitHubApiJsonContext.Safe.ListSshKeyResponse);
            if (keys is null) return json;
            var sb = new StringBuilder(128);
            sb.AppendLine("ID\t标题");
            foreach (var key in keys) sb.AppendLine($"{key.Id}\t{key.Title}");
            return sb.ToString();
        } catch { return json; }
    }

    /// <summary>
    /// 查看 SSH Key 详情 — 调 GET /user/keys/{id}
    /// </summary>
    [McpTool(GitHubToolNameEnumConstants.GhSshKeyView, "查看 SSH Key 详情", "github", ConcurrencySafe = true)]
    public async Task<ToolResult> GhSshKeyViewAsync(
        [McpToolParameter("Key ID", Required = true)] int key_id,
        [McpToolOptions] GitHubCommonOptions? common = null,
        CancellationToken cancellationToken = default) {
        if (_apiClient is null) return ApiClientNotConfigured();
        var result = await _apiClient.SendAsync(HttpMethod.Get, $"user/keys/{key_id}", ct: cancellationToken).ConfigureAwait(false);
        if (!result.Success) return Fail(EnrichApiError(result.Error, "gh_ssh_key_view", $"user/keys/{key_id}"));
        return Ok(FormatGhOutput(result.Body, common?.Verbosity, common?.JsonFields, SummarizeSshKeyView, "id,title,key", common?.Jq, common?.SkipLines, common?.StartLine, "gh_ssh_key_view"));
    }

    /// <summary>
    /// SSH Key 详情 — 人类可读格式
    /// </summary>
    private static string SummarizeSshKeyView(string json) {
        try {
            var key = JsonSerializer.Deserialize(json, GitHubApiJsonContext.Safe.SshKeyResponse);
            if (key is null) return json;
            var sb = new StringBuilder(256);
            sb.AppendLine($"ID: {key.Id}");
            sb.AppendLine($"标题: {key.Title}");
            if (!string.IsNullOrEmpty(key.Key)) {
                var keyPreview = key.Key.Length > 80 ? key.Key[..80] + "..." : key.Key;
                sb.AppendLine($"Key: {keyPreview}");
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
        [McpToolOptions] GitHubCommonOptions? common = null,
        CancellationToken cancellationToken = default) {
        if (_apiClient is null) return ApiClientNotConfigured();
        var result = await _apiClient.SendAsync(HttpMethod.Get, "user/gpg_keys", ct: cancellationToken).ConfigureAwait(false);
        if (!result.Success) return Fail(EnrichApiError(result.Error, "gh_gpg_key_list", "user/gpg_keys"));
        return Ok(FormatGhOutput(result.Body, common?.Verbosity, common?.JsonFields, SummarizeGpgKeyList, "id,key_id,public_key", common?.Jq, common?.SkipLines, common?.StartLine, "gh_gpg_key_list"));
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
        return result.Success ? OkBrief(result.Body, "GPG Key 添加成功") : Fail(EnrichApiError(result.Error, "gh_gpg_key_add", "user/gpg_keys"));
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
        return result.Success ? Ok($"已删除 GPG Key {key_id}") : Fail(EnrichApiError(result.Error, "gh_gpg_key_delete", $"user/gpg_keys/{key_id}"));
    }

    /// <summary>
    /// 精简 GPG Key 列表 — 表格格式(id, key_id, can_sign)
    /// </summary>
    private static string SummarizeGpgKeyList(string json) {
        try {
            var keys = JsonSerializer.Deserialize(json, GitHubApiJsonContext.Safe.ListGpgKeyResponse);
            if (keys is null) return json;
            var sb = new StringBuilder(128);
            sb.AppendLine("ID\tKey ID\t可签名");
            foreach (var key in keys) sb.AppendLine($"{key.Id}\t{key.KeyId}\t{key.CanSign}");
            return sb.ToString();
        } catch { return json; }
    }

    /// <summary>
    /// 查看 GPG Key 详情 — 调 GET /user/gpg_keys/{id}
    /// </summary>
    [McpTool(GitHubToolNameEnumConstants.GhGpgKeyView, "查看 GPG Key 详情", "github", ConcurrencySafe = true)]
    public async Task<ToolResult> GhGpgKeyViewAsync(
        [McpToolParameter("Key ID", Required = true)] int key_id,
        [McpToolOptions] GitHubCommonOptions? common = null,
        CancellationToken cancellationToken = default) {
        if (_apiClient is null) return ApiClientNotConfigured();
        var result = await _apiClient.SendAsync(HttpMethod.Get, $"user/gpg_keys/{key_id}", ct: cancellationToken).ConfigureAwait(false);
        if (!result.Success) return Fail(EnrichApiError(result.Error, "gh_gpg_key_view", $"user/gpg_keys/{key_id}"));
        return Ok(FormatGhOutput(result.Body, common?.Verbosity, common?.JsonFields, SummarizeGpgKeyView, "id,key_id,public_key,can_sign", common?.Jq, common?.SkipLines, common?.StartLine, "gh_gpg_key_view"));
    }

    /// <summary>
    /// GPG Key 详情 — 人类可读格式
    /// </summary>
    private static string SummarizeGpgKeyView(string json) {
        try {
            var key = JsonSerializer.Deserialize(json, GitHubApiJsonContext.Safe.GpgKeyResponse);
            if (key is null) return json;
            var sb = new StringBuilder(256);
            sb.AppendLine($"ID: {key.Id}");
            sb.AppendLine($"Key ID: {key.KeyId}");
            sb.AppendLine($"可签名: {key.CanSign}");
            if (!string.IsNullOrEmpty(key.PublicKey)) {
                var keyPreview = key.PublicKey.Length > 80 ? key.PublicKey[..80] + "..." : key.PublicKey;
                sb.AppendLine($"PublicKey: {keyPreview}");
            }
            return sb.ToString();
        } catch { return json; }
    }
}
