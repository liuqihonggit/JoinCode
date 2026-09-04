# jcc mcp 子命令后续工作计划

> 关联 ADR: [0065](../../adr/0065-jcc-mcp-subcommand.md)
> 创建日期: 2026-09-05

## 已完成

- ✅ `jcc mcp call/list/search/schema` 四个子命令（commit 4a46138a5）
- ✅ `jcc mcp serve` 子命令 — stdio + http 双模式，暴露 387 个工具（commit d4fc6e3d3）
- ✅ PrBodyGenerator `[Register]` 修复 — 30 个 gh 工具全部注册（commit cc93df1af）
- ✅ WithHostAsync dispose 噪音修复（commit f58d2c5d9）
- ✅ gh 工具参数 `?? ""` bug 修复 — IsNullOrWhiteSpace 替代（commit 17bb811ed）
- ✅ README 启动参数表格补全（30 个参数按 7 类分组）
- ✅ ADR 0065 状态 accepted
- ✅ 冒烟验证: gh_pr_view/gh_pr_list/gh_repo_view/gh_issue_list/gh_run_list + serve initialize/tools/list/tools/call

## 待办

### P1 — gh 工具批量验证

已验证 5 个工具，剩余 24 个 gh 工具未真实调用验证。按优先级分批：

**批次1 — PR 相关（6 个）**：
- [ ] `gh_pr_create` — 创建 PR（需测试仓库）
- [ ] `gh_pr_merge` — 合并 PR（用 #177 或 #178 测试，注意 auto-merge）
- [ ] `gh_pr_comment` — 评论 PR
- [ ] `gh_pr_review` — review PR
- [ ] `gh_pr_close` — 关闭 PR
- [ ] `gh_pr_reopen` — 重开 PR

**批次2 — Issue 相关（4 个）**：
- [ ] `gh_issue_create` — 创建 issue
- [ ] `gh_issue_comment` — 评论 issue
- [ ] `gh_issue_close` — 关闭 issue
- [ ] `gh_issue_reopen` — 重开 issue

**批次3 — Repo/Run/Release 相关（7 个）**：
- [ ] `gh_repo_create` — 创建仓库
- [ ] `gh_repo_delete` — 删除仓库（危险，需确认）
- [ ] `gh_run_view` — 查看运行详情
- [ ] `gh_run_rerun` — 重运行 CI
- [ ] `gh_release_create` — 创建 release
- [ ] `gh_release_list` — 列出 release
- [ ] `gh_release_view` — 查看 release

**批次4 — 其他（7 个）**：
- [ ] `gh_branch_list` — 列出分支
- [ ] `gh_branch_create` — 创建分支
- [ ] `gh_commit_list` — 列出 commit
- [ ] `gh_tag_list` — 列出 tag
- [ ] `gh_workflow_list` — 列出 workflow
- [ ] `gh_workflow_run` — 运行 workflow
- [ ] `gh_search` — 搜索代码/issue/PR

**验证方法**：每个工具用 `jcc mcp call <tool> --args '<json>' --json` 调用，确认返回数据结构正确、无报错。涉及写操作的（create/delete/merge/close）需在测试仓库或确认后执行。

### P2 — jcc mcp serve 增强

- [ ] **有状态模式**：当前 serve 仅无状态模式（`statelessMode: true`），加 `--stateful` 选项支持 MCP-Session-Id + DELETE 终止
- [ ] **SSE 通知推送**：GET /mcp/ 开 SSE 流推送 NotificationReceived（工具列表变更等）
- [ ] **Origin 白名单**：加 `--allowed-origins <list>` 选项防 DNS rebinding
- [ ] **工具过滤**：加 `--allow-category <list>` / `--deny-tool <list>` 选项，控制暴露哪些工具给外部
- [ ] **认证**：加 `--auth-token <token>` 选项，外部请求需带 Authorization header

### P3 — jcc mcp call 增强

- [ ] **进度回调**：长时工具（如 gh_run_rerun）支持 `--progress` 显示 ToolProgressCallback
- [ ] **超时**：加 `--timeout <seconds>` 选项，默认 30s
- [ ] **批量调用**：加 `--batch <file>` 从文件读多个调用（JSON Lines），顺序执行

### P4 — PR 合并

- [ ] 确认 PR #177（gh 工具复刻）CI 通过
- [ ] 创建新 PR 包含 jcc mcp 子命令 + serve + bug 修复
- [ ] auto-merge 合并到 main

### P5 — 文档

- [ ] README 加 `jcc mcp` 子命令用法章节（中英文）
- [ ] AGENTS.md 加 `jcc mcp serve` 启动测试说明（MockServer 联调）
- [ ] ADR 0066 — jcc mcp serve 设计决策（如需独立 ADR）

## 风险

- **serve 启动开销**：构建完整 DI 容器需 1-2 秒（387 个工具注册），高频调用场景应复用 serve 服务端而非每次 `jcc mcp call`
- **HttpListener 权限**：非 localhost 监听需管理员权限或 `netsh http add urlacl` 预授权
- **工具暴露安全**：serve 暴露全部 387 个工具（含 write/delete/bash 等危险工具），生产环境需配合 `--allow-category` 限制

<!-- 🤖 Auto Decision: 2026-09-05 -->
<!-- 决策: jcc mcp serve 用 JccMcpServer 继承 McpServer 而非包装器 -->
<!-- 原因: McpServer 的 HandleListTools/HandleCallToolAsync 改为 protected virtual 后，子类 override 最干净，复用 ProcessMessageAsync 全部逻辑 -->
<!-- 替代方案: 包装器（复制 ProcessMessageAsync，违反 DRY）/ 修改 McpServer 接受 IToolProvider 接口（过度抽象）-->
<!-- 验证: 编译通过，冒烟测试 initialize+tools/list(387)+tools/call 全部成功 ✅ -->
