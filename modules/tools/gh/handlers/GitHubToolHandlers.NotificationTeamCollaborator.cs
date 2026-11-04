namespace McpToolDispatch;

/// <summary>
/// GitHub Notification/Team/Collaborator 工具 — 列出通知/团队/协作者
/// <para>ADR: 0073 — 直调 GitHub REST API</para>
/// </summary>
public partial class GitHubToolHandlers {
    /// <summary>列出通知 — 调 GET /notifications</summary>
    [McpTool(GitHubToolNameEnumConstants.GhNotificationList, "列出 GitHub 通知(支持未读过滤)", "github", ConcurrencySafe = true)]
    public async Task<ToolResult> GhNotificationListAsync(
        [McpToolParameter(WellKnownParam.Limit)] int? limit = null,
        [McpToolParameter("只列出未读(true)/全部(false,默认)", Required = false)] bool? unread = null,
        [McpToolOptions] GitHubCommonOptions? common = null,
        CancellationToken cancellationToken = default) {
        if (_apiClient is null) return ApiClientNotConfigured();
        var query = new Dictionary<string, string> { ["per_page"] = (limit ?? 30).ToString() };
        if (unread == true) query["all"] = "false";
        var result = await _apiClient.SendAsync(HttpMethod.Get, "notifications", query: query, ct: cancellationToken).ConfigureAwait(false);
        if (!result.Success) return Fail(EnrichApiError(result.Error, "gh_notification_list", "notifications"));
        return Ok(FormatGhOutput(result.Body, common?.Verbosity, common?.JsonFields, SummarizeNotificationList, "id,unread,reason,subject.title,subject.type,repository.full_name,updated_at", common?.Jq, common?.SkipLines, common?.StartLine, "gh_notification_list"));
    }

    /// <summary>列出组织团队 — 调 GET /orgs/{org}/teams</summary>
    [McpTool(GitHubToolNameEnumConstants.GhTeamList, "列出组织团队", "github", ConcurrencySafe = true)]
    public async Task<ToolResult> GhTeamListAsync(
        [McpToolParameter("组织名", Required = true)] string org,
        [McpToolParameter(WellKnownParam.Limit)] int? limit = null,
        [McpToolOptions] GitHubCommonOptions? common = null,
        CancellationToken cancellationToken = default) {
        if (_apiClient is null) return ApiClientNotConfigured();
        var query = new Dictionary<string, string> { ["per_page"] = (limit ?? 30).ToString() };
        var result = await _apiClient.SendAsync(HttpMethod.Get, $"orgs/{org}/teams", query: query, ct: cancellationToken).ConfigureAwait(false);
        if (!result.Success) return Fail(EnrichApiError(result.Error, "gh_team_list", $"orgs/{org}/teams"));
        return Ok(FormatGhOutput(result.Body, common?.Verbosity, common?.JsonFields, SummarizeTeamList, "id,name,slug,description,privacy,permission,members_count,repos_count", common?.Jq, common?.SkipLines, common?.StartLine, "gh_team_list"));
    }

    /// <summary>列出仓库协作者 — 调 GET /repos/{o}/{r}/collaborators</summary>
    [McpTool(GitHubToolNameEnumConstants.GhCollaboratorList, "列出仓库协作者(含权限)", "github", ConcurrencySafe = true)]
    public async Task<ToolResult> GhCollaboratorListAsync(
        [McpToolParameter(WellKnownParam.Limit)] int? limit = null,
        [McpToolOptions] GitHubCommonOptions? common = null,
        CancellationToken cancellationToken = default)
        => await ExecuteGhAsync(common?.Repo, common?.WorkingDir, cancellationToken, async (client, owner, repoName) => {
            var query = new Dictionary<string, string> { ["per_page"] = (limit ?? 30).ToString() };
            var result = await client.SendAsync(HttpMethod.Get, $"repos/{owner}/{repoName}/collaborators", query: query, ct: cancellationToken).ConfigureAwait(false);
            if (!result.Success) return Fail(EnrichApiError(result.Error, "gh_collaborator_list", $"repos/{owner}/{repoName}/collaborators"));
            return Ok(FormatGhOutput(result.Body, common?.Verbosity, common?.JsonFields, SummarizeCollaboratorList, "login,permissions.admin,permissions.push,permissions.pull", common?.Jq, common?.SkipLines, common?.StartLine, "gh_collaborator_list"));
        }).ConfigureAwait(false);

    private static string SummarizeNotificationList(string json) {
        try {
            var notifications = JsonSerializer.Deserialize(json, GitHubApiJsonContext.Safe.ListNotificationItemResponse);
            if (notifications is null) return json;
            var sb = new StringBuilder(256);
            sb.AppendLine("未读\t类型\t标题\t仓库\t原因");
            foreach (var n in notifications) {
                var unread = n.Unread ? "●" : "";
                var title = n.Subject?.Title ?? "";
                var type = n.Subject?.Type ?? "";
                var repo = n.Repository?.FullName ?? "";
                var reason = n.Reason ?? "";
                sb.AppendLine($"{unread}\t{type}\t{title}\t{repo}\t{reason}");
            }
            return sb.ToString();
        } catch { return json; }
    }

    private static string SummarizeTeamList(string json) {
        try {
            var teams = JsonSerializer.Deserialize(json, GitHubApiJsonContext.Safe.ListTeamItemResponse);
            if (teams is null) return json;
            var sb = new StringBuilder(256);
            sb.AppendLine("名称\tSlug\t隐私\t权限\t成员\t仓库");
            foreach (var t in teams) {
                sb.AppendLine($"{t.Name}\t{t.Slug}\t{t.Privacy}\t{t.Permission}\t{t.MembersCount}\t{t.ReposCount}");
            }
            return sb.ToString();
        } catch { return json; }
    }

    private static string SummarizeCollaboratorList(string json) {
        try {
            var collaborators = JsonSerializer.Deserialize(json, GitHubApiJsonContext.Safe.ListCollaboratorItemResponse);
            if (collaborators is null) return json;
            var sb = new StringBuilder(256);
            sb.AppendLine("用户\tAdmin\tPush\tPull");
            foreach (var c in collaborators) {
                var p = c.Permissions;
                sb.AppendLine($"{c.Login}\t{(p?.Admin == true ? "✓" : "")}\t{(p?.Push == true ? "✓" : "")}\t{(p?.Pull == true ? "✓" : "")}");
            }
            return sb.ToString();
        } catch { return json; }
    }
}
