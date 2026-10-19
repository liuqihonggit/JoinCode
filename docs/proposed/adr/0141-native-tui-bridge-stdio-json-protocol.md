# ADR 0141: native_tui bridge 进程协议 — stdio 行分格 JSON

- 状态：proposed
- 日期：2026-10-20
- 关联：ADR 0140（native_tui 自研像素级对齐）、TASK055（M6 引擎接入里程碑）

## 背景与问题

native_tui（TUI 渲染层，独立零依赖工程）需要与引擎进程（jcc.exe）通信：用户提交输入、
引擎推送回合事件（thinking/工具调用/流式 token/完成）。ADR 0140 已决策 bridge 层采用
进程协议（非库内嵌），本 ADR 决定协议的具体形态与消息契约。

## 决策

**newline-delimited JSON over stdio（子进程 stdin/stdout 管道）**：
- TUI 作为父进程拉起引擎子进程，请求写入子进程 stdin，事件从子进程 stdout 读取
- 每条消息一行 JSON（`\n` 分隔），UTF-8 编码
- 消息类型判别：`type` 字段（System.Text.Json `[JsonPolymorphic]` 多态序列化，AOT 兼容）

### 消息契约

请求（TUI → Engine，写 stdin）：

| type | 字段 | 语义 |
|------|------|------|
| `submit` | `text: string` | 提交用户输入，开始回合 |
| `interrupt` | — | 打断当前回合 |
| `quit` | — | 退出会话 |

事件（Engine → TUI，读 stdout）：

| type | 字段 | 语义 |
|------|------|------|
| `turn_started` | — | 回合开始（busy 挂载 spinner） |
| `thinking_started` | — | 进入思考（spinner Thinking 模式） |
| `thinking_ended` | `durationMs: long` | 思考结束（驱动 ThinkingLifecycle） |
| `tool_call` | `name: string`, `args: string` | 工具调用消息（`⎿` 缩进项） |
| `tool_result` | `summary: string` | 工具结果摘要 |
| `token_delta` | `length: int` | 流式 token 增量（spinner ResponseLength 累加） |
| `assistant_message` | `text: string` | 完整助手消息（追加滚动区） |
| `turn_completed` | — | 回合完成（idle） |
| `turn_interrupted` | — | 回合被打断（idle） |
| `error` | `message: string` | 错误（含诱导说明） |

### 容错语义

- 空行跳过；未知 `type` 跳过并给诊断（支持类型清单 — 错误提示必须有诱导方式）
- 消息 DTO 双向转换全部走 `BridgeJsonContext`（JsonSerializer.Serialize/Deserialize），禁止手写拼接

## 替代方案（考虑后放弃）

| 方案 | 放弃原因 |
|------|----------|
| 本地 socket（named pipe / Unix socket） | 需端口/管道管理、连接生命周期与重连逻辑；stdio 由 OS 直接提供父子进程通道，零额外基建 |
| gRPC / MessagePack | 引入重依赖，违反 native_tui 零依赖工程约束；JSON 可直接人工调试（tee 日志） |
| 进程内嵌（引擎以库形式链接进 TUI） | 违反 ADR 0140 已定的进程协议决策；引擎崩溃会连带 TUI；无法独立升级 |
| 长度前缀二进制帧 | 解析需手写状态机；行分格 JSON 用 ReadLine 即可，且人眼可读 |

## 实施范围

- M6e-1（本 ADR 随附）：DTO 契约 + BridgeJsonContext + 协议读写器 + 单元测试（内存流级验证）
- M6e-2（后续）：jcc.exe 服务端事件泵 + Demo 真实子进程对接（真实 exe 手动验收，ADR 0080）

## 后果

- 正向：协议与传输解耦（未来可换 socket 只动读写器）；DTO 契约即文档；AOT 安全
- 负向：行分格要求消息内无裸换行（JSON 序列化自动转义，天然满足）；单进程吞吐上限受管道限制（回合事件频率极低，不构成约束）
