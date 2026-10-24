# ADR 0143: 引擎库化多宿主架构 — JoinCode.Engine 抽取 + native_tui 宿主

- 状态：superseded by 0149
- 日期：2026-10-20
- 实施完成：2026-10-20（阶段一 PR #431 合并 main=680f725cb + 收尾 PR #432；阶段二同日四批次落地，见下文阶段二实施结果）
- 关联：ADR 0140（native_tui 自研像素级对齐）、ADR 0141（bridge stdio 协议，降级为可选形态）、ADR 0142（superseded by 本 ADR，jcc 宿主内嵌方向）、TASK055

## 背景与问题

M6 引擎接入完结后，M7 待办暴露进程协议的"协议税"（详见 ADR 0142 背景陈述）：
session_info / agent_progress（300 子代理分组显示）/ 权限协商每特性都要 DTO 协议化；
高频 token_delta 走 stdio JSON 序列化。

用户对架构方向做出关键澄清（2026-10-20 原话要点）：

> jcc.exe 是不是可以作为库？然后 GUI/TUI/这些分别引用和使用……这样按理说可以
> 共享协议层，并且写到 jcc.exe 上面做协议。

即：**引擎能力库化，GUI/TUI/CLI 是平等的多个宿主引用引擎库，协议层（事件模型）
唯一数据源写在引擎侧**。依赖方向：各宿主 → 引擎库。

这与 ADR 0142 的"jcc.exe 当宿主塞 TUI（`--tui` 入口）"是两个不同架构——用户澄清后
0142 被本 ADR 取代。

### 现状调查（2026-10-20）

- 引擎当库的**事实先例已存在**：`modules/apps/gui/JoinCodeGui.csproj` 与
  `modules/apps/tui/JoinCodeTui.csproj` 均 ProjectReference 引用
  `modules/apps/cli/JoinCode.csproj`（jcc Exe，.NET Exe 工程可直接被引用）；
  gui 的 Hosting 层（JccChatSession）走 `JoinCode.App.Builder`（EngineSessionFactory）
  + Abstractions 门面组装
- **问题**：引擎能力与 CLI 宿主细节耦合在同一个 Exe 工程里——native_tui 若直接
  引用 jcc Exe，会把 CLI 命令路由、控制台输出等宿主细节一并拖进来，无法"平等复用
  干净边界"

## 决策

**两阶段：先抽取引擎库，再做 native_tui 宿主**（用户确认的执行顺序）。

### 阶段一：抽取引擎库

`modules/apps/cli/JoinCode.csproj` 拆分为：

**新库工程**（建议 `modules/apps/engine/JoinCode.Engine.csproj`）——引擎能力：

| 目录 | 内容 | 归库依据 |
|------|------|----------|
| `app/builder/` | EngineSessionFactory（LoadConfig + BuildHost + ConfigureModules） | gui 已直接消费；宿主无关的组装工厂 |
| `core/services/` | CliSession（CreateMainAgent：prompt injection/审计/上下文持久化完整链路） | 主代理构造是引擎核心，宿主无关 |
| `adapters/SessionController.cs` 等 | 回合驱动 + SessionTurnResult + AgentStreamChunkAdapter | 回合生命周期是引擎能力 |
| `core/bridge/` | 协议三件套（BridgeProtocol / BridgeEventConsumer / BridgeServeLoop） | **协议层在引擎侧**的关键载体（见下） |

**CLI 宿主薄壳**（留在 JoinCode.csproj，jcc.exe）：

| 目录 | 内容 | 留宿主依据 |
|------|------|-----------|
| `entry/` | startup / runners | 进程入口是宿主形态 |
| `core/commands/` | FlatSubCommandRouter 路由 + 各命令（含 BridgeServeCommand——命令是 CLI 宿主形态，消费引擎库 BridgeServeLoop） | 命令体系是 CLI 专属 |
| `core/display` `output` `interaction` `vim` `trust` `onboarding` | 控制台呈现/交互 | CLI 专属体验 |
| `adapters/` 的 CliEventConsumer、ConsolePresentationAdapter、PresentationAdapterFactory、CliPermissionConfirmationHandler | CLI 控制台消费形态 | 控制台是宿主 IO |
| `pipe/` `queue/` | CLI 管道与队列 | CLI 专属 |

