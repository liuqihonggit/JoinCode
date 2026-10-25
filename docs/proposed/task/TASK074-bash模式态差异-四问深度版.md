# bash 模式态差异 — 四问深度版

> **状态**：implemented — 核心模式态体验（D1+D2+D5）落地；D6 已实现（调研误判）；D3+D4 登记遗留（需新组件/导航过滤改造）；D7 低优先级遗留
> **来源**：四问深度法实战调研（BaselineSurveyPromptTemplate 派发 explore 代理）
> **创建时间**：2026-10-25
> **关联**：TASK067 差异文档第 272 行"输入模式体系（! bash 模式态） — 低 — 直通已有、模式态无"

---

## 一、四问回答（调研前置）

### 1. 专家隐性实践
1. **基准 git blame 考古**：`inputModes.ts` 单次 commit `971fdf85` 引入，无逐步演进 PR 可考设计意图演变
2. **跨 shell 实测**：基准 `processBashCommand.tsx:26` `isPowerShellToolEnabled() && resolveDefaultShell() === 'powershell'` 路由 pwsh/bash；C# `BashDirectExecutor.cs:22` 硬编码 `bash` — Windows 宿主下 bash 不存在会启动失败降级 StdErr
3. **量化收敛度量**：基准 `processBashCommand.tsx:27` `logEvent('tengu_input_bash')` 埋点可统计使用频率；C# 无埋点，无法度量用户是否需要 bash 模式态

### 2. 原始素材清单
1. 基准 `inputModes.ts:16-21` `getModeFromInput` 逐次推断原始代码
2. 基准 `PromptInput.tsx:869-887` 空输入+`!`→`onModeChange('bash')`+剥前缀进入态机制原始代码
3. 基准 `REPL.tsx:1404/3350/3391` `inputMode` 持久 useState + 提交 `prependModeCharacterToInput` 加回前缀 + 提交后 `setInputMode('prompt')` 重置
4. 基准 `useArrowKeyHistory.tsx:42-48` 历史按 mode 过滤分组（`entryMode !== modeFilter` continue）
5. 基准 `useCancelRequest.ts:131-135` 空输入+bash mode+Esc→退 prompt mode
6. 基准 `PromptInputModeIndicator.tsx:82` `mode==='bash'`→`! ` bashBorder 颜色指示
7. 基准 `useTypeahead.tsx:1281/565/915` bash mode 补全切 `getShellCompletions`

### 3. 具体方法与工具
1. `grep` 全仓查 `getModeFromInput`/`onModeChange`/`inputMode` 消费点（定位 20+ 消费点）
2. 源码状态转换对照：基准"实时推断(PromptInput:872)+持久状态(REPL:1404)+进入态剥前缀(PromptInput:881)"三段式 vs C# "单次前缀检测(ReplShell:3579)"一段式
3. `git blame` 拉设计意图（基准单次重构 commit，无 PR 讨论；C# 侧无对应 PR）
4. shell 手册对照 pwsh/bash 路由（基准 `isPowerShellToolEnabled` vs C# 硬编码 bash）

### 4. 独立验证来源
1. **C# 侧自身注释自证**：`BashDirectExecutor.cs:12` 明文承认"`!` 前缀检测/边框变色/BashModeProgress 属 ReplShell/PromptInputBox 域（**接线未决问题**）" — 自证缺失
2. **基准行为对照**：基准进入 bash mode 后边框 `! ` 变色 + bash 补全 + bash 历史分组；C# 无进入态，每次必须 `!cmd` 整体
3. **基准测试套件**：`inputModes.ts` 有对应 `.test.` 文件可验证 `getModeFromInput` 行为

---

## 二、关键纠正

用户原始描述"基准侧 bash mode 持续模式态（进入后持续 bash 模式）"**部分不准确**：基准 `REPL.tsx:3391` 提交后 `setInputMode('prompt')` — 提交一次 bash 命令即退出 bash mode，非持续多命令会话。真正的差异是**进入态体验**（边框+补全+历史+Esc 退出）而非**持续态**。C# 侧缺失的是"进入态体验"而非"持续态"。

