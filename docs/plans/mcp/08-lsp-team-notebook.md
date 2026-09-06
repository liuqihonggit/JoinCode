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

## 问题记录

| 工具 | 问题描述 | 根因 | 修复 |
|------|----------|------|------|
| | | | |

## 交接说明

> 本计划由第三轮 AI 窗口处理。每次只手动执行一个命令测试,遇到任何不适都需要改代码修复。
