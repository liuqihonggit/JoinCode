# GitHub MCP 工具复刻任务清单

## 背景

用户需求：复刻 git 的 `gh` CLI 工具到项目 MCP，直接暴露给 LLM 调用，不经过 bash/shell（避免管道超时中断）。尤其要优化下载类操作（Release asset 下载复用项目已有的多线程分片并行下载 `IDownloader`），解决"总是下载失败、用户体验不好"的痛点。AGENTS.md 中 gh CLI 排错避坑指南（坑1-5）全部需要内化为工具逻辑。

## 现状（探索结论）

- **已有基建**：`IGitHubCommandRunner`（`infrastructure/Infrastructure/IO/Process/GitHubCommandRunner.cs:14`）已含重试/超时/`GH_TERMINAL_PROMPT=0`/PR body 自动生成，但只暴露 `CreatePr`/`ListPrs`，其余 gh 能力未暴露给 LLM
- **已有下载器**：`IDownloader`（`infrastructure/Infrastructure/Network/Downloader/`）多线程分片 + 断点续传已完整实现，已暴露为 `download_file` MCP 工具（`DownloadToolHandlers.cs:20`）
- **MCP 机制**：`[McpToolDispatch]` + `[McpTool]` + 源码生成器完备，DI 自动装配
- **缺口**：无 `GitHubToolHandlers` 把 gh 的 PR/Issue/Run/Release/Repo/api 全套作为 MCP 工具暴露给 LLM

## 决策依据（用户确认）

1. **范围**：全部一次做完（PR + Run + Release + Issue + Repo + gh api 通用调用，约 29 个工具方法）
2. **位置**：`services/Mcp/src/GitHub/`（与 `SubscribePRToolHandlers` 同侧，GitHub 远程操作属于服务层，依赖方向干净）
3. **ToolCategory**：新增 `ToolCategory.GitHub` + `[EnumValue("github")]`（语义清晰，与本地 `Git` 区分）

## 避坑要点（AGENTS.md 坑1-5 内化）

| 坑 | 根因 | 复刻对策 |
|----|------|----------|
| 坑1 `gh api`+jq 引号被吃 | PowerShell 双引号边界 | MCP 工具用 C# Process 直接调 gh，不经 PowerShell；禁用 `--jq`，改 `gh api ... --jq '.'` 输出完整 JSON 后用 `JsonDocument` + `RelaxedJsonSerializer` 解析（AOT 友好） |
| 坑2 `gh run view --log-failed` 超时 | 日志几万行 | 工具加 `maxLines` 参数（默认 200，超出截断并提示）；优先 `--job <id> --log` 精准拉单 job；超时限制 30s |
| 坑3 `gh api .../logs` 被 Sandbox 拦 | 网络策略 | 工具优先用 `gh run view --job <id> --log`（走不同 API 路径），不用 `gh api .../logs`；描述中注明 |
| 坑4 `gh pr checks` 格式 | `skipping` 非失败 | 工具解析结构化输出，返回 JSON；正确处理 `skipping` 语义（不当作失败）；用 `WithEntityMetadata` 暴露结构化字段 |
| 坑5 管道+Select-String 超时 | PowerShell 管道死锁 | MCP 工具用 `IProcessService` 捕获 stdout 到 `StringBuilder`，不用 PowerShell 管道；大日志重定向写文件再查 |

## 任务列表

### P0 基建 [待办]
- [ ] P0.1 `ToolCategory.cs` 新增 `GitHub` 枚举值 + `[EnumValue("github")]`
- [ ] P0.2 全量重建 Generators.slnx + Foundation.slnx（源码生成器扫描新枚举）
- [ ] P0.3 新建 `GitHubToolNameConstants` 常量类（SSOT，所有 29 个工具名）
- [ ] P0.4 新建 `services/Mcp/src/GitHub/GitHubToolHandlers.cs` 骨架 + 构造函数注入 `IGitHubCommandRunner`/`IDownloader`/`IFileSystem`/`ILogger`
- [ ] P0.5 编译 `services/Mcp/src/Mcp.csproj` 通过

### P1 PR 全套（8 个工具） [待办]
- [ ] P1.1 `gh_pr_view` — PR 详情（号/标题/状态/URL/body），支持 `--json` 结构化返回
- [ ] P1.2 `gh_pr_list` — PR 列表（state/limit/author 过滤），复用 `IGitHubCommandRunner.ListPrsAsync`
- [ ] P1.3 `gh_pr_diff` — PR diff（`gh pr diff`，返回 patch 文本）
- [ ] P1.4 `gh_pr_checks` — CI 检查（解析 `pass/fail/pending/skipping`，`skipping` 不当失败，`WithEntityMetadata` 结构化）
- [ ] P1.5 `gh_pr_merge` — 合并 PR（`--squash`/`--merge`/`--rebase` + `--auto`）
- [ ] P1.6 `gh_pr_checkout` — 检出 PR 分支
- [ ] P1.7 `gh_pr_close` — 关闭 PR
- [ ] P1.8 `gh_pr_reopen` — 重开 PR
- [ ] P1.9 编译 + 单元测试（mock `IGitHubCommandRunner`）+ commit

### P2 Run 全套（4 个工具，含日志截断） [待办]
- [ ] P2.1 `gh_run_list` — Actions Run 列表（`--limit`/`--status`/`--branch` 过滤）
- [ ] P2.2 `gh_run_view` — Run 详情 + 日志（`--job <id>` 精准拉，`maxLines` 截断默认 200，超时 30s，优先 `gh run view --job --log` 不用 `gh api .../logs`）
- [ ] P2.3 `gh_run_rerun` — 重跑失败 job（`--failed`）
- [ ] P2.4 `gh_run_cancel` — 取消 Run
- [ ] P2.5 编译 + 单元测试 + commit

