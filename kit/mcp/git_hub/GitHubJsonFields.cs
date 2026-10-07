namespace McpToolDispatch;

/// <summary>
/// GitHub API JSON 字段名常量 — 高频使用的字段名统一管理，消除硬编码字符串字面量
/// <para>仅用于 JsonElement.TryGetProperty/GetProperty/GetString/GetInt32/GetBoolean 等访问上下文</para>
/// <para>禁止用于字典 key、字符串比较、序列化特性等其他场景</para>
/// </summary>
internal static class GitHubJsonFields {
    // 通用标识
    public const string Id = "id";
    public const string Name = "name";
    public const string Title = "title";
    public const string Number = "number";
    public const string Login = "login";
    public const string State = "state";
    public const string NodeId = "node_id";
    public const string Url = "url";
    public const string HtmlUrl = "html_url";
    public const string Description = "description";
    public const string Type = "type";
    public const string Slug = "slug";

    // GraphQL 包裹层
    public const string Data = "data";
    public const string Organization = "organization";
    public const string Repository = "repository";
    public const string Nodes = "nodes";
    public const string Items = "items";
    public const string TotalCount = "total_count";

    // PR / Issue 核心
    public const string User = "user";
    public const string Head = "head";
    public const string Ref = "ref";
    public const string Draft = "draft";
    public const string Body = "body";
    public const string BodyText = "body_text";
    public const string Labels = "labels";
    public const string Assignees = "assignees";
    public const string Reviewers = "reviewers";
    public const string Milestone = "milestone";
    public const string Conclusion = "conclusion";
    public const string Status = "status";
    public const string DisplayTitle = "display_title";

    // 时间戳
    public const string CreatedAt = "created_at";
    public const string UpdatedAt = "updated_at";
    public const string MergedAt = "merged_at";
    public const string ClosedAt = "closed_at";
    public const string PublishedAt = "published_at";
    public const string StartedAt = "started_at";
    public const string CreatedBy = "created_by";
    public const string Date = "date";

    // 仓库元数据
    public const string Private = "private";
    public const string Fork = "fork";
    public const string Archived = "archived";
    public const string Disabled = "disabled";
    public const string DefaultBranch = "default_branch";
    public const string Language = "language";
    public const string StargazersCount = "stargazers_count";
    public const string ForksCount = "forks_count";
    public const string OpenIssuesCount = "open_issues_count";

    // Release / Asset
    public const string Assets = "assets";
    public const string TagName = "tag_name";
    public const string TargetCommitish = "target_commitish";
    public const string UploadUrl = "upload_url";
    public const string ZipballUrl = "zipball_url";
    public const string TarballUrl = "tarball_url";

    // Git 对象
    public const string Sha = "sha";
    public const string Commit = "commit";
    public const string Tree = "tree";
    public const string Blob = "blob";
    public const string Size = "size";
    public const string Content = "content";
    public const string Encoding = "encoding";
    public const string Path = "path";
    public const string Mode = "mode";
    public const string Message = "message";
    public const string Author = "author";
    public const string Committer = "committer";
    public const string Email = "email";

    // Workflow / Run
    public const string Runs = "runs";
    public const string WorkflowId = "workflow_id";
    public const string RunNumber = "run_number";
    public const string Event = "event";
    public const string Branch = "branch";
    public const string CheckSuiteId = "check_suite_id";
    public const string AppId = "app_id";

    // 组织 / 权限
    public const string Privacy = "privacy";
    public const string Visibility = "visibility";
    public const string CanAdmin = "can_admin";
    public const string CanWrite = "can_write";
    public const string CanRead = "can_read";
    public const string Environment = "environment";
    public const string SelectedRepositoryIds = "selected_repository_ids";

    // 分页
    public const string PerPage = "per_page";
}
