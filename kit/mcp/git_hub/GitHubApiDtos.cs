namespace McpToolDispatch;

/// <summary>
/// GitHub API 请求/响应 DTO — 固定结构 JSON 用 DTO + JsonContext 双向转换（编译期类型安全）
/// <para>替代手动拼接 JSON 字符串和 JsonDocument 动态解析，符合 NativeAOT + JsonContext 约束</para>
/// </summary>

/// <summary>Gist create 请求 body — POST /gists</summary>
internal sealed class GistCreateRequest {
    /// <summary>文件名 → 内容</summary>
    [JsonPropertyName("files")]
    public Dictionary<string, GistFileContent> Files { get; init; } = new();

    /// <summary>描述</summary>
    [JsonPropertyName("description")]
    public string? Description { get; init; }

    /// <summary>是否公开</summary>
    [JsonPropertyName("public")]
    public bool Public { get; init; }
}

/// <summary>Gist 文件内容</summary>
internal sealed class GistFileContent {
    /// <summary>文件内容</summary>
    [JsonPropertyName("content")]
    public string Content { get; init; } = "";
}

/// <summary>Gist 响应 — GET /gists/{id}</summary>
internal sealed class GistResponse {
    /// <summary>Gist ID</summary>
    [JsonPropertyName("id")]
    public string? Id { get; init; }

    /// <summary>描述</summary>
    [JsonPropertyName("description")]
    public string? Description { get; init; }

    /// <summary>是否公开</summary>
    [JsonPropertyName("public")]
    public bool Public { get; init; }

    /// <summary>文件映射</summary>
    [JsonPropertyName("files")]
    public Dictionary<string, GistFileDetail> Files { get; init; } = new();

    /// <summary>HTML URL</summary>
    [JsonPropertyName("html_url")]
    public string? HtmlUrl { get; init; }
}

/// <summary>Gist 文件详情</summary>
internal sealed class GistFileDetail {
    /// <summary>文件名</summary>
    [JsonPropertyName("filename")]
    public string? Filename { get; init; }

    /// <summary>文件内容</summary>
    [JsonPropertyName("content")]
    public string? Content { get; init; }
}

/// <summary>Gist list 响应元素 — GET /gists</summary>
internal sealed class GistListItem {
    /// <summary>Gist ID</summary>
    [JsonPropertyName("id")]
    public string? Id { get; init; }

    /// <summary>描述</summary>
    [JsonPropertyName("description")]
    public string? Description { get; init; }

    /// <summary>是否公开</summary>
    [JsonPropertyName("public")]
    public bool Public { get; init; }

    /// <summary>文件映射</summary>
    [JsonPropertyName("files")]
    public Dictionary<string, GistFileDetail> Files { get; init; } = new();
}

/// <summary>Workflow dispatch 请求 body — POST /actions/workflows/{id}/dispatches</summary>
internal sealed class WorkflowDispatchRequest {
    /// <summary>运行分支或 tag</summary>
    [JsonPropertyName("ref")]
    public string Ref { get; init; } = "";

    /// <summary>输入参数</summary>
    [JsonPropertyName("inputs")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Dictionary<string, string> Inputs { get; init; } = new();
}

/// <summary>Label create 请求 body — POST /labels</summary>
internal sealed class LabelCreateRequest {
    /// <summary>标签名</summary>
    [JsonPropertyName("name")]
    public string Name { get; init; } = "";

    /// <summary>颜色(6 字符 hex)</summary>
    [JsonPropertyName("color")]
    public string? Color { get; init; }

    /// <summary>描述</summary>
    [JsonPropertyName("description")]
    public string? Description { get; init; }
}

/// <summary>Deploy key add 请求 body — POST /keys</summary>
internal sealed class DeployKeyAddRequest {
    /// <summary>Key 标题</summary>
    [JsonPropertyName("title")]
    public string Title { get; init; } = "";

    /// <summary>SSH public key</summary>
    [JsonPropertyName("key")]
    public string Key { get; init; } = "";

    /// <summary>是否只读</summary>
    [JsonPropertyName("read_only")]
    public bool? ReadOnly { get; init; }
}

/// <summary>Autolink create 请求 body — POST /keys/autolinks</summary>
internal sealed class AutolinkCreateRequest {
    /// <summary>键前缀</summary>
    [JsonPropertyName("key_prefix")]
    public string KeyPrefix { get; init; } = "";

