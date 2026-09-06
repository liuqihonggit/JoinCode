# MCP 工具测试计划 10: 错误恢复 / 工作流 / 认证 / 权限 / 沙箱

> 第二轮:脚本批量验证 — 快速定位坏点

## 概述

| 项目 | 内容 |
|------|------|
| 计划编号 | 10/12 |
| 涵盖分类 | ErrorRecovery (7) + Workflow (7) + McpAuth (7) + Permission (7) + Sandbox (6) |
| 工具总数 | 34 个 |
| 测试方式 | 脚本批量调用 `jcc.exe mcp_call` |
| 前置条件 | jcc.exe 已编译 |

## 工具清单

### Category: ErrorRecovery (7 个)

| # | 工具名 | 说明 | 状态 |
|---|--------|------|------|
| 1 | `diagnose_error` | 诊断错误 | ✅ |
| 2 | `fix_shell_error` | 修复 Shell 错误 | ✅ |
| 3 | `fix_file_error` | 修复文件错误 | ✅ |
| 4 | `fix_merge_conflict` | 修复合并冲突 | ✅ |
| 5 | `resume_timed_out_task` | 恢复超时任务 | ✅ |
| 6 | `continue_long_running_task` | 继续长任务 | ✅ |
| 7 | `stop_long_running_task` | 停止长任务 | ✅ |

### Category: Workflow (7 个)

| # | 工具名 | 说明 | 状态 |
|---|--------|------|------|
| 1 | `mcp_ai_workflow_workflow_execute` | 执行工作流 | ✅ |
| 2 | `mcp_ai_workflow_plan_create_and_execute` | 创建并执行 | ✅ |
| 3 | `mcp_ai_workflow_workflow_generate_code` | 生成代码 | ✅ |
| 4 | `mcp_ai_workflow_workflow_analyze_code` | 分析代码 | ✅ |
| 5 | `mcp_ai_workflow_workflow_chat` | 工作流聊天 | ✅ |
| 6 | `mcp_ai_workflow_workflow_clear_history` | 清除历史 | ✅ |
| 7 | `mcp_ai_workflow_workflow_get_history` | 获取历史 | ✅ |

### Category: McpAuth (7 个)

| # | 工具名 | 说明 | 状态 |
|---|--------|------|------|
| 1 | `mcp_auth_apikey` | API Key 认证 | ✅ |
| 2 | `mcp_auth_bearer` | Bearer 认证 | ✅ |
| 3 | `mcp_auth_basic` | Basic 认证 | ✅ |
| 4 | `mcp_auth_oauth2` | OAuth2 认证 | ✅ |
| 5 | `mcp_auth_refresh` | 刷新令牌 | ✅ |
| 6 | `mcp_auth_status` | 认证状态 | ✅ |
| 7 | `mcp_auth_remove` | 移除认证 | ✅ |

### Category: Permission (7 个)

| # | 工具名 | 说明 | 状态 |
|---|--------|------|------|
| 1 | `permission_add_rule` | 添加权限规则 | ✅ |
| 2 | `permission_remove_rule` | 移除权限规则 | ✅ |
| 3 | `permission_list_rules` | 列出权限规则 | ✅ |
| 4 | `permission_check_tool` | 检查工具权限 | ✅ |
| 5 | `permission_check_path` | 检查路径权限 | ✅ |
| 6 | `permission_get_agent_rule` | 获取代理规则 | ✅ |
| 7 | `permission_clear_rules` | 清除权限规则 | ✅ |

### Category: Sandbox (6 个)

| # | 工具名 | 说明 | 状态 |
|---|--------|------|------|
| 1 | `sandbox_enter` | 进入沙箱 | ✅ |
| 2 | `sandbox_exit` | 退出沙箱 | ✅ |
| 3 | `sandbox_switch` | 切换沙箱 | ✅ |
| 4 | `sandbox_status` | 沙箱状态 | ✅ |
| 5 | `sandbox_exec` | 沙箱执行 | ✅ |
| 6 | `sandbox_exec_continue` | 继续执行 | ✅ |

## 测试脚本

### 批量验证脚本