**命名清理**：CliSession 等 `Cli` 前缀与多宿主定位不符，随抽取重命名（如
CliSession → EngineSession，实施时定名）；`CreateMainAgent` 的 internal static
复用先例保持（跨工程可见性经 InternalsVisibleTo 调整）。

### 阶段二：native_tui 宿主

- **位置**：`native_tui/apps/NativeTui.Host`（对齐既有 `apps/NativeTui.Demo` 先例）
- **引用**：ProjectReference 跨 slnx 引擎库 + `native_tui/src/NativeTui.Core`
- **组装**：类比 gui 的 Hosting 层（JccChatSession）——**进程内直连**：
  IEventConsumer 事件直接喂 ReplView，不经 DTO 序列化
- **ADR 0140 完整成立**：NativeTui.Core 保持零依赖（组装全在宿主层，Core 不引引擎）

### 协议层唯一数据源（核心架构不变式）

```
引擎侧（唯一数据源）
  IEventConsumer 事件模型  ←—— 协议本体（进程内形态，GUI/TUI 宿主消费）
  BridgeProtocol DTO       ←—— JSON 投影（跨进程形态，bridge_serve/远程/调试消费）
```

- 两种形态**同源**：bridge DTO 是 IEventConsumer 事件的序列化投影，演进必须先改
  事件模型再同步投影（禁止投影先行）
- ADR 0141 bridge stdio 降级为可选部署形态：`bridge_serve` 与协议三件套保留不删，
  服务远程会话/headless 脚本/协议级调试；不再作为主通路演进首选

## 替代方案（考虑后放弃）

| 方案 | 放弃原因 |
|------|----------|
| ADR 0142 方案 C（jcc.exe 当宿主塞 `--tui`） | 用户意图是多宿主平等共享协议层，非单宿主内嵌；jcc 当宿主时 GUI 无法复用同一接入模式 |
| 宿主直接引用 jcc Exe（gui 现状模式一步到位） | 引擎与 CLI 宿主耦合，native_tui 引用会拖进 CLI 命令路由/控制台输出等宿主细节 |
| 维持 bridge 唯一通路 | M7 每特性协议税；300 子代理高频事件序列化开销 |
| TUI 代码搬进 modules/ | 428 测试迁移 + JCC 分析器全套约束砸到现有代码；推翻 ADR 0140 独立工程 |

## 风险与缓解

| 风险 | 缓解 |
|------|------|
| 工程拆分结构手术量大 | 渐进式迁移：先建空库工程接好引用 → 逐目录搬移 → 每步编译+测试+commit；禁止一次性大规模重构 |
| gui/tui 宿主迁移回归 | gui 引用面窄（App.Builder + Abstractions 门面，已查清），切换后全量 gui 测试验证 |
| 测试归属 | host.tests 中测引擎组件的用例随代码迁到引擎库就近测试工程（ADR 0137 就近隔离原则） |
| AOT | 库工程沿用 IsAotCompatible；jcc 薄壳与宿主 Release AOT 编译验证兜底 |
| bridge_serve 命令消费链断裂 | BridgeServeCommand 留 CLI 宿主消费引擎库 BridgeServeLoop，InternalsVisibleTo 同步调整，跨进程冒烟回归 |

## 实施范围

1. **阶段一（先行）**：边界清单落地 → 建库工程 → 逐目录搬移（每步编译+测试+commit）
   → gui/tui 宿主切引引擎库 → 全量回归（host.tests + gui 测试 + bridge 冒烟）
2. **阶段二**：NativeTui.Host 宿主工程（TDD：进程内直连适配器）→ Demo 保留 bridge
   形态回归验证
3. **M7 特性改道**：session_info / agent_progress / 权限对话框改为进程内事件直连
   实现（不再扩展 ADR 0141 事件表）

