# GUI 重构：多 subAgent 运行期显示设计

> 状态：待用户确认架构决策
> 日期：2026-08-25
> 范围：`app/JoinCodeGui`（Avalonia GUI）全量重构 + 引擎事件链路补通

---

## 一、现状调研结论

### 1.1 链路断裂点（核心问题）

subAgent 事件从引擎到 GUI 的链路**完全断裂**：

| 层 | 现状 | 证据 |
|----|------|------|
| 引擎执行层 | `AgentStreamExecutionMiddleware` 收到子代理流式 chunk（Content/ToolCallStart/Complete/Error）后**只写 LogDebug，不转发 UI** | `core/execution/Hands/src/ToolHandlers/Handlers/SystemTools/Agent/AgentStreamExecutionMiddleware.cs:59-62` |
| 协议层 | `ChatStreamEvent` 无 agent 身份字段（无 AgentId/Role/Description），只有 ToolProgress 的纯文本 | `foundation/Abstractions/01-ai/LLM/Chat/Core/ChatStreamEvent.cs:16-56` |
| GUI 消息模型 | `ChatUiMessageKind` 仅 Text/Thinking/ToolCall/ToolResult 四值，扁平无层级 | `app/JoinCodeGui/ViewModels/ChatUiMessageKind.cs:6-12` |
| GUI 事件消费 | `ToolProgress` 只覆盖 `currentToolCall.Content`（单行文本），无聚合、无分组 | `app/JoinCodeGui/ViewModels/MainViewModel.cs:1412-1417` |

### 1.2 可复用资产

| 资产 | 位置 | 说明 |
|------|------|------|
| `AgentStreamChunk` 流 | `IAgentService.RunAgentStreamAsync` | 已携带 AgentId/Type/ToolName/Content/ExecutionTimeMs — 数据源现成 |
| `AgentStreamExecutionMiddleware.Order=400` | Hands/Agent | 转发事件的天然挂载点 |
| 旧 TUI `SubAgentCardManager` | `app/JoinCodeTui/Tui/Rendering/SubAgentCardManager.cs` | 展开/折叠状态机（最多同时展开 3 个，LRU 驱逐），逻辑可直接移植 |
| `AgentStateReport`/`AgentStateInfo` | `foundation/Abstractions/00-core/Models/Agent/AgentStateReport.cs` | 状态上报模型（Pending/Running/Paused/Completed/Failed/Cancelled + Progress）已存在 |
| `IAgentOutputChannelManager` | Abstractions.Interfaces | 已注入 AgentStreamExecutionMiddleware 构造，疑似为输出分通道预留 |

### 1.3 GUI 现存痛点

1. `MainViewModel.cs` **1785 行**巨石类：会话管理 + 消息组装 + 设置 + 主题 + 权限弹窗回调全部耦合
2. 事件循环内联在 `SendMessageCoreAsync` 中（1340-1468 行），switch-case 直接操作消息集合，无法测试
3. 无虚拟化：`Messages` ObservableCollection 全量渲染，长对话性能差
4. 工具调用卡片无折叠/展开态区分（ClaudeCode 有 queued/running/success/error 五态视觉）
5. 无全局运行状态条（spinner/耗时/token 聚合）

### 1.4 ClaudeCode TUI 参考结论（调研自 claude-code-rev-main）

可迁移到 Avalonia 的核心模式：

| ClaudeCode 模式 | GUI 迁移形态 |
|----------------|-------------|
| 每 agent 一行进度（类型徽章+工具数+tokens+当前活动） | AgentRunPanel 树形列表，每 agent 固定 2 行卡片 |
| 运行区只显示尾部 3 条活动 + "+N more" 折叠计数 | 运行中 agent 卡片内固定高度活动列表 |
| 连续 search/read 合并为一条摘要（现在时/过去时动词） | 活动去重管道（同类连续工具聚合计数） |
| 多 agent 并行 → 树形 ├─/└─ 列表 + 共享加载点 | ItemsControl + 缩进装饰线 |
| 完成态定格 "Done (N tool uses · X tokens · 2m12s)" | 完成卡片统计三元组 |
| transcript 双态（ctrl+o 切换全量回放） | agent 卡片点击展开完整子代理日志页 |
| 后台化收起为一行 pill + 全局树形管理器 | 后台 agent 收起为一行；顶栏 "N 个后台代理" 入口 |
| 卡死检测 >3s 无心跳渐变红 | DispatcherTimer 心跳检查 + 颜色插值 |
| 热路径隔离（动画钟只在最小子组件） | spinner 动画独立小控件；agent 列表走普通数据绑定 |