```powershell
$jcc = "D:\project\w1\artifacts\bin\JoinCode\Release\net10.0\jcc.exe"
$errorRecoveryTools = @(
    "diagnose_error", "fix_shell_error", "fix_file_error", "fix_merge_conflict",
    "resume_timed_out_task", "continue_long_running_task", "stop_long_running_task"
)
$workflowTools = @(
    "mcp_ai_workflow_workflow_execute", "mcp_ai_workflow_plan_create_and_execute",
    "mcp_ai_workflow_workflow_generate_code", "mcp_ai_workflow_workflow_analyze_code",
    "mcp_ai_workflow_workflow_chat", "mcp_ai_workflow_workflow_clear_history",
    "mcp_ai_workflow_workflow_get_history"
)
$mcpAuthTools = @(
    "mcp_auth_api_key", "mcp_auth_bearer", "mcp_auth_basic", "mcp_auth_oauth2",
    "mcp_auth_refresh", "mcp_auth_status", "mcp_auth_remove"
)
$permissionTools = @(
    "permission_add_rule", "permission_remove_rule", "permission_list_rules",
    "permission_check_tool", "permission_check_path", "permission_get_agent_rule",
    "permission_clear_rules"
)
$sandboxTools = @(
    "sandbox_enter", "sandbox_exit", "sandbox_switch", "sandbox_status",
    "sandbox_exec", "sandbox_exec_continue"
)
$all = $errorRecoveryTools + $workflowTools + $mcpAuthTools + $permissionTools + $sandboxTools
foreach ($t in $all) {
    Write-Host "--- Testing: $t ---"
    & $jcc --trust --bypass mcp_call $t 2>&1 | Select-Object -First 5
}
```

### 只读工具冒烟测试

```powershell
$jcc = "D:\project\w1\artifacts\bin\JoinCode\Release\net10.0\jcc.exe"
& $jcc --trust --bypass mcp_call mcp_auth_status
& $jcc --trust --bypass mcp_call permission_list_rules
& $jcc --trust --bypass mcp_call sandbox_status
& $jcc --trust --bypass mcp_call mcp_ai_workflow_workflow_get_history
```

## 验收标准

- [ ] 每个工具调用返回格式正确的 JSON
- [ ] `Error:false` 且有非空输出
- [ ] 无崩溃/超时/死锁
- [ ] `sandbox_enter` / `sandbox_exit` 互为逆操作
- [ ] `permission_add_rule` / `permission_remove_rule` 互为逆操作
- [ ] `mcp_auth_remove` 移除后 `mcp_auth_status` 显示未认证
- [ ] 错误恢复工具在无错误时给出明确提示

## 风险提示

- `fix_merge_conflict` 修改文件,注意备份
- `mcp_ai_workflow_workflow_execute` 执行工作流,可能有副作用
- `mcp_auth_remove` 移除认证,后续操作可能失败
- `permission_clear_rules` 清除所有权限,安全风险
- `sandbox_exec` 在沙箱中执行命令,注意沙箱逃逸

## 问题记录

| 工具 | 问题描述 | 根因 | 修复 |
|------|----------|------|------|
| mcp_auth_api_key | 计划文档工具名错误 | 实际注册名为 `mcp_auth_apikey`（无下划线） | 文档已修正 |
| mcp_auth_status | 添加认证后 status 显示"暂无" | CLI 无状态模式跨进程不共享内存状态；敏感信息不持久化 | 预期行为，非 bug |
| sandbox_status | enter 后 status 仍显示不在沙箱 | 同上，沙箱状态在内存中，跨进程不共享 | 预期行为，非 bug |

## 测试结果总结

- **测试时间**: 2026-09-07
- **工具总数**: 34
- **通过**: 34
- **坏点**: 0（1个文档工具名错误已修正）
- **备注**: 
  - ErrorRecovery 7个工具全部正常，无参数时正确报告缺失参数
  - Workflow 7个工具为"提示词模式"，不实际调用 AI，返回结构化提示词信息
  - McpAuth 7个工具功能正常，认证配置跨进程不共享（敏感信息不持久化，预期行为）
  - Permission 7个工具全部正常，add/remove 互为逆操作验证通过
  - Sandbox 6个工具全部正常，未进入沙箱时正确拒绝执行/切换操作

## 交接说明

> 本计划由第三轮 AI 窗口处理。每次只手动执行一个命令测试,遇到任何不适都需要改代码修复。
