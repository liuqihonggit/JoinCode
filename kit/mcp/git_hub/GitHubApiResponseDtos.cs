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
    /// <summary>commit SHA</summary>
    [JsonPropertyName("sha")]
    public string? Sha { get; init; }
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
    /// <summary>node ID(GraphQL)</summary>
    [JsonPropertyName("node_id")]
    public string? NodeId { get; init; }
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
    /// <summary>合并提交 SHA</summary>
    [JsonPropertyName("merge_commit_sha")]
    public string? MergeCommitSha { get; init; }
    /// <summary>diff URL</summary>
    [JsonPropertyName("diff_url")]
    public string? DiffUrl { get; init; }
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
    /// <summary>仓库 node_id(数字 ID)</summary>
    [JsonPropertyName("id")]
    public long Id { get; init; }
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

// === Cache 响应 ===

/// <summary>Actions 缓存列表响应 — GET /actions/caches</summary>
internal sealed class CacheListResponse {
    /// <summary>总数</summary>
    [JsonPropertyName("total_count")]
    public int TotalCount { get; init; }
    /// <summary>缓存列表</summary>
    [JsonPropertyName("actions_caches")]
    public List<CacheItemResponse> ActionsCaches { get; init; } = new();
}

/// <summary>Actions 缓存项</summary>
internal sealed class CacheItemResponse {
    /// <summary>缓存 ID</summary>
    [JsonPropertyName("id")]
    public long Id { get; init; }
    /// <summary>缓存 key</summary>
    [JsonPropertyName("key")]
    public string Key { get; init; } = "";
    /// <summary>ref</summary>
    [JsonPropertyName("ref")]
    public string Ref { get; init; } = "";
    /// <summary>大小(字节)</summary>
    [JsonPropertyName("size_in_bytes")]
    public long SizeInBytes { get; init; }
    /// <summary>最后使用时间</summary>
    [JsonPropertyName("last_used_at")]
    public string? LastUsedAt { get; init; }
}

// === Ruleset 响应 ===

/// <summary>规则集响应 — GET /rulesets 或 /rulesets/{id}</summary>
internal sealed class RulesetResponse {
    /// <summary>规则集 ID</summary>
    [JsonPropertyName("id")]
    public long Id { get; init; }
    /// <summary>名称</summary>
    [JsonPropertyName("name")]
    public string Name { get; init; } = "";
    /// <summary>目标</summary>
    [JsonPropertyName("target")]
    public string? Target { get; init; }
    /// <summary>执行级别</summary>
    [JsonPropertyName("enforcement")]
    public string? Enforcement { get; init; }
    /// <summary>HTML URL</summary>
    [JsonPropertyName("html_url")]
    public string? HtmlUrl { get; init; }
}

// === Codespace 响应 ===

/// <summary>Codespace 仓库引用(嵌套)</summary>
internal sealed class CodespaceRepositoryRefResponse {
    /// <summary>仓库全名</summary>
    [JsonPropertyName("full_name")]
    public string FullName { get; init; } = "";
}

/// <summary>Codespace git_status(嵌套)</summary>
internal sealed class CodespaceGitStatusResponse {
    /// <summary>ref</summary>
    [JsonPropertyName("ref")]
    public string? Ref { get; init; }
}

/// <summary>Codespace 项</summary>
internal sealed class CodespaceItemResponse {
    /// <summary>名称</summary>
    [JsonPropertyName("name")]
    public string Name { get; init; } = "";
    /// <summary>显示名</summary>
    [JsonPropertyName("display_name")]
    public string? DisplayName { get; init; }
    /// <summary>仓库引用</summary>
    [JsonPropertyName("repository")]
    public CodespaceRepositoryRefResponse? Repository { get; init; }
    /// <summary>状态</summary>
    [JsonPropertyName("state")]
    public string? State { get; init; }
    /// <summary>git 状态</summary>
    [JsonPropertyName("git_status")]
    public CodespaceGitStatusResponse? GitStatus { get; init; }
}

/// <summary>Codespace 列表响应 — GET /user/codespaces</summary>
internal sealed class CodespaceListResponse {
    /// <summary>codespace 列表</summary>
    [JsonPropertyName("codespaces")]
    public List<CodespaceItemResponse> Codespaces { get; init; } = new();
}

// === GraphQL 通用嵌套 ===

