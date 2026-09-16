# 0108. 统一邮箱基类 MailboxBase + 有名管道邮箱 + 主机选举 + 全局编译队列

- 状态：accepted
- 日期：2026-09-16
- 决策者：用户 + AI
- 前置：ADR 0107（文件邮箱锁替代跨进程共享 Mutex + Actor 邮箱模型）

## 背景

### 问题1：四种邮箱没有统一基类

当前邮箱实现分散，无统一基类：

| 邮箱类型 | 实现 | 基类 | 背压 | 水位线 | 跨进程 |
|---------|------|------|------|--------|--------|
| 进程内 | `InProcessMailbox` | `ServiceEntity` | ❌ `CreateUnbounded` | ❌ | ❌ |
| 文件 | `TeammateMailboxService` + `MailboxActor` | `ActorBase<MailboxCommand, Unit>` | ✅ | ✅ | ✅ `FileMailboxLock` |
| 有名管道 | ❌ 未实现 | — | — | — | — |
| 网络 | ❌ 未实现 | — | — | — | — |

`InProcessMailbox` 用 `Channel.CreateUnbounded`，无背压无水位线，生产方无法被限速，OOM 风险。

### 问题2：无有名管道邮箱，跨进程子代理无法互访

当前跨进程通信依赖文件邮箱（JSONL 文件 + `FileMailboxLock` 互斥），缺点：
- 轮询读文件，延迟高（`MailboxPoller` 周期拉取）
- 无双工实时通道
- 文件 IO 开销
- 无法实现"tell 异步"语义（写文件 ≠ 投递到对端内存）

用户要求：每个子代理可以跨进程相互访问，放弃文件邮箱机制。

### 问题3：无主机发现/选举，多 jcc 进程无法协调

多个 jcc.exe 进程同时运行时，无主机选举机制：
- 无法确定谁是主机、谁是从机
- 主机掉线无故障转移
- 无全局编译队列协调，多进程并发编译导致 OOM

### 问题4：无全局编译队列，并发编译 OOM

多进程同时触发编译（如多个子代理并行工作），各自独立编译，内存峰值叠加导致 OOM。需要全局编译队列跨进程串行化。

## 决策

### 决策1：MailboxBase<TMessage> 继承 ActorBase

```
MailboxBase<TMessage> : ActorBase<MailboxCmd<TMessage>, MailboxEvt<TMessage>>
├── InProcessMailbox        — 进程内邮箱（内存 Channel，背压改造）
├── NamedPipeMailbox        — 有名管道邮箱（跨进程双工）
├── FileMailbox             — 文件邮箱（持久化，改造继承）
└── NetworkMailbox          — 网络邮箱（QQ/飞书，预留接口）
```

**统一能力**（继承自 ActorBase）：
- 双工：输入 Channel（命令）+ 输出 Channel（事件）
- 背压：有界输入通道 + `ActorBackpressure`（容量+满时策略+超时）
- 水位线：高水位线（容量*0.8）+ 危险水位线（容量*0.95），触发 `WatermarkReachedEvt`
- tell 异步：`SendAsync` 只入队不等待响应（fire-and-forget）
- 串行化：Consumer 线程独占处理命令，路由表无需锁

**邮箱命令**（`MailboxCmd<TMessage>`）：
- `SendCmd(agentId, message)` — 投递消息到指定 Agent
- `BroadcastCmd(message, excludeAgentId)` — 广播
- `RegisterCmd(agentId, sessionId)` — 注册 Agent 邮箱
- `UnregisterCmd(agentId)` — 注销

**邮箱事件**（`MailboxEvt<TMessage>`）：
- `WatermarkReachedEvt(agentId, level, count, capacity)` — 水位线告警，生产方限速
- `AgentRegisteredEvt(agentId)` / `AgentUnregisteredEvt(agentId)` — 生命周期

**路由表**：`ConcurrentDictionary<string, Channel<TMessage>>`，每个 Agent 独立有界 Channel + 水位线。接收方通过 `ReceiveAsync(agentId)` 直接读 Agent Channel。

### 决策2：传输层可插拔 ITransportTopology

```
ITransportTopology
├── StarTopology   — 星型（主机中心转发），优先实现
├── MeshTopology   — 网状（点对点直连），后续
└── BusTopology    — 总线（共享服务器），后续
```

跨进程邮箱（NamedPipe/File/Network）通过 `ITransportTopology` 抽象传输层，拓扑可插拔替换。先实现星型拓扑（用户描述的主机选举场景），验证方向后扩展网状和总线。

### 决策3：主机选举 — 句柄小者胜 + 完整上下文同步

**选举协议**：
1. 每个 jcc 进程启动后探测有名管道（固定管道名 `jcc-mailbox-host`）
2. 探测失败 → 注册自己为主机（创建 `NamedPipeServerStream`）
3. 探测成功 → 连接主机，注册为从机
4. 多主机冲突 → 比较进程句柄（`Environment.ProcessId`），句柄小者保留为主机，大者降级为从机
5. 主机掉线 → 从机检测心跳超时，根据完整上下文直接替代为新主机

