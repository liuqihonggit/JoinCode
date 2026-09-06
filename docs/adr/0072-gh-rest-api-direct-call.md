# 0072. gh_* MCP 工具重写为 GitHub REST API 直调（摆脱系统 gh 依赖）

- 状态：proposed
- 日期：2026-09-07
- 决策者：项目架构组

## 背景

用户诉求：卸载系统 `gh` CLI，让 AI 统一用 `jcc mcp_call gh_*` 操作 GitHub，实现"jcc 自包含、不依赖外部 gh"。

当前 `GitHubToolHandlers`（`services/Mcp/src/GitHub/`）共 6 大类、20+ 方法，**全部是系统 `gh` CLI 的包装层**：

- 调用链：`GhPrViewAsync` → `RunGhAsync("pr view ...")` → `IGitHubCommandRunner.ExecuteAsync` → `GitHubCommandRunner`（`FileName="gh"` 起子进程）
- 证据：`GitHubToolHandlers.Pr.cs:14`、`GitHubToolHandlers.Api.cs:51`、`GitHubCommandRunner.cs:53`
- 后果：卸载系统 `gh` 后，所有 `jcc mcp_call gh_*` 报"找不到 gh"全部失效

对比 `jcc rg`（ADR 0070）已是真独立实现（RgEngine，不依赖系统 rg），可直接替代。`gh_*` 必须同样重写为真独立实现才能替代系统 gh。

## 决策

### 决策1：HttpClient 直调 GitHub REST API，不再起 gh 子进程

**选择**：新建 `IGitHubApiClient` + `GitHubApiClient`（HttpClient 直调 `https://api.github.com/`），`GitHubToolHandlers` 改依赖 `IGitHubApiClient`，删除 `RunGhAsync` → `_gh.ExecuteAsync` 调用链。

**理由**：
- 真正摆脱系统 gh 依赖，jcc 自包含
- HttpClient 连接池复用（ADR 0031），比每次起 gh 子进程开销低
- 直接拿 JSON 响应，无需 `gh --json` 中转 + JsonDocument 二次解析
- 错误处理更精确（HTTP 状态码 + GitHub 错误响应体），优于 gh 退出码 + stderr 文本

### 决策2：Token 来源 — JCC_GITHUB_TOKEN 优先，GITHUB_TOKEN 兜底

**选择**：`JCC_GITHUB_TOKEN` 环境变量优先，回退 `GITHUB_TOKEN`，都没有则抛 `ConfigurationException[GRD017]`。

**理由**：
- `JCC_GITHUB_TOKEN` 已在 `LocalizerInitializer` 定义（StringKey.GitHubServiceNotConfigured）
- `GITHUB_TOKEN` 是 GitHub 官方标准变量，CI 环境自动注入
- 两个都没有说明用户未配置，应明确报错而非静默失败（反例5：配置大于代码）

### 决策3：API Base URL 可配置（支持 GitHub Enterprise）

**选择**：`JCC_GITHUB_API_URL` 环境变量，默认 `https://api.github.com`。

**理由**：
- GitHub Enterprise 用 `https://{host}/api/v3`，硬编码 base URL 无法支持
- 环境变量配置，零代码改动切换

### 决策4：JSON 解析用 JsonContext 源码生成器（AOT 友好）

**选择**：为 GitHub 响应 DTO 定义 `JsonContext`，用 `JsonSerializer.Deserialize<T>(ref reader, options)`。

**理由**：
- 项目强制 NativeAOT（ADR 0002），禁止反射 emit / dynamic
- `JsonDocument` 虽 AOT 兼容但每次解析分配，`JsonContext` 编译期生成代码零分配
- DTO 类型明确，优于 `JsonElement.GetProperty("xxx")` 字符串硬编码

### 决策5：分页用 Link header，rate limit 用 Retry-After

**选择**：
- 分页：解析响应 `Link` header（`<url>; rel="next"`），`paginate=true` 时自动跟随
- Rate limit：HTTP 403 + `X-RateLimit-Remaining: 0` → 读 `Retry-After` header → `Task.Delay` 后重试
- 二次 rate limit（`secondary rate limit`）同样处理

**理由**：
- GitHub REST API 标准分页机制是 Link header，非页码查询参数
- Rate limit 命中不报错而重试，对齐 `GitHubCommandRunner` 现有指数退避行为

### 决策6：Run 日志用 zip 流解压

**选择**：`GET /repos/{owner}/{repo}/actions/runs/{run_id}/logs` 返回 zip 流，用 `ZipArchive` 解压逐文件读取，复用现有 `RunLogCache` 两级缓存。