### P3 Release 全套（6 个工具，download 复用 IDownloader） [待办]
- [ ] P3.1 `gh_release_list` — Release 列表
- [ ] P3.2 `gh_release_view` — Release 详情（含 asset 列表）
- [ ] P3.3 `gh_release_create` — 创建 Release（tag/name/notes/draft/prerelease）
- [ ] P3.4 `gh_release_download` — 下载 Release asset（**复用 `IDownloader` 多线程分片 + 断点续传**，`max_threads`/`resume` 参数透传，解决下载失败痛点）
- [ ] P3.5 `gh_release_upload` — 上传 asset 到 Release
- [ ] P3.6 `gh_release_delete` — 删除 Release
- [ ] P3.7 编译 + 单元测试 + commit

### P4 Issue 全套（5 个工具） [待办]
- [ ] P4.1 `gh_issue_list` — Issue 列表（state/label/assignee 过滤）
- [ ] P4.2 `gh_issue_view` — Issue 详情
- [ ] P4.3 `gh_issue_create` — 创建 Issue（title/body/label/assignee）
- [ ] P4.4 `gh_issue_close` — 关闭 Issue
- [ ] P4.5 `gh_issue_comment` — 评论 Issue
- [ ] P4.6 编译 + 单元测试 + commit

### P5 Repo 全套（5 个工具） [待办]
- [ ] P5.1 `gh_repo_view` — Repo 详情
- [ ] P5.2 `gh_repo_clone` — 克隆 Repo（`-- --depth=1` 浅克隆支持）
- [ ] P5.3 `gh_repo_create` — 创建 Repo（public/private/internal）
- [ ] P5.4 `gh_repo_fork` — Fork Repo
- [ ] P5.5 `gh_repo_list` — Repo 列表（`--limit`）
- [ ] P5.6 编译 + 单元测试 + commit

### P6 gh api 通用调用（1 个工具） [待办]
- [ ] P6.1 `gh_api` — 通用 REST API 调用（method/path/fields/paginate，**禁用 `--jq`**，输出完整 JSON 用 `JsonDocument` + `RelaxedJsonSerializer` 解析后结构化返回）
- [ ] P6.2 编译 + 单元测试 + commit

### P7 收尾 [待办]
- [ ] P7.1 全量重建 `Services.slnx` + 上层链路编译通过
- [ ] P7.2 全量单元测试通过（`dotnet test Services.slnx -c Release /p:SkipLocalPack=true --filter "Category!=Integration"`）
- [ ] P7.3 更新本任务清单 checkbox + 记数（29 个工具方法）
- [ ] P7.4 记录自主决策到本文件末尾（`<!-- 🤖 Auto Decision -->` 格式）
- [ ] P7.5 AGENTS.md gh CLI 避坑章节标注"MCP 工具已内化"反向引用

## 执行原则

- **渐进式**：每完成一个 P 段就 编译 → 单元测试 → commit，持续推进不中断
- **TDD**：每个工具方法先写单元测试（mock `IGitHubCommandRunner`）再写实现
- **守红线**：不删文件，归档到 `.xxx/{名}.{后缀}.{时间戳}.del`
- **不预估时间**：AI 编码迅速，时间预估无意义
- **中文描述**：所有 `[McpTool]` description 用中文（对齐 AGENTS.md 规则2）
- **SSOT**：工具名用 `GitHubToolNameConstants.Xxx` 常量，禁止硬编码字符串
- **AOT 友好**：JSON 解析用 `JsonDocument` + `RelaxedJsonSerializer`，禁用 `dynamic`/`JsonNode`/`--jq`
- **结构化返回**：PR/Issue/Run 等用 `WithEntityMetadata` 暴露结构化字段，方便 LLM 推理
- **全量重建**：新增 `[McpToolDispatch]` 类后必须 `dotnet build --no-incremental`

## 关键文件路径速查

| 用途 | 路径 |
|------|------|
| ToolCategory 枚举 | `foundation/Abstractions/00-core/Core/Utils/Constants/ToolCategory.cs` |
| IGitHubCommandRunner 接口 | `foundation/Abstractions/00-core/Interfaces/Process/IGitHubCommandRunner.cs` |
| GitHubCommandRunner 实现 | `infrastructure/Infrastructure/IO/Process/GitHubCommandRunner.cs` |
| IDownloader 接口 | `infrastructure/Infrastructure/Network/Downloader/Abstractions/IDownloader.cs` |
| DownloadToolHandlers 范例 | `core/execution/Hands/src/ToolHandlers/Handlers/SystemTools/Web/DownloadToolHandlers.cs` |
| SubscribePRToolHandlers 范例 | `services/Mcp/src/User/SubscribePRToolHandlers.cs` |
| WorktreeToolHandlers 范例 | `services/Mcp/src/Workflow/WorktreeToolHandlers.cs` |
| ToolResultBuilder | `foundation/Abstractions/03-hands/Tools/ToolResultBuilder.cs` |
| 新 Handler 目标位置 | `services/Mcp/src/GitHub/GitHubToolHandlers.cs` |
| 新常量类目标位置 | `services/Mcp/src/GitHub/GitHubToolNameConstants.cs` |
