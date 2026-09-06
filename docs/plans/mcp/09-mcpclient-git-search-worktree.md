# MCP 工具测试计划 09: MCP客户端 / Git / 搜索 / Worktree

> 第二轮:脚本批量验证 — 快速定位坏点

## 概述

| 项目 | 内容 |
|------|------|
| 计划编号 | 09/12 |
| 涵盖分类 | McpClient (9) + Git (9) + Search (8) + Worktree (8) |
| 工具总数 | 34 个 |
| 测试方式 | 脚本批量调用 `jcc.exe mcp_call` |
| 前置条件 | jcc.exe 已编译,git 仓库可用 |

## 工具清单

### Category: McpClient (9 个)

| # | 工具名 | 说明 | 状态 |
|---|--------|------|------|
| 1 | `mcp_connect` | 连接 MCP 服务 | ⬜ |
| 2 | `mcp_disconnect` | 断开 MCP 服务 | ⬜ |
| 3 | `mcp_disable_server` | 禁用服务 | ⬜ |
| 4 | `mcp_enable_server` | 启用服务 | ⬜ |
| 5 | `mcp_list_tools` | 列出工具 | ⬜ |
| 6 | `mcp_call_tool` | 调用工具 | ⬜ |
| 7 | `mcp_list_resources` | 列出资源 | ⬜ |
| 8 | `mcp_read_resource` | 读取资源 | ⬜ |
| 9 | `mcp_list_prompts` | 列出提示 | ⬜ |

### Category: Git (9 个)

| # | 工具名 | 说明 | 状态 |
|---|--------|------|------|
| 1 | `git_status` | Git 状态 | ⬜ |
| 2 | `git_add` | Git 添加 | ⬜ |
| 3 | `git_commit` | Git 提交 | ⬜ |
| 4 | `git_push` | Git 推送 | ⬜ |
| 5 | `git_pull` | Git 拉取 | ⬜ |
| 6 | `git_log` | Git 日志 | ⬜ |
| 7 | `git_diff` | Git 差异 | ⬜ |
| 8 | `git_branch` | Git 分支 | ⬜ |
| 9 | `git_clone` | Git 克隆 | ⬜ |

### Category: Search (8 个)

| # | 工具名 | 说明 | 状态 |
|---|--------|------|------|
| 1 | `glob` | Glob 匹配 | ⬜ |
| 2 | `grep` | Grep 搜索 | ⬜ |
| 3 | `search_code` | 代码搜索 | ⬜ |
| 4 | `search_text` | 文本搜索 | ⬜ |
| 5 | `search_files` | 文件搜索 | ⬜ |
| 6 | `search_codebase` | 代码库搜索 | ⬜ |
| 7 | `code_search` | 代码搜索 | ⬜ |
| 8 | `symbol_search` | 符号搜索 | ⬜ |

### Category: Worktree (8 个)

| # | 工具名 | 说明 | 状态 |
|---|--------|------|------|
| 1 | `worktree_create` | 创建工作树 | ⬜ |
| 2 | `worktree_remove` | 移除工作树 | ⬜ |
| 3 | `worktree_list` | 列出工作树 | ⬜ |
| 4 | `worktree_status` | 工作树状态 | ⬜ |
| 5 | `worktree_cleanup` | 清理工作树 | ⬜ |
| 6 | `worktree_find_git` | 查找 Git 工作树 | ⬜ |
| 7 | `worktree_list_all` | 列出所有工作树 | ⬜ |
| 8 | `worktree_merge` | 合并工作树 | ⬜ |

## 测试脚本

### 批量验证脚本

```powershell
$jcc = "D:\project\w1\artifacts\bin\JoinCode\Release\net10.0\jcc.exe"
$mcpClientTools = @(
    "mcp_connect", "mcp_disconnect", "mcp_disable_server", "mcp_enable_server",
    "mcp_list_tools", "mcp_call_tool", "mcp_list_resources", "mcp_read_resource", "mcp_list_prompts"
)
$gitTools = @(
    "git_status", "git_add", "git_commit", "git_push", "git_pull",
    "git_log", "git_diff", "git_branch", "git_clone"
)
$searchTools = @(
    "glob", "grep", "search_code", "search_text", "search_files",
    "search_codebase", "code_search", "symbol_search"
)
$worktreeTools = @(
    "worktree_create", "worktree_remove", "worktree_list", "worktree_status",
    "worktree_cleanup", "worktree_find_git", "worktree_list_all", "worktree_merge"
)
foreach ($t in $mcpClientTools + $gitTools + $searchTools + $worktreeTools) {
    Write-Host "--- Testing: $t ---"
    & $jcc --trust --bypass mcp_call $t 2>&1 | Select-Object -First 5
}
```

### 只读工具冒烟测试

```powershell
$jcc = "D:\project\w1\artifacts\bin\JoinCode\Release\net10.0\jcc.exe"
& $jcc --trust --bypass mcp_call git_status
& $jcc --trust --bypass mcp_call git_log
& $jcc --trust --bypass mcp_call git_branch
& $jcc --trust --bypass mcp_call worktree_list
& $jcc --trust --bypass mcp_call mcp_list_tools
```

## 验收标准

- [ ] 每个工具调用返回格式正确的 JSON
- [ ] `Error:false` 且有非空输出
- [ ] 无崩溃/超时/死锁
- [ ] `mcp_connect` / `mcp_disconnect` 互为逆操作
- [ ] `worktree_create` / `worktree_remove` 互为逆操作
- [ ] 搜索工具无结果时返回空列表(非崩溃)
- [ ] Git 工具在非 Git 仓库时优雅降级

## 风险提示

- `git_push` / `git_pull` 涉及网络,可能超时
- `git_clone` 可能克隆大仓库,注意磁盘空间
- `git_commit` 修改仓库历史,注意分支保护
- `worktree_create` 创建新目录,注意路径冲突
- `worktree_merge` 合并分支,可能产生冲突

## 问题记录

| 工具 | 问题描述 | 根因 | 修复 |
|------|----------|------|------|
| | | | |

## 交接说明

> 本计划由第三轮 AI 窗口处理。每次只手动执行一个命令测试,遇到任何不适都需要改代码修复。