---

## 二、目标架构

```
引擎 (core)                      协议 (foundation)              GUI (app)
─────────────                    ─────────────────              ─────────────────
AgentStreamExecution             ChatStreamEvent                MainViewModel (瘦身)
Middleware ──转发──▶             + AgentStarted                  └─ ChatTurnProcessor ◀─ 新增：事件→VM 组装器（可单测）
                                 + AgentProgress                     └─ SubAgentRunTracker ◀─ 新增：agent 运行态聚合（可单测）
                                 + AgentFinished                          └─ AgentRunVm 集合
                                                                     └─ Views:
                                                                        AgentRunPanelView   ◀─ 新增：多 agent 树形面板
                                                                        AgentActivityView   ◀─ 新增：尾部3条活动+折叠
                                                                        GlobalStatusBar     ◀─ 新增：spinner+聚合统计
                                                                        TranscriptWindow    ◀─ 新增：agent 完整回放
```

### 2.1 协议层扩展（ChatStreamEvent）

新增三个事件类型（枚举追加，带 `[EnumValue]`，需全量重建生成器链）：

```csharp
public enum ChatStreamEventType
{
    // ...现有 9 值不动...
    [EnumValue("agentStarted")]  AgentStarted,
    [EnumValue("agentProgress")] AgentProgress,
    [EnumValue("agentFinished")] AgentFinished,
}

// ChatStreamEvent 新增字段
public string? AgentId { get; init; }          // 子代理唯一 ID
public string? AgentName { get; init; }        // 显示名（如 "explore"）
public string? AgentDescription { get; init; } // 任务描述
public string? AgentRole { get; init; }        // 角色（researcher/coder/...）
public bool? AgentSuccess { get; init; }       // 仅 AgentFinished 携带
public long? AgentExecutionTimeMs { get; init; }
public int? AgentToolUseCount { get; init; }
public long? AgentTokenCount { get; init; }
```

### 2.2 引擎层改造（唯一改动点）

`AgentStreamExecutionMiddleware.InvokeAsync` 的 switch 中：

- `AgentStreamChunkType.ToolCallStart` → 发 `AgentProgress`（携带 AgentId+ToolName）
- `Content` chunk → 节流发 `AgentProgress`（活动摘要文本）
- `Complete/Error` → 发 `AgentFinished`

转发通道二选一（待确认，见决策 D1）：挂到 `context` 让 AgentToolHandlers 以 `ChatStreamEvent.AgentProgress` 形式汇入主 `StreamWithEventsAsync` 流。

### 2.3 GUI 层新组件

| 组件 | 职责 |
|------|------|
| `SubAgentRunTracker`（纯 C#，可单测） | 按 AgentId 聚合运行态：状态机 Monitoring→Running→Done/Failed；工具计数；尾部 N 条活动环形缓冲；连续同类工具合并摘要 |
| `AgentRunVm` | 绑定模型：StateColor/RoleBadge/CurrentActivity/ToolUseCount/Tokens/Elapsed/IsExpanded |
| `AgentRunPanelView` | 对话流内的多 agent 卡片组（ItemsControl+缩进线），完成态定格统计 |
| `GlobalStatusBar` | 底部状态条：随机动词 spinner + 总耗时 + 聚合 token + "N 个后台代理"入口 |
| `TranscriptWindow` | 双击 agent 卡片弹出完整子代理回放窗口 |
| `ChatTurnProcessor` | 从 MainViewModel 抽出的事件→VM 组装器（消除巨石类，事件循环可单测） |

---

## 三、待确认架构决策

### D1：引擎→GUI 事件通道

- **方案 A（推荐）**：扩展 `ChatStreamEvent` 加 Agent* 三事件，middleware 经 context 上浮汇入主流。优点：GUI 单一消费入口、协议显式、与 ToolProgress 同构；缺点：动 foundation 枚举需七层全量重建一次。
- 方案 B：复用 `ToolProgress`，把 agent 信息序列化进 ProgressMessage 字符串。优点零协议改动；缺点：字符串协议脆弱，违反"配置大于代码"精神。
- 方案 C：GUI 直接订阅 `IAgentOutputChannelManager` 独立通道。优点不动协议；缺点 GUI 出现第二个事件源，与 StreamAsync 生命周期对齐复杂。

### D2：消息模型层级表达

