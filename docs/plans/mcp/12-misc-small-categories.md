# MCP 工具测试计划 12: 其余小类别

> 第二轮:脚本批量验证 — 快速定位坏点

## 概述

| 项目 | 内容 |
|------|------|
| 计划编号 | 12/12 |
| 涵盖分类 | Build + CodeExecution + Todo + Vcr + Goal + CodeGeneration + Brief + StructuredOutput + Sleep + Policy + Context + Snip + Terminal + Repl + Browser + Peers + PrSubscription + RemoteTrigger + Monitor + Notification + StepEvidence + Interaction + FileTransfer |
| 工具总数 | 36 个 |
| 测试方式 | 脚本批量调用 `jcc.exe mcp_call` |
| 前置条件 | jcc.exe 已编译 |

## 工具清单

### Category: Build (3 个)

| # | 工具名 | 说明 | 状态 |
|---|--------|------|------|
| 1 | `build_output` | 构建输出 | ⬜ |
| 2 | `build_queue_status` | 构建队列状态 | ⬜ |
| 3 | `build_cancel` | 取消构建 | ⬜ |

### Category: CodeExecution (3 个)

| # | 工具名 | 说明 | 状态 |
|---|--------|------|------|
| 1 | `execute_csharp_code` | 执行 C# 代码 | ⬜ |
| 2 | `evaluate_expression` | 求值表达式 | ⬜ |
| 3 | `test_code_snippet` | 测试代码片段 | ⬜ |

### Category: Todo (3 个)

| # | 工具名 | 说明 | 状态 |
|---|--------|------|------|
| 1 | `todo_write` | 写入 Todo | ⬜ |
| 2 | `todo_list` | 列出 Todo | ⬜ |
| 3 | `todo_update` | 更新 Todo | ⬜ |

### Category: Vcr (3 个)

| # | 工具名 | 说明 | 状态 |
|---|--------|------|------|
| 1 | `vcr_record` | 开始录制 | ⬜ |
| 2 | `vcr_playback` | 回放 | ⬜ |
| 3 | `vcr_status` | 录制状态 | ⬜ |

### Category: Goal (3 个)

| # | 工具名 | 说明 | 状态 |
|---|--------|------|------|
| 1 | `goal_get` | 获取目标 | ⬜ |
| 2 | `goal_update` | 更新目标 | ⬜ |
| 3 | `goal_graph_define` | 定义目标图 | ⬜ |

### Category: CodeGeneration (3 个)

| # | 工具名 | 说明 | 状态 |
|---|--------|------|------|
| 1 | `generate_csharp_code` | 生成 C# 代码 | ⬜ |
| 2 | `generate_unit_test` | 生成单元测试 | ⬜ |
| 3 | `generate_api_controller` | 生成 API 控制器 | ⬜ |

### Category: Brief (3 个)

| # | 工具名 | 说明 | 状态 |
|---|--------|------|------|
| 1 | `brief_mode` | 简洁模式 | ⬜ |
| 2 | `brief_status` | 简洁状态 | ⬜ |
| 3 | `send_user_message` | 发送用户消息 | ⬜ |

### Category: StructuredOutput (2 个)

| # | 工具名 | 说明 | 状态 |
|---|--------|------|------|
| 1 | `structured_output_register` | 注册结构化输出 | ⬜ |
| 2 | `structured_output_validate` | 验证结构化输出 | ⬜ |

### Category: Sleep (2 个)

| # | 工具名 | 说明 | 状态 |
|---|--------|------|------|
| 1 | `sleep` | 休眠 | ⬜ |
| 2 | `sleep_until` | 休眠直到 | ⬜ |

### Category: Policy (2 个)

| # | 工具名 | 说明 | 状态 |
|---|--------|------|------|
| 1 | `policy_check` | 策略检查 | ⬜ |
| 2 | `policy_list` | 列出策略 | ⬜ |

### Category: 单工具分类 (11 个)

