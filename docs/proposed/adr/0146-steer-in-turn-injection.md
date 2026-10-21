# 0146. steer 轮内注入（引擎架构扩展）

- 状态：proposed
- 日期：2026-10-22
- 决策者：项目架构组

## 背景

DSH 前端两个消费点依赖 steer（轮内注入）：
1. `session/prompt mode:"steer"` — 忙时发送直接注入当前轮
2. `session/updateQueue action.kind:"steer"` — QueueDock 把排队消息逐条 steer 注入

引擎现状（2026-10-21 调研实证）：`IJccChatSession.StreamAsync(string, ct)` 一轮独占 `IAsyncEnumerable`，**无运行中注入点**；`modules/apps/engine/` 全目录无 `Steer` 命中（`PromptInjection` 是轮前 `!` 命令注入，非轮内）。宿主侧 updateQueue 的 steer 分支当前诚实返回 `session/steer-unavailable`。

已有可参考资产：w2 侧 `InterruptAndSend → RequestSteer` 子代理邮箱转发链（路由测试 6/6 绿）——证明 Actor 邮箱模型支持"打断+发送"语义，但那是 AgentService 层，非 ChatService 轮次层。

## 决策（草案）

在 ChatStream 中间件管道插入**注入中间件**（`SteerInjectionMiddleware`）：

1. 引擎暴露 `ISteerChannel`（Channel<SteerMessage>）——宿主 `session/prompt mode:"steer"` 在忙轮写入
2. 注入点选**工具调用间隙**（ToolCallEnd → ToolCallStart 之间）：中间件在流转发间隙 `TryRead` 非阻塞探针，命中即把 SteerMessage 追加为 user 消息进下一 LLM 请求
3. 注入结果以 `ChatStreamEvent` 新枚举 `Steered` 上报（宿主投影成 DSH `session/steer-accepted` 事件）
4. 队列 drain：轮次结束（Complete）后由 TurnRunner 检查 pending steer 队列（与现有 mode=queue 接续复用）

## 替代方案（考虑过但放弃）

| 方案 | 放弃原因 |
|------|----------|
| 降级"下轮优先"（steer 语义换 queue） | 与前端 steer 提示（注入当前轮）不符，欺骗性兼容 |
| StreamAsync 加第二参数/新接口 SteerAsync | 破坏 IAsyncEnumerable 单流模型，所有宿主改动面大 |
| AgentService 层 InterruptAndSend 复用 | 层级错配：那是子代理邮箱打断，非主会话轮内注入 |

## 开放问题（实施前须拍板）

1. 注入窗口边界：纯文本回复中段是否允许注入（当前草案只在工具间隙）？
2. sensenova 等 openai-compatible 供应商对"请求已发出后追加上下文"的支持边界（服务端不可注入，只能本地循环新请求）
3. Token 预算：注入消息计入当前轮预算还是下一轮

## 关联

- 上游：ADR 0143（DSH 托管）、TASK058 P2-B3（updateQueue steer 诚实拒绝）
- 参照：w2 InterruptAndSend→RequestSteer 链（AgentService 层先例）
- 消费：DSH QueueDock steerQueue()、session/prompt mode:"steer"
