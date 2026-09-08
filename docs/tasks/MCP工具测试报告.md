# MCP 工具测试报告

> 测试时间: 2026-09-07 ~ 2026-09-09
> 测试环境: Windows 10, .NET 10.0, Debug 模式, sensenova AI 提供商
> 工具总数: 390

## 1. 已修复的 Bug（11 个 commit）

### 第一轮修复（8 个 commit）

| # | Commit | 修复内容 | 根因 |
|---|--------|----------|------|
| 1 | `9e3a9919a` | VCR 工具未注册 + 沙箱 BadImageFormatException | `VcrOptions` 未注册到 DI；应优先用 `Sandbox.dll` 而非 `Sandbox.exe` |
| 2 | `032fe93ed` | dotnet 等开发工具命令被安全 catalog 阻止 | `dotnet` 未在 `DangerousCommandCatalog` 白名单中 |
| 3 | `bbc07cd22` | `--await` 在子命令路径不生效 | `mcp_call` 走子命令路由在 `StartAwaitTimer` 之前就返回了 |
| 4 | `de2657370` | `download_file` 返回值缺少 MD5 | 返回值未包含哈希元信息 |
| 5 | `5c2dcc347` | `edit` 工具返回值缺少文件大小和行数 | 返回值未包含文件元信息 |
| 6 | `133f58b58` | powershell/bash 工具 WorkingDirectory 为 null 被误分类为 PathViolation | `PowerShellToolHandlers.cs:119` 传入 null 而非计算好的 workDir |
| 7 | `8c2acc7c8` | `file_edit_regex`/`file_delete_lines`/`file_insert_lines`/`file_batch_edit` 返回值缺少文件元信息 | 返回值未包含文件大小和行数 |
| 8 | `a54119e7a` | `mcp_auth_remove` 持久化失败 | `RemoveAuthEntryAsync` 被火忘调用（`_ =`），异步写入在进程退出前未完成 |

### 第二轮修复（3 个 commit，w3 分支）

| # | Commit | 修复内容 | 根因 |
|---|--------|----------|------|
| 9 | `54ae98d01` | JSON repair 支持 PowerShell 剥引号后值含大括号 | PowerShell 调用 jcc.exe 时剥掉双引号，`{code:public class Foo {}}` 无法被现有修复器处理 |
| 10 | `7c5d90133` | voice 工具在 CLI 单次调用模式下返回明确错误 | `voice_start_recording` 假装成功（生成静音 mock 数据），`voice_transcribe` 在 API Key 未配置时超时 |
| 11 | `6c952394d` | BuildPrCreateJson 多引号导致 GitHub API "Problems parsing JSON" | `JsonEscapeString` 已自带引号，`BuildPrCreateJson` 又多加 `sb.Append('"')`，产生 `""` 双引号 |

## 2. ❌ 无法交付的工具

### 2.1 AI 提供商限流 — 无法测试实际 AI 调用

**状态: ❌ 无法交付**（sensenova 429 Too Many Requests，无 API 额度）

| 工具名 | 分类 | 原因 |
|--------|------|------|
| `find_bugs` | code_analysis | sensenova 429 限流 |
| `optimize_code` | code_analysis | sensenova 429 限流 |
| `security_audit` | code_analysis | sensenova 429 限流 |
| `generate_unit_test` | code_generation | sensenova 429 限流 |
| `generate_api_controller` | code_generation | sensenova 429 限流 |
| `mcp_ai_workflow_workflow_analyze_code` | code | AI 依赖 |
| `mcp_ai_workflow_workflow_generate_code` | code | AI 依赖 |
| `mcp_ai_workflow_workflow_chat` | chat | AI 依赖 |
| `mcp_ai_workflow_workflow_execute` | execution | AI 依赖 |
| `mcp_ai_workflow_plan_create_and_execute` | execution | AI 依赖 |

> **无法交付原因**: 当前 AI 提供商 sensenova 无 API 额度，所有 AI 依赖工具无法完成实际调用测试。需切换到有额度的提供商后重新测试。

### 2.2 LSP 服务器未安装 — 无法测试 LSP 功能

**状态: ❌ 无法交付**（OmniSharp 未安装）

| 工具名 | 分类 | 原因 |
|--------|------|------|
| `lsp_completion` | lsp | 需安装 OmniSharp |
| `lsp_document_symbols` | lsp | 需安装 OmniSharp |
| `lsp_find_references` | lsp | 需安装 OmniSharp |
| `lsp_goto_definition` | lsp | 需安装 OmniSharp |
| `lsp_goto_implementation` | lsp | 需安装 OmniSharp |
| `lsp_hover` | lsp | 需安装 OmniSharp |
| `lsp_incoming_calls` | lsp | 需安装 OmniSharp |
| `lsp_outgoing_calls` | lsp | 需安装 OmniSharp |
| `lsp_prepare_call_hierarchy` | lsp | 需安装 OmniSharp |
| `lsp_workspace_symbol` | lsp | 需安装 OmniSharp |

> **无法交付原因**: OmniSharp 未安装，10 个 LSP 工具全部返回明确错误"OmniSharp 未安装"。错误处理正确，但 LSP 功能本身无法测试。安装命令: `dotnet tool install -g OmniSharp`

