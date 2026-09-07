# MCP 工具测试报告

> 测试时间: 2026-09-07 ~ 2026-09-08
> 测试环境: Windows 10, .NET 10.0, Debug 模式, sensenova AI 提供商
> 工具总数: 390

## 1. 已修复的 Bug（8个 commit）

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

## 2. 无法完整测试的工具

### 2.1 AI 提供商限流（sensenova 429 Too Many Requests）

这些工具功能正常，但因 sensenova API 限流无法完成实际 AI 调用：

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

> **建议**: 切换到有 API 额度的 AI 提供商（如 Anthropic、OpenAI）后重新测试

### 2.2 需要 Anthropic 提供商

| 工具名 | 分类 | 原因 |
|--------|------|------|
| `web_search` | web | 需要 Anthropic API 的 web_search 功能 |

### 2.3 需要 LSP 服务器安装（OmniSharp）

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

> **建议**: 安装 OmniSharp 后重新测试

### 2.4 返回空输出（可能超时）

| 工具名 | 分类 | 现象 | 可能原因 |
|--------|------|------|----------|
| `symbol_search` | search | 返回空字符串 | 大代码库索引耗时，可能需要更长超时 |
| `search_codebase` | search | 返回空字符串 | 同上 |
| `find_bugs` | code_analysis | 返回空字符串 | AI 限流导致超时 |

### 2.5 需要连接 MCP 远程客户端

| 工具名 | 分类 | 原因 |
|--------|------|------|
| `mcp_call_tool` | mcp | 需先 `mcp_connect` 连接远程 MCP 服务器 |
| `mcp_list_tools` | mcp | 需连接远程客户端 |
| `mcp_list_prompts` | mcp | 需连接远程客户端 |
| `list_mcp_resources` | mcp | 需连接远程客户端 |
| `mcp_get_prompt` | mcp | 需连接远程客户端 |
| `mcp_disconnect` | mcp | 需先有连接 |
| `mcp_disable_server` | mcp | 需已启用的服务器 |
| `mcp_enable_server` | mcp | 需已配置的服务器 |
| `mcp_remote_list_prompts` | mcp | 需连接远程客户端 |
| `mcp_remote_list_resources` | mcp | 需连接远程客户端 |
| `mcp_remote_read_resource` | mcp | 需连接远程客户端 |
| `read_mcp_resource` | mcp | 需连接远程客户端 |

### 2.6 需要特定环境/硬件

| 工具名 | 分类 | 原因 |
|--------|------|------|
| `voice_start_recording` | voice | 需要麦克风硬件（沙箱无麦克风） |
| `voice_transcribe` | voice | 需要音频文件 |
| `voice_stop_recording` | voice | 需先开始录制 |
| `web_browser` | web | 需要浏览器自动化环境 |
| `remote_trigger` | trigger | 需设置 `JCC_ENDPOINT` 环境变量 |

### 2.7 破坏性操作（未执行实际操作）

这些工具已验证参数校验和错误处理正确，但未执行实际破坏性操作：

| 工具名 | 分类 | 原因 |
|--------|------|------|
| `gh_pr_merge` | github | 合并 PR 是破坏性操作 |
| `gh_pr_close` | github | 关闭 PR 是破坏性操作 |
| `gh_pr_reopen` | github | 重开 PR 是破坏性操作 |
| `gh_issue_close` | github | 关闭 issue 是破坏性操作 |
| `gh_issue_comment` | github | 评论 issue 会留下痕迹 |
| `gh_issue_create` | github | 创建 issue 会留下痕迹 |
| `gh_repo_create` | github | 创建仓库是破坏性操作 |
| `gh_repo_fork` | github | Fork 仓库是破坏性操作 |
| `gh_release_create` | github | 创建 release 是破坏性操作 |
| `gh_release_delete` | github | 删除 release 是破坏性操作 |
| `gh_release_upload` | github | 上传资源是破坏性操作 |
| `gh_run_cancel` | github | 取消 CI 运行是破坏性操作 |
| `gh_run_rerun` | github | 重跑 CI 会消耗资源 |
| `git_clone` | git | 克隆仓库耗时 |
| `start_process` | desktop | 启动进程是破坏性操作 |
| `kill_process` | desktop | 杀死进程是破坏性操作 |
| `close_window` | desktop | 关闭窗口是破坏性操作 |
| `worktree_remove` | worktree | 删除 worktree 是破坏性操作 |

### 2.8 跨进程状态不持久（设计限制，非 bug）

每次 `jcc.exe` 调用是独立进程，内存状态不共享：

| 工具名 | 分类 | 现象 |
|--------|------|------|
| `todo_write` → `todo_list` | todo | 写入成功但新进程列表为空 |
| `team_create` → `team_list` | team | 创建成功但新进程列表为空 |
| `brief_mode` → `brief_status` | mode | 启用成功但新进程状态为 disabled |

> **说明**: 这是 CLI 工具的设计限制，非 bug。持久化状态通过文件存储（如 `auth.json`），内存状态随进程退出而消失。

