# MCP 工具测试计划 07: 文件与计划 File + Plan

> 第二轮:脚本批量验证 — 快速定位坏点

## 概述

| 项目 | 内容 |
|------|------|
| 计划编号 | 07/12 |
| 涵盖分类 | File (12) + Plan (11) |
| 工具总数 | 23 个 |
| 测试方式 | 脚本批量调用 `jcc.exe mcp_call` |
| 前置条件 | jcc.exe 已编译,测试目录可读写 |

## 工具清单

### Category: File (12 个)

| # | 工具名 | 说明 | 状态 |
|---|--------|------|------|
| 1 | `file_read` | 读取文件 | ⬜ |
| 2 | `file_write` | 写入文件 | ⬜ |
| 3 | `file_edit` | 编辑文件 | ⬜ |
| 4 | `file_delete` | 删除文件 | ⬜ |
| 5 | `directory_list` | 列出目录 | ⬜ |
| 6 | `file_edit_regex` | 正则编辑 | ⬜ |
| 7 | `file_insert_lines` | 插入行 | ⬜ |
| 8 | `file_delete_lines` | 删除行 | ⬜ |
| 9 | `file_batch_edit` | 批量编辑 | ⬜ |
| 10 | `file_snip_lines` | 截取行 | ⬜ |
| 11 | `file_snip_preview` | 截取预览 | ⬜ |
| 12 | `file_apply_patch` | 应用补丁 | ⬜ |

### Category: Plan (11 个)

| # | 工具名 | 说明 | 状态 |
|---|--------|------|------|
| 1 | `enter_plan_mode` | 进入计划模式 | ⬜ |
| 2 | `exit_plan_mode` | 退出计划模式 | ⬜ |
| 3 | `get_plan_status` | 计划状态 | ⬜ |
| 4 | `add_plan_step` | 添加步骤 | ⬜ |
| 5 | `approve_plan_step` | 批准步骤 | ⬜ |
| 6 | `reject_plan_step` | 拒绝步骤 | ⬜ |
| 7 | `execute_plan_steps` | 执行步骤 | ⬜ |
| 8 | `modify_plan_step` | 修改步骤 | ⬜ |
| 9 | `remove_plan_step` | 移除步骤 | ⬜ |
| 10 | `get_plan_history` | 计划历史 | ⬜ |
| 11 | `verify_plan_execution` | 验证执行 | ⬜ |

## 测试脚本

### 批量验证脚本

```powershell
$jcc = "D:\project\w1\artifacts\bin\JoinCode\Release\net10.0\jcc.exe"
$fileTools = @(
    "file_read", "file_write", "file_edit", "file_delete", "directory_list",
    "file_edit_regex", "file_insert_lines", "file_delete_lines",
    "file_batch_edit", "file_snip_lines", "file_snip_preview", "file_apply_patch"
)
$planTools = @(
    "enter_plan_mode", "exit_plan_mode", "get_plan_status", "add_plan_step",
    "approve_plan_step", "reject_plan_step", "execute_plan_steps",
    "modify_plan_step", "remove_plan_step", "get_plan_history", "verify_plan_execution"
)
foreach ($t in $fileTools + $planTools) {
    Write-Host "--- Testing: $t ---"
    & $jcc --trust --bypass mcp_call $t 2>&1 | Select-Object -First 5
}
```

### 只读工具冒烟测试

```powershell
$jcc = "D:\project\w1\artifacts\bin\JoinCode\Release\net10.0\jcc.exe"
# 列出当前目录
& $jcc --trust --bypass mcp_call directory_list --args-file .\dir_list_args.json
# 计划状态
& $jcc --trust --bypass mcp_call get_plan_status
& $jcc --trust --bypass mcp_call get_plan_history
```

## 验收标准

- [ ] 每个工具调用返回格式正确的 JSON
- [ ] `Error:false` 且有非空输出
- [ ] 无崩溃/超时/死锁
- [ ] `file_read` 不存在的文件给出明确错误(非崩溃)
- [ ] `file_write` + `file_read` 往返一致
- [ ] `enter_plan_mode` + `exit_plan_mode` 互为逆操作
- [ ] `file_apply_patch` 支持 git apply 格式

## 风险提示

- **`file_delete` 是破坏性操作**,测试时用临时文件
- `file_write` 可能覆盖重要文件,注意路径验证
- `file_edit_regex` 正则错误可能损坏文件
- `execute_plan_steps` 执行计划步骤,可能有副作用

## 问题记录

| 工具 | 问题描述 | 根因 | 修复 |
|------|----------|------|------|
| | | | |

## 交接说明

> 本计划由第三轮 AI 窗口处理。每次只手动执行一个命令测试,遇到任何不适都需要改代码修复。