- **方案 A（推荐）**：`ChatUiMessage` 增加 `Kind=AgentRunGroup` + 持有 `List<AgentRunVm>` 子列表（组合而非继承）。优点：对话流顺序天然保持（agent 组卡在触发它的 ToolCall 之后）；缺点：ChatUiMessage 变复合。
- 方案 B：完全独立的顶层 `ObservableCollection<AgentRunVm>` + 对话流外置面板（右栏）。优点消息流零改动；缺点 agent 与触发上下文空间分离，违背 ClaudeCode 内联模式。

### D3：重构节奏

- **方案 A（推荐）**：两阶段——先打通链路+最小显示（tracker+panel），编译提交后再做 MainViewModel 瘦身拆分（ChatTurnProcessor 抽取）。符合渐进式铁律。
- 方案 B：一次性全量重构（链路+组件+拆分同批）。风险高，违反渐进式迁移策略。

---

## 四、任务分解（D3=方案A 时）

| # | 任务 | TDD 锚点 |
|---|------|---------|
| 1 | ChatStreamEvent 加 Agent* 事件类型+字段（红：事件工厂单测） | Unit |
| 2 | AgentStreamExecutionMiddleware 转发改造（红：middleware 单测断言上浮事件序列） | Unit |
| 3 | SubAgentRunTracker（红：状态机/折叠合并/LRU 驱逐单测，移植旧 TUI CardManager 测试思路） | Unit |
| 4 | AgentRunVm + AgentRunPanelView + DataTemplate（红：VM 单测；绿：冒烟启动） | Unit+冒烟 |
| 5 | GlobalStatusBar + 心跳变红（红：心跳计时单测） | Unit |
| 6 | TranscriptWindow 回放（红：transcript 组装单测） | Unit |
| 7 | MainViewModel 瘦身 → ChatTurnProcessor 抽取（行为等价重构，现有 MainViewModelTests 保护） | refactor |
| 8 | E2E：MockServer 多 agent 场景冒烟（jcc.exe --await） | E2E |

每步：🔴红 → 实现 → 编译 → 🟢绿 → commit。

## 决策记录

<!-- 🤖 Auto Decision: 2026-08-25 -->
<!-- 决策: 用户确认可视化四形态（内嵌运行卡片/并行树形列表/完成定格/全局状态条）按设计实现 -->
<!-- 原因: 对齐 ClaudeCode 内联模式，agent 状态紧贴触发上下文 -->
<!-- 替代方案: 右栏独立面板（违背内联参考模式，未采用）-->
<!-- 验证: D1/D2/D3 按推荐方案 A 执行 -->

<!-- 🤖 Auto Decision: 2026-08-26 (T1, commit 1d3e98167) -->
<!-- 决策: 子代理中间活动复用现有事件类型 + AgentId 路由键，仅新增 AgentStarted/AgentFinished 两个枚举值 -->
<!-- 原因: 对齐 TS onProgress 附着 toolUseID 模式；枚举增量最小；Switch 可选回调保证 AskClarifyCommand/SessionController 零改动兼容 -->
<!-- 替代方案: 三个全新事件类型（活动事件语义与现有类型重复，未采用）-->

<!-- 🤖 Auto Decision: 2026-08-26 (T2, commit 3a7621154) -->
<!-- 决策: SubAgentEventChannel 排空侧经 ChatMiddlewareContext.SubAgentEvents 显式传递，发射侧由 ToolExecutionHandler 进入 AsyncLocal 作用域 -->
<!-- 原因: 实测 AsyncLocal 在异步迭代器段内 Set 后跨 yield 不可见（AsyncLocalInIteratorTests 固化该平台行为），QueryLoop 是迭代器禁走环境态；ToolExecutionHandler 是普通异步方法可可靠传播 -->
<!-- 替代方案: 全链路显式参数传递（需改 orchestrator/gateway/executor 四层签名，侵入过大）-->
<!-- 验证: QueryLoop 合流测试绿：子代理事件出现在主 ToolStart 与 ToolEnd 之间；嵌套作用域隔离测试绿 -->

<!-- 🤖 Auto Decision: 2026-08-26 (T3+T4) -->
<!-- 决策: 连续搜索/读取从第 2 次起折叠为"搜索/读取 N 次…"摘要；展开上限 LRU=3 移植旧 TUI SubAgentCardManager -->
<!-- 原因: 单次保留工具名可读性；LRU 防止多 agent 同时展开刷屏 -->
<!-- 验证: Tracker 9 测试 + GUI 全量 364 测试全绿 -->
