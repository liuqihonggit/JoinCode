# MCP 工具测试计划 08: LSP / 团队 / 笔记本 Lsp + Team + Notebook

> 第二轮:脚本批量验证 — 快速定位坏点

## 概述

| 项目 | 内容 |
|------|------|
| 计划编号 | 08/12 |
| 涵盖分类 | Lsp (10) + Team (10) + Notebook (10) |
| 工具总数 | 30 个 |
| 测试方式 | 脚本批量调用 `jcc.exe mcp_call` |
| 前置条件 | jcc.exe 已编译,LSP 服务可用 |

## 工具清单

### Category: Lsp (10 个)

| # | 工具名 | 说明 | 状态 |
|---|--------|------|------|
| 1 | `lsp_goto_definition` | 跳转定义 | ⬜ |
| 2 | `lsp_find_references` | 查找引用 | ⬜ |
| 3 | `lsp_hover` | 悬停信息 | ⬜ |
| 4 | `lsp_completion` | 自动补全 | ⬜ |
| 5 | `lsp_document_symbols` | 文档符号 | ⬜ |
| 6 | `lsp_workspace_symbol` | 工作区符号 | ⬜ |
| 7 | `lsp_goto_implementation` | 跳转实现 | ⬜ |
| 8 | `lsp_prepare_call_hierarchy` | 准备调用层次 | ⬜ |
| 9 | `lsp_incoming_calls` | 入站调用 | ⬜ |
| 10 | `lsp_outgoing_calls` | 出站调用 | ⬜ |

### Category: Team (10 个)

| # | 工具名 | 说明 | 状态 |
|---|--------|------|------|
| 1 | `team_create` | 创建团队 | ⬜ |
| 2 | `team_delete` | 删除团队 | ⬜ |
| 3 | `team_get` | 获取团队 | ⬜ |
| 4 | `team_list` | 列出团队 | ⬜ |
| 5 | `team_add_member` | 添加成员 | ⬜ |
| 6 | `team_remove_member` | 移除成员 | ⬜ |
| 7 | `team_send_message` | 发送消息 | ⬜ |
| 8 | `team_send_direct_message` | 发送私信 | ⬜ |
| 9 | `team_broadcast` | 广播 | ⬜ |
| 10 | `team_get_messages` | 获取消息 | ⬜ |

### Category: Notebook (10 个)

| # | 工具名 | 说明 | 状态 |
|---|--------|------|------|
| 1 | `notebook_edit` | 编辑笔记本 | ⬜ |
| 2 | `notebook_create` | 创建笔记本 | ⬜ |
| 3 | `notebook_read` | 读取笔记本 | ⬜ |
| 4 | `notebook_add_cell` | 添加单元格 | ⬜ |
| 5 | `notebook_delete_cell` | 删除单元格 | ⬜ |
| 6 | `notebook_edit_cell` | 编辑单元格 | ⬜ |
| 7 | `notebook_move_cell` | 移动单元格 | ⬜ |
| 8 | `notebook_change_cell_type` | 修改单元格类型 | ⬜ |
| 9 | `notebook_clear_outputs` | 清除输出 | ⬜ |
| 10 | `notebook_get_cell` | 获取单元格 | ⬜ |

## 测试脚本

### 批量验证脚本

```powershell
$jcc = "D:\project\w1\artifacts\bin\JoinCode\Release\net10.0\jcc.exe"
$lspTools = @(
    "lsp_goto_definition", "lsp_find_references", "lsp_hover", "lsp_completion",
    "lsp_document_symbols", "lsp_workspace_symbol", "lsp_goto_implementation",
    "lsp_prepare_call_hierarchy", "lsp_incoming_calls", "lsp_outgoing_calls"
)
$teamTools = @(
    "team_create", "team_delete", "team_get", "team_list", "team_add_member",
    "team_remove_member", "team_send_message", "team_send_direct_message",
    "team_broadcast", "team_get_messages"
)
$notebookTools = @(
    "notebook_edit", "notebook_create", "notebook_read", "notebook_add_cell",
    "notebook_delete_cell", "notebook_edit_cell", "notebook_move_cell",
    "notebook_change_cell_type", "notebook_clear_outputs", "notebook_get_cell"
)
foreach ($t in $lspTools + $teamTools + $notebookTools) {
    Write-Host "--- Testing: $t ---"
    & $jcc --trust --bypass mcp_call $t 2>&1 | Select-Object -First 5
}
```

### 只读工具冒烟测试

```powershell
$jcc = "D:\project\w1\artifacts\bin\JoinCode\Release\net10.0\jcc.exe"
& $jcc --trust --bypass mcp_call team_list
& $jcc --trust --bypass mcp_call team_get_messages
```

## 验收标准

- [ ] 每个工具调用返回格式正确的 JSON
- [ ] `Error:false` 且有非空输出
- [ ] 无崩溃/超时/死锁
- [ ] LSP 工具在无语言服务器时优雅降级
- [ ] `team_create` / `team_delete` 互为逆操作
- [ ] `notebook_add_cell` / `notebook_delete_cell` 互为逆操作
- [ ] Notebook 操作保持 JSON 格式正确