/// <summary>GraphQL data 包装</summary>
internal sealed class GraphQLDataResponse<T> {
    /// <summary>data 节点</summary>
    [JsonPropertyName("data")]
    public T? Data { get; init; }
}

/// <summary>GraphQL repository 包装</summary>
internal sealed class GraphQLRepositoryResponse<T> {
    /// <summary>repository 节点</summary>
    [JsonPropertyName("repository")]
    public T? Repository { get; init; }
}

/// <summary>GraphQL nodes 包装</summary>
internal sealed class GraphQLNodesResponse<T> {
    /// <summary>nodes 列表</summary>
    [JsonPropertyName("nodes")]
    public List<T> Nodes { get; init; } = new();
}

// === Discussion GraphQL 响应 ===

/// <summary>Discussion 作者引用(嵌套)</summary>
internal sealed class DiscussionAuthorRefResponse {
    /// <summary>登录名</summary>
    [JsonPropertyName("login")]
    public string Login { get; init; } = "";
}

/// <summary>Discussion 分类引用(嵌套)</summary>
internal sealed class DiscussionCategoryRefResponse {
    /// <summary>分类名</summary>
    [JsonPropertyName("name")]
    public string Name { get; init; } = "";
}

/// <summary>Discussion 列表项</summary>
internal sealed class DiscussionListItemResponse {
    /// <summary>编号</summary>
    [JsonPropertyName("number")]
    public int Number { get; init; }
    /// <summary>标题</summary>
    [JsonPropertyName("title")]
    public string Title { get; init; } = "";
    /// <summary>作者</summary>
    [JsonPropertyName("author")]
    public DiscussionAuthorRefResponse? Author { get; init; }
    /// <summary>分类</summary>
    [JsonPropertyName("category")]
    public DiscussionCategoryRefResponse? Category { get; init; }
    /// <summary>创建时间</summary>
    [JsonPropertyName("createdAt")]
    public string? CreatedAt { get; init; }
}

/// <summary>Discussion 详情</summary>
internal sealed class DiscussionDetailResponse {
    /// <summary>编号</summary>
    [JsonPropertyName("number")]
    public int Number { get; init; }
    /// <summary>标题</summary>
    [JsonPropertyName("title")]
    public string Title { get; init; } = "";
    /// <summary>正文</summary>
    [JsonPropertyName("body")]
    public string? Body { get; init; }
    /// <summary>作者</summary>
    [JsonPropertyName("author")]
    public DiscussionAuthorRefResponse? Author { get; init; }
    /// <summary>分类</summary>
    [JsonPropertyName("category")]
    public DiscussionCategoryRefResponse? Category { get; init; }
    /// <summary>创建时间</summary>
    [JsonPropertyName("createdAt")]
    public string? CreatedAt { get; init; }
    /// <summary>URL</summary>
    [JsonPropertyName("url")]
    public string? Url { get; init; }
}

/// <summary>Discussion discussions 包装(含 nodes)</summary>
internal sealed class DiscussionListWrapperResponse {
    /// <summary>discussions 节点</summary>
    [JsonPropertyName("discussions")]
    public GraphQLNodesResponse<DiscussionListItemResponse> Discussions { get; init; } = new();
}

/// <summary>Discussion 分类项(含 id)</summary>
internal sealed class DiscussionCategoryItemResponse {
    /// <summary>node ID</summary>
    [JsonPropertyName("id")]
    public string Id { get; init; } = "";
    /// <summary>分类名</summary>
    [JsonPropertyName("name")]
    public string Name { get; init; } = "";
}

/// <summary>Discussion 分类列表包装</summary>
internal sealed class DiscussionCategoryListWrapperResponse {
    /// <summary>discussionCategories 节点</summary>
    [JsonPropertyName("discussionCategories")]
    public GraphQLNodesResponse<DiscussionCategoryItemResponse> DiscussionCategories { get; init; } = new();
}

/// <summary>GraphQL node id 响应(通用,只有 id 字段)</summary>
internal sealed class GraphQLNodeIdResponse {
    /// <summary>node ID</summary>
    [JsonPropertyName("id")]
    public string Id { get; init; } = "";
}