    /// <summary>URL 模板</summary>
    [JsonPropertyName("url_template")]
    public string UrlTemplate { get; init; } = "";
}

/// <summary>SSH key add 请求 body — POST /user/keys</summary>
internal sealed class SshKeyAddRequest {
    /// <summary>Key 标题</summary>
    [JsonPropertyName("title")]
    public string Title { get; init; } = "";

    /// <summary>SSH public key</summary>
    [JsonPropertyName("key")]
    public string Key { get; init; } = "";
}

/// <summary>GPG key add 请求 body — POST /user/gpg_keys</summary>
internal sealed class GpgKeyAddRequest {
    /// <summary>ASCII armored GPG key</summary>
    [JsonPropertyName("armored_public_key")]
    public string ArmoredPublicKey { get; init; } = "";
}

/// <summary>Variable set 请求 body — POST/PUT /actions/variables</summary>
internal sealed class VariableSetRequest {
    /// <summary>变量名</summary>
    [JsonPropertyName("name")]
    public string Name { get; init; } = "";

    /// <summary>变量值</summary>
    [JsonPropertyName("value")]
    public string Value { get; init; } = "";
}

/// <summary>Secret set 请求 body — PUT /actions/secrets/{name}</summary>
internal sealed class SecretSetRequest {
    /// <summary>加密后的 secret 值（base64 编码的 sealed box）</summary>
    [JsonPropertyName("encrypted_value")]
    public string EncryptedValue { get; init; } = "";

