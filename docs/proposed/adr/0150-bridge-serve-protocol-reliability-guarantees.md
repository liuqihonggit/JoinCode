# ADR 0150: bridge_serve NDJSON 协议可靠性保障

> 📍 **导航**: [docs/](../../README.md) › [adr/](../README.md)
> 🔗 **上游索引**: [adr/README.md](../README.md) — 修改本文档后须同步更新此索引

> 状态: proposed
> 创建: 2026-10-26
> 决策者: 用户主导（跨进程通信可靠性要求 — 流水号/ACK/幂等/回函编号确认）
> 影响: modules/apps/engine/bridge/BridgeProtocol.cs + modules/apps/cli/core/commands/core/BridgeServeLoop.cs + 所有消费方（native_tui BridgeEventPump + E2E DualRoleConversationRunner）
> 关联: [ADR 0141](../implemented/adr/architecture/0141-webui-bridge-protocol.md) · [ADR 0149](../implemented/adr/architecture/0149-jcc-default-tui-host-merge-native-tui.md)

## 背景

bridge_serve NDJSON 协议（ADR 0141 CLI 版）是 jcc bridge_serve 子命令的 stdio 通信协议：
- 请求（stdin）：submit / interrupt / set_permission_mode / stop_task / quit
- 事件（stdout）：session_info / turn_started / thinking_* / tool_call / tool_result / assistant_message / turn_completed / turn_interrupted / error

当前协议**缺乏跨进程通信可靠性保障**：

| 保障 | 当前状态 | 风险场景 |
|------|---------|---------|
| 流水号 | ❌ 无 sequence number | 重试时无法关联请求与响应 |
| ACK 回函 | ❌ 无请求-响应关联编号 | 客户端无法确认请求被收到 |
| 幂等 | ⚠️ 仅忙时 submit 返回 Error | 重发 submit 可能启动新回合 |
| 重发 | ❌ 无重发机制 | 进程崩溃后未处理请求丢失 |
| MD5 校验 | ❌ 无完整性校验 | JSON 解析错误时消息被跳过 |

**stdio 管道的可靠性边界**：
- 内核保证不丢失、不乱序、不重复（FIFO 字节流）
- 单次写入 < PIPE_BUF（Linux 4096）原子
- **不保证**：进程崩溃时未处理消息不丢失、JSON 解析错误时消息不跳过、客户端重试时不重复执行

**触发事件**：E2E 测试重试 16 次每次重发 submit，但无幂等保障和回函编号，无法关联响应与请求。CI #983/#985/#988 反复超时/断言失败。

## 决策

1. **流水号（seq）**：每条 BridgeRequest 加 `seq` 字段（`ulong`，客户端单调递增）。BridgeEvent 加 `ack_seq` 字段（可选 `ulong?`，关联触发的请求 seq）。
   - submit(seq=1) → turn_started(ack_seq=1) + 事件 + turn_completed(ack_seq=1)
   - 回合失败：error(ack_seq=1) + turn_completed(ack_seq=1)
   - 消费方通过 ack_seq 关联响应与请求

2. **ACK 回函**：turn_completed / turn_interrupted / error 都带 ack_seq，作为请求的回函确认。消费方收到带 ack_seq 的回合结束事件，确认请求已处理。

3. **幂等 submit**：bridge_serve 维护 `lastProcessedSeq`，相同 seq 的 submit 不重复执行，返回已处理回执（error "重复提交，已忽略" + turn_completed(ack_seq=seq)）。不同 seq 的 submit 正常处理。

4. **不加 MD5 校验**：stdio 管道由内核保证字节流完整，JSON 解析错误由 `BridgeProtocol.TryReadRequest` 返回 Skipped + stderr 诊断处理。MD5 增加开销无收益。

5. **不加自动重发**：stdio 管道不丢失数据。进程崩溃由客户端检测 `IsRunning` 处理。客户端重试是上层逻辑（E2E 测试的 16 次重试），不由协议层负责。

6. **协议版本协商**：session_info 事件加 `protocol_version` 字段（当前 "2025-11-25"）。客户端检测版本不匹配时拒绝连接。

## 替代方案

1. **JSON-RPC 2.0（id + method + params）** — 通用规范，自带 id 关联。但 params 弱类型（JsonElement 动态解析），与 ADR 0133"DTO 双向转换"冲突且 AOT 不友好；弃
2. **gRPC over stdio** — 二进制协议，自带流水号/ACK/重发。但与 NDJSON 文本协议冲突，不可读调试；弃
3. **不加任何保障（维持现状）** — stdio 管道内核保证可靠性，当前问题用 turn_completed 修复即可。但重试场景缺乏幂等保障，客户端无法关联响应与请求；弃（用户明确要求）
4. **全量 ACK 机制（每条请求都回 ACK）** — 类似 TCP ACK。但 bridge_serve 是单回合模式（同时只有一个回合），turn_completed 就是回函，不需要逐条 ACK；弃

## 后果

### 正面
- 客户端可通过 ack_seq 关联响应与请求，重试时判断是否重复
- 幂等 submit 防止重试导致重复执行
- protocol_version 协商防止版本不匹配
- 回合失败也发 turn_completed（已修复，CI #983 根因）

### 负面
- BridgeProtocol 协议变更：BridgeRequest 加 seq 字段，BridgeEvent 加 ack_seq 字段
- 所有消费方需更新：native_tui BridgeEventPump、E2E DualRoleConversationRunner
- 序列化兼容：seq/ack_seq 为可选字段（`ulong?`），旧客户端不发送时默认 null，bridge_serve 容忍 null seq（不幂等但兼容）

### 实现顺序
1. BridgeProtocol.cs：BridgeRequest 加 seq 字段，BridgeEvent 加 ack_seq 字段
2. BridgeServeLoop.cs：RunTurnAsync 传递 seq，事件带 ack_seq；幂等 submit 检查
3. BridgeEventConsumer.cs：事件携带 ack_seq
4. native_tui BridgeEventPump：消费 ack_seq
5. E2E DualRoleConversationRunner：submit 带 seq，等 turn_completed(ack_seq=seq)
6. 测试：幂等 submit 验证、ack_seq 关联验证