/// <summary>Discussion 单个包装(含 discussion 节点,只有 id)</summary>
internal sealed class DiscussionSingleWrapperResponse {
    /// <summary>discussion 节点</summary>
    [JsonPropertyName("discussion")]
    public GraphQLNodeIdResponse? Discussion { get; init; }
}

/// <summary>Discussion 详情包装(含完整 discussion 节点)</summary>
internal sealed class DiscussionViewWrapperResponse {
    /// <summary>discussion 节点</summary>
    [JsonPropertyName("discussion")]
    public DiscussionDetailResponse? Discussion { get; init; }
}

// === Project GraphQL 响应 ===

/// <summary>Project 列表项</summary>
internal sealed class ProjectListItemResponse {
    /// <summary>编号</summary>
    [JsonPropertyName("number")]
    public int Number { get; init; }
    /// <summary>标题</summary>
    [JsonPropertyName("title")]
    public string Title { get; init; } = "";
    /// <summary>URL</summary>
    [JsonPropertyName("url")]
    public string? Url { get; init; }
    /// <summary>是否关闭</summary>
    [JsonPropertyName("closed")]
    public bool Closed { get; init; }
    /// <summary>状态</summary>
    [JsonPropertyName("state")]
    public string? State { get; init; }
}

/// <summary>Project projectsV2 包装(含 nodes)</summary>
internal sealed class ProjectListWrapperResponse {
    /// <summary>projectsV2 节点</summary>
    [JsonPropertyName("projectsV2")]
    public GraphQLNodesResponse<ProjectListItemResponse> ProjectsV2 { get; init; } = new();
}

/// <summary>Project item content(嵌套)</summary>
internal sealed class ProjectItemContentResponse {
    /// <summary>编号</summary>
    [JsonPropertyName("number")]
    public int Number { get; init; }
    /// <summary>标题</summary>
    [JsonPropertyName("title")]
    public string Title { get; init; } = "";
}

/// <summary>Project item</summary>
internal sealed class ProjectItemResponse {
    /// <summary>内容</summary>
    [JsonPropertyName("content")]
    public ProjectItemContentResponse? Content { get; init; }
}

/// <summary>Project items 包装(含 nodes)</summary>
internal sealed class ProjectItemsWrapperResponse {
    /// <summary>items 节点</summary>
    [JsonPropertyName("items")]
    public GraphQLNodesResponse<ProjectItemResponse> Items { get; init; } = new();
}

/// <summary>Project 详情</summary>
internal sealed class ProjectDetailResponse {
    /// <summary>标题</summary>
    [JsonPropertyName("title")]
    public string Title { get; init; } = "";
    /// <summary>URL</summary>
    [JsonPropertyName("url")]
    public string? Url { get; init; }
    /// <summary>是否关闭</summary>
    [JsonPropertyName("closed")]
    public bool Closed { get; init; }
    /// <summary>状态</summary>
    [JsonPropertyName("state")]
    public string? State { get; init; }
    /// <summary>items(含 nodes)</summary>
    [JsonPropertyName("items")]
    public GraphQLNodesResponse<ProjectItemResponse> Items { get; init; } = new();
}

/// <summary>Project projectV2 包装(含 id,用于 node_id 提取)</summary>
internal sealed class ProjectV2IdWrapperResponse {
    /// <summary>projectV2 节点</summary>
    [JsonPropertyName("projectV2")]
    public GraphQLNodeIdResponse? ProjectV2 { get; init; }
}

/// <summary>Project projectV2 详情包装(含完整 projectV2 节点)</summary>
internal sealed class ProjectV2DetailWrapperResponse {
    /// <summary>projectV2 节点</summary>
    [JsonPropertyName("projectV2")]
    public ProjectDetailResponse? ProjectV2 { get; init; }
}

/// <summary>GraphQL viewer 包装(通用)</summary>
internal sealed class GraphQLViewerResponse<T> {
    /// <summary>viewer 节点</summary>
    [JsonPropertyName("viewer")]
    public T? Viewer { get; init; }
}

/// <summary>GraphQL organization 包装(通用)</summary>
internal sealed class GraphQLOrganizationResponse<T> {
    /// <summary>organization 节点</summary>
    [JsonPropertyName("organization")]
    public T? Organization { get; init; }
}

// === CheckRun 响应 ===