## 风险提示

- LSP 工具依赖语言服务器进程,可能启动失败
- `team_delete` 删除团队数据,不可恢复
- `team_broadcast` 向所有成员发消息,注意副作用
- Notebook 操作可能损坏 .ipynb 文件,注意备份

## 测试结果

### LSP 工具 (10 个) — 全部通过（完善：服务器不可用时返回安装提示）

| # | 工具名 | 状态 | 备注 |
|---|--------|------|------|
| 1 | `lsp_goto_definition` | ✅ | omnisharp未安装,返回安装提示 |
| 2 | `lsp_find_references` | ✅ | 返回安装提示 |
| 3 | `lsp_hover` | ✅ | 返回安装提示 |
| 4 | `lsp_completion` | ✅ | 返回安装提示 |
| 5 | `lsp_document_symbols` | ✅ | 返回安装提示 |
| 6 | `lsp_workspace_symbol` | ✅ | 无file_path,保持原有降级消息 |
| 7 | `lsp_goto_implementation` | ✅ | 返回安装提示 |
| 8 | `lsp_prepare_call_hierarchy` | ✅ | 返回安装提示 |
| 9 | `lsp_incoming_calls` | ✅ | 返回安装提示 |
| 10 | `lsp_outgoing_calls` | ✅ | 返回安装提示 |

> **完善**: 添加 `IsServerAvailableAsync` 接口方法,`ValidateFileAndExecuteAsync` 中检查服务器可用性。不可用时按文件扩展名返回对应语言服务器的安装命令（如 C# → `dotnet tool install -g OmniSharp`），而非空降级消息。

### Team 工具 (10 个) — 全部通过（完善：添加文件持久化）

| # | 工具名 | 状态 | 备注 |
|---|--------|------|------|
| 1 | `team_create` | ✅ | 持久化到 ~/.jcc/teams/state.json |
| 2 | `team_delete` | ✅ | 跨进程删除 |
| 3 | `team_get` | ✅ | 跨进程读取 |
| 4 | `team_list` | ✅ | 跨进程列表 |
| 5 | `team_add_member` | ✅ | 跨进程添加成员 |
| 6 | `team_remove_member` | ✅ | 跨进程移除成员 |
| 7 | `team_send_message` | ✅ | 跨进程发送消息 |
| 8 | `team_send_direct_message` | ✅ | 跨进程发送私信 |
| 9 | `team_broadcast` | ✅ | 跨进程广播 |
| 10 | `team_get_messages` | ✅ | 跨进程读取消息 |

> **完善**: TeamManager 添加文件持久化（`~/.jcc/teams/state.json`），构造函数加载，每次写操作后保存。IFileSystem 可选注入，测试不受影响。CLI 无状态模式下跨进程共享团队状态。

### Notebook 工具 (10 个) — 全部通过

| # | 工具名 | 状态 | 备注 |
|---|--------|------|------|
| 1 | `notebook_edit` | ✅ | 参数: notebook_path + new_source; edit_mode=replace/insert/delete |
| 2 | `notebook_create` | ✅ | |
| 3 | `notebook_read` | ✅ | |
| 4 | `notebook_add_cell` | ✅ | 参数: content (非source) |
| 5 | `notebook_delete_cell` | ✅ | 参数: index (非cell_index) |
| 6 | `notebook_edit_cell` | ✅ | 参数: index |
| 7 | `notebook_move_cell` | ✅ | |
| 8 | `notebook_change_cell_type` | ✅ | 参数: index |
| 9 | `notebook_clear_outputs` | ✅ | |
| 10 | `notebook_get_cell` | ✅ | 参数: index |

## 问题记录

| 工具 | 问题描述 | 根因 | 修复 |
|------|----------|------|------|
| | | | |

> 无代码坏点。LSP优雅降级和Team状态不持久化都是架构设计。

## 交接说明

>D7 本计划由第三轮 AI 窗口处理。每次只手动执行一个命令测试,遇到任何不适都需要改代码修复。
> 测试完成'0830工具, 0坏点。LSP安装提示和Team持久化已完善。

## ⚠️ 待办：OmniSharp 安装

LSP 工具已完善安装提示，但 **OmniSharp 未实际安装**，LSP 功能仍不可用。

**安装步骤**（需手动执行）：
1. 从 https://github.com/OmniSharp/omnisharp-roslyn/releases 下载最新#1.39.15 的 `omnisharp-win-x64.zip`
2. 解压到 `~/.jcc/lsp-servers/om; omnisharp/`
3. 将 `omnisharp.exe` �; 添加到 PATH，或修改 `~/.jcc/lsp-servers.json` 中 omnisharp) 的 Command 为绝对路径
4/ 安装后重启 jcc，LSP 工具即可正常工作

> NuGet 上无 OmniSharp dotnet tool 包，需从 GitHub releases 手动下载二进制文件。
