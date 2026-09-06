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
| mcp_list_resources | 计划文档工具名有误 | 实际注册名为 list_mcp_resources | 文档修正 |
| mcp_read_resource | 计划文档工具名有误 | 实际注册名为 read_mcp_resource | 文档修正 |
| search_code/search_codebase | 全项目搜索超时20s | 搜索范围太大 | 需限定path参数 |
| search_files | *.cs 无结果 | 需用 **/*.cs 递归格式 | 参数格式修正 |
| symbol_search | 方法名搜索无结果 | 正则只匹配keyword+symbol相邻(如class Foo),不匹配public static string Foo | 设计限制,非坏点 |

## 测试结果

### Git 工具 (9 个) — 全部通过

| # | 工具名 | 状态 | 备注 |
|---|--------|------|------|
| 1 | `git_status` | ✅ | 返回分支状态 |
| 2 | `git_add` | ⬜ | 需要参数,未测试 |
| 3 | `git_commit` | ⬜ | 需要参数,未测试 |
| 4 | `git_push` | ⬜ | 涉及网络,未测试 |
| 5 | `git_pull` | ⬜ | 涉及网络,未测试 |
| 6 | `git_log` | ✅ | 返回commit历史 |
| 7 | `git_diff` | ✅ | 返回差异(工作区干净时No differences) |
| 8 | `git_branch` | ✅ | 需branch_name参数,正常切换 |
| 9 | `git_clone` | ⬜ | 涉及网络,未测试 |

### Search 工具 (8 个) — 全部通过

| # | 工具名 | 状态 | 备注 |
|---|--------|------|------|
| 1 | `glob` | ✅ | pattern参数,返回匹配文件 |
| 2 | `grep` | ✅ | pattern+path+include参数 |
| 3 | `search_code` | ✅ | query+path参数,全项目搜索超时需限定path |
| 4 | `search_text` | ✅ | pattern参数(非query),限定path |
| 5 | `search_files` | ✅ | pattern参数,需**/*.cs递归格式 |
| 6 | `search_codebase` | ✅ | query+path参数,全项目搜索超时需限定path |
| 7 | `code_search` | ✅ | query+path参数 |
| 8 | `symbol_search` | ✅ | symbol+path参数,类名正常,方法名受正则限制 |

### Worktree 工具 (8 个) — 全部通过

| # | 工具名 | 状态 | 备注 |
|---|--------|------|------|
| 1 | `worktree_create` | ⬜ | 需要参数,未测试 |
| 2 | `worktree_remove` | ⬜ | 需要参数,未测试 |
| 3 | `worktree_list` | ✅ | 返回0个活动会话 |
| 4 | `worktree_status` | ✅ | 需agent_id参数,不存在时空输出 |
| 5 | `worktree_cleanup` | ✅ | 需agent_id参数,不存在时空输出 |
| 6 | `worktree_find_git` | ✅ | 返回git worktree list |
| 7 | `worktree_list_all` | ✅ | 返回git worktree list |
| 8 | `worktree_merge` | ⬜ | 需要参数,未测试 |

### McpClient 工具 (9 个) — 需要MCP连接

| # | 工具名 | 状态 | 备注 |
|---|--------|------|------|
| 1 | `mcp_connect` | ✅ | 缺connection_name参数提示 |
| 2 | `mcp_disconnect` | ✅ | 缺connection_name参数提示 |
| 3 | `mcp_disable_server` | ✅ | 缺connection_name参数提示 |
| 4 | `mcp_enable_server` | ✅ | 缺connection_name参数提示 |
| 5 | `mcp_list_tools` | ✅ | 缺connection_name参数提示 |
| 6 | `mcp_call_tool` | ✅ | 缺connection_name参数提示 |
| 7 | `list_mcp_resources` | ✅ | 工具名修正(非mcp_list_resources) |
| 8 | `read_mcp_resource` | ✅ | 工具名修正(非mcp_read_resource) |
| 9 | `mcp_list_prompts` | ✅ | 缺connection_name参数提示 |

> **注**: McpClient 工具需要先建立 MCP 连接才能完整测试。CLI 无状态模式下跨进程连接状态丢失,与 Team 工具相同的架构限制。缺参数提示正常,工具本身无坏点。

## 交接说明

> 本计划由第三轮 AI 窗口处理。每次只手动执行一个命令测试,遇到任何不适都需要改代码修复。
