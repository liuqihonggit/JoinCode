namespace McpToolDispatch;

// GitHub API 响应 DTO — 用于反序列化 GitHub REST API 响应，替代 JsonDocument.Parse 手动解析
// JsonContext 用 SnakeCaseLower 策略，属性名自动转 snake_case

// === Label 响应 ===

/// <summary>Label 响应 — GET /labels</summary>
internal sealed class LabelResponse {
    /// <summary>标签 ID</summary>
    [JsonPropertyName("id")]
    public int Id { get; init; }
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

// === Org 响应 ===

/// <summary>组织响应 — GET /user/orgs</summary>
internal sealed class OrgResponse {
    /// <summary>组织登录名</summary>
    [JsonPropertyName("login")]
    public string Login { get; init; } = "";
    /// <summary>描述</summary>
    [JsonPropertyName("description")]
    public string? Description { get; init; }
}

// === SSH Key 响应 ===

/// <summary>SSH Key 响应 — GET /user/keys</summary>
internal sealed class SshKeyResponse {
    /// <summary>Key ID</summary>
    [JsonPropertyName("id")]
    public int Id { get; init; }
    /// <summary>Key 标题</summary>
    [JsonPropertyName("title")]
    public string Title { get; init; } = "";
    /// <summary>SSH public key</summary>
    [JsonPropertyName("key")]
    public string? Key { get; init; }
}

// === GPG Key 响应 ===

/// <summary>GPG Key 响应 — GET /user/gpg_keys</summary>
internal sealed class GpgKeyResponse {
    /// <summary>Key ID</summary>
    [JsonPropertyName("id")]
    public int Id { get; init; }
    /// <summary>Key ID(hex)</summary>
    [JsonPropertyName("key_id")]
    public string KeyId { get; init; } = "";
    /// <summary>ASCII armored public key</summary>
    [JsonPropertyName("public_key")]
    public string? PublicKey { get; init; }
    /// <summary>是否可签名</summary>
    [JsonPropertyName("can_sign")]
    public bool CanSign { get; init; }
}

// === Secret 响应 ===

/// <summary>Secret 列表响应 — GET /actions/secrets</summary>
internal sealed class SecretListResponse {
    /// <summary>总数</summary>
    [JsonPropertyName("total_count")]
    public int TotalCount { get; init; }
    /// <summary>secret 列表</summary>
    [JsonPropertyName("secrets")]
    public List<SecretItemResponse> Secrets { get; init; } = new();
}

/// <summary>Secret 列表项</summary>
internal sealed class SecretItemResponse {
    /// <summary>名称</summary>
    [JsonPropertyName("name")]
    public string Name { get; init; } = "";
    /// <summary>创建时间</summary>
    [JsonPropertyName("created_at")]
    public string? CreatedAt { get; init; }
}

/// <summary>公钥响应 — GET /actions/secrets/public-key</summary>
internal sealed class PublicKeyResponse {
    /// <summary>公钥 ID</summary>
    [JsonPropertyName("key_id")]
    public string KeyId { get; init; } = "";
    /// <summary>公钥(base64)</summary>
    [JsonPropertyName("key")]
    public string Key { get; init; } = "";
}

// === Variable 响应 ===

/// <summary>Variable 列表响应 — GET /actions/variables</summary>
internal sealed class VariableListResponse {
    /// <summary>总数</summary>
    [JsonPropertyName("total_count")]
    public int TotalCount { get; init; }
    /// <summary>variable 列表</summary>
    [JsonPropertyName("variables")]
    public List<VariableItemResponse> Variables { get; init; } = new();
}

/// <summary>Variable 列表项</summary>
internal sealed class VariableItemResponse {
    /// <summary>名称</summary>
    [JsonPropertyName("name")]
    public string Name { get; init; } = "";
    /// <summary>值</summary>
    [JsonPropertyName("value")]
    public string Value { get; init; } = "";
    /// <summary>创建时间</summary>
    [JsonPropertyName("created_at")]
    public string? CreatedAt { get; init; }
    /// <summary>更新时间</summary>
    [JsonPropertyName("updated_at")]
    public string? UpdatedAt { get; init; }
}

// === Search 响应 ===

/// <summary>搜索仓库响应 — GET /search/repositories</summary>
internal sealed class SearchRepoResponse {
    /// <summary>总数</summary>
    [JsonPropertyName("total_count")]
    public int TotalCount { get; init; }
    /// <summary>仓库列表</summary>
    [JsonPropertyName("items")]
    public List<SearchRepoItemResponse> Items { get; init; } = new();
}

/// <summary>搜索仓库项</summary>
internal sealed class SearchRepoItemResponse {
    /// <summary>全名(owner/repo)</summary>
    [JsonPropertyName("full_name")]
    public string FullName { get; init; } = "";
    /// <summary>star 数</summary>
    [JsonPropertyName("stargazers_count")]
    public int StargazersCount { get; init; }
    /// <summary>描述</summary>
    [JsonPropertyName("description")]
    public string? Description { get; init; }
    /// <summary>HTML URL</summary>
    [JsonPropertyName("html_url")]
    public string? HtmlUrl { get; init; }
}

/// <summary>搜索 Issue/PR 响应 — GET /search/issues</summary>
internal sealed class SearchIssueResponse {
    /// <summary>总数</summary>
    [JsonPropertyName("total_count")]
    public int TotalCount { get; init; }
    /// <summary>Issue/PR 列表</summary>
    [JsonPropertyName("items")]
    public List<SearchIssueItemResponse> Items { get; init; } = new();
}

/// <summary>搜索 Issue/PR 项</summary>
internal sealed class SearchIssueItemResponse {
    /// <summary>编号</summary>
    [JsonPropertyName("number")]
    public int Number { get; init; }
    /// <summary>标题</summary>
    [JsonPropertyName("title")]
    public string Title { get; init; } = "";
    /// <summary>状态</summary>
    [JsonPropertyName("state")]
    public string State { get; init; } = "";
    /// <summary>HTML URL</summary>
    [JsonPropertyName("html_url")]
    public string? HtmlUrl { get; init; }
    /// <summary>仓库 URL</summary>
    [JsonPropertyName("repository_url")]
    public string? RepositoryUrl { get; init; }
}

// === Workflow 响应 ===

/// <summary>Workflow 列表响应 — GET /actions/workflows</summary>
internal sealed class WorkflowListResponse {
    /// <summary>总数</summary>
    [JsonPropertyName("total_count")]
    public int TotalCount { get; init; }
    /// <summary>workflow 列表</summary>
    [JsonPropertyName("workflows")]
    public List<WorkflowResponse> Workflows { get; init; } = new();
}

/// <summary>Workflow 响应 — GET /actions/workflows/{id}</summary>
internal sealed class WorkflowResponse {
    /// <summary>Workflow ID</summary>
    [JsonPropertyName("id")]
    public long Id { get; init; }
    /// <summary>名称</summary>
    [JsonPropertyName("name")]
    public string Name { get; init; } = "";
    /// <summary>状态</summary>
    [JsonPropertyName("state")]
    public string State { get; init; } = "";
    /// <summary>路径</summary>
    [JsonPropertyName("path")]
    public string Path { get; init; } = "";
    /// <summary>HTML URL</summary>
    [JsonPropertyName("html_url")]
    public string? HtmlUrl { get; init; }
    /// <summary>YAML 定义内容</summary>
    [JsonPropertyName("definition")]
    public string? Definition { get; init; }
}

// === Release 响应 ===

/// <summary>Release asset 响应</summary>
internal sealed class ReleaseAssetResponse {
    /// <summary>Asset ID</summary>
    [JsonPropertyName("id")]
    public long Id { get; init; }
    /// <summary>Asset 名称</summary>
    [JsonPropertyName("name")]
    public string Name { get; init; } = "";
    /// <summary>大小(字节)</summary>
    [JsonPropertyName("size")]
    public long Size { get; init; }
    /// <summary>下载 URL</summary>
    [JsonPropertyName("browser_download_url")]
    public string? BrowserDownloadUrl { get; init; }
    /// <summary>digest(attestation)</summary>
    [JsonPropertyName("digest")]
    public string? Digest { get; init; }
}

/// <summary>Release 响应 — GET /releases/{id} 或 /releases/tags/{tag}</summary>
internal sealed class ReleaseResponse {
    /// <summary>Release ID</summary>
    [JsonPropertyName("id")]
    public long Id { get; init; }
    /// <summary>tag 名</summary>
    [JsonPropertyName("tag_name")]
    public string TagName { get; init; } = "";
    /// <summary>release 名</summary>
    [JsonPropertyName("name")]
    public string Name { get; init; } = "";
    /// <summary>正文</summary>
    [JsonPropertyName("body")]
    public string? Body { get; init; }
    /// <summary>是否 draft</summary>
    [JsonPropertyName("draft")]
    public bool Draft { get; init; }
    /// <summary>是否预发布</summary>
    [JsonPropertyName("prerelease")]
    public bool Prerelease { get; init; }
    /// <summary>创建时间</summary>
    [JsonPropertyName("created_at")]
    public string? CreatedAt { get; init; }
    /// <summary>发布时间</summary>
    [JsonPropertyName("published_at")]
    public string? PublishedAt { get; init; }
    /// <summary>HTML URL</summary>
    [JsonPropertyName("html_url")]
    public string? HtmlUrl { get; init; }
    /// <summary>目标 commitish</summary>
    [JsonPropertyName("target_commitish")]
    public string? TargetCommitish { get; init; }
    /// <summary>asset 列表</summary>
    [JsonPropertyName("assets")]
    public List<ReleaseAssetResponse> Assets { get; init; } = new();
}

/// <summary>generate-notes 响应 — POST /releases/generate-notes</summary>
internal sealed class ReleaseGenerateNotesResponse {
    /// <summary>生成的 notes 内容</summary>
    [JsonPropertyName("body")]
    public string? Body { get; init; }
}

// === Repo 响应 ===

/// <summary>Autolink 响应 — GET /keys/autolinks</summary>
internal sealed class AutolinkResponse {
    /// <summary>Autolink ID</summary>
    [JsonPropertyName("id")]
    public int Id { get; init; }
    /// <summary>键前缀</summary>
    [JsonPropertyName("key_prefix")]
    public string KeyPrefix { get; init; } = "";
    /// <summary>URL 模板</summary>
    [JsonPropertyName("url_template")]
    public string UrlTemplate { get; init; } = "";
}

/// <summary>Deploy Key 响应 — GET /keys</summary>
internal sealed class DeployKeyResponse {
    /// <summary>Key ID</summary>
    [JsonPropertyName("id")]
    public int Id { get; init; }
    /// <summary>标题</summary>
    [JsonPropertyName("title")]
    public string Title { get; init; } = "";
    /// <summary>是否只读</summary>
    [JsonPropertyName("read_only")]
    public bool ReadOnly { get; init; }
    /// <summary>创建时间</summary>
    [JsonPropertyName("created_at")]
    public string? CreatedAt { get; init; }
}

/// <summary>Gitignore 模板列表响应 — GET /gitignore/templates</summary>
internal sealed class GitignoreListResponse {
    /// <summary>模板名列表</summary>
    [JsonPropertyName("names")]
    public List<string> Names { get; init; } = new();
}

/// <summary>Gitignore 模板内容响应 — GET /gitignore/templates/{name}</summary>
internal sealed class GitignoreTemplateResponse {
    /// <summary>模板内容</summary>
    [JsonPropertyName("source")]
    public string? Source { get; init; }
}

/// <summary>License 响应 — GET /licenses</summary>
internal sealed class LicenseResponse {
    /// <summary>License key</summary>
    [JsonPropertyName("key")]
    public string Key { get; init; } = "";
    /// <summary>License 名称</summary>
    [JsonPropertyName("name")]
    public string Name { get; init; } = "";
    /// <summary>SPDX ID</summary>
    [JsonPropertyName("spdx_id")]
    public string SpdxId { get; init; } = "";
}

/// <summary>Topics 响应 — GET /repos/{o}/{r}/topics</summary>
internal sealed class TopicsResponse {
    /// <summary>topic 名列表</summary>
    [JsonPropertyName("names")]
    public List<string> Names { get; init; } = new();
}

// === PR/Issue/Repo 嵌套引用 ===

/// <summary>用户引用(嵌套对象,只需 login)</summary>
internal sealed class UserRefResponse {
    /// <summary>登录名</summary>
    [JsonPropertyName("login")]
    public string Login { get; init; } = "";
}

/// <summary>分支引用(嵌套对象,只需 ref)</summary>
internal sealed class BranchRefResponse {
    /// <summary>分支名</summary>
    [JsonPropertyName("ref")]
    public string Ref { get; init; } = "";
}

/// <summary>标签引用(嵌套对象,只需 name)</summary>
internal sealed class LabelRefResponse {
    /// <summary>标签名</summary>
    [JsonPropertyName("name")]
    public string Name { get; init; } = "";
}

// === PR 详情/列表 ===

/// <summary>PR 详情响应 — GET /pulls/{n}</summary>
internal sealed class PrDetailResponse {
    /// <summary>PR 编号</summary>
    [JsonPropertyName("number")]
    public int Number { get; init; }
    /// <summary>标题</summary>
    [JsonPropertyName("title")]
    public string Title { get; init; } = "";
    /// <summary>状态</summary>
    [JsonPropertyName("state")]
    public string State { get; init; } = "";
    /// <summary>是否 draft</summary>
    [JsonPropertyName("draft")]
    public bool Draft { get; init; }
    /// <summary>是否可合并</summary>
    [JsonPropertyName("mergeable")]
    public bool? Mergeable { get; init; }
    /// <summary>可合并状态</summary>
    [JsonPropertyName("mergeable_state")]
    public string? MergeableState { get; init; }
    /// <summary>作者</summary>
    [JsonPropertyName("user")]
    public UserRefResponse? User { get; init; }
    /// <summary>head 分支</summary>
    [JsonPropertyName("head")]
    public BranchRefResponse? Head { get; init; }
    /// <summary>base 分支</summary>
    [JsonPropertyName("base")]
    public BranchRefResponse? Base { get; init; }
    /// <summary>新增行数</summary>
    [JsonPropertyName("additions")]
    public int Additions { get; init; }
    /// <summary>删除行数</summary>
    [JsonPropertyName("deletions")]
    public int Deletions { get; init; }
    /// <summary>变更文件数</summary>
    [JsonPropertyName("changed_files")]
    public int ChangedFiles { get; init; }
    /// <summary>HTML URL</summary>
    [JsonPropertyName("html_url")]
    public string? HtmlUrl { get; init; }
}

/// <summary>PR 列表项 — pulls API 数组元素</summary>
internal sealed class PrListItemResponse {
    /// <summary>PR 编号</summary>
    [JsonPropertyName("number")]
    public int Number { get; init; }
    /// <summary>标题</summary>
    [JsonPropertyName("title")]
    public string Title { get; init; } = "";
    /// <summary>状态</summary>
    [JsonPropertyName("state")]
    public string State { get; init; } = "";
    /// <summary>作者</summary>
    [JsonPropertyName("user")]
    public UserRefResponse? User { get; init; }
}

/// <summary>PR 列表 items 包装 — search API {items:[...]} 格式</summary>
internal sealed class PrListItemsResponse {
    /// <summary>PR 列表</summary>
    [JsonPropertyName("items")]
    public List<PrListItemResponse> Items { get; init; } = new();
}

// === Issue 详情/列表 ===

/// <summary>Issue 详情响应 — GET /issues/{n}</summary>
internal sealed class IssueDetailResponse {
    /// <summary>Issue 编号</summary>
    [JsonPropertyName("number")]
    public int Number { get; init; }
    /// <summary>标题</summary>
    [JsonPropertyName("title")]
    public string Title { get; init; } = "";
    /// <summary>状态</summary>
    [JsonPropertyName("state")]
    public string State { get; init; } = "";
    /// <summary>作者</summary>
    [JsonPropertyName("user")]
    public UserRefResponse? User { get; init; }
    /// <summary>HTML URL</summary>
    [JsonPropertyName("html_url")]
    public string? HtmlUrl { get; init; }
    /// <summary>创建时间</summary>
    [JsonPropertyName("created_at")]
    public string? CreatedAt { get; init; }
    /// <summary>标签列表</summary>
    [JsonPropertyName("labels")]
    public List<LabelRefResponse> Labels { get; init; } = new();
}

/// <summary>Issue 列表项 — issues API 数组元素</summary>
internal sealed class IssueListItemResponse {
    /// <summary>Issue 编号</summary>
    [JsonPropertyName("number")]
    public int Number { get; init; }
    /// <summary>标题</summary>
    [JsonPropertyName("title")]
    public string Title { get; init; } = "";
    /// <summary>状态</summary>
    [JsonPropertyName("state")]
    public string State { get; init; } = "";
    /// <summary>作者</summary>
    [JsonPropertyName("user")]
    public UserRefResponse? User { get; init; }
}

/// <summary>Issue 列表 items 包装 — search API {items:[...]} 格式</summary>
internal sealed class IssueListItemsResponse {
    /// <summary>Issue 列表</summary>
    [JsonPropertyName("items")]
    public List<IssueListItemResponse> Items { get; init; } = new();
}

// === Repo 详情 ===

/// <summary>Repo 详情响应 — GET /repos/{o}/{r}</summary>
internal sealed class RepoDetailResponse {
    /// <summary>仓库名</summary>
    [JsonPropertyName("name")]
    public string Name { get; init; } = "";
    /// <summary>全名(owner/repo)</summary>
    [JsonPropertyName("full_name")]
    public string FullName { get; init; } = "";
    /// <summary>是否私有</summary>
    [JsonPropertyName("private")]
    public bool Private { get; init; }
    /// <summary>默认分支</summary>
    [JsonPropertyName("default_branch")]
    public string? DefaultBranch { get; init; }
    /// <summary>star 数</summary>
    [JsonPropertyName("stargazers_count")]
    public int StargazersCount { get; init; }
    /// <summary>fork 数</summary>
    [JsonPropertyName("forks_count")]
    public int ForksCount { get; init; }
    /// <summary>HTML URL</summary>
    [JsonPropertyName("html_url")]
    public string? HtmlUrl { get; init; }
}

// === Comment / PrStatus ===

/// <summary>评论响应 — GET /issues/{n}/comments</summary>
internal sealed class CommentResponse {
    /// <summary>正文</summary>
    [JsonPropertyName("body")]
    public string Body { get; init; } = "";
    /// <summary>作者</summary>
    [JsonPropertyName("user")]
    public UserRefResponse? User { get; init; }
}

/// <summary>PR 状态项 — gh pr status 列表元素</summary>
internal sealed class PrStatusItemResponse {
    /// <summary>PR 编号</summary>
    [JsonPropertyName("number")]
    public int Number { get; init; }
    /// <summary>标题</summary>
    [JsonPropertyName("title")]
    public string Title { get; init; } = "";
    /// <summary>作者</summary>
    [JsonPropertyName("user")]
    public UserRefResponse? User { get; init; }
    /// <summary>head 分支</summary>
    [JsonPropertyName("head")]
    public BranchRefResponse? Head { get; init; }
    /// <summary>是否 draft</summary>
    [JsonPropertyName("draft")]
    public bool Draft { get; init; }
    /// <summary>是否可合并</summary>
    [JsonPropertyName("mergeable")]
    public bool? Mergeable { get; init; }
}

/// <summary>PR 引用(存在性检测用)</summary>
internal sealed class PullRequestRefResponse;

/// <summary>Issue 状态项 — gh issue status 列表元素</summary>
internal sealed class IssueStatusItemResponse {
    /// <summary>Issue 编号</summary>
    [JsonPropertyName("number")]
    public int Number { get; init; }
    /// <summary>标题</summary>
    [JsonPropertyName("title")]
    public string Title { get; init; } = "";
    /// <summary>作者</summary>
    [JsonPropertyName("user")]
    public UserRefResponse? User { get; init; }
    /// <summary>PR 引用(存在则跳过,该条目是 PR 不是 Issue)</summary>
    [JsonPropertyName("pull_request")]
    public PullRequestRefResponse? PullRequest { get; init; }
}
