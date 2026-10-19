# ADR 0142: native_tui 进程内嵌入 — jcc.exe 引用 NativeTui.Core 直连

- 状态：superseded by 0143（2026-10-20 同日用户澄清真实意图："引擎库化 + GUI/TUI/CLI 多宿主平等 + 协议层唯一数据源在引擎侧"——jcc.exe 当宿主塞 TUI 的方向被 [ADR 0143](0143-engine-library-multi-host-architecture.md) 取代。本 ADR 未实施零代码；其背景陈述（M7 协议税）与风险分析被 0143 继承）
- 日期：2026-10-20
- 关联：ADR 0140（native_tui 自研像素级对齐）、ADR 0141（bridge stdio 协议，降级为可选形态）、ADR 0143（取代本 ADR）、TASK055

## 背景与问题

M6 引擎接入（bridge 进程协议双端）完结后，M7 待办暴露出进程协议的"协议税"：

1. **每个交互特性都要协议化** — session_info（banner 数据源）、agent_progress（多子代理
   分组显示，300 规模）、权限协商（权限对话框需双向请求/确认）都必须设计 DTO、扩展
   事件表、双端同步实现
2. **高频事件走序列化** — 300 个子代理并行的 token_delta/工具事件全量走 stdio JSON
   行协议，吞吐与调试成本随规模上升
3. **单 exe 诉求** — 用户希望 TUI 直接长在 jcc.exe 上，而不是两个 exe 组合运行

ADR 0141 替代方案表中否决过"进程内嵌（引擎以库形式链接进 TUI）"，但那是 **TUI 做宿主、
引擎做库** 的方向（引擎进 TUI 进程）；本 ADR 决策的是**反方向：jcc.exe 做宿主、TUI 做
库**，依赖方向不同，不复用该否决结论。

## 决策

**jcc.exe 工程引用 NativeTui.Core，新增 `--tui` 启动入口，进程内直连**：

1. **依赖方向：jcc → native_tui**（单向）。NativeTui.Core 保持独立 slnx、零 modules
   依赖（ADR 0140 字面成立：native_tui 不依赖任何主工程工程，方向是主工程引用它）
2. **`--tui` 入口分流 Console**：jcc.exe 启动时该参数走 TUI 模式（独占 stdin/stdout，
   原生 ANSI 模式），否则走既有 CLI 链路；入口互斥防 Console 双占
3. **进程内直连适配层**：jcc 侧新增 TUI 适配器（类比 BridgeEventConsumer 的进程内
   版本）——引擎回合事件（IEventConsumer）直接喂 ReplView，不经 DTO 序列化；
   session_info/权限协商/agent_progress 以进程内事件直连，**零协议税**
4. **ADR 0141 bridge stdio 降级为可选部署形态**：`bridge_serve` 子命令与协议三件套
   保留，服务远程会话/headless 脚本/协议级调试/未来第三方客户端；不删除、不再作为
   主通路演进首选
5. **native_tui 测试与构建隔离不变**：428 个测试留在 native_tui 独立测试工程；
   jcc 引用的是编译产物（工程引用），不收编源码

## 替代方案（考虑后放弃）

| 方案 | 放弃原因 |
|------|----------|
| 维持 bridge 唯一通路 | M7 每特性协议税；300 子代理高频事件序列化开销；双 exe 组合运行 |
| TUI 代码搬进 modules/ | 428 测试迁移 + JCC 分析器全套约束（目录纯度等）砸到现有代码；改 TUI 一行编主工程链；推翻 ADR 0140 独立工程 |
| 引擎做库链接进 TUI（原 0141 否决项） | 引擎崩溃连累 TUI 宿主；引擎无法独立 CLI 运行；与既有 CLI 入口体系冲突 |

## 风险与缓解

| 风险 | 缓解 |
|------|------|
| 双 Directory.Build.props 共存冲突（nuget 源/分析器） | 实现首步做共存验证：jcc 引用 NativeTui.Core 后全量编译，冲突项在引用属性上显式关闭 |
| 进程隔离丧失（TUI 崩溃连累引擎） | TUI 适配层异常边界 + 栈帧断言（开心路径设计）；bridge 形态保留作逃生通道 |
| 主工程 AOT（PublishAot）约束 | NativeTui.Core 本就按 AOT 风格实现（无动态/反射 emit），Release 编译验证兜底 |
| 入口 Console 双占 | `--tui` 分流互斥：TUI 模式下禁用 CLI 输出路径（对齐 bridge_serve 的 stderr 重定向先例） |

## 实施范围

- M7 前置子任务（本 ADR 随附推进）：① 双 Build.props 共存验证 ② jcc 工程引用 +
  `--tui` 入口 + 进程内适配器（TDD）③ Demo 保留 bridge 形态回归验证
- M7 各特性（session_info/agent_progress/权限对话框）改为进程内事件直连实现

## 后果

- 正向：M7 特性零协议税；单 exe；300 子代理高频事件零序列化；ADR 0140 隔离投资保留
- 负向：进程隔离丧失（缓解见上）；jcc CI 需拉起 native_tui 编译（构建链延伸）；
  bridge 协议演进动力下降（降级为可选形态的天然代价）
