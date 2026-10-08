# gh CLI 参数对齐文档

> jcc gh 是独立实现的 GitHub CLI（HttpClient 直调 REST API），不是系统 gh CLI 的包装/转发。
> AI 训练数据中系统 gh CLI 用法占主导，jcc gh 参数名不同，本文档记录所有差异和映射。

## 长选项别名映射（ResolveGhCliAlias）

三种映射模式：
- **FixedValue**：固定值（`--auto` → `auto_merge=true`）
- **TakeNextToken**：取下一 token 作值（`--job 456` → `job_id=456`）
- **RenameOnly**：仅重命名，值按 inlineValue 或 bool flag 逻辑（`--enable-issues` → `has_issues=true`）

| 系统 gh CLI 写法 | jcc gh 正确写法 | 状态 |
|-----------------|----------------|------|
| `--auto` | `--auto_merge` | ✅ 已自动映射（FixedValue） |
| `--squash` | `--merge_method squash` | ✅ 已自动映射 |
| `--merge` | `--merge_method merge` | ✅ 已自动映射 |
| `--rebase` | `--merge_method rebase` | ✅ 已自动映射 |
| `--failed` | `--failed_only` | ✅ 已自动映射（gh run rerun --failed） |
| `--job` | `--job_id` | ✅ 已自动映射（gh run view --job，TakeNextToken） |
| `--private`/`--public`/`--internal` | `--visibility private/public/internal` | ✅ 已自动映射（gh repo create） |
| `--duplicate` | `--duplicate_of` | ✅ 已自动映射（gh issue close --duplicate，TakeNextToken） |
| `--completed`/`--not-planned` | `--reason completed/not_planned` | ✅ 已自动映射（gh issue close） |
| `--approve`/`--request-changes`/`--comment` | `--action approve/request_changes/comment` | ✅ 已自动映射（gh pr review） |
| `--enable-issues`/`--enable-wiki`/`--enable-projects` | `--has_issues`/`--has_wiki`/`--has_projects` | ✅ 已自动映射（gh repo edit，RenameOnly） |
| `--latest` | `--make_latest` | ✅ 已自动映射（gh release create/edit，RenameOnly） |
| `--event` | `--event_type` | ✅ 已自动映射（gh run list，TakeNextToken） |
| `--notes-file` | `--notes_file` | ✅ 连字符自动归一化（handler 读文件内容作为 notes） |
| `--body-file` | `--body_file` | ✅ 连字符自动归一化（handler 读文件内容作为 body） |
| `--add-label`/`--remove-label` | `--label` | ⚠️ jcc 用 `--label` 替换全部标签（非追加/移除），错误提示会建议 `--label` |
| `--delete-branch` | `--delete_branch` | ✅ 连字符自动归一化 |
| `--json number,title,url` | `--json number,title,url` | ✅ 已支持精确字段选择（FilterJsonFields） |
| `-f key=value` | `--fields key=value` | ✅ 已自动映射（短选项 -f → fields，重复 -f 追加逗号分隔） |
| `--version` | `jcc gh --help` | ❌ 需改写（jcc 子命令不支持 --version） |

## 短选项映射（ResolveGhShortOption）

通用规则：
- `-L` → `limit`（所有 `_list` 命令）
- `-w` → `web`（所有 `_view` 命令）

per-command 映射（按工具名 + 短字母）：

| 工具 | 短选项 | 映射到 | 说明 |
|------|--------|--------|------|
| gh_pr_list | `-s` | state | |
| gh_pr_list | `-S` | search | |
| gh_pr_list | `-a` | author | |
| gh_pr_list | `-A` | assignee | |
| gh_pr_list | `-l` | label | |
| gh_pr_list | `-B` | base | |
| gh_pr_list | `-H` | head | |
| gh_pr_list | `-d` | draft | |
| gh_pr_view | `-c` | comments | |
| gh_pr_create | `-t` | title | |
| gh_pr_create | `-b` | body | |
| gh_pr_create | `-F` | body_file | |
| gh_pr_create | `-B` | base | |
| gh_pr_create | `-H` | head | |
| gh_pr_create | `-d` | draft | |
| gh_pr_create | `-l` | label | |
| gh_pr_create | `-A` | assignee | |
| gh_pr_create | `-r` | reviewer | |
| gh_pr_create | `-p` | project | |
| gh_pr_create | `-m` | milestone | |
| gh_issue_list | `-s` | state | |
| gh_issue_list | `-S` | search | |
| gh_issue_list | `-a` | author | |
| gh_issue_list | `-A` | assignee | |
| gh_issue_list | `-l` | label | |
| gh_issue_view | `-c` | comments | |
| gh_issue_create | `-t` | title | |
| gh_issue_create | `-b` | body | |
| gh_issue_create | `-F` | body_file | |
| gh_issue_create | `-l` | label | |
| gh_issue_create | `-A` | assignee | |
| gh_issue_create | `-p` | project | |
| gh_issue_create | `-m` | milestone | |
| gh_repo_list | `-l` | language | |
| gh_repo_create | `-n` | name | |
| gh_run_list | `-w` | workflow | |
| gh_run_list | `-e` | event | |
| gh_run_list | `-s` | status | per-command 覆盖（非 state） |
| gh_run_list | `-B` | branch | |
| gh_run_list | `-u` | user | |
| gh_run_view | `-l` | log | |
| gh_run_view | `-j` | job | |
| gh_release_create | `-t` | title | |
| gh_release_create | `-n` | notes | |
| gh_release_create | `-F` | notes_file | per-command 覆盖（非 body_file） |
| gh_release_create | `-d` | draft | |
| gh_release_create | `-p` | prerelease | |
| gh_api | `-f` | fields | 重复 -f 追加逗号分隔 |