## 后果

- 正向：协议层唯一数据源（事件模型本体 + DTO 投影同源）；GUI/TUI/CLI 三宿主平等；
  M7 特性零协议税；300 子代理高频事件零序列化；ADR 0140 完整成立；完全对齐 gui 先例
- 负向：工程结构手术（渐进式缓解）；bridge 协议演进动力下降（0141 降级的天然代价）；
  CI 编译链多一个工程；`Cli` 前缀重命名触及面广（无后向兼容包袱，按 AGENTS.md
  大修大改原则一次到位）

## 实施结果（2026-10-20，阶段一完结）

阶段一全程渐进式落地（每步编译+测试+commit，PR #431 squash 合并 main）：

| 批次 | 内容 |
|------|------|
| Step 0 | ADR 0143 制定（取代 0142） |
| Step 1 | 建 JoinCode.Engine 库工程，协议与会话核心六件 git mv（命名空间不变，跨程序集同命名空间） |
| Step 2 | EngineAgentFactory 主代理工厂提取（CliSession 留宿主，仅提取 CreateMainAgent） |
| Step 3a | 契约与构建器引擎化（EngineApplicationBuilder/EngineConfigInitializer 拆分） |
| Step 3b | 五模块 + EngineSessionFactory 搬库 + CoreModule 拆分（CliRegistrationModule 兜底 49 类型） |
| Step 3c | 24 个引擎侧 [Register] 资产搬移（17 telemetry hooks + TrustFolderManager + user_experience 5 件）；生成器新增 AssemblyMetadata `AutoRegisterScope=CurrentAssembly` 开关（解决聚合库扫引用闭包 CS0122 + 重复注册） |
| Step 4 | tui 宿主整体移除（Terminal.Gui 旧 TUI 归档，native_tui 自研接任）；gui 切直引 engine；CommandQueue 搬回 cli、SlashCommandRunner + Prefix 族留守 engine |
| 收尾 | CliServiceContext→EngineServiceContext 改名（engine 内 Cli 前缀清零） |

### 实施期决策修订（与原决策的差异）

1. **CliSession 未改名**——分类调查后 CliSession 是 CLI 宿主表现层（TerminalHelper 终端
   交互重度耦合），留 cli 不搬，无需 EngineSession 改名；进 engine 的服务上下文已改名
   EngineServiceContext
2. **启动管道留 cli**——StartupContext + 12+ 启动步骤全部 HOST（管道组装只在 cli 两个
   ModeRunner；gui 走 CreateGuiSessionAsync、native_tui 阶段二走 bridge_serve，
   均不经启动管道），原"SystemPromptApplyStep/NonInteractiveExitCleanupStep 属 ENGINE
   待 StartupContext 拆分后随迁"判定撤销——引擎能力已在 IChatService/IStopHookManager
   接口上，步骤只是 CLI 参数翻译层
3. **pipe 协议族留 cli**——命名管道是 CLI 宿主专属设施（CreateGuiSessionAsync 显式
   clearPipeEndpoint；native_tui 走 stdin/stdout bridge 协议），边界判据=是否存在
   跨宿主消费而非"依赖干净"
4. **Dock submodule 恢复**——三 submodule 移除后用户撤销 GUI 侧部分：GUI 保留原样
   （新 GUI 在其他分支开发），Dock 恢复；Terminal.Gui/Editor 保持移除（tui 已删）

## 阶段二实施结果（2026-10-20，native_tui 宿主落地）

四个渐进批次落地（每批编译+测试+commit），`native_tui/apps/NativeTui.Host` 进程内直连引擎：