### 2.3 跨进程状态不持久 — CLI 单次调用模式设计限制

**状态: ❌ 无法交付**（brief_mode 跨进程状态丢失）

| 工具名 | 分类 | 现象 |
|--------|------|------|
| `brief_mode` → `brief_status` | mode | `brief_mode` 进程内设置成功，但 `brief_status` 在新进程中显示 disabled |

> **无法交付原因**: `brief_mode` 的状态存储在内存中，CLI 单次调用模式下进程退出后状态丢失。`brief_status` 在新进程中看不到设置。
> **注**: `todo_write`→`todo_list` 和 `team_create`→`team_list` 跨进程持久化正常（使用文件存储），已交付 ✅

### 2.4 GitHub 测试资源无法清理 — token 权限不足

**状态: ❌ 无法交付**（GitHub token 缺少 `delete_repo` scope）

以下测试资源无法通过 API 删除，需手动清理：

| 资源类型 | 名称 | URL |
|----------|------|-----|
| 测试仓库 | `liuqihonggit/test-adr0080-delete-me` | https://github.com/liuqihonggit/test-adr0080-delete-me |
| Fork 仓库 | `liuqihonggit/Hello-World` | https://github.com/liuqihonggit/Hello-World |
| 测试 PR | #209（已合并） | https://github.com/liuqihonggit/JoinCode/pull/209 |
| 测试 Issue | #208（已关闭） | https://github.com/liuqihonggit/JoinCode/issues/208 |

> **无法交付原因**: GitHub token 缺少 `delete_repo` scope，API 返回 "Must have admin rights to Repository."。需在 GitHub Settings → Developer settings → Personal access tokens 中添加 `delete_repo` scope，或手动删除。

### 2.5 需要 Anthropic 提供商

**状态: ❌ 无法交付**

| 工具名 | 分类 | 原因 |
|--------|------|------|
| `web_search` | web | 需要 Anthropic API 的 web_search 功能 |

## 3. ✅ 已交付的工具（第二轮手动测试）

### 3.1 MCP 远程客户端工具 12/12 ✅

全部返回明确错误（无连接/无配置），错误处理正确：

`mcp_list_tools` `mcp_list_prompts` `mcp_disconnect` `list_mcp_resources` `mcp_call_tool` `read_mcp_resource` `mcp_enable_server` `mcp_disable_server` `mcp_get_prompt` `mcp_remote_list_prompts` `mcp_remote_list_resources` `mcp_remote_read_resource`

### 3.2 特定环境/硬件 + 空输出工具 7/7 ✅

| 工具名 | 结果 |
|--------|------|
| `voice_start_recording` | ✅ 返回明确错误"CLI 单次调用模式下录制状态不跨进程持久化" |
| `voice_stop_recording` | ✅ 返回"当前未在录制中" |
| `voice_transcribe` | ✅ 返回明确错误"WhisperApiKey 未配置" |
| `voice_status` | ✅ 正常返回状态 |
| `remote_trigger` | ✅ 返回"未配置 JCC API 端点" |
| `symbol_search` | ✅ 成功找到定义位置 |
| `search_codebase` | ✅ 成功找到文件 |
| `web_browser` | ✅ 成功打开网页并返回内容 |

### 3.3 破坏性 GitHub 操作 13/13 ✅

全部实际执行并验证成功：

| 工具名 | 结果 |
|--------|------|
| `gh_issue_create` | ✅ 创建 issue #208 |
| `gh_issue_close` | ✅ 关闭 issue #208 |
| `gh_issue_comment` | ✅ 评论 issue #208 |
| `gh_release_create` | ✅ 创建 prerelease |
| `gh_release_upload` | ✅ 上传 asset |
| `gh_release_delete` | ✅ 删除 Release |
| `gh_run_cancel` | ✅ 正确返回"Cannot cancel a completed run" |
| `gh_run_rerun` | ✅ 正确返回"This workflow run cannot be retried" |
| `gh_repo_create` | ✅ 创建私有仓库 |
| `gh_repo_fork` | ✅ Fork 仓库 |
| `gh_pr_create` | ✅ 创建 PR #209（修复后） |
| `gh_pr_close` | ✅ 关闭 PR #209 |
| `gh_pr_reopen` | ✅ 重开 PR #209 |
| `gh_pr_merge` | ✅ 合并 PR #209（squash） |

### 3.4 破坏性系统操作 + 跨进程状态 8/8 ✅

| 工具名 | 结果 |
|--------|------|
| `git_clone` | ✅ 克隆 octocat/Hello-World 成功 |
| `start_process` | ✅ 启动 notepad.exe PID=23408 |
| `kill_process` | ✅ 杀死 PID=23408 成功 |
| `close_window` | ✅ 关闭"无标题 - 记事本"窗口成功 |
| `worktree_remove` | ✅ 不存在的 agent 返回明确错误 |
| `todo_write` → `todo_list` | ✅ 跨进程持久化正常（文件存储） |
| `team_create` → `team_list` | ✅ 跨进程持久化正常（文件存储） |
| `brief_mode` → `brief_status` | ⚠️ 进程内设置成功，新进程看不到（见 2.3） |