支持形式：
- `-L 5`（空格分隔）
- `-L5`（内联值）
- `-d`（bool flag，无值=true）
- `-f name=test -f color=ff0000`（重复追加 → `fields="name=test,color=ff0000"`）

## gh 分组清单（28 个）

| 分组 | 说明 | 单级命令 |
|------|------|----------|
| pr | PR 管理 | |
| issue | Issue 管理 | |
| repo | 仓库管理 | |
| release | Release 管理 | |
| run | Actions Run 管理 | |
| branch | 分支保护 | |
| api | 通用 REST 调用 | ✅ |
| label | 标签管理 | |
| search | 搜索 | |
| workflow | Workflow 管理 | |
| auth | 认证 | |
| config | 配置 | |
| gist | Gist 管理 | |
| org | 组织管理 | |
| ssh-key | SSH Key | 连字符分组名 |
| gpg-key | GPG Key | 连字符分组名 |
| secret | Secret 管理 | |
| variable | Variable 管理 | |
| cache | Actions 缓存 | |
| ruleset | 仓库规则集 | |
| codespace | Codespace | |
| discussion | Discussion | |
| project | Project | |
| alias | 别名 | |
| extension | 扩展 | |
| browse | 浏览器打开 | ✅ |
| status | 跨仓库状态 | ✅ |
| licenses | 许可证列表 | ✅ |

连字符分组名（`ssh-key`/`gpg-key`）自动转下划线拼接工具名：`gh ssh-key list` → `gh_ssh_key_list`。

## 已知限制

| 问题 | 状态 | 说明 |
|------|------|------|
| jcc 启动 ~6 秒 | ⚠️ 待优化 | `BuildHostAsync` 每次创建完整 DI 容器，需架构改动（Host 缓存或轻量级 Host） |
| optional 参数不能用位置参数 | ⚠️ 设计限制 | `gh repo clone owner/repo target-dir` 报错，需用 `--dir target-dir` |
| `gh api -f` POST 请求 | ⚠️ 设计差异 | `-f` 映射到查询参数 fields，POST 请求需用 `--body` 传请求体 |
| `gh pr edit` 全参数 | ✅ 已实现 | add_label/remove_label/milestone/remove_milestone/body_file/add_project/remove_project/attach 全部真正实现 |
| `gh issue edit` 全参数 | ✅ 已实现 | 全 18 个 GraphQL 参数真正实现: addSubIssue/removeSubIssue/addBlockedBy/removeBlockedBy/updateIssueIssueType/deleteProjectV2Item + 附件上传 |

## 根因

jcc gh 不是系统 gh CLI 的包装/转发，是独立实现（HttpClient 直调 REST API），参数名用 snake_case（`auto_merge`/`merge_method`），系统 gh CLI 用 kebab-case + 缩写（`--auto`/`--squash`）。遇到未知选项时 jcc 会建议最接近的参数（"你是不是想用 --auto_merge?"）。

## 相关文件

- `app/cli/core/commands/core/GhCommandResolver.cs` — 别名映射（ResolveGhCliAlias）、短选项映射（ResolveGhShortOption）、参数绑定（GhArgsBinder.Bind）
- `app/cli/core/commands/core/GhGroup.cs` — gh 分组枚举（28 个）
- `app/cli/core/commands/core/GhSubCommand.cs` — gh 子命令入口
- `lib/abstractions/abs_core/core_utils/constants/tool_names/GitHubToolName.cs` — 工具名枚举
- `kit/mcp/git_hub/GitHubToolHandlers.*.cs` — handler 实现
- `kit/mcp/git_hub/GitHubApiResponseDtos.cs` — 所有响应 DTO 定义
- `kit/mcp/git_hub/GitHubApiDtos.cs` — 请求 DTO + JsonContext 定义（GitHubApiJsonContext）
- `lib/infrastructure/io/process/GitCommandRunner.cs` — git 命令执行器（含默认超时）
- ADR 0089: 禁止系统 gh CLI
- ADR 0090: jcc gh CLI 子命令
- ADR 0132: jcc 编译产物部署与 gh 问题修复指南

## JsonDocument.Parse → DTO + JsonSerializer.Deserialize 重构

