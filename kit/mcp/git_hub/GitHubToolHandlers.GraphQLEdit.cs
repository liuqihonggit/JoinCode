namespace McpToolDispatch;

/// <summary>
/// GitHub GraphQL edit 辅助方法 — addSubIssue/removeSubIssue/addBlockedBy/removeBlockedBy/updateIssueIssueType/deleteProjectV2Item
/// <para>所有方法均调 GraphQL endpoint(POST graphql)，用 BuildGraphQL 构建查询</para>
/// </summary>
public partial class GitHubToolHandlers {
    /// <summary>
    /// 获取仓库的 database ID — GET /repos/{owner}/{repo} 解析 id 字段（用于 attach 上传）
    /// </summary>
    private async Task<long?> GetRepoDatabaseIdAsync(IGitHubApiClient client, string owner, string repoName, CancellationToken ct) {
        var result = await client.SendAsync(HttpMethod.Get, $"repos/{owner}/{repoName}", ct: ct).ConfigureAwait(false);
        if (!result.Success) return null;
        try {
            using var doc = JsonDocument.Parse(result.Body);
            return doc.RootElement.TryGetProperty("id", out var n) ? n.GetInt64() : null;
        } catch (Exception ex) { _logger?.LogWarning(ex, "解析 repo id 失败"); return null; }
    }

    /// <summary>
    /// 按 title 查 Project v2 ID — 先查 organization projects，再查 viewer projects
    /// </summary>
    private async Task<string?> FindProjectIdByTitleAsync(IGitHubApiClient client, string owner, string projectTitle, CancellationToken ct) {
        var orgQuery = BuildGraphQL($"query{{organization(login:\"{owner}\"){{projectsV2(first:50){{nodes{{id title}}}}}}}}");
        var orgResult = await client.SendAsync(HttpMethod.Post, "graphql", orgQuery, ct: ct).ConfigureAwait(false);
        if (orgResult.Success) {
            try {
                using var doc = JsonDocument.Parse(orgResult.Body);
                var nodes = doc.RootElement.GetProperty("data").GetProperty("organization").GetProperty("projectsV2").GetProperty("nodes");
                foreach (var node in nodes.EnumerateArray()) {
                    if (node.TryGetProperty("title", out var t) && t.GetString() == projectTitle) {
                        return node.GetProperty("id").GetString();
                    }
                }
            } catch (Exception ex) { _logger?.LogDebug(ex, "解析 organization projectsV2 失败"); }
        }
        var viewerQuery = BuildGraphQL("query{viewer{projectsV2(first:50){nodes{id title}}}}");
        var viewerResult = await client.SendAsync(HttpMethod.Post, "graphql", viewerQuery, ct: ct).ConfigureAwait(false);
        if (viewerResult.Success) {
            try {
                using var doc = JsonDocument.Parse(viewerResult.Body);
                var nodes = doc.RootElement.GetProperty("data").GetProperty("viewer").GetProperty("projectsV2").GetProperty("nodes");
                foreach (var node in nodes.EnumerateArray()) {
                    if (node.TryGetProperty("title", out var t) && t.GetString() == projectTitle) {
                        return node.GetProperty("id").GetString();
                    }
                }
            } catch (Exception ex) { _logger?.LogDebug(ex, "解析 viewer projectsV2 失败"); }
        }
        return null;
    }

    /// <summary>
    /// 查 Project items 找到匹配 contentNodeId 的 item ID — 用于 deleteProjectV2Item
    /// </summary>
    private async Task<string?> FindProjectItemIdAsync(IGitHubApiClient client, string projectId, string contentNodeId, CancellationToken ct) {
        var query = BuildGraphQL($"query{{node(id:\"{projectId}\"){{...on ProjectV2{{items(first:100){{nodes{{id content{{...on Issue{{id}} ...on PullRequest{{id}}}}}}}}}}}}}}");
        var result = await client.SendAsync(HttpMethod.Post, "graphql", query, ct: ct).ConfigureAwait(false);
        if (!result.Success) return null;
        try {
            using var doc = JsonDocument.Parse(result.Body);
            var nodes = doc.RootElement.GetProperty("data").GetProperty("node").GetProperty("items").GetProperty("nodes");
            foreach (var node in nodes.EnumerateArray()) {
                if (node.TryGetProperty("content", out var content) && content.TryGetProperty("id", out var id) && id.GetString() == contentNodeId) {
                    return node.GetProperty("id").GetString();
                }
            }
        } catch (Exception ex) { _logger?.LogWarning(ex, "解析 project items 失败"); }
        return null;
    }