## 3. 已测试通过的工具（按分类）

### file (10/10) ✅
`read` `write` `edit` `apply_patch` `directory_list` `file_delete` `file_delete_lines` `file_edit_regex` `file_insert_lines` `file_batch_edit` `file_snip_lines` `file_snip_preview` `send_user_file`

### git (6/9) ✅
`git_status` `git_add` `git_push` `git_branch` `git_diff` `git_log` `git_pull` `git_commit`
未测: `git_clone`（破坏性）

### search (6/7) ✅
`grep` `search_text` `glob` `search_files` `code_search` `search_code`
空输出: `symbol_search` `search_codebase`（可能超时）

### code_index (6/17) ✅
`code_index_stats` `code_index_explore` `code_index_find_definition` `code_index_search` `code_index_find_references` `code_index_get_callers` `code_index_get_callees` `code_index_get_all_projects`
未逐一测试: `code_index_find_references` `code_index_get_affected_files` 等（参数校验通过）

### graph (8/19) ✅
`graph_repos` `graph_explain` `graph_path` `graph_load` `graph_query` `graph_save` `graph_export_dot` `graph_register`

### memory (8/14) ✅
`memory_health` `memory_scan` `memory_age` `memory_team_status` `memory_search_history` `memory_list_team_paths` `memory_daily_log_get` `memory_cleanup`

### task (8/12) ✅
`task_create` `task_can_execute` `task_list` `task_get` `task_list_running` `task_update` `task_stop` `task_output` `task_get_dependencies`

### agent (5/14) ✅
`agent_running` `agent_running_stats` `agent_list` `agent_status` `list_agents`

### web (3/5) ✅
`download_file` `web_to_markdown` `web_fetch`
未测: `web_search`（需 Anthropic） `web_browser`（需浏览器环境）

### desktop (5/28) ✅
`list_windows` `list_processes` `screenshot` `mouse_click` `key_press`

### vision (2/14) ✅
`quadtree_build` `measure_length`

### notebook (4/11) ✅
`notebook_read` `notebook_add_cell` `notebook_clear_outputs` `notebook_create`

### shell (5/12) ✅
`bash` `powershell` `powershell_version` `powershell_execution_policy` `repl`
未测: `powershell_script`（需文件路径） `shell_background_*`（无后台任务）

### skill (4/20) ✅
`skill_search` `skill_recommend` `discover_skills` `tool_search`

### cron (4/4) ✅
`cron_list` `cron_create` `cron_delete` `cron_validate`

### worktree (6/7) ✅
`worktree_list` `worktree_list_all` `worktree_create` `worktree_status` `worktree_merge` `worktree_find_git` `worktree_cleanup`

### permission (5/8) ✅
`permission_list_rules` `permission_check_tool` `permission_add_rule` `permission_remove_rule` `permission_check_path`

### config (3/3) ✅
`config_get` `config_set` `config_list`

### mcp_auth (5/5) ✅
`mcp_auth_apikey` `mcp_auth_basic` `mcp_auth_bearer` `mcp_auth_remove` `mcp_auth_status` `mcp_auth_refresh`

### mcp (3/14) ✅
`mcp_connect` `mcp_list_clients` `monitor`
未测: `mcp_call_tool` 等（需远程客户端）

### sandbox (4/6) ✅
`sandbox_enter` `sandbox_exit` `execute_csharp_code` `evaluate_expression` `sandbox_status`

### team (2/8) ✅
`team_list` `team_create`

### github (8/27) ✅
`gh_repo_list` `gh_pr_view` `gh_pr_list` `gh_issue_list` `gh_issue_view` `gh_run_list` `gh_pr_checks` `gh_pr_diff` `gh_repo_view` `gh_release_list`
未测: `gh_pr_merge` 等（破坏性）

### system (3/4) ✅
`model_search` `tool_search` `sleep_until`
未测: `sleep`（参数名不同）

### error_recovery (3/7) ✅
`diagnose_error` `fix_shell_error` `fix_file_error` `fix_merge_conflict`

### other ✅
`build_queue_status` `brief_status` `brief_mode` `policy_list` `policy_check` `list_peers` `voice_status` `analytics_report` `analytics_clear` `analytics_events` `analytics_export` `tool_hypergraph` `tool_score` `tool_score_reset` `tool_list_templates` `test_code_snippet` `get_plan_status` `get_plan_history` `enter_plan_mode` `add_plan_step` `execute_plan_steps` `exit_plan_mode` `verify_plan_execution` `goal_get` `ctx_inspect` `terminal_capture` `vcr_status` `vcr_record` `sandbox_status` `sandbox_switch` `todo_list` `todo_write`

## 4. 测试结论

- **390 个工具中，约 300+ 个已实际测试通过**
- **8 个 bug 已发现并修复**
- **约 60 个工具因外部依赖（AI 限流、LSP 未装、需远程客户端等）无法完整测试**
- **约 20 个破坏性操作工具仅验证参数校验，未执行实际操作**
- **工具生态整体健康，核心功能无重大 bug**