/// <summary>Check-run 列表响应 — GET /commits/{sha}/check-runs</summary>
internal sealed class CheckRunListResponse {
    /// <summary>check_runs 列表</summary>
    [JsonPropertyName("check_runs")]
    public List<CheckRunItemResponse> CheckRuns { get; init; } = new();
}

/// <summary>Check-run 项</summary>
internal sealed class CheckRunItemResponse {
    /// <summary>名称</summary>
    [JsonPropertyName("name")]
    public string Name { get; init; } = "";
    /// <summary>状态(queued/in_progress/completed)</summary>
    [JsonPropertyName("status")]
    public string? Status { get; init; }
    /// <summary>结论(success/failure/cancelled/skipped/neutral/timed_out)</summary>
    [JsonPropertyName("conclusion")]
    public string? Conclusion { get; init; }
}

// === Required Status Checks 响应 ===

/// <summary>Required status checks 响应 — GET /branches/{b}/protection/required_status_checks</summary>
internal sealed class RequiredStatusChecksResponse {
    /// <summary>contexts 列表</summary>
    [JsonPropertyName("contexts")]
    public List<string> Contexts { get; init; } = new();
}

/// <summary>完整 Branch protection 响应(仅提取 required_status_checks.contexts) — 用于 BuildSyncedContexts</summary>
internal sealed class BranchProtectionContextsResponse {
    /// <summary>required_status_checks 节点</summary>
    [JsonPropertyName("required_status_checks")]
    public RequiredStatusChecksResponse? RequiredStatusChecks { get; init; }
}

// === Workflow Run 响应 ===

/// <summary>Workflow run 列表响应 — GET /actions/runs</summary>
internal sealed class WorkflowRunListResponse {
    /// <summary>workflow_runs 列表</summary>
    [JsonPropertyName("workflow_runs")]
    public List<WorkflowRunItemResponse> WorkflowRuns { get; init; } = new();
}

/// <summary>Workflow run 项</summary>
internal sealed class WorkflowRunItemResponse {
    /// <summary>run ID(GitHub API 返回数字)</summary>
    [JsonPropertyName("id")]
    public long Id { get; init; }
    /// <summary>结论</summary>
    [JsonPropertyName("conclusion")]
    public string? Conclusion { get; init; }
}

// === GraphQLEdit 辅助 DTO ===

/// <summary>Project id+title 项(用于 FindProjectIdByTitle)</summary>
internal sealed class ProjectIdTitleItemResponse {
    /// <summary>node ID</summary>
    [JsonPropertyName("id")]
    public string Id { get; init; } = "";
    /// <summary>标题</summary>
    [JsonPropertyName("title")]
    public string Title { get; init; } = "";
}

/// <summary>Project id+title 列表包装</summary>
internal sealed class ProjectIdTitleListWrapperResponse {
    /// <summary>projectsV2 节点</summary>
    [JsonPropertyName("projectsV2")]
    public GraphQLNodesResponse<ProjectIdTitleItemResponse> ProjectsV2 { get; init; } = new();
}

/// <summary>Project item content(用于 FindProjectItemId,只有 id)</summary>
internal sealed class ProjectItemIdContentResponse {
    /// <summary>node ID</summary>
    [JsonPropertyName("id")]
    public string Id { get; init; } = "";
}

/// <summary>Project item(用于 FindProjectItemId)</summary>
internal sealed class ProjectItemIdItemResponse {
    /// <summary>item node ID</summary>
    [JsonPropertyName("id")]
    public string Id { get; init; } = "";
    /// <summary>content</summary>
    [JsonPropertyName("content")]
    public ProjectItemIdContentResponse? Content { get; init; }
}

/// <summary>Project node items 包装</summary>
internal sealed class ProjectNodeItemsWrapperResponse {
    /// <summary>items 节点</summary>
    [JsonPropertyName("items")]
    public GraphQLNodesResponse<ProjectItemIdItemResponse> Items { get; init; } = new();
}

/// <summary>GraphQL node 包装(用于 FindProjectItemId)</summary>
internal sealed class GraphQLNodeWrapperResponse<T> {
    /// <summary>node 节点</summary>
    [JsonPropertyName("node")]
    public T? Node { get; init; }
}

/// <summary>Issue type 项</summary>
internal sealed class IssueTypeItemResponse {
    /// <summary>node ID</summary>
    [JsonPropertyName("id")]
    public string Id { get; init; } = "";
    /// <summary>类型名</summary>
    [JsonPropertyName("name")]
    public string Name { get; init; } = "";
}

