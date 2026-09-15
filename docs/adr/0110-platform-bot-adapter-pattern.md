# 0110. 平台机器人适配器模式 — IPlatformBotAdapter 抽象 QQ/飞书/Discord

- 状态：accepted
- 日期：2026-09-16
- 决策者：用户 + AI
- 前置：ADR 0108（统一邮箱基类 MailboxBase — NetworkMailbox 原计划"仅预留接口"）

## 背景

### 问题

ADR 0108 决策4预留了 `NetworkMailbox` 接口但未实现。用户要求接入 QQ 频道机器人 API 和飞书开放平台 API，使子代理可通过外部消息平台通信。

直接在 `NetworkMailbox` 中硬编码 QQ/飞书 API 调用会导致：
1. **平台耦合** — 每加一个平台（Discord/Slack/企业微信）就要改 NetworkMailbox
2. **测试困难** — 单元测试需要 mock HTTP 调用，平台 API 散落在邮箱内部
3. **重复代码** — 各平台的认证/重试/错误处理逻辑重复
4. **违反开闭原则** — 扩展需修改已有类

### 现有基础设施

| 能力 | 现状 | 位置 |
|------|------|------|
| MailboxBase | 统一邮箱基类，HandleSendAsync/HandleBroadcastAsync 可重写 | `lib/async_lock/MailboxBase.cs:51` |
| NetworkMailbox | 预留壳，继承 MailboxBase 但无传输实现 | `llm/agents/Coordinator/Core/Messaging/NetworkMailbox.cs:10` |
| HttpClient | 已有 `HttpClient` 全局复用 | `System.Net.Http` |
| JSON 序列化 | `RelaxedJsonSerializer` 统一 | ADR 0042 |

## 决策

### 决策1：适配器模式 — IPlatformBotAdapter 抽象平台差异

```
IPlatformBotAdapter : IAsyncDisposable
├── QqBotAdapter      — QQ 频道机器人（POST + Bearer token）
├── FeishuBotAdapter  — 飞书开放平台（tenant_access_token + 消息卡片）
└── 未来: DiscordBotAdapter / SlackBotAdapter / WeworkBotAdapter
```

**接口职责**：
- `PlatformName` — 平台标识（"qq"/"feishu"/"discord"）
- `StartAsync(ct)` — 建立连接、认证、启动接收
- `SendAsync(targetId, text, ct)` — 发送到平台指定目标（频道/群/用户）
- `ReceiveAsync(ct)` — 从平台接收消息流（webhook/WebSocket 长轮询）

**PlatformMessage** record：`SourceId`（发送者）+ `TargetId`（目标）+ `Text`（内容）+ `Timestamp`（时间戳）

### 决策2：NetworkMailbox 委托适配器

`NetworkMailbox` 重写 `HandleSendAsync`/`HandleBroadcastAsync`：
- **发送**：本地投递（`DeliverToAgent`）+ 适配器远程投递（`_adapter.SendAsync`）
- **接收**：后台 `ReceiveLoopAsync` 从适配器读取 `PlatformMessage`，转换为 `CoordinatorMessage` 投递到本地 Agent Channel
- **广播**：遍历已注册 Agent，逐个本地投递 + 适配器远程投递
- **消息映射**：`targetIdSelector`/`textSelector` 委托从 `CoordinatorMessage` 提取平台目标 ID 和文本（默认用 `ToAgentId` 和 `Content`）

### 决策3：适配器通过构造函数注入

```csharp
public NetworkMailbox(IPlatformBotAdapter adapter, ...)
```

- 平台差异由适配器封装，`NetworkMailbox` 不感知具体平台
- 测试用 `MockPlatformBotAdapter`（已实现 6 个单元测试）
- 生产用 `QqBotAdapter` / `FeishuBotAdapter`，通过 DI 注入

## 替代方案

### 方案A：策略接口 + 逐平台实现类（否决）

为每个平台定义独立接口（`IQqBot`/`IFeishuBot`），`NetworkMailbox` 持有多个接口。

**否决理由**：
- 接口爆炸 — N 个平台 = N 个接口
- `NetworkMailbox` 需感知所有平台接口，违反开闭原则
- 无统一 `SendAsync`/`ReceiveAsync` 签名，调用方需 switch 分发

### 方案B：配置驱动 + 反射加载（否决）

`NetworkMailbox` 读配置文件选择平台，反射创建适配器实例。

**否决理由**：
- 反射不兼容 NativeAOT（ADR 约束：强制 AOT）
- 运行时错误，无编译期类型安全
- 违反"约定大于配置"原则

### 方案C：NetworkMailbox 直接硬编码 QQ/飞书（否决）

在 `HandleSendAsync` 中 `switch (platform)` 逐平台硬编码 HTTP 调用。

**否决理由**：
- 违反开闭原则，每加平台改 NetworkMailbox
- 平台逻辑与邮箱逻辑耦合，测试困难
- 重复代码（认证/重试/错误处理）

## 后果

- **正面**：
  - 新增平台只需实现 `IPlatformBotAdapter`，不改 `NetworkMailbox`（开闭原则）
  - 平台差异隔离在适配器内，`NetworkMailbox` 只管邮箱语义
  - 测试用 mock 适配器，无需真实 HTTP
  - 适配器可独立测试（QQ API 认证/飞书 token 刷新）

- **负面**：
  - 多一层适配器抽象，简单场景略显重
  - `PlatformMessage` 是通用 record，平台特有字段（如飞书消息卡片、QQ @提及）需通过 `Text` 编码或扩展 record

- **中性**：
  - 适配器启动/认证/重连逻辑由各自实现，`NetworkMailbox` 不管理适配器生命周期（仅 `DisposeAsync` 释放）

## 验证标准

1. `QqBotAdapter.SendAsync` → 调用 QQ Bot API（POST + Bearer token）
2. `FeishuBotAdapter.SendAsync` → 调用飞书开放平台 API（tenant_access_token）
3. `NetworkMailbox.TellAsync` → 本地投递 + 适配器远程投递
4. 适配器接收消息 → `ReceiveLoopAsync` 转换为 `CoordinatorMessage` 投递本地
5. `MockPlatformBotAdapter` 单元测试 6 个全通过
