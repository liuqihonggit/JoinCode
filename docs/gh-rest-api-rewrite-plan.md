# gh_* MCP 工具重写为 REST API 直调 — 执行计划

> ADR: [0073](adr/0073-gh-rest-api-direct-call.md)
> 目标：卸载系统 `gh` CLI，jcc 自包含，AI 统一用 `jcc mcp_call gh_*` 操作 GitHub

## 现状

| 文件 | 当前实现 | 问题 |
|------|----------|------|
| `services/Mcp/src/GitHub/GitHubToolHandlers.*.cs` | 全部调 `RunGhAsync` → 起子进程 `gh` | 依赖系统 gh，卸载即瘫 |
| `infrastructure/Infrastructure/IO/Process/GitHubCommandRunner.cs` | `FileName="gh"` 起进程 | 要归档 |
| `foundation/Abstractions/.../IGitHubCommandRunner.cs` | 接口 | 要归档 |
| `composition/Composition/src/Commands/hands/Tools/InstallGitHubAppCommand.cs` | 依赖 `IGitHubCommandRunner` | 需同步改造 |

## 执行清单

### 阶段1：基础设施

- [ ] **T1** 新建 `IGitHubApiClient` 接口（Abstractions 层）
  - `SendAsync(method, path, body?, query?)` → `GitHubApiResponse`
  - `SendStreamingAsync(...)` → `IAsyncEnumerable<string>`（日志逐行）
  - 支持 token 解析、base URL、分页、rate limit、错误映射
- [ ] **T2** 新建 `GitHubApiClient` 实现（Infrastructure 层）
  - HttpClient（DI 注入，连接池 ADR 0031）
  - Token：`JCC_GITHUB_TOKEN` → `GITHUB_TOKEN` → 抛 GRD017
  - Base URL：`JCC_GITHUB_API_URL` 默认 `https://api.github.com`
  - Headers：`Authorization: Bearer {token}`、`Accept: application/vnd.github+json`、`User-Agent: jcc`
  - Rate limit：403 + `X-RateLimit-Remaining:0` → 读 `Retry-After` → 重试
  - 分页：解析 `Link` header
- [ ] **T3** 新建 GitHub DTO + `GitHubJsonContext`（源码生成器，AOT 友好）
  - PrDto、IssueDto、RepoDto、ReleaseDto、RunDto、WorkflowRunJobDto 等
- [ ] **T4** 单元测试 `GitHubApiClientTests`（用 HttpHandler mock）

### 阶段2：重写工具（按依赖顺序）

- [ ] **T5** 重写 `GitHubToolHandlers.Api.cs` → `GhApiAsync` 用 `IGitHubApiClient.SendAsync`
- [ ] **T6** 重写 `GitHubToolHandlers.Pr.cs`（8 方法）
  - view/list/diff/checks/merge/checkout/close/reopen
  - checkout 需 `git fetch + git checkout`（本地 git，非 GitHub API）
- [ ] **T7** 重写 `GitHubToolHandlers.Issue.cs`（5 方法）
  - list/view/create/close/comment
- [ ] **T8** 重写 `GitHubToolHandlers.Repo.cs`（5 方法）
  - view/clone/create/fork/list
  - clone 需本地 `git clone`（非 GitHub API）
- [ ] **T9** 重写 `GitHubToolHandlers.Release.cs`（6 方法）
  - list/view/create/download/upload/delete
  - download 已用 IDownloader，只需列表查询改 REST
  - upload 需 multipart upload to `upload_url`
- [ ] **T10** 重写 `GitHubToolHandlers.Run.cs`（4 方法，最复杂）
  - list/view/rerun/cancel
  - view --log：`GET /actions/runs/{id}/logs` 返回 zip → `ZipArchive` 解压 → 复用 `RunLogCache`

### 阶段3：收尾

- [ ] **T11** 归档 `GitHubCommandRunner.cs` + `IGitHubCommandRunner.cs` 到 `.xxx/`（ADR 0008）
- [ ] **T12** 改造 `InstallGitHubAppCommand`（去掉 `IGitHubCommandRunner` 依赖）
- [ ] **T13** 更新 DI 注册（移除 `GitHubCommandRunner`，加 `GitHubApiClient`）
- [ ] **T14** 全量编译 + 测试
- [ ] **T15** 更新 AGENTS.md：AI 用 `jcc mcp_call gh_*`，卸载系统 gh
- [ ] **T16** 处理 rg：jcc 入 PATH + AGENTS.md 规则 AI 用 jcc rg

## 每步流程（TDD 铁律）

每个 Tn 都走：🔴单元红(失败测试) → 🟢单元绿(实现) → 🔵重构 → 编译 → git 提交

## 关键约束

- AOT 兼容：禁止反射 emit / dynamic，JSON 用 `GitHubJsonContext`
- 禁止删除文件：归档到 `.xxx/`（ADR 0008）
- Token 不存在抛 GRD017，不静默失败（反例5）
- `gh_pr_checkout` / `gh_repo_clone` 涉及本地 git 操作，非纯 REST，需保留 `IProcessService` 调 `git`

<!-- 🤖 Auto Decision: 2026-09-07 -->
<!-- 决策: 用 HttpClient 直调 GitHub REST API 替代起 gh 子进程 -->
<!-- 原因: 用户要卸载系统 gh，当前 gh_* 全是包装层，必须真重写 -->
<!-- 替代方案: Octokit.NET(放弃,AOT 兼容性未知) / GraphQL v4(放弃,扁平工具用 REST 足够) -->