| # | 工具名 | 分类 | 说明 | 状态 |
|---|--------|------|------|------|
| 1 | `ctx_inspect` | Context | 上下文检查 | ⬜ |
| 2 | `snip` | Snip | 代码片段 | ⬜ |
| 3 | `terminal_capture` | Terminal | 终端捕获 | ⬜ |
| 4 | `repl` | Repl | REPL 交互 | ⬜ |
| 5 | `web_browser` | Browser | 浏览器 | ⬜ |
| 6 | `list_peers` | Peers | 列出对等端 | ⬜ |
| 7 | `subscribe_pr` | PrSubscription | 订阅 PR | ⬜ |
| 8 | `remote_trigger` | RemoteTrigger | 远程触发 | ⬜ |
| 9 | `monitor` | Monitor | 监控 | ⬜ |
| 10 | `push_notification` | Notification | 推送通知 | ⬜ |
| 11 | `complete_step` | StepEvidence | 完成步骤 | ⬜ |
| 12 | `ask_user_question` | Interaction | 询问用户 | ⬜ |
| 13 | `send_user_file` | FileTransfer | 发送文件 | ⬜ |

## 测试脚本

### 批量验证脚本

```powershell
$jcc = "D:\project\w1\artifacts\bin\JoinCode\Release\net10.0\jcc.exe"
$tools = @(
    # Build
    "build_output", "build_queue_status", "build_cancel",
    # CodeExecution
    "execute_csharp_code", "evaluate_expression", "test_code_snippet",
    # Todo
    "todo_write", "todo_list", "todo_update",
    # Vcr
    "vcr_record", "vcr_playback", "vcr_status",
    # Goal
    "goal_get", "goal_update", "goal_graph_define",
    # CodeGeneration
    "generate_csharp_code", "generate_unit_test", "generate_api_controller",
    # Brief
    "brief_mode", "brief_status", "send_user_message",
    # StructuredOutput
    "structured_output_register", "structured_output_validate",
    # Sleep
    "sleep", "sleep_until",
    # Policy
    "policy_check", "policy_list",
    # 单工具分类
    "ctx_inspect", "snip", "terminal_capture", "repl", "web_browser",
    "list_peers", "subscribe_pr", "remote_trigger", "monitor",
    "push_notification", "complete_step", "ask_user_question", "send_user_file"
)
foreach ($t in $tools) {
    Write-Host "--- Testing: $t ---"
    & $jcc --trust --bypass mcp_call $t 2>&1 | Select-Object -First 5
}
```

### 只读工具冒烟测试

```powershell
$jcc = "D:\project\w1\artifacts\bin\JoinCode\Release\net10.0\jcc.exe"
& $jcc --trust --bypass mcp_call todo_list
& $jcc --trust --bypass mcp_call vcr_status
& $jcc --trust --bypass mcp_call goal_get
& $jcc --trust --bypass mcp_call brief_status
& $jcc --trust --bypass mcp_call policy_list
& $jcc --trust --bypass mcp_call list_peers
& $jcc --trust --bypass mcp_call build_queue_status
```

## 验收标准

- [ ] 每个工具调用返回格式正确的 JSON
- [ ] `Error:false` 且有非空输出
- [ ] 无崩溃/超时/死锁
- [ ] `vcr_record` / `vcr_playback` 往返一致
- [ ] `todo_write` + `todo_list` 往返一致
- [ ] `sleep` 超时参数正确处理
- [ ] `ask_user_question` 在非交互模式下优雅降级
- [ ] `execute_csharp_code` 编译错误时给出明确提示

## 风险提示

- `build_cancel` 取消构建,注意副作用
- `execute_csharp_code` 执行任意代码,注意安全
- `vcr_record` 录制操作,可能消耗资源
- `remote_trigger` 远程触发,注意安全
- `push_notification` 发送通知,注意频率限制
- `sleep` / `sleep_until` 阻塞执行,注意超时设置
- `send_user_file` / `send_user_message` 涉及用户交互

## 问题记录

| 工具 | 问题描述 | 根因 | 修复 |
|------|----------|------|------|
| | | | |

## 交接说明

> 本计划由第三轮 AI 窗口处理。每次只手动执行一个命令测试,遇到任何不适都需要改代码修复。