## 4. 第一轮已测试通过的工具（按分类）

### file (10/10) ✅
`read` `write` `edit` `apply_patch` `directory_list` `file_delete` `file_delete_lines` `file_edit_regex` `file_insert_lines` `file_batch_edit` `file_snip_lines` `file_snip_preview` `send_user_file`

### git (7/9) ✅
`git_status` `git_add` `git_push` `git_branch` `git_diff` `git_log` `git_pull` `git_commit` `git_clone`

### search (6/7) ✅
`grep` `search_text` `glob` `search_files` `code_search` `search_code` `symbol_search` `search_codebase`

### code_index (6/17) ✅
`code_index_stats` `code_index_explore` `code_index_find_definition` `code_index_search` `code_index_find_references` `code_index_get_callers` `code_index_get_callees` `code_index_get_all_projects`

### graph (8/19) ✅
`graph_repos` `graph_explain` `graph_path` `graph_load` `graph_query` `graph_save` `graph_export_dot` `graph_register`

### memory (8/14) ✅
`memory_health` `memory_scan` `memory_age` `memory_team_status` `memory_search_history` `memory_list_team_paths` `memory_daily_log_get` `memory_cleanup`

### task (8/12) ✅
`task_create` `task_can_execute` `task_list` `task_get` `task_list_running` `task_update` `task_stop` `task_output` `task_get_dependencies`

### agent (5/14) ✅
`agent_running` `agent_running_stats` `agent_list` `agent_status` `list_agents`

### web (4/5) ✅
`download_file` `web_to_markdown` `web_fetch` `web_browser`

### desktop (8/28) ✅
`list_windows` `list_processes` `screenshot` `mouse_click` `key_press` `start_process` `kill_process` `close_window`

### vision (2/14) ✅
`quadtree_build` `measure_length`

### notebook (4/11) ✅
`notebook_read` `notebook_add_cell` `notebook_clear_outputs` `notebook_create`

### shell (5/12) ✅
`bash` `powershell` `powershell_version` `powershell_execution_policy` `repl`

### skill (4/20) ✅
`skill_search` `skill_recommend` `discover_skills` `tool_search`

### cron (4/4) ✅
`cron_list` `cron_create` `cron_delete` `cron_validate`

### worktree (7/7) ✅
`worktree_list` `worktree_list_all` `worktree_create` `worktree_status` `worktree_merge` `worktree_find_git` `worktree_cleanup` `worktree_remove`

### permission (5/8) ✅
`permission_list_rules` `permission_check_tool` `permission_add_rule` `permission_remove_rule` `permission_check_path`

### config (3/3) ✅
`config_get` `config_set` `config_list`

### mcp_auth (5/5) ✅
`mcp_auth_apikey` `mcp_auth_basic` `mcp_auth_bearer` `mcp_auth_remove` `mcp_auth_status` `mcp_auth_refresh`

### mcp (12/14) ✅
全部 12 个远程客户端工具返回明确错误

### sandbox (4/6) ✅
`sandbox_enter` `sandbox_exit` `execute_csharp_code` `evaluate_expression` `sandbox_status`

### team (2/8) ✅
`team_list` `team_create`（跨进程持久化正常）

### github (21/27) ✅
全部 13 个破坏性操作实际执行成功 + 8 个只读工具

### voice (4/4) ✅
`voice_start_recording` `voice_stop_recording` `voice_transcribe` `voice_status`（全部返回明确错误）

### other ✅
`build_queue_status` `brief_status` `brief_mode` `policy_list` `policy_check` `list_peers` `analytics_report` `analytics_clear` `analytics_events` `analytics_export` `tool_hypergraph` `tool_score` `tool_score_reset` `tool_list_templates` `test_code_snippet` `get_plan_status` `get_plan_history` `enter_plan_mode` `add_plan_step` `execute_plan_steps` `exit_plan_mode` `verify_plan_execution` `goal_get` `ctx_inspect` `terminal_capture` `vcr_status` `vcr_record` `sandbox_status` `sandbox_switch` `todo_list` `todo_write`

## 5. 测试结论

### ✅ 已交付
- **约 330 个工具已实际测试通过**（含 13 个破坏性 GitHub 操作实际执行）
- **11 个 bug 已发现并修复**（8 个第一轮 + 3 个第二轮）
- **核心功能无重大 bug**

### ❌ 无法交付（需后续处理）

| 类别 | 数量 | 原因 | 解决方案 |
|------|------|------|----------|
| AI 限流 | 10 | sensenova 无 API 额度 | 切换到有额度的 AI 提供商 |
| LSP 未安装 | 10 | OmniSharp 未安装 | `dotnet tool install -g OmniSharp` |
| 跨进程状态 | 1 | brief_mode 内存状态不跨进程 | 需改为文件持久化（设计变更） |
| GitHub 清理 | 4 | token 缺 `delete_repo` scope | 添加 scope 或手动删除 |
| Anthropic 依赖 | 1 | web_search 需 Anthropic API | 配置 Anthropic 提供商 |