> 将 GitHub API 响应解析从 `JsonDocument.Parse` + `TryGetProperty` + `GetString` 手动提取改为 `JsonSerializer.Deserialize<T>` + DTO 属性访问，符合 NativeAOT + JsonContext 约束。

### 已完成（73 处）

| 批次 | 文件 | 处数 | DTO |
|------|------|------|-----|
| 1 | Label/OrgKey/Secret/Variable/Search/Workflow | 10 | LabelResponse/OrgResponse/SshKeyResponse/GpgKeyResponse/SecretListResponse/VariableListResponse/SearchRepoResponse/SearchIssueResponse/WorkflowListResponse/WorkflowResponse |
| 2 | Release/Repo | 12 | ReleaseResponse/ReleaseAssetResponse/ReleaseGenerateNotesResponse/AutolinkResponse/DeployKeyResponse/GitignoreListResponse/GitignoreTemplateResponse/LicenseResponse/TopicsResponse |
| 3 | Pr/Issue/Repo 详情+列表 | 5 | PrDetailResponse/IssueDetailResponse/RepoDetailResponse/PrListItemResponse/IssueListItemResponse |
| 4 | Comments/PrStatus | 2 | CommentResponse/PrStatusItemResponse |
| 5 | IssueStatus | 1 | IssueStatusItemResponse/PullRequestRefResponse |
| 6 | P4.cs 全部 | 20 | CacheListResponse/RulesetResponse/CodespaceListResponse + GraphQL 通用泛型包装 |
| 7 | Pr.cs 全部 | 13 | CheckRunListResponse/RequiredStatusChecksResponse/WorkflowRunListResponse |
| 8 | GraphQLEdit.cs 全部 | 7 | ProjectIdTitleItemResponse/IssueTypeItemResponse/IssueParentWrapperResponse/AttachmentUploadResponse |
| 9 | Run.cs 6处 + Issue.cs 4处 | 10 | RunDetailResponse/RunJobListResponse/RunArtifactListResponse/NodeIdResponse |
| 10 | RunPoller/LogFilter/LogFetcher/LogCache | 5 | 复用 RunDetailResponse/CheckRunListResponse/RunJobListResponse |
| 11 | BranchProtectionAuditor/Handlers/AuthConfig/Workflow/RunListBrief/RunView/Branch | 13 | MilestoneItemResponse/AuthUserResponse/WorkflowRunListBriefResponse/BranchProtectionContextsResponse |

### 保留 JsonDocument.Parse（5 处 — Utf8JsonWriter 动态字段过滤/JSON 重写）

| 文件 | 方法 | 原因 |
|------|------|------|
| GitHubToolHandlers.cs | FilterJsonFields | 通用 `--json` 参数动态字段过滤，字段列表运行时传入 |
| GitHubRunListSummarizer.cs | SummarizeRunList | Utf8JsonWriter + CopyProperty 动态字段过滤 |
| GitHubToolHandlers.Release.cs | SummarizeReleaseList | Utf8JsonWriter + CopyProperty 动态字段过滤 |
| GitHubToolHandlers.Repo.cs | SummarizeRepoList | Utf8JsonWriter + CopyProperty 动态字段过滤 |
| GitHubToolHandlers.Branch.cs | BuildFullProtectionPutBody | Utf8JsonWriter JSON 重写（保留原字段+替换 required_status_checks.checks） |

## Bridge 手写 JSON 拼接 → DTO + JsonSerializer.Serialize 重构

> 将 Bridge API 请求体/消息构造从 StringBuilder + EscapeJsonString 手写拼接改为 DTO + JsonSerializer.Serialize，符合 NativeAOT + JsonContext 约束。

### 已完成（11 处）

| 文件 | 方法 | 处数 | DTO |
|------|------|------|-----|
| BridgeSessionApi.cs | CreateAsync + UpdateTitleAsync + ReconnectAsync + ReconnectSessionAsync | 4 | BridgeCreateSessionRequestBody/BridgeSessionContextRequestBody/BridgeGitSourceRequest/BridgeUpdateTitleRequest/BridgeReconnectRequestBody/BridgeReconnectSessionRequestBody |
| BridgeDeviceTokenService.cs | EnrollTrustedDeviceAsync + ReadTokenFromStorageAsync | 3 | BridgeEnrollDeviceRequest/BridgeDeviceTokenResponse |
| BridgeCodeSessionApi.cs | CreateCodeSessionAsync | 2 | BridgeCreateCodeSessionRequest/BridgeCodeSessionResponse/BridgeCodeSessionIdResponse |
| BridgeMessaging.cs | MakeResultMessage + SendControlResponseAsync | 2 | BridgeResultMessageDto/BridgeControlResponseDto/BridgeControlResponseBodyDto |

### 删除的辅助方法

| 文件 | 方法 | 原因 |
|------|------|------|
| BridgeSessionApi.cs | EscapeJsonString | 已被 DTO + JsonSerializer 替代 |
| BridgeCodeSessionApi.cs | JsonEncode | 同上 |
| BridgeMessaging.cs | EscapeJsonString + JsonEncode | 同上 |