**理由**：
- GitHub REST API 的 run logs 端点返回 zip（非文本），与 `gh run view --log` 输出格式不同
- `ZipArchive` 在 System.IO.Compression，AOT 兼容
- 复用 `RunLogCache`（Level1 摘要 + Level2 section 内容）保持跨进程缓存语义

### 决策7：归档 GitHubCommandRunner 到 .xxx/，不删除

**选择**：`GitHubCommandRunner.cs` + `IGitHubCommandRunner.cs` 移到 `.xxx/`（按 ADR 0008 归档规范），DI 注册移除，`GitHubToolHandlers` 改依赖 `IGitHubApiClient`。

**理由**：
- AGENTS.md 红线"禁止删除文件，用移动代替删除"
- 归档保留历史可追溯，符合渐进式安全原则
- `InstallGitHubAppCommand` 仍依赖 `IGitHubCommandRunner`，需同步改造或保留接口

### 决策8：渐进式重写顺序（按依赖关系）

**选择**：api → pr → issue → repo → release → run

**理由**：
- `gh_api` 是通用调用，其他工具可复用 `IGitHubApiClient.SendAsync` 基础方法
- pr/issue/repo 是纯 REST JSON，最简单
- release 下载已用 IDownloader，只需把列表查询改 REST
- run 日志最复杂（zip 解压），放最后

## 替代方案

### 方案1：保留 GitHubCommandRunner 作 fallback

放弃。网络失败时回退起 gh 子进程——但用户目标是卸载 gh，fallback 到不存在的工具更糟。要么真直调，要么不重写。

### 方案2：用 Octokit.NET 库

放弃。引入第三方库，需验证 AOT 兼容性（ADR 0002 拒绝不兼容 AOT 的包），且 Octokit 维护活跃度下降。HttpClient 直调更轻量可控。

### 方案3：用 GraphQL API v4

放弃。GraphQL 适合复杂嵌套查询（一次拿 PR+review+comments），但当前工具是扁平的（pr view / issue list），REST 足够。GraphQL 需构造查询字符串，错误处理更复杂。

### 方案4：只重写高频工具，低频仍用 gh

放弃。用户要卸载 gh，半重写半包装无法实现卸载目标。必须全重写。

## 后果

- 正面：
  - jcc 自包含，可真正卸载系统 gh
  - HttpClient 连接池比起子进程开销低
  - 错误处理更精确（HTTP 状态码 vs 退出码）
  - JSON 直调，无需 gh --json 中转
  - 支持 GitHub Enterprise（可配置 API URL）
- 负面：
  - 重写工作量大（20+ 方法 + 测试）
  - Run 日志 zip 解压增加复杂度
  - Token 必须用户手动配置（gh CLI 自动用 keyring，REST 直调读环境变量）
  - `InstallGitHubAppCommand` 依赖 `IGitHubCommandRunner`，需同步改造
- 中性：
  - 新增 `IGitHubApiClient` + `GitHubApiClient` + GitHub DTO + JsonContext
  - 归档 `GitHubCommandRunner` + `IGitHubCommandRunner` 到 `.xxx/`
  - AGENTS.md 更新：AI 用 `jcc mcp_call gh_*`，卸载系统 gh

## 渐进式执行顺序

1. ⬜ 写 ADR 0072（本文档）
2. ⬜ 写任务文档 `docs/gh-rest-api-rewrite-plan.md`
3. ⬜ 搭基础：`IGitHubApiClient` + `GitHubApiClient`（HttpClient + token + 错误处理 + 分页 + rate limit）+ JsonContext + 单元测试
4. ⬜ 重写 `gh_api`（通用 API 调用）+ 测试
5. ⬜ 重写 `gh_pr_*`（8 方法）+ 测试
6. ⬜ 重写 `gh_issue_*`（5 方法）+ 测试
7. ⬜ 重写 `gh_repo_*`（5 方法）+ 测试
8. ⬜ 重写 `gh_release_*`（6 方法）+ 测试
9. ⬜ 重写 `gh_run_*`（4 方法，zip 解压）+ 测试
10. ⬜ 归档 `GitHubCommandRunner` + `IGitHubCommandRunner` 到 `.xxx/`，更新 DI
11. ⬜ 更新 AGENTS.md：AI 用 `jcc mcp_call gh_*`，卸载系统 gh
12. ⬜ 处理 rg：jcc 入 PATH + AGENTS.md 规则 AI 用 jcc rg