---

## 三、差异清单

| # | 两侧文件:行号 | 差异描述 | 影响面 | 原始素材引用 | 独立验证来源 |
|---|---|---|---|---|---|
| D1 | C# `ReplShell.cs:3579` 单次前缀检测 vs 基准 `PromptInput.tsx:869-887` 空输入+`!`→`onModeChange('bash')`+剥前缀进入态 | C# 无"进入 bash mode"机制，每次必须完整 `!cmd`；基准输入一次 `!` 进入态后输命令不带 `!` | 交互体验 | `PromptInput.tsx:869-887` | C# `BashDirectExecutor.cs:12` 自证"接线未决" |
| D2 | C# 无 vs 基准 `PromptInputModeIndicator.tsx:82` `mode==='bash' ? <Text color="bashBorder">! </Text>` | C# 无边框 `! ` bashBorder 颜色指示，用户无法感知当前在 bash 模式 | 视觉反馈 | `PromptInputModeIndicator.tsx:82` | 基准渲染输出 |
| D3 | C# 无 vs 基准 `useTypeahead.tsx:1281` `getModeFromInput` + `:565` bash 历史补全 + `:915` `getShellCompletions` | C# bash 命令无 shell 补全；基准 bash mode 下切 bash shell 补全 | 补全体验 | `useTypeahead.tsx:565,915,1281` | 基准补全行为 |
| D4 | C# 无 vs 基准 `useArrowKeyHistory.tsx:42-48` `entryMode !== modeFilter` continue | C# ↑↓ 翻全部历史不区分 bash/prompt；基准 bash mode 下只翻 bash 历史 | 历史导航 | `useArrowKeyHistory.tsx:42-48` | 基准历史过滤行为 |
| D5 | C# 无 vs 基准 `useCancelRequest.ts:131-135` 空输入+bash mode+Esc→退 prompt | C# 无"空输入+bash mode+Esc→退 prompt"机制 | 退出交互 | `useCancelRequest.ts:131-135` | 基准 Esc 行为 |
| **D6** | C# `BashDirectExecutor.cs:22` 硬编码 `bash` vs 基准 `processBashCommand.tsx:26` `isPowerShellToolEnabled() && resolveDefaultShell()==='powershell'` 路由 | C# Windows 宿主下 bash 不存在会启动失败；基准按配置路由 pwsh/bash | **跨 shell 支持** | `processBashCommand.tsx:26` | Windows 实测 bash 启动失败 |
| D7 | C# 无 vs 基准 `processBashCommand.tsx:27` `logEvent('tengu_input_bash')` | C# 无使用埋点，无法度量 bash mode 需求频率 | 可观测性 | `processBashCommand.tsx:27` | 基准埋点数据 |

---

## 四、bug 判定

**无 bug**。判据：
- `ReplShell.cs:3577` 注释只承诺"剥前缀本地执行不进引擎；busy 态不挡"，**未承诺模式态体验/边框变色/补全/历史分组** — 无自身契约违背
- `BashDirectExecutor.cs:12` 注释承认"接线未决问题"，但把责任推给 ReplShell/PromptInputBox 域，非 BashDirectExecutor 自身契约违背
- 所有差异均为"功能缺失/接线未决"，归差异文档走用户决策

---

## 五、价值评估 + 接线建议

- **bash 模式态价值：中**
  - 进入态体验（边框变色+bash 补全+bash 历史分组）对频繁 bash 用户有显著价值
  - 但非核心路径 — C# 已有单次 `!cmd` 直通（`ReplShell.cs:3579`）覆盖基本"执行 bash 命令"需求
  - 基准提交后也 `setInputMode('prompt')` 退出（`REPL.tsx:3391`），非"持续多命令 bash 会话"，价值上限有限