/// <summary>Issue type 列表包装</summary>
internal sealed class IssueTypeListWrapperResponse {
    /// <summary>issueTypes 节点</summary>
    [JsonPropertyName("issueTypes")]
    public GraphQLNodesResponse<IssueTypeItemResponse> IssueTypes { get; init; } = new();
}

/// <summary>Issue parent 引用(只有 id)</summary>
internal sealed class IssueParentRefResponse {
    /// <summary>parent node ID</summary>
    [JsonPropertyName("id")]
    public string Id { get; init; } = "";
}

/// <summary>Issue parent 内容</summary>
internal sealed class IssueParentContentResponse {
    /// <summary>parent</summary>
    [JsonPropertyName("parent")]
    public IssueParentRefResponse? Parent { get; init; }
}

/// <summary>Issue parent 包装(含 issue 节点)</summary>
internal sealed class IssueParentWrapperResponse {
    /// <summary>issue 节点</summary>
    [JsonPropertyName("issue")]
    public IssueParentContentResponse? Issue { get; init; }
}

/// <summary>附件上传响应</summary>
internal sealed class AttachmentUploadResponse {
    /// <summary>附件 URL</summary>
    [JsonPropertyName("url")]
    public string? Url { get; init; }
}

// === Run 详情/列表/Job/Artifact 响应（Run.cs 专用） ===

/// <summary>Run 详情响应 — GET /actions/runs/{id}（含 status/conclusion/html_url/display_title 等关键字段）</summary>
internal sealed class RunDetailResponse {
    /// <summary>Run ID</summary>
    [JsonPropertyName("id")]
    public long Id { get; init; }
    /// <summary>Run 名称</summary>
    [JsonPropertyName("name")]
    public string? Name { get; init; }
    /// <summary>head commit SHA</summary>
    [JsonPropertyName("head_sha")]
    public string? HeadSha { get; init; }
    /// <summary>head 分支名</summary>
    [JsonPropertyName("head_branch")]
    public string? HeadBranch { get; init; }
    /// <summary>状态(queued/in_progress/completed)</summary>
    [JsonPropertyName("status")]
    public string? Status { get; init; }
    /// <summary>结论(success/failure/cancelled/timed_out/neutral)</summary>
    [JsonPropertyName("conclusion")]
    public string? Conclusion { get; init; }
    /// <summary>HTML URL</summary>
    [JsonPropertyName("html_url")]
    public string? HtmlUrl { get; init; }
    /// <summary>显示标题</summary>
    [JsonPropertyName("display_title")]
    public string? DisplayTitle { get; init; }
    /// <summary>run 编号</summary>
    [JsonPropertyName("run_number")]
    public int RunNumber { get; init; }
    /// <summary>触发事件</summary>
    [JsonPropertyName("event")]
    public string? Event { get; init; }
    /// <summary>创建时间</summary>
    [JsonPropertyName("created_at")]
    public string? CreatedAt { get; init; }
    /// <summary>更新时间</summary>
    [JsonPropertyName("updated_at")]
    public string? UpdatedAt { get; init; }
}

/// <summary>Run 列表(run_number 查找用) — GET /actions/runs</summary>
internal sealed class RunNumberLookupListResponse {
    /// <summary>workflow_runs 列表</summary>
    [JsonPropertyName("workflow_runs")]
    public List<RunNumberLookupItemResponse> WorkflowRuns { get; init; } = new();
}

/// <summary>Run 列表项(run_number 查找用)</summary>
internal sealed class RunNumberLookupItemResponse {
    /// <summary>Run ID</summary>
    [JsonPropertyName("id")]
    public long Id { get; init; }
    /// <summary>run 编号</summary>
    [JsonPropertyName("run_number")]
    public int RunNumber { get; init; }
}

/// <summary>Run job 列表响应 — GET /actions/runs/{id}/jobs</summary>
internal sealed class RunJobListResponse {
    /// <summary>job 列表</summary>
    [JsonPropertyName("jobs")]
    public List<RunJobItemResponse> Jobs { get; init; } = new();
}