    /// <summary>
    /// 按 title 添加 Issue/PR 到 Project v2 — 查 project ID 后调 addProjectV2ItemById
    /// </summary>
    private async Task<(bool Success, string? Error)> AddToProjectByTitleAsync(IGitHubApiClient client, string owner, string contentNodeId, string projectTitle, CancellationToken ct) {
        var projectId = await FindProjectIdByTitleAsync(client, owner, projectTitle, ct).ConfigureAwait(false);
        if (string.IsNullOrEmpty(projectId)) return (false, $"未找到项目: {projectTitle}");
        var mutation = BuildGraphQL($"mutation{{addProjectV2ItemById(input:{{projectId:\"{projectId}\",contentId:\"{contentNodeId}\"}}){{item{{id}}}}}}");
        var result = await client.SendAsync(HttpMethod.Post, "graphql", mutation, ct: ct).ConfigureAwait(false);
        return result.Success ? (true, null) : (false, result.Error);
    }

    /// <summary>
    /// 按 title 从 Project v2 移除 Issue/PR — 查 project ID + item ID 后调 deleteProjectV2Item
    /// </summary>
    private async Task<(bool Success, string? Error)> RemoveFromProjectByTitleAsync(IGitHubApiClient client, string owner, string contentNodeId, string projectTitle, CancellationToken ct) {
        var projectId = await FindProjectIdByTitleAsync(client, owner, projectTitle, ct).ConfigureAwait(false);
        if (string.IsNullOrEmpty(projectId)) return (false, $"未找到项目: {projectTitle}");
        var itemId = await FindProjectItemIdAsync(client, projectId, contentNodeId, ct).ConfigureAwait(false);
        if (string.IsNullOrEmpty(itemId)) return (false, $"Issue/PR 不在项目 {projectTitle} 中");
        var mutation = BuildGraphQL($"mutation{{deleteProjectV2Item(input:{{projectId:\"{projectId}\",itemId:\"{itemId}\"}}){{clientMutationId}}}}");
        var result = await client.SendAsync(HttpMethod.Post, "graphql", mutation, ct: ct).ConfigureAwait(false);
        return result.Success ? (true, null) : (false, result.Error);
    }

    /// <summary>
    /// 添加子 issue — GraphQL addSubIssue(input: {issueId: parentId, subIssueId: childId, replaceParent: true})
    /// </summary>
    private async Task<(bool Success, string? Error)> AddSubIssueGraphQLAsync(IGitHubApiClient client, string parentId, string childId, CancellationToken ct) {
        var mutation = BuildGraphQL($"mutation{{addSubIssue(input:{{issueId:\"{parentId}\",subIssueId:\"{childId}\",replaceParent:true}}){{issue{{id}}}}}}");
        var result = await client.SendAsync(HttpMethod.Post, "graphql", mutation, ct: ct).ConfigureAwait(false);
        return result.Success ? (true, null) : (false, result.Error);
    }

    /// <summary>
    /// 移除子 issue — GraphQL removeSubIssue(input: {issueId: parentId, subIssueId: childId})
    /// </summary>
    private async Task<(bool Success, string? Error)> RemoveSubIssueGraphQLAsync(IGitHubApiClient client, string parentId, string childId, CancellationToken ct) {
        var mutation = BuildGraphQL($"mutation{{removeSubIssue(input:{{issueId:\"{parentId}\",subIssueId:\"{childId}\"}}){{issue{{id}}}}}}");
        var result = await client.SendAsync(HttpMethod.Post, "graphql", mutation, ct: ct).ConfigureAwait(false);
        return result.Success ? (true, null) : (false, result.Error);
    }

    /// <summary>
    /// 添加 blocked-by 关系 — GraphQL addBlockedBy(input: {issueId, blockingIssueId})
    /// </summary>
    private async Task<(bool Success, string? Error)> AddBlockedByGraphQLAsync(IGitHubApiClient client, string issueId, string blockingIssueId, CancellationToken ct) {
        var mutation = BuildGraphQL($"mutation{{addBlockedBy(input:{{issueId:\"{issueId}\",blockingIssueId:\"{blockingIssueId}\"}}){{issue{{id}}}}}}");
        var result = await client.SendAsync(HttpMethod.Post, "graphql", mutation, ct: ct).ConfigureAwait(false);
        return result.Success ? (true, null) : (false, result.Error);
    }

    /// <summary>
    /// 移除 blocked-by 关系 — GraphQL removeBlockedBy(input: {issueId, blockingIssueId})
    /// </summary>
    private async Task<(bool Success, string? Error)> RemoveBlockedByGraphQLAsync(IGitHubApiClient client, string issueId, string blockingIssueId, CancellationToken ct) {
        var mutation = BuildGraphQL($"mutation{{removeBlockedBy(input:{{issueId:\"{issueId}\",blockingIssueId:\"{blockingIssueId}\"}}){{issue{{id}}}}}}");
        var result = await client.SendAsync(HttpMethod.Post, "graphql", mutation, ct: ct).ConfigureAwait(false);
        return result.Success ? (true, null) : (false, result.Error);
    }

