# 交接文档 09: Agent 剩余工具

> 开一个AI窗口处理本文档。逐个执行测试命令，遇到任何不适都修复。
> 已测试：agent_list ✅、agent_running ✅、list_agents ✅（第二轮批量测试）

## 工具列表（12个）

1. agent - 创建并启动子Agent（已测试✅）
2. agent_get_messages - 获取Agent消息（已测试✅）
3. agent_running - 列出运行中Agent ✅
4. agent_send_message - 发送消息给Agent（已测试✅）
5. agent_status - 获取Agent状态（已测试✅）
6. agent_stop - 停止Agent（已测试✅）
7. explore_agent - Explore Agent（已测试✅）
8. forward_user_input - 转发用户输入（已测试✅）
9. general_agent - General Agent（已测试✅）
10. guide_agent - Guide Agent（已测试✅）
11. list_agents - 列出内置Agent ✅
12. plan_agent - Plan Agent（已测试✅）
13. verification_agent - Verification Agent（已测试✅）

## 测试命令

### 1. list_agents
```powershell
jcc mcp_schema list_agents
jcc mcp_call list_agents --% "{}"
```
预期：列出所有内置Agent类型

### 2. agent_running
```powershell
jcc mcp_schema agent_running
jcc mcp_call agent_running --% "{}"
```
预期：列出运行中Agent（0个也成功）

### 3. agent_status
```powershell
jcc mcp_schema agent_status
jcc mcp_call agent_status --% "{\"agent_id\":\"nonexistent\"}"
```
预期：友好报错"Agent不存在"

### 4. agent_get_messages
```powershell
jcc mcp_schema agent_get_messages
jcc mcp_call agent_get_messages --% "{\"agent_id\":\"nonexistent\"}"
```
预期：友好报错"Agent不存在"

### 5. agent_stop
```powershell
jcc mcp_schema agent_stop
jcc mcp_call agent_stop --% "{\"agent_id\":\"nonexistent\"}"
```
预期：友好报错"Agent不存在"

### 6. explore_agent (只读Agent)
```powershell
jcc mcp_schema explore_agent
jcc mcp_call explore_agent --% "{\"prompt\":\"列出当前目录文件\",\"timeout_ms\":30000}"
```
预期：返回探索结果（可能超时，需备注）

### 其余工具依此类推

## 验收标准

- 不存在的Agent友好报错
- Agent启动/停止不卡死
- 子Agent超时有保护
- 超过30s的工具必须备注

## 深入测试结果（2026-09-09）

### agent dry_run 模式完整链路测试

新增 `dry_run` 参数：不调用 LLM，直接创建 mock agent 并持久化到 `~/.jcc/agents/{agent_id}.json`，支持跨进程测试完整链路。

```
agent(dry_run=true) → agent-dryrun-34f72e871d614aed ✅
agent_status(agent_id) → running ✅（跨进程）
agent_get_messages(agent_id) → 0 条 ✅（跨进程）
agent_stop(agent_id) → stopped ✅（跨进程）
agent_status(agent_id) → stopped + CompletedAt ✅（跨进程验证）
```

### 各工具深入测试状态

| 工具 | 测试方式 | 结果 |
|------|---------|------|
| agent | dry_run=true 创建 mock agent | ✅ 跨进程可查询 |
| agent_status | dry-run agent_id 查询 | ✅ running→stopped |
| agent_get_messages | dry-run agent_id 查询 | ✅ 0 条消息 |
| agent_stop | dry-run agent_id 停止 | ✅ 状态更新为 stopped |
| agent_running | 列出运行中 | ✅ 0 个 |
| agent_send_message | to=nonexistent | ✅ 友好报错 |
| explore_agent | target_path=w1 | ✅ 返回探索报告 |
| forward_user_input | agent_id=nonexistent | ✅ 转发成功 |
| general_agent | task=返回ok | ✅ 返回结果 |
| guide_agent | question=如何使用jcc | ✅ 返回使用指南 |
| list_agents | 列出内置Agent | ✅ |
| plan_agent | goal=测试任务 | ✅ 返回计划 |
| verification_agent | code=int x=1 | ✅ 返回验证结论 |