| 批次 | commit | 内容 |
|------|--------|------|
| 1 | 079065e23 | Host 工程（csproj 跨 slnx 直引 engine）+ InProcessEventConsumer（IEventConsumer 第四实现：语义对齐 engine BridgeEventConsumer，WriteEvent 序列化 stdout → ConcurrentQueue 内存 record）+ 9 测试 |
| 2 | 9649bb7d5 | EngineTurnClient（BridgeServeLoop.DispatchRequest 进程内版：Submit 忙时拒绝/Interrupt 取消/终态翻译；共享队列 FIFO 保序）+ consumer 注入重构 + 7 测试 |
| 3 | c096a0e11 | EngineSession 组装（照抄 BridgeServeCommand 样板：CreateGuiSessionAsync + 延迟绑定 SessionController + PermissionChecker 安全闸 + session_info 种子事件）+ HostSession 交互主循环（inline 渲染独占）+ --selftest 真实冒烟 + 5 测试 |
| 4 | 本提交 | native_tui.slnx 注册 Host+Host.Tests + ADR/TASK055 文档收口 |

### 阶段二关键决策

1. **组装照抄 BridgeServeCommand**——CreateGuiSessionAsync 组装入口（gui 先例），
   SessionController 首次 submit 延迟绑定（consumer 即事件出口），权限应用器经
   PermissionChecker（CurrentMode setter 含 bypass 安全闸），session_info 种子事件经
   client 内部 Enqueue 入队（事件流统一走 DrainEvents 消费面）
2. **回合 busy 全事件驱动**——不做乐观预挂 BeginTurn（进程内 TurnStarted 零延迟）；
   忙时提交由 EngineTurnClient 拒绝（Error 诱导 Esc 打断），避免预挂后无 TurnStarted
   事件导致 busy 卡死
3. **HostSession 只取 DemoSession bridge 模式**——inline 渲染独占（无视口模式、无模拟
   回合、无引擎退出检测）；键处理/渲染循环与 DemoSession 结构相同（约 270 行形似），
   ReplShell 提取列为 M7 前置任务（权限对话框需共享外壳，见 TASK055）
4. **引擎侧 PermissionMode 与 Core 同名消歧**——GlobalUsings 别名
   （EnginePermissionMode/EnginePermissionModeExtensions 等 4 别名），两侧枚举值一致
   经字符串互转（协议值 plan/auto/ask/bypass/unattended 唯一约定）
5. **Console.Out 重定向 stderr**——对齐 bridge 先例：TUI 渲染走重定向前捕获的独立
   writer，引擎内部 Console 输出（日志默认 console provider）不污染渲染流

### 验证

- 单元：Host.Tests 21/21（consumer 9 + client 7 + 终态翻译 5）
- selftest（ADR 0080 真实组装非 mock）：引擎进程内组装 + session_info 真实数据 +
  PermissionChecker 真实回执（GUI 模块链 IPermissionChecker 注册实测确认）+ 无效模式
  Error 诱导，退出码 0；负路径（stdout 重定向检测/未知参数诱导）退出码 1
- 回归：Core NativeTui.Tests 458/458 + Demo selftest 通过（Demo 未触碰）
- 交互模式手动验收（ADR 0080）：待用户真实终端运行（Enter 提交/Esc 打断/Shift+Tab
  权限循环），验收记录见 TASK055

### 遗留（阶段二收口后更新）

- ~~阶段二 native_tui 宿主~~ 已完成（上文）
- M7 特性改道：session_info 已进程内直连；agent_progress / 权限对话框待 M7 走进程内
  事件（不再扩展 ADR 0141 事件表）；权限挂起首版按打断处理
  （PermissionPendingConfirmationException 翻译 OCE + 诱导文案）
- ~~ReplShell 提取~~ 已完成（commit a92bc1a16）：IReplEngine 接口
  （Submit/Interrupt/SetPermissionMode/DrainEvents/DrainDiagnostics/IsAlive）统一引擎
  交互面 — HostSession/DemoSession 删除归档 `.xxx/`，Host 直连 EngineTurnClient、
  Demo 走 SimulatedEngine（时钟注入+忙时拒绝 Error 诱导）与 BridgeEngine（子进程适配），
  模拟回合改事件驱动（spinner Message 直改弃用 — 真实路径本就空消息+随机动词）、
  权限本地回执统一走事件；Core 新增 22 单元测试（480/480），净删约 920 行重复代码。
  M7 权限对话框可直接在 ReplShell 单点扩展
