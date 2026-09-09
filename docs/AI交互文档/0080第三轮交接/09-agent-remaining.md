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

## 验收标准（严格 — 对齐 ADR0080）

每个工具必须满足以下全部条件才算通过：

1. **格式正确** — 启动参数格式统一，无歧义
2. **Error:false+有意义输出** — 空输出/0条记录)不算成功，必须返回有意义的信息
3. **友好报错** — 不存在的ID/缺参数/类型错误必须友好报错，不崩溃不泄露内部堆栈
4. **不卡死** — 30s 内必须返回，超时必须有备注
5. **跨进程可用** — 持久化到文件，跨 mcp_call 进程可查询
6. **边缘场景** — 读取文件再执行命令出错、并发操作、空输入等边缘场景必须处理
7. **不用"设计限制"安慰自己** — 用不了就是用不了，客户也用不了

### 当前测试状态（需深入补全）

| 工具 | 当前状态 | 问题 | 需深入测试 |
|------|---------|------|-----------|
| agent | dry_run 创建成功 | — | ✅ 已深入 |
| agent_status | 跨进程查询成功 | — | ✅ 已深入 |
| agent_get_messages | 返回 0 条消息 | ❌ 0条不算成功 | 需先发消息再查询 |
| agent_stop | 跨进程停止成功 | — | ✅ 已深入 |
| agent_running | 0 个运行中 | ❌ 0个不算成功 | 需先创建agent再查询 |
| agent_send_message | nonexistent 友好报错 | ❌ 只测了不存在ID | 需用真实agent发消息 |
| explore_agent | 返回探索报告 | — | ✅ 已深入 |
| forward_user_input | 转发成功 | ❌ 只测了不存在ID | 需用真实agent转发 |
| general_agent | 返回结果 | — | ✅ 已深入 |
| guide_agent | 返回使用指南 | — | ✅ 已深入 |
| list_agents | 列出内置Agent | — | ✅ 已深入 |
| plan_agent | 返回计划 | — | ✅ 已深入 |
| verification_agent | 返回验证结论 | — | ✅ 已深入 |

### 需补全的深入测试项

1. **agent_get_messages** — 需先 agent_send_message 发消息: 真实agent → 发消息 → 查询消息 > 0 条
2. **agent_running** — 需先创建 dry_run agent → 查询 agent_running > 0 个
3. **agent_send_message** — 需用真实 agent_id 发消息 → 验证消息送达
4. **forward_user_input** — 需用真实 agent_id 转发输入 → 验证转发成功