    /// <summary>公钥 ID（从 GET /actions/secrets/public-key 获取）</summary>
    [JsonPropertyName("key_id")]
    public string KeyId { get; init; } = "";
}

// === Issue DTO ===

/// <summary>Issue create 请求 — POST /issues</summary>
internal sealed class IssueCreateRequest {
    /// <summary>标题</summary>
    [JsonPropertyName("title")]
    public string Title { get; init; } = "";
    /// <summary>正文</summary>
    [JsonPropertyName("body")]
    public string? Body { get; init; }
    /// <summary>标签</summary>
    [JsonPropertyName("labels")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public List<string> Labels { get; init; } = new();
    /// <summary>指派人</summary>
    [JsonPropertyName("assignees")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public List<string> Assignees { get; init; } = new();
    /// <summary>里程碑 ID</summary>
    [JsonPropertyName("milestone")]
    public int? Milestone { get; init; }
}

/// <summary>Issue edit 请求 — PATCH /issues/{n}</summary>
internal sealed class IssueEditRequest {
    /// <summary>标题</summary>
    [JsonPropertyName("title")]
    public string? Title { get; init; }
    /// <summary>正文</summary>
    [JsonPropertyName("body")]
    public string? Body { get; init; }
    /// <summary>标签</summary>
    [JsonPropertyName("labels")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public List<string> Labels { get; init; } = new();
    /// <summary>指派人</summary>
    [JsonPropertyName("assignees")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public List<string> Assignees { get; init; } = new();
    /// <summary>里程碑 ID</summary>
    [JsonPropertyName("milestone")]
    public int? Milestone { get; init; }
    /// <summary>状态(open/closed)</summary>
    [JsonPropertyName("state")]
    public string? State { get; init; }
    /// <summary>状态原因</summary>
    [JsonPropertyName("state_reason")]
    public string? StateReason { get; init; }
}

/// <summary>Issue/PR 添加标签请求 — POST /issues/{n}/labels</summary>
internal sealed class LabelsAddRequest {
    /// <summary>标签名列表</summary>
    [JsonPropertyName("labels")]
    public List<string> Labels { get; init; } = new();
}

/// <summary>Issue/PR 评论 — POST /issues/{n}/comments</summary>
internal sealed class CommentRequest {
    /// <summary>评论内容</summary>
    [JsonPropertyName("body")]
    public string Body { get; init; } = "";
}

// === PR DTO ===

/// <summary>PR create 请求 — POST /pulls</summary>
internal sealed class PrCreateRequest {
    /// <summary>标题</summary>
    [JsonPropertyName("title")]
    public string Title { get; init; } = "";
    /// <summary>head 分支</summary>
    [JsonPropertyName("head")]
    public string Head { get; init; } = "";
    /// <summary>base 分支</summary>
    [JsonPropertyName("base")]
    public string? Base { get; init; }
    /// <summary>正文</summary>
    [JsonPropertyName("body")]
    public string? Body { get; init; }
    /// <summary>是否 draft</summary>
    [JsonPropertyName("draft")]
    public bool? Draft { get; init; }
    /// <summary>维护者可修改</summary>
    [JsonPropertyName("maintainer_can_modify")]
    public bool? MaintainerCanModify { get; init; }
}

/// <summary>PR edit 请求 — PATCH /pulls/{n}</summary>
internal sealed class PrEditRequest {
    /// <summary>标题</summary>
    [JsonPropertyName("title")]
    public string? Title { get; init; }
    /// <summary>正文</summary>
    [JsonPropertyName("body")]
    public string? Body { get; init; }
    /// <summary>base 分支</summary>
    [JsonPropertyName("base")]
    public string? Base { get; init; }
    /// <summary>状态</summary>
    [JsonPropertyName("state")]
    public string? State { get; init; }
    /// <summary>是否 draft</summary>
    [JsonPropertyName("draft")]
    public bool? Draft { get; init; }
}

/// <summary>PR review 请求 — POST /pulls/{n}/reviews</summary>
internal sealed class PrReviewRequest {
    /// <summary>事件(APPROVE/REQUEST_CHANGES/COMMENT)</summary>
    [JsonPropertyName("event")]
    public string Event { get; init; } = "";
    /// <summary>评论</summary>
    [JsonPropertyName("body")]
    public string? Body { get; init; }
}

/// <summary>PR merge 请求 — PUT /pulls/{n}/merge</summary>
internal sealed class PrMergeRequest {
    /// <summary>合并方法(squash/merge/rebase)</summary>
    [JsonPropertyName("merge_method")]
    public string? MergeMethod { get; init; }
    /// <summary>提交标题</summary>
    [JsonPropertyName("commit_title")]
    public string? CommitTitle { get; init; }
    /// <summary>提交消息</summary>
    [JsonPropertyName("commit_message")]
    public string? CommitMessage { get; init; }
    /// <summary>SHA</summary>
    [JsonPropertyName("sha")]
    public string? Sha { get; init; }
    /// <summary>是否 draft</summary>
    [JsonPropertyName("draft")]
    public bool? Draft { get; init; }
}

/// <summary>PR update-branch 请求 — PUT /pulls/{n}/update-branch</summary>
internal sealed class PrUpdateBranchRequest {
    /// <summary>更新方法(merge/rebase)</summary>
    [JsonPropertyName("update_method")]
    public string UpdateMethod { get; init; } = "merge";
}

/// <summary>PR ready 请求 — PATCH /pulls/{n} {"draft":false}</summary>
internal sealed class PrDraftRequest {
    /// <summary>是否 draft</summary>
    [JsonPropertyName("draft")]
    public bool Draft { get; init; }
}

/// <summary>Issue/PR lock 请求 — PUT /issues/{n}/lock</summary>
internal sealed class LockRequest {
    /// <summary>锁定原因</summary>
    [JsonPropertyName("lock_reason")]
    public string? LockReason { get; init; }
}

// === Release DTO ===

/// <summary>Release create 请求 — POST /releases</summary>
internal sealed class ReleaseCreateRequest {
    /// <summary>tag 名</summary>
    [JsonPropertyName("tag_name")]
    public string TagName { get; init; } = "";
    /// <summary>目标 commitish</summary>
    [JsonPropertyName("target_commitish")]
    public string? TargetCommitish { get; init; }
    /// <summary>release 名</summary>
    [JsonPropertyName("name")]
    public string? Name { get; init; }
    /// <summary>正文</summary>
    [JsonPropertyName("body")]
    public string? Body { get; init; }
    /// <summary>是否 draft</summary>
    [JsonPropertyName("draft")]
    public bool? Draft { get; init; }
    /// <summary>是否预发布</summary>
    [JsonPropertyName("prerelease")]
    public bool? Prerelease { get; init; }
    /// <summary>是否自动生成 notes</summary>
    [JsonPropertyName("generate_release_notes")]
    public bool? GenerateReleaseNotes { get; init; }
    /// <summary>标记为 latest(true/false/legacy)</summary>
    [JsonPropertyName("make_latest")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? MakeLatest { get; init; }
    /// <summary>discussion 分类名(创建 discussion)</summary>
    [JsonPropertyName("discussion_category_name")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? DiscussionCategoryName { get; init; }
    /// <summary>上一个 tag 名(自动生成 notes 的起始 tag)</summary>
    [JsonPropertyName("previous_tag_name")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? PreviousTagName { get; init; }
}

/// <summary>Release edit 请求 — PATCH /releases/{id}</summary>
internal sealed class ReleaseEditRequest {
    /// <summary>tag 名</summary>
    [JsonPropertyName("tag_name")]
    public string? TagName { get; init; }
    /// <summary>release 名</summary>
    [JsonPropertyName("name")]
    public string? Name { get; init; }
    /// <summary>正文</summary>
    [JsonPropertyName("body")]
    public string? Body { get; init; }
    /// <summary>是否 draft</summary>
    [JsonPropertyName("draft")]
    public bool? Draft { get; init; }
    /// <summary>是否预发布</summary>
    [JsonPropertyName("prerelease")]
    public bool? Prerelease { get; init; }
    /// <summary>目标 commitish</summary>
    [JsonPropertyName("target_commitish")]
    public string? TargetCommitish { get; init; }
    /// <summary>标记为 latest(true/false/legacy)</summary>
    [JsonPropertyName("make_latest")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? MakeLatest { get; init; }
}

/// <summary>Release generate-notes 请求 — POST /releases/generate-notes</summary>
internal sealed class ReleaseGenerateNotesRequest {
    /// <summary>tag 名</summary>
    [JsonPropertyName("tag_name")]
    public string? TagName { get; init; }
    /// <summary>目标 commitish</summary>
    [JsonPropertyName("target_commitish")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? TargetCommitish { get; init; }
}

// === Repo DTO ===

/// <summary>Repo create 请求 — POST /user/repos 或 /orgs/{org}/repos</summary>
internal sealed class RepoCreateRequest {
    /// <summary>仓库名</summary>
    [JsonPropertyName("name")]
    public string Name { get; init; } = "";
    /// <summary>描述</summary>
    [JsonPropertyName("description")]
    public string? Description { get; init; }
    /// <summary>主页</summary>
    [JsonPropertyName("homepage")]
    public string? Homepage { get; init; }
    /// <summary>是否私有</summary>
    [JsonPropertyName("private")]
    public bool? Private { get; init; }
    /// <summary>可见性(仅 internal 时设置)</summary>
    [JsonPropertyName("visibility")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Visibility { get; init; }
    /// <summary>是否有 issues</summary>
    [JsonPropertyName("has_issues")]
    public bool? HasIssues { get; init; }
    /// <summary>是否有 wiki</summary>
    [JsonPropertyName("has_wiki")]
    public bool? HasWiki { get; init; }
    /// <summary>是否自动初始化</summary>
    [JsonPropertyName("auto_init")]
    public bool? AutoInit { get; init; }
    /// <summary>gitignore 模板</summary>
    [JsonPropertyName("gitignore_template")]
    public string? GitignoreTemplate { get; init; }
    /// <summary>license 模板</summary>
    [JsonPropertyName("license_template")]
    public string? LicenseTemplate { get; init; }
}

/// <summary>Repo template generate 请求 — POST /repos/{o}/{r}/generate</summary>
internal sealed class RepoTemplateGenerateRequest {
    /// <summary>仓库名</summary>
    [JsonPropertyName("name")]
    public string Name { get; init; } = "";
    /// <summary>描述</summary>
    [JsonPropertyName("description")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Description { get; init; }
    /// <summary>是否私有</summary>
    [JsonPropertyName("private")]
    public bool? Private { get; init; }
    /// <summary>可见性</summary>
    [JsonPropertyName("visibility")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Visibility { get; init; }
    /// <summary>是否包含所有分支(默认 false,只复制默认分支)</summary>
    [JsonPropertyName("include_all_branches")]
    public bool? IncludeAllBranches { get; init; }
}

/// <summary>Repo edit 请求 — PATCH /repos/{o}/{r}</summary>
internal sealed class RepoEditRequest {
    /// <summary>描述</summary>
    [JsonPropertyName("description")]
    public string? Description { get; init; }
    /// <summary>主页</summary>
    [JsonPropertyName("homepage")]
    public string? Homepage { get; init; }
    /// <summary>可见性(public/private/internal)</summary>
    [JsonPropertyName("visibility")]
    public string? Visibility { get; init; }
    /// <summary>是否有 issues</summary>
    [JsonPropertyName("has_issues")]
    public bool? HasIssues { get; init; }
    /// <summary>是否有 wiki</summary>
    [JsonPropertyName("has_wiki")]
    public bool? HasWiki { get; init; }
    /// <summary>默认分支</summary>
    [JsonPropertyName("default_branch")]
    public string? DefaultBranch { get; init; }
    /// <summary>合并后是否删除分支</summary>
    [JsonPropertyName("delete_branch_on_merge")]
    public bool? DeleteBranchOnMerge { get; init; }
    /// <summary>是否启用 Projects</summary>
    [JsonPropertyName("has_projects")]
    public bool? HasProjects { get; init; }
    /// <summary>是否启用 Discussions</summary>
    [JsonPropertyName("has_discussions")]
    public bool? HasDiscussions { get; init; }
    /// <summary>是否允许 squash merge</summary>
    [JsonPropertyName("allow_squash_merge")]
    public bool? AllowSquashMerge { get; init; }
    /// <summary>是否允许 merge commit</summary>
    [JsonPropertyName("allow_merge_commit")]
    public bool? AllowMergeCommit { get; init; }
    /// <summary>是否允许 rebase merge</summary>
    [JsonPropertyName("allow_rebase_merge")]
    public bool? AllowRebaseMerge { get; init; }
    /// <summary>是否允许 auto-merge</summary>
    [JsonPropertyName("allow_auto_merge")]
    public bool? AllowAutoMerge { get; init; }
    /// <summary>是否允许 update branch</summary>
    [JsonPropertyName("allow_update_branch")]
    public bool? AllowUpdateBranch { get; init; }
    /// <summary>是否允许 fork</summary>
    [JsonPropertyName("allow_forking")]
    public bool? AllowForking { get; init; }
    /// <summary>是否为模板仓库</summary>
    [JsonPropertyName("is_template")]
    public bool? IsTemplate { get; init; }
    /// <summary>squash merge commit 消息模板</summary>
    [JsonPropertyName("squash_pr_commit_message")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? SquashPrCommitMessage { get; init; }
    /// <summary>安全与分析设置</summary>
    [JsonPropertyName("security_and_analysis")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public SecurityAndAnalysis? SecurityAndAnalysis { get; init; }
}

/// <summary>安全与分析嵌套 DTO — 用于 RepoEditRequest</summary>
internal sealed class SecurityAndAnalysis {
    /// <summary>高级安全</summary>
    [JsonPropertyName("advanced_security")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public SecurityFeature? AdvancedSecurity { get; init; }
    /// <summary>密钥扫描</summary>
    [JsonPropertyName("secret_scanning")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public SecurityFeature? SecretScanning { get; init; }
    /// <summary>密钥扫描推送保护</summary>
    [JsonPropertyName("secret_scanning_push_protection")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public SecurityFeature? SecretScanningPushProtection { get; init; }
}

/// <summary>安全功能开关 — status=enabled/disabled</summary>
internal sealed class SecurityFeature {
    /// <summary>状态(enabled/disabled)</summary>
    [JsonPropertyName("status")]
    public string Status { get; init; } = "";
}

/// <summary>Topics 请求 — PUT /repos/{o}/{r}/topics</summary>
internal sealed class TopicsRequest {
    /// <summary>topic 名称列表</summary>
    [JsonPropertyName("names")]
    public List<string> Names { get; init; } = new();
}

/// <summary>Repo rename 请求 — POST /repos/{o}/{r}/rename</summary>
internal sealed class RepoRenameRequest {
    /// <summary>新仓库名</summary>
    [JsonPropertyName("new_name")]
    public string NewName { get; init; } = "";
}

/// <summary>Repo sync 请求 — POST /repos/{o}/{r}/merge-upstream</summary>
internal sealed class RepoSyncRequest {
    /// <summary>分支名</summary>
    [JsonPropertyName("branch")]
    public string Branch { get; init; } = "";
}

/// <summary>Repo set-default 请求 — PATCH /repos/{o}/{r}</summary>
internal sealed class RepoSetDefaultRequest {
    /// <summary>默认分支</summary>
    [JsonPropertyName("default_branch")]
    public string DefaultBranch { get; init; } = "";
}

/// <summary>Repo archive 请求 — PATCH /repos/{o}/{r}</summary>
internal sealed class RepoArchiveRequest {
    /// <summary>是否归档</summary>
    [JsonPropertyName("archived")]
    public bool Archived { get; init; }
}

/// <summary>Repo fork 请求 — POST /repos/{o}/{r}/forks</summary>
internal sealed class RepoForkRequest {
    /// <summary>目标组织</summary>
    [JsonPropertyName("organization")]
    public string? Organization { get; init; }
    /// <summary>Fork 仓库名(默认同原名)</summary>
    [JsonPropertyName("name")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Name { get; init; }
    /// <summary>只 fork 默认分支</summary>
    [JsonPropertyName("default_branch_only")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool? DefaultBranchOnly { get; init; }
}

// === Codespace DTO ===

/// <summary>Codespace create 请求 — POST /user/codespaces</summary>
internal sealed class CodespaceCreateRequest {
    /// <summary>仓库 ID</summary>
    [JsonPropertyName("repository_id")]
    public long RepositoryId { get; init; }
    /// <summary>分支名</summary>
    [JsonPropertyName("ref")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Ref { get; init; }
    /// <summary>machine 类型</summary>
    [JsonPropertyName("machine")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Machine { get; init; }
    /// <summary>devcontainer 路径</summary>
    [JsonPropertyName("devcontainer_path")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? DevcontainerPath { get; init; }
    /// <summary>显示名</summary>
    [JsonPropertyName("display_name")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? DisplayName { get; init; }
}

// === GraphQL DTO ===

/// <summary>GraphQL 请求信封 — POST /graphql，body 为 {"query":"..."}</summary>
internal sealed class GraphQLRequest {
    /// <summary>GraphQL 查询或 mutation 字符串</summary>
    [JsonPropertyName("query")]
    public string Query { get; init; } = "";
}

// === Run DTO ===

/// <summary>Run rerun-jobs 请求 — POST /actions/runs/{id}/rerun-jobs</summary>
internal sealed class RunRerunJobsRequest {
    /// <summary>job IDs</summary>
    [JsonPropertyName("job_ids")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public List<long> JobIds { get; init; } = new();
    /// <summary>是否启用 debug 日志</summary>
    [JsonPropertyName("enable_debug_logging")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool? EnableDebugLogging { get; init; }
}

/// <summary>PR assignees 请求 — POST /issues/{n}/assignees</summary>
internal sealed class AssigneesRequest {
    /// <summary>指派人列表</summary>
    [JsonPropertyName("assignees")]
    public List<string> Assignees { get; init; } = new();
}

/// <summary>PR reviewers 请求 — POST /pulls/{n}/requested_reviewers</summary>
internal sealed class ReviewersRequest {
    /// <summary>审查人列表</summary>
    [JsonPropertyName("reviewers")]
    public List<string> Reviewers { get; init; } = new();
}

/// <summary>Issue/PR milestone 请求 — PATCH /issues/{n}</summary>
internal sealed class MilestoneRequest {
    /// <summary>里程碑 ID</summary>
    [JsonPropertyName("milestone")]
    public int? Milestone { get; init; }
}


/// <summary>
/// GitHub API DTO 的 JSON 序列化上下文 — AOT 模式需要源码生成器注册类型
/// </summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower, WriteIndented = false, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(GistCreateRequest))]
[JsonSerializable(typeof(GistResponse))]
[JsonSerializable(typeof(GistListItem))]
[JsonSerializable(typeof(List<GistListItem>))]
[JsonSerializable(typeof(WorkflowDispatchRequest))]
[JsonSerializable(typeof(LabelCreateRequest))]
[JsonSerializable(typeof(DeployKeyAddRequest))]
[JsonSerializable(typeof(AutolinkCreateRequest))]
[JsonSerializable(typeof(SshKeyAddRequest))]
[JsonSerializable(typeof(GpgKeyAddRequest))]
[JsonSerializable(typeof(VariableSetRequest))]
[JsonSerializable(typeof(SecretSetRequest))]
[JsonSerializable(typeof(IssueCreateRequest))]
[JsonSerializable(typeof(IssueEditRequest))]
[JsonSerializable(typeof(LabelsAddRequest))]
[JsonSerializable(typeof(CommentRequest))]
[JsonSerializable(typeof(PrCreateRequest))]
[JsonSerializable(typeof(PrEditRequest))]
[JsonSerializable(typeof(PrReviewRequest))]
[JsonSerializable(typeof(PrMergeRequest))]
[JsonSerializable(typeof(PrUpdateBranchRequest))]
[JsonSerializable(typeof(PrDraftRequest))]
[JsonSerializable(typeof(LockRequest))]
[JsonSerializable(typeof(ReleaseCreateRequest))]
[JsonSerializable(typeof(ReleaseEditRequest))]
[JsonSerializable(typeof(ReleaseGenerateNotesRequest))]
[JsonSerializable(typeof(RepoCreateRequest))]
[JsonSerializable(typeof(RepoTemplateGenerateRequest))]
[JsonSerializable(typeof(RepoEditRequest))]
[JsonSerializable(typeof(SecurityAndAnalysis))]
[JsonSerializable(typeof(SecurityFeature))]
[JsonSerializable(typeof(TopicsRequest))]
[JsonSerializable(typeof(RepoRenameRequest))]
[JsonSerializable(typeof(RepoSyncRequest))]
[JsonSerializable(typeof(RepoSetDefaultRequest))]
[JsonSerializable(typeof(RepoArchiveRequest))]
[JsonSerializable(typeof(RepoForkRequest))]
[JsonSerializable(typeof(CodespaceCreateRequest))]
[JsonSerializable(typeof(RunRerunJobsRequest))]
[JsonSerializable(typeof(AssigneesRequest))]
[JsonSerializable(typeof(ReviewersRequest))]
[JsonSerializable(typeof(MilestoneRequest))]
[JsonSerializable(typeof(GraphQLRequest))]
[JsonSerializable(typeof(List<string>))]
// === 响应 DTO ===
[JsonSerializable(typeof(LabelResponse))]
[JsonSerializable(typeof(List<LabelResponse>))]
[JsonSerializable(typeof(OrgResponse))]
[JsonSerializable(typeof(List<OrgResponse>))]
[JsonSerializable(typeof(SshKeyResponse))]
[JsonSerializable(typeof(List<SshKeyResponse>))]
[JsonSerializable(typeof(GpgKeyResponse))]
[JsonSerializable(typeof(List<GpgKeyResponse>))]
[JsonSerializable(typeof(SecretListResponse))]
[JsonSerializable(typeof(VariableListResponse))]
[JsonSerializable(typeof(VariableItemResponse))]
[JsonSerializable(typeof(PublicKeyResponse))]
[JsonSerializable(typeof(SearchRepoResponse))]
[JsonSerializable(typeof(SearchIssueResponse))]
[JsonSerializable(typeof(WorkflowListResponse))]
[JsonSerializable(typeof(WorkflowResponse))]
[JsonSerializable(typeof(ReleaseResponse))]
[JsonSerializable(typeof(List<ReleaseResponse>))]
[JsonSerializable(typeof(ReleaseAssetResponse))]
[JsonSerializable(typeof(ReleaseGenerateNotesResponse))]
[JsonSerializable(typeof(AutolinkResponse))]
[JsonSerializable(typeof(List<AutolinkResponse>))]
[JsonSerializable(typeof(DeployKeyResponse))]
[JsonSerializable(typeof(List<DeployKeyResponse>))]
[JsonSerializable(typeof(GitignoreListResponse))]
[JsonSerializable(typeof(GitignoreTemplateResponse))]
[JsonSerializable(typeof(LicenseResponse))]
[JsonSerializable(typeof(List<LicenseResponse>))]
[JsonSerializable(typeof(TopicsResponse))]
[JsonSerializable(typeof(PrDetailResponse))]
[JsonSerializable(typeof(PrListItemResponse))]
[JsonSerializable(typeof(List<PrListItemResponse>))]
[JsonSerializable(typeof(PrListItemsResponse))]
[JsonSerializable(typeof(IssueDetailResponse))]
[JsonSerializable(typeof(IssueListItemResponse))]
[JsonSerializable(typeof(List<IssueListItemResponse>))]
[JsonSerializable(typeof(IssueListItemsResponse))]
[JsonSerializable(typeof(RepoDetailResponse))]
internal sealed partial class GitHubApiJsonContext : JsonSerializerContext
{
    private static readonly Lazy<GitHubApiJsonContext> s_safe = new(() => new GitHubApiJsonContext(
        new JsonSerializerOptions(Default!.Options)
        {
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            TypeInfoResolver = Default!
        }));

    /// <summary>
    /// 带 UnsafeRelaxedJsonEscaping 的上下文 — 不转义中文等非 ASCII 字符
    /// </summary>
    public static GitHubApiJsonContext Safe => s_safe.Value;
}
