# MCP 工具测试计划 04: 分析与智能体 Analytics + Agent

> 第二轮:脚本批量验证 — 快速定位坏点

## 概述

| 项目 | 内容 |
|------|------|
| 计划编号 | 04/12 |
| 涵盖分类 | Analytics (15) + Agent (14) |
| 工具总数 | 29 个 |
| 测试方式 | 脚本批量调用 `jcc.exe mcp_call` |
| 前置条件 | jcc.exe 已编译 |

## 工具清单

### Category: Analytics (15 个)

| # | 工具名 | 说明 | 状态 |
|---|--------|------|------|
| 1 | `agent_system_stats` | 系统统计 | ⬜ |
| 2 | `agent_list_stats` | 列出统计 | ⬜ |
| 3 | `agent_stats` | 智能体统计 | ⬜ |
| 4 | `agent_history` | 智能体历史 | ⬜ |
| 5 | `agent_running_stats` | 运行中统计 | ⬜ |
| 6 | `agent_execution_detail` | 执行详情 | ⬜ |
| 7 | `agent_clear_history` | 清除历史 | ⬜ |
| 8 | `tool_score` | 工具评分 | ⬜ |
| 9 | `tool_hypergraph` | 工具超图 | ⬜ |
| 10 | `tool_score_reset` | 重置评分 | ⬜ |
| 11 | `analytics_report` | 分析报告 | ⬜ |
| 12 | `analytics_tools` | 工具分析 | ⬜ |
| 13 | `analytics_events` | 事件分析 | ⬜ |
| 14 | `analytics_export` | 导出分析 | ⬜ |
| 15 | `analytics_clear` | 清除分析 | ⬜ |

### Category: Agent (14 个)

| # | 工具名 | 说明 | 状态 |
|---|--------|------|------|
| 1 | `plan_agent` | 计划智能体 | ⬜ |
| 2 | `explore_agent` | 探索智能体 | ⬜ |
| 3 | `verification_agent` | 验证智能体 | ⬜ |
| 4 | `general_agent` | 通用智能体 | ⬜ |
| 5 | `guide_agent` | 引导智能体 | ⬜ |
| 6 | `list_agents` | 列出智能体 | ⬜ |
| 7 | `agent` | 智能体 | ⬜ |
| 8 | `agent_list` | 智能体列表 | ⬜ |
| 9 | `agent_status` | 智能体状态 | ⬜ |
| 10 | `agent_stop` | 停止智能体 | ⬜ |
| 11 | `agent_running` | 运行中智能体 | ⬜ |
| 12 | `agent_send_message` | 发送消息 | ⬜ |
| 13 | `forward_user_input` | 转发用户输入 | ⬜ |
| 14 | `agent_get_messages` | 获取消息 | ⬜ |

## 测试脚本

### 批量验证脚本

```powershell
$jcc = "D:\project\w1\artifacts\bin\JoinCode\Release\net10.0\jcc.exe"
$analyticsTools = @(
    "agent_system_stats", "agent_list_stats", "agent_stats", "agent_history",
    "agent_running_stats", "agent_execution_detail", "agent_clear_history",
    "tool_score", "tool_hypergraph", "tool_score_reset",
    "analytics_report", "analytics_tools", "analytics_events", "analytics_export", "analytics_clear"
)
$agentTools = @(
    "plan_agent", "explore_agent", "verification_agent", "general_agent",
    "guide_agent", "list_agents", "agent", "agent_list", "agent_status",
    "agent_stop", "agent_running", "agent_send_message", "forward_user_input", "agent_get_messages"
)
foreach ($t in $analyticsTools + $agentTools) {
    Write-Host "--- Testing: $t ---"
    & $jcc --trust --bypass mcp_call $t 2>&1 | Select-Object -First 5
}
```

### 只读工具冒烟测试

```powershell
$jcc = "D:\project\w1\artifacts\bin\JoinCode\Release\net10.0\jcc.exe"
& $jcc --trust --bypass mcp_call agent_system_stats
& $jcc --trust --bypass mcp_call list_agents
& $jcc --trust --bypass mcp_call tool_hypergraph
```

## 验收标准

- [ ] 每个工具调用返回格式正确的 JSON
- [ ] `Error:false` 且有非空输出
- [ ] 无崩溃/超时/死锁
- [ ] `agent_clear_history` / `analytics_clear` 可重复执行(幂等)
- [ ] `tool_score_reset` 重置后评分归零
- [ ] 智能体启动/停止有明确状态返回

## 风险提示

- `agent_clear_history` / `analytics_clear` 是破坏性操作,清除后不可恢复
- 智能体启动可能消耗资源,注意 `agent_stop` 配对使用
- `tool_score_reset` 影响全局评分,测试后需恢复

## 问题记录

| 工具 | 问题描述 | 根因 | 修复 |
|------|----------|------|------|
| `analytics_report` | 空数据时抛 "Sequence contains no elements" 异常 | `AnalyticsService.GetUsageReport` 中 `Average()` 在空集合上抛异常 | 用 `DefaultIfEmpty(0).Average()` 优雅降级 |
| `agent` (文档) | 文档写 `agent` 但实际工具名是 `Agent` | 文档错误 | 文档需更新 |
| `agent_send_message` (文档) | 文档写 `agent_send_message` 但实际工具名是 `SendMessage` | 文档错误 | 文档需更新 |

## 交接说明

> 本计划由第三轮 AI 窗口处理。每次只手动执行一个命令测试,遇到任何不适都需要改代码修复。

## 最终测试结果 (2026-09-06)

| 状态 | 数量 | 说明 |
|------|------|------|
| OK | 11 | 正常返回有意义的结果 |
| ERROR | 16 | 全部是缺参数错误(预期行为) |
| 坏点 | 1 | analytics_report空数据异常(已修复) |
| 文档错误 | 2 | agent→Agent, agent_send_message→SendMessage |

### 通过的工具 (11个)

**Analytics (8个)**: agent_system_stats, agent_list_stats, agent_running_stats, tool_score, tool_hypergraph, analytics_tools, analytics_events, analytics_export
**Agent (3个)**: list_agents, agent_list, agent_running

### 修复的坏点

- `analytics_report`: 空数据时 `Average()` 抛异常 → `DefaultIfEmpty(0).Average()` 优雅降级 (commit cb3239e4e)
