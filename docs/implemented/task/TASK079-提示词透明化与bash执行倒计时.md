# 提示词透明化 + bash 执行倒计时 + 端点异常结案（TASK079）

> **状态**：implemented — 三需求落地 + E2E/单元全绿（2026-10-31）
> **来源**：用户四项指令（① TUI 可展开查看注入内容 ② bash 执行倒计时 ③ 端点覆盖异常调查 ④ 真实测试）
> **关联**：TASK077（画布闸门）、TASK078（SystemNotice + CommandTerminal 旁路）

---

## 一、Req1：提示词透明化（/prompt）

| 基建实现 | 消费点 | 已实现 | 已验收 |
|---------|--------|--------|--------|
| `PromptTransparencyEngine` 装饰器（NativeTui.Core/repl/）：/prompt 拦截 → 宿主注入的快照 dump 进 transcript（AssistantMessageItem，ctrl+o 回看） | Host 引擎栈最外层（transparencyEngine → clarifyEngine → pickerEngine → client） | ✅ | ✅ 3 行为测试 |
| 快照数据源 = 引擎会话服务真实对象：`ISystemPromptProvider.GetSections()`（首次注入的系统提示词全文，逐节渲染）+ `IMcpToolRegistry.GetAllToolsAsync()`（全部工具按类目分组，名称/描述/Kind） | Host 惰性缓存（Lazy — 首次 /prompt 构建后复用） | ✅ | ✅ 编译 + 全仓 |
| `ReplShell.AddTranscriptNote(string)` — Submit 主线程落版通道 | 装饰器回调 | ✅ | ✅ |

**设计取舍**：dump-to-transcript（基准 `[` dump-to-scrollback 同语义族）而非新弹窗组件 — 零新 UI、ctrl+o 可回看、可复制。

## 二、Req2：bash/powershell 执行倒计时

| 基建实现 | 消费点 | 已实现 | 已验收 |
|---------|--------|--------|--------|
| `SpinnerItem.BeginToolCountdown/EndToolCountdown` + 状态 part `bash 剩余 Ns`（WallNowMs 时基逐帧递减，0s 钳制） | spinner 状态行（suffix 之后、timer 之前 — 用户第一眼位置） | ✅ | ✅ 4 渲染测试 |
| `ReplBridgeDriver`：ctor `shellTimeoutSeconds`（Host 解析 `ShellExecutionConfig.AbsoluteTimeoutSeconds`，默认 120）+ ToolCall（bash/powershell 协议字面量）启动、ToolResult/TurnInterrupted/EndTurn 清除 | ApplyToolCall/ApplyToolResult/EndTurn | ✅ | ✅ 3 生命周期测试 |
| Host：`session.Services.GetService<ShellExecutionConfig>()` 解析超时注入 | ReplShell 构造透传 | ✅ | ✅ 全仓编译 |

## 三、Req3：JCC_ENDPOINT"覆盖失效"结案（非缺陷）

调查结论：**端点覆盖本身正常**（jcc 诊断输出确认解析到 localhost:9912）。抓不到请求的根因 = **winpty/ConPTY 子进程内 .NET HttpClient 初始化失败**（`无法加载或初始化请求的服务提供程序`，无限重试）——python 子进程同环境 HTTP 正常，排除 winsock；普通管道（无 ConPTY）子进程 jcc 请求**成功到达**监听器。属探针环境限制，非产品缺陷。用户真实终端不受影响（验收已证）。
附：`-p` 模式请求 body 无 tools 字段（打印模式不挂工具，设计使然）；TUI 首次请求含 `tools/tool_groups/tool_descriptions` 三段（两阶段加载协议）。

## 四、Req4：真实测试

- 契约/单元：ToolCountdown 7 + PromptTransparency 3 + AnthropicQueryService 94（含排序/防重 4）+ ToolSearch 15 + SystemNotice 4 — 全绿。
- 回归：NativeTui.Tests **2991/2991**、abs 719、infra 599、host 1494、mcp ToolSearch/UserInteraction 全绿。
- 真实管道（无 ConPTY）端到端：jcc -p → 本地监听器收到真实请求（system 12307 字符 + tool_groups/tool_descriptions 协议三段齐备）— 落 `.xxx/first_prompt_capture.json`。
- 交互级验收（TUI 视觉/交互）由用户执行：/prompt 查看、bash 运行观察倒计时。

## 五、遗留候选

- TUI 首屏折叠项形态的透明化（当前 /prompt 命令触发；可扩展为自动插入折叠项）。
- 组名清单注入系统提示词（跨 prompts/engine 管线，~49 组名 1-2KB）。

<!-- 🤖 Auto Decision: 2026-10-31 -->
<!-- 决策: 透明化走 dump-to-transcript（/prompt 命令）而非新弹窗；倒计时走 spinner 状态 part 而非独立行 -->
<!-- 原因: 复用基准既有语义族（[ dump-to-scrollback / spinner parts 渐进宽度），零新 UI 组件，数据源单一（会话服务真实对象） -->
<!-- 替代方案: 独立对话框组件（新交互面成本高）；引擎每秒推 ToolProgress 倒计时事件（引擎侧定时器风险） -->
<!-- 验证: 单元 14 个新增全绿 + 全仓编译 + 真实管道 E2E ✅ -->