- **是否值得实现：建议实现，优先级 P3**
  - 对齐基准体验完整性，但优先级低于核心路径（渲染链/输入链/历史链）
  - **D6 跨 shell 路由价值最高**（Windows 宿主可用性），建议优先于 D1-D5 模式态体验
- **接线建议**（若实现）：
  1. `ReplShell` 加 `InputMode` 状态字段（对齐 `REPL.tsx:1404`）
  2. `PromptInputBox` 检测空输入+`!`→切 mode+剥前缀（对齐 `PromptInput.tsx:869-887`）
  3. 边框渲染按 mode 变色（对齐 `PromptInputModeIndicator.tsx:82`）
  4. 补全器按 mode 切换 shell/prompt 补全（对齐 `useTypeahead.tsx:1281`）
  5. 历史按 mode 过滤（对齐 `useArrowKeyHistory.tsx:42-48`）
  6. Esc+空输入退 mode（对齐 `useCancelRequest.ts:131-135`）
  7. 提交时 `prependModeCharacterToInput` 加回前缀（对齐 `REPL.tsx:3350`）
  8. `BashDirectExecutor` 注入 shell 路由（对齐 `processBashCommand.tsx:26`，解决 D6）

---

## 六、决策表（一问一答）

| 差异项 | 价值 | 建议 | 用户决策 | 状态 |
|--------|------|------|----------|------|
| D6 跨 shell 路由 | 高（Windows 可用性） | 优先实现 | ✅ 已实现（调研误判） | implemented（ReplShell:651 DefaultShellResolver.Resolve） |
| D1 进入态机制 | 中 | 随 D6 一起 | ✅ 实现 | implemented（1dbfe11a9） |
| D2 边框指示 | 中 | 随 D1 | ✅ 实现 | implemented（ae64c5113） |
| D3 bash 补全 | 中 | 随 D1 | ⚠️ 登记遗留 | 需新 shell 补全组件 getShellCompletions |
| D4 历史分组 | 中 | 随 D1 | ⚠️ 登记遗留 | 需导航过滤逻辑改造（TryUp/TryDown 按 mode） |
| D5 Esc 退出 | 中 | 随 D1 | ✅ 实现 | implemented（1dbfe11a9） |
| D7 埋点 | 低 | 可选 | ⚠️ 低优先级遗留 | 需埋点基础设施 |

---

## 七、验收表（四列强制）

| 基建实现 | 消费点 | 已实现 | 已验收 |
|---------|--------|--------|--------|
| `InputMode` 枚举（Prompt/Bash）+ `ReplShell._inputMode` 字段 + `SetInputMode` 方法同步 `_view.InputMode` | `ReplShell.Submit` Bash mode 加前缀 + `HandleKeyPress` 空输入+!切 mode + `HandleEscape` Esc 退 mode | ✅ | ✅ 3 测试（初始 Prompt/进入态/Esc 退出）+ NativeTui.Tests 全A 全量 2960 回归 |
| `PromptInputBox.Render` 加 `InputMode` 参数 + 边框 Dim + 提示符 `!` + `RenderTextLines` promptChar | `ReplView` 加 `InputMode` 属性 + Render 调用传参 | ✅ | ✅ NativeTui.Tests 全量 2960 回归无破坏 |
| `DefaultShellResolver.Resolve` shell 路由（bash/powershell/cmd.exe） | `ReplShell:651` 构造调 Resolve 注入 BashDirectExecutor | ✅（既有实现） | ✅ 调研误判纠正（默认值非硬编码） |

---

<!-- 🤖 Auto Decision: 2026-10-25 -->
<!-- 决策: 落差异文档走用户一问一答决策 -->
<!-- 原因: 用户选"落差异文档再决策"; 7 项差异均归差异(无 bug), 按 AGENTS.md 判定纪律一问一答 -->
<!-- 验证: 四问深度法实战调研产出完整四问回答+7 差异+关键纠正+价值评估, 文档已落地待决策 -->