/// <summary>Run job 项</summary>
internal sealed class RunJobItemResponse {
    /// <summary>Job ID</summary>
    [JsonPropertyName("id")]
    public long Id { get; init; }
    /// <summary>Job 名称</summary>
    [JsonPropertyName("name")]
    public string Name { get; init; } = "";
    /// <summary>状态</summary>
    [JsonPropertyName("status")]
    public string Status { get; init; } = "";
    /// <summary>结论</summary>
    [JsonPropertyName("conclusion")]
    public string? Conclusion { get; init; }
}

/// <summary>Run artifact 列表响应 — GET /actions/runs/{id}/artifacts</summary>
internal sealed class RunArtifactListResponse {
    /// <summary>artifact 列表</summary>
    [JsonPropertyName("artifacts")]
    public List<RunArtifactItemResponse> Artifacts { get; init; } = new();
}

/// <summary>Run artifact 项</summary>
internal sealed class RunArtifactItemResponse {
    /// <summary>Artifact ID</summary>
    [JsonPropertyName("id")]
    public long Id { get; init; }
    /// <summary>Artifact 名称</summary>
    [JsonPropertyName("name")]
    public string Name { get; init; } = "";
}

// === node_id 提取(通用) ===

/// <summary>node_id 提取响应(通用,只有 node_id 字段) — 用于从 REST API 响应提取 node_id 供 GraphQL mutation 使用</summary>
internal sealed class NodeIdResponse {
    /// <summary>node ID(GraphQL mutation 需要)</summary>
    [JsonPropertyName("node_id")]
    public string? NodeId { get; init; }
}

// === Milestone 响应(ResolveMilestoneIdAsync 专用) ===

/// <summary>Milestone 项 — GET /repos/{owner}/{repo}/milestones 返回顶层数组</summary>
internal sealed class MilestoneItemResponse {
    /// <summary>Milestone 编号</summary>
    [JsonPropertyName("number")]
    public int Number { get; init; }
    /// <summary>Milestone 标题</summary>
    [JsonPropertyName("title")]
    public string Title { get; init; } = "";
}

// === Auth User 响应(AuthConfig 专用) ===

/// <summary>Auth 用户响应 — GET /user</summary>
internal sealed class AuthUserResponse {
    /// <summary>登录名</summary>
    [JsonPropertyName("login")]
    public string Login { get; init; } = "";
    /// <summary>显示名称</summary>
    [JsonPropertyName("name")]
    public string? Name { get; init; }
}

// === Workflow Run 列表(Brief 格式化专用) ===

/// <summary>Workflow run 列表响应(Brief 格式化) — GET /actions/runs</summary>
internal sealed class WorkflowRunListBriefResponse {
    /// <summary>总数</summary>
    [JsonPropertyName("total_count")]
    public int TotalCount { get; init; }
    /// <summary>workflow_runs 列表</summary>
    [JsonPropertyName("workflow_runs")]
    public List<WorkflowRunBriefItemResponse> WorkflowRuns { get; init; } = new();
}

/// <summary>Workflow run 列表项(Brief 格式化)</summary>
internal sealed class WorkflowRunBriefItemResponse {
    /// <summary>Run ID</summary>
    [JsonPropertyName("id")]
    public long Id { get; init; }
    /// <summary>Run 名称</summary>
    [JsonPropertyName("name")]
    public string? Name { get; init; }
    /// <summary>head 分支名</summary>
    [JsonPropertyName("head_branch")]
    public string? HeadBranch { get; init; }
    /// <summary>状态(queued/in_progress/completed)</summary>
    [JsonPropertyName("status")]
    public string? Status { get; init; }
    /// <summary>结论</summary>
    [JsonPropertyName("conclusion")]
    public string? Conclusion { get; init; }
    /// <summary>run 编号</summary>
    [JsonPropertyName("run_number")]
    public int RunNumber { get; init; }
    /// <summary>创建时间</summary>
    [JsonPropertyName("created_at")]
    public string? CreatedAt { get; init; }
    /// <summary>更新时间</summary>
    [JsonPropertyName("updated_at")]
    public string? UpdatedAt { get; init; }
    /// <summary>HTML URL</summary>
    [JsonPropertyName("html_url")]
    public string? HtmlUrl { get; init; }
    /// <summary>显示标题</summary>
    [JsonPropertyName("display_title")]
    public string? DisplayTitle { get; init; }
    /// <summary>触发事件</summary>
    [JsonPropertyName("event")]
    public string? Event { get; init; }
}