    /// <summary>
    /// 查 issue type ID by name — GraphQL repository.issueTypes
    /// </summary>
    private async Task<string?> FindIssueTypeIdAsync(IGitHubApiClient client, string owner, string repoName, string typeName, CancellationToken ct) {
        var query = BuildGraphQL($"query{{repository(owner:\"{owner}\",name:\"{repoName}\"){{issueTypes(first:50){{nodes{{id name}}}}}}}}");
        var result = await client.SendAsync(HttpMethod.Post, "graphql", query, ct: ct).ConfigureAwait(false);
        if (!result.Success) return null;
        try {
            using var doc = JsonDocument.Parse(result.Body);
            var nodes = doc.RootElement.GetProperty("data").GetProperty("repository").GetProperty("issueTypes").GetProperty("nodes");
            foreach (var node in nodes.EnumerateArray()) {
                if (node.TryGetProperty("name", out var n) && n.GetString() == typeName) {
                    return node.GetProperty("id").GetString();
                }
            }
        } catch (Exception ex) { _logger?.LogWarning(ex, "解析 issueTypes 失败"); }
        return null;
    }

    /// <summary>
    /// 设置 issue 类型 — GraphQL updateIssueIssueType(input: {issueId, issueTypeId})
    /// </summary>
    private async Task<(bool Success, string? Error)> SetIssueTypeAsync(IGitHubApiClient client, string issueId, string issueTypeId, CancellationToken ct) {
        var mutation = BuildGraphQL($"mutation{{updateIssueIssueType(input:{{issueId:\"{issueId}\",issueTypeId:\"{issueTypeId}\"}}){{issue{{id}}}}}}");
        var result = await client.SendAsync(HttpMethod.Post, "graphql", mutation, ct: ct).ConfigureAwait(false);
        return result.Success ? (true, null) : (false, result.Error);
    }

    /// <summary>
    /// 移除 issue 类型 — GraphQL updateIssueIssueType(input: {issueId, issueTypeId: null})
    /// </summary>
    private async Task<(bool Success, string? Error)> RemoveIssueTypeAsync(IGitHubApiClient client, string issueId, CancellationToken ct) {
        var mutation = BuildGraphQL($"mutation{{updateIssueIssueType(input:{{issueId:\"{issueId}\",issueTypeId:null}}){{issue{{id}}}}}}");
        var result = await client.SendAsync(HttpMethod.Post, "graphql", mutation, ct: ct).ConfigureAwait(false);
        return result.Success ? (true, null) : (false, result.Error);
    }

    /// <summary>
    /// 查 issue 的 parent ID — GraphQL repository.issue(number) { parent { id } }
    /// </summary>
    private async Task<string?> GetIssueParentIdAsync(IGitHubApiClient client, string owner, string repoName, string number, CancellationToken ct) {
        var query = BuildGraphQL($"query{{repository(owner:\"{owner}\",name:\"{repoName}\"){{issue(number:{number}){{parent{{id}}}}}}}}");
        var result = await client.SendAsync(HttpMethod.Post, "graphql", query, ct: ct).ConfigureAwait(false);
        if (!result.Success) return null;
        try {
            using var doc = JsonDocument.Parse(result.Body);
            var issue = doc.RootElement.GetProperty("data").GetProperty("repository").GetProperty("issue");
            if (issue.TryGetProperty("parent", out var parent) && parent.ValueKind == JsonValueKind.Object) {
                return parent.GetProperty("id").GetString();
            }
        } catch (Exception ex) { _logger?.LogWarning(ex, "解析 issue parent 失败"); }
        return null;
    }

    /// <summary>
    /// 上传附件到 GitHub — POST github.com/user-attachments/assets?name={fileName}&amp;repository_id={repoId}
    /// <para>返回附件 URL，可在 issue/PR body 中用 markdown 引用</para>
    /// </summary>
    private async Task<(string? AssetUrl, string? Error)> AttachFileAsync(IGitHubApiClient client, string owner, string repoName, string filePath, CancellationToken ct) {
        var repoId = await GetRepoDatabaseIdAsync(client, owner, repoName, ct).ConfigureAwait(false);
        if (repoId is null) return (null, "无法获取仓库 ID");
        if (!_fs.FileExists(filePath)) return (null, $"文件不存在: {filePath}");
        var fileName = Path.GetFileName(filePath);
        var fileBytes = await _fs.ReadAllBytesAsync(filePath, ct).ConfigureAwait(false);
        using var fileStream = new MemoryStream(fileBytes);
        var result = await client.UploadAttachmentAsync(repoId.Value, fileName, fileStream, ct).ConfigureAwait(false);
        if (!result.Success) return (null, result.Error);
        try {
            using var doc = JsonDocument.Parse(result.Body);
            var url = doc.RootElement.TryGetProperty("url", out var u) ? u.GetString() : null;
            return (url, null);
        } catch (Exception ex) { return (null, $"解析附件响应失败: {ex.Message}"); }
    }
}