**完整上下文**（主机定期同步给所有从机）：
- 路由表：`AgentId → ProcessId` 映射
- 未投递消息队列：每个 Agent 的待投递消息
- 全局编译队列状态：排队中/执行中的编译任务

**故障转移**：从机本地缓存上下文快照，主机心跳超时后，句柄最小的从机用快照重建主机服务，其他从机重连。

### 决策4：全局编译队列 — 跨进程串行防 OOM

基于 `ActorBackpressure.Build`（容量 100 + Wait + 60s 超时）：
- 所有进程的编译请求通过有名管道邮箱投递到主机
- 主机维护全局编译队列，串行执行（同一时刻只有一个编译在跑）
- 从机编译请求等待主机调度结果
- 防止多进程并发编译导致内存峰值叠加 OOM

## 替代方案

### 方案A：独立 MailboxBase 不继承 ActorBase（否决）

`MailboxBase` 内部用 `ConcurrentDictionary<string, Channel>` + `ActorBackpressure`，不继承 `ActorBase`。

**否决理由**：
- 重复实现背压/水位线/双工/串行化，违反 DRY
- 与 Actor 体系平行，不统一（用户明确要求"继承同一个基础类"）
- `ActorBase` 已提供全部能力，无理由重新实现

### 方案B：每种邮箱独立实现不继承基类（否决）

**否决理由**：违反 DRY，四种邮箱各自维护背压/水位线/双工逻辑，维护成本高，行为不一致。

### 方案C：用现有 IMailbox 接口 + 不同实现（否决）

**否决理由**：`IMailbox` 是接口不是基类，无背压/水位线/双工能力，且 `InProcessMailbox` 当前用 `CreateUnbounded` 无背压。接口无法强制子类实现背压。

### 方案D：MailboxBase 包含多个子 Actor（否决）

每个 Agent 一个独立 `ActorBase` 子 Actor，`MailboxBase` 是容器。

**否决理由**：每 Agent 一个 Consumer 线程，Agent 多时线程开销大；且路由表管理复杂。`MailboxBase` 继承 `ActorBase` 单 Consumer 串行处理命令更简洁。

## 后果

- **正面**：
  - 四种邮箱统一基类，复用 `ActorBase` 全部背压/水位线/双工能力
  - `InProcessMailbox` 获得背压+水位线，生产方可被限速
  - 有名管道邮箱实现跨进程双工实时通信，子代理可跨进程互访
  - 主机选举 + 故障转移，多 jcc 进程高可用协调
  - 全局编译队列跨进程串行，防 OOM
  - 传输层可插拔，后续扩展网状/总线拓扑不影响邮箱核心

- **负面**：
  - `MailboxBase` 增加一层抽象，简单场景略显重
  - 有名管道平台差异（Windows `NamedPipeServerStream` vs Linux `UnixDomainSocketEndPoint`），需抽象
  - 完整上下文同步增加网络开销和复杂度

- **中性**：
  - 三种拓扑分阶段实现，先星型（开心路径），网状/总线后续补充
  - 网络邮箱（QQ/飞书）仅预留接口，不在此 ADR 实现

## 实现路线（开心路径优先）

1. `MailboxBase<TMessage>` 抽象基类（继承 `ActorBase`）
2. 改造 `InProcessMailbox` 继承 `MailboxBase`（加背压+水位线）
3. `ITransportTopology` 传输层接口
4. 星型拓扑 `NamedPipeMailbox` + `HostElectionService`
5. 改造 `FileMailbox` 继承 `MailboxBase`
6. 预留 `NetworkMailbox` 接口
7. 全局编译队列 `GlobalBuildQueue`
8. 主机故障转移（完整上下文同步）
9. 单元测试 + E2E 测试

## 实现状态（2026-09-16 追加）

### 已落地

| 决策 | 实现提交 | 状态 |
|------|---------|------|
| 决策1：MailboxBase 继承 ActorBase | `7ea2e3633` | ✅ 四种邮箱均继承 MailboxBase |
| 决策2：ITransportTopology 可插拔 | `a8c8523ea` | ✅ 星型/网状/总线三种拓扑 |
| 决策3：主机选举 | `7ea2e3633` | ✅ HostElectionService 句柄小者胜 |
| 决策4：全局编译队列 | `7ea2e3633` | ✅ GlobalBuildQueue 跨进程串行 |

### 超出原计划（后续 ADR 收编）

| 实现 | 说明 | 收编 ADR |
|------|------|---------|
| NetworkMailbox + IPlatformBotAdapter | 原计划"仅预留接口"，后续完整实现 QQ/飞书适配器 | [0110](0110-platform-bot-adapter-pattern.md) |
| NamedPipeFactory 统一管道工厂 | 管道缓冲区 65536 只定义一次，全项目改用工厂 | 实现细节（commit `1aa38399a`），非架构决策 |
| TransportDiagnostics 一键诊断开关 | 所有 Transport 关键路径永久埋点 | 实现细节（commit `a84ac7629`），非架构决策 |
| 管道缓冲区为 0 导致写阻塞 bug | 5参数构造函数默认 inBufferSize=0 | bug 修复（commit `7e0c40f4e`），非架构决策 |
