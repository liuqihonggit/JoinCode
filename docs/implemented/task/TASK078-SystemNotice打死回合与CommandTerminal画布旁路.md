# SystemNotice 打死回合 + CommandTerminal 画布旁路（TASK078）

> **状态**：implemented — 双缺陷修复 + E2E 全绿（2026-10-31）
> **来源**：用户验收 TASK077 时报告 "/?? 需求澄清命令执行中大量错位 + 需求澄清失败: Unknown event type: SystemNotice"
> **创建时间**：2026-10-31
> **关联**：TASK076（resize 锚定）、TASK077（画布抑制闸门 — 本任务是其两个漏网旁路的补全）；后续候选：TUI 原生需求澄清流程、IEventConsumer.OnSystemNotice、CPR 光标自校准

---

## 一、缺陷 1：SystemNotice 打死整个回合（协议缺口）

**现象**：`/?? 你好` → 引擎中间件发系统通知事件 → 回合失败 `需求澄清失败: Unknown event type: SystemNotice`。

**根因**：`ChatStreamEvent.Switch/Match`（abstractions `abs_ai/llm/chat/chat_core/ChatStreamEvent.cs`）
的 switch **没有 `ChatStreamEventType.SystemNotice` 分支** → 落 default 抛
`InvalidOperationException`。而 `LoopInterventionMiddleware.cs:73` 等中间件正常产出该事件
（枚举定义了、工厂方法有了、消费端没接 — 半成品协议）。

**修复**：Switch 增加 `Action<string>? onSystemNotice` 可选回调，未提供时**静默忽略**
（对齐 AgentStarted/SteerInput 既有先例 — 既有消费方 SessionController/AskClarifyCommand 零改动兼容）；
Match 增加 `Func<string, T>? onSystemNotice`，未提供时抛**引导性异常**（对齐 AgentStarted 模式 —
静默 default(T) 是引用类型空值脚枪）。TDD：4 契约测试（abs.tests/llm/chat/）红 → 绿。

## 二、缺陷 2：composition 命令画布旁路（三层漏网之第二层半）

**现象**：/?? 会话期间 `╔══ 需求澄清模式 ══╗` 盒子、`━━ AI 分析中 ━━` 横幅、流式正文**直接写上 TUI 画布**，
与 ReplShell 渲染帧双写互踩（用户 paste 整帧错乱）。

**根因**：composition 命令的 `TerminalHelper` 是 **global using 别名**
（composition `GlobalUsings.cs:152` → `JoinCode.Abstractions.Shell.CommandTerminal`），
TASK077 的闸门补丁打在 cli 层 TerminalHelper 上，**没打在这个真正的类上**。
CommandTerminal（abstractions abs_hands/shell/）的 WriteLine/WriteRaw/WriteLineReal 走
ICommandConsole 委托 + 捕获的 `_realOut`；AskClarifyCommand 还用
`Console.OpenStandardOutput()` **重开物理句柄** + `CommandTerminal.SetOut` 换装 —
闸门被整层绕过。

**修复**（abstractions 同层咽喉点 — 覆盖所有 composition 命令）：
- `CommandTerminal` 写方法（WriteLine/NewLine/WriteRaw×4/WriteLineReal/WriteRawReal/WriteError/WriteErrorRaw）
  加 `TryWriteThroughGate` 闸门改道 — 激活期写 `System.Console.Out`（= 闸门捕获 writer → 环形缓冲）；
- `CommandTerminal.SetOut` 闸门激活期**拒绝换装**（no-op）— 封死物理句柄重开旁路；
- 同型补丁补齐 cli 层 `TerminalHelper`（8 写方法 + SetOut 咽喉）— 防 cli 直调路径。
- TDD：CommandTerminal 5 契约测试（abs.tests/shell/）+ TerminalHelper 4 契约测试（host.tests/cli/）。

## 三、验收（ConPTY E2E，真实 jcc.exe + MockServer，/?? 场景）

| 断言 | 修复前 | 修复后 |
|---|---|---|
| `Unknown event type` | 回合死亡 | **无**（spinner 正常运行，回合存活） |
| `╔══ 需求澄清模式 ══╗` 盒子上画布 | 有 | **无**（拦进环形缓冲） |
| `━━ AI 分析中 ━━` / `[系统提示...]` 上画布 | 有 | **无** |
| 帧布局 | 盒子压分隔线 + banner 错乱双影 | banner/消息/输入框/footer 全对齐 |

回归：abs.tests **719/719** ✅；infra.tests/utils **599/600**（1 skipped）✅；host.tests **1494/1494** ✅；
NativeTui.Tests **2974/2974** ✅；跨域契约变更走 `Build-AllDomains.ps1` 全仓编译 ✅。

## 四、过程教训（写进记忆的坑）

1. **global using 别名骗过了补丁定位** — rg "class TerminalHelper" 只有一个类，
   但 composition 的 `TerminalHelper` 标识符经 GlobalUsings 别名指向另一个类。
   改"同名类"前必须查**所有消费方的标识符解析**（别名/using），不能只查类定义。
2. **跨仓引用的增量编译陷阱** — `dotnet build modules/apps/cli/JoinCode.csproj` 未重编
   native_tui 子树（自带 artifacts 目录），E2E 跑了陈旧 NativeTui.Host.dll 得出假阴性；
   验证二进制新鲜度要搜产物（注意 .NET 字符串字面量在元数据是 **UTF-16**，UTF-8 搜索恒 False）。
   跨域变更必须走 `Build-AllDomains.ps1`（AGENTS.md 规则 6 再次应验）。
3. **pyte 把 CJK 按 1 列解码** — 列级 VT 分析必须用 wcwidth 忠实解码器
   （`native_tui/tool/screenshot/resize_probe/vt_grid.py`，含 conhost pending-wrap/滚屏建模）。

## 五、后续候选（不阻塞）

- TUI 原生需求澄清流程（当前 /?? 在 TUI 内的直写 UI 全部进环形缓冲，会话结束随冲刷可见 — 功能语义后续 TUI 化）。
- `IEventConsumer.OnSystemNotice` 接口扩展 + TUI 系统通知消息项（通知当前静默丢弃）。
- CPR（CSI 6n）光标位回报自校准 — 对任何残余外部写的终极防御。
