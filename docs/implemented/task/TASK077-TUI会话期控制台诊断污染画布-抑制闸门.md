# TUI 会话期控制台诊断污染画布 — 抑制闸门（TASK077）

> **状态**：implemented — 字节级根因确证 + 闸门落地 + E2E 复验全绿（2026-10-31）
> **来源**：用户验收 TASK076 时报告"输入命令之后跑的功能有大量错位显示"；ConPTY 复现 + 原始流字节级解剖
> **创建时间**：2026-10-31
> **关联**：TASK076（resize fullReset 锚定修复 — 本任务是其验收过程中暴露的第二个独立缺陷）；ADR 0077（MockServer 联测 — 引擎 idle timeout 另案）

---

## 一、现象

命令执行 → 工具调用 → 拒绝/失败 → **错误消息上屏时大量错位**：spinner 行与提示行文字交错
（`* ⎿  提示: /b7w 记thinking` — spinner 的 "thinking" 尾巴叠写到提示行）、错误消息渲染进输入框区域、
游离的分隔线碎片、旧内容残留不清。resize（TASK076 修复后）已对齐，错位集中在**会话期事件帧**。

## 二、字节级根因（ConPTY 原始流解剖，`.xxx/resize_probe/jcc_garble_stream.bin`）

VtGrid 操作日志（忠实 conhost 语义：wcwidth 宽度 + pending-wrap）定位到第一个失同步操作：

```
PUT (29,9) '[DIAG-ERR] 19:04:24.824 [CrashStore] 快照已捕获: [ERROR] MainAgent:
System.AggregateException: Both streaming and non-streaming fallback failed...'
```

因果链：回合失败 → `CrashSnapshotStore.cs:56` → `Diag.WriteError`（**无条件**输出，
`Diag.cs:72-74` "错误必须始终可见，不受 JCC_DEBUGLOG 控制"）→ `AsyncStderrWriter`（后台 Timer 线程）
→ `Console.Error.WriteLine` → **诊断行写进 TUI 画布**：

1. 诊断行 ~200 列，从输入行第 9 列起写 → 在 30 行终端的**底行溢出换行** → **终端滚屏一行**；
2. `InlineComposer` 相对光标模型（内容坐标信念）对滚屏无感知 → 此后所有帧的相对移动（CUU/CUF）
   落点整体错位 → spinner 尾巴逐帧下移叠写、分隔线从错误列起写、旧行不清 — **永久失同步不可自愈**；
3. 后台线程写入还能与渲染主循环**帧间交错**（用户 paste 的文字叠写形态）。

**既有防线为何失效**：`NativeTui.Host/Program.cs` 既有 `Console.SetOut(Console.Error)`
（"引擎内部任何 Console 输出不得污染 TUI 渲染流"）— 只把污染从 stdout 挪到 stderr；
真实终端里 **stderr 与 stdout 是同一块画布**，防线形同虚设。

## 三、修复：ConsoleSuppressionGate（Console 层防御，零容忍全覆盖）

| 基建实现 | 消费点 | 已实现 | 已验收 |
|---------|--------|--------|--------|
| `ConsoleSuppressionGate`（abstractions `abs_core/core_utils/diagnostics/`）：Begin 换装 `Console.SetOut/SetError` 为环形缓冲捕获 writer（行缓冲、\n 切分、宽行不截断）；最外层 Dispose 恢复原 writers + 冲刷回原 stderr（CI/E2E 诊断捕获不受损）；嵌套引用计数；1024 行上限丢旧；`TakeSuppressedLines` 排水面（/debuglog 类消费方） | `NativeTui.Host.RunInteractiveAsync`（TUI 会话边界 — jcc 交互全路径覆盖；`tuiOutput` 渲染独占 writer 先于换装捕获） | ✅ | ✅ E2E（下表） |
| 契约测试 ×5（抑制期双通道不落盘 / 环形持有+排水 / 最外层冲刷 / 嵌套不恢复 / 上限丢旧） | `test/unit/infra.tests/utils/diagnostics/ConsoleSuppressionGateTests.cs` | ✅ 5/5 | ✅ red（CS0103）→ green |
| `vt_grid.py`（resize_probe 工具族）— 忠实 conhost 语义 VT 解码器（wcwidth 宽度 + pending-wrap + 滚屏建模）；替代 pyte（实测 CJK 按 1 列解码，`'中文ab'` 占 4 列 vs 真实终端 6 列，列级分析失真） | 列级根因分析 + E2E 网格断言 | ✅ | ✅ 本任务全程使用 |

**设计取舍**：闸门放 `System.Console.SetOut/SetError` 层而非 Diag 内部 — 一次拦截
`Diag`/`AsyncStderrWriter`（后台 Timer 走 Console.Error）/`McpPluginBootstrap`（后台加载链终止
直接 `Console.Error.WriteLine`）/未来任何直写组件；错误提示诱导原则不受损 — 错误本身已由 TUI
消息项上屏（"回合执行失败: …"），stderr 副本在会话期是纯污染。

## 四、E2E 验收（同一 ConPTY 场景：真实 jcc.exe + MockServer，回合失败路径）

| 断言 | 修复前（garble_stream） | 修复后（gated_stream） |
|---|---|---|
| 流中 `[DIAG-ERR]` 出现次数 | ≥1（帧间写入） | **0** |
| 错误帧布局 | spinner/提示残留 + 24 空格游离分隔线碎片 + 错误行截断错位 | 错误消息紧接用户消息行、完整渲染、输入框/footer 各就各位 |
| 终端滚屏失同步 | 有（底行溢出） | 无 |

回归：infra.tests/utils **599/600**（1 skipped）✅；NativeTui.Tests **2974/2974** ✅
（首跑 1 例 flaky 未复现，与本修复无关 — 时序偶发，如实记录）。

## 五、边界与后续

- ⚠️ 引擎侧 "Stream idle timeout"（mock 流式脚本不收尾）是独立联测问题 — ADR 0077 范畴，另案。
- ⚠️ 相对光标模型对**任何**外部终端写（含未来新组件）仍脆弱 — 深层防御方向：CSI 6n（CPR）
  光标位回报自校准（流中已见 `[6n` 发送，读取链未接）。记录为后续候选，不阻塞本修复。

<!-- 🤖 Auto Decision: 2026-10-31 -->
<!-- 决策: 闸门放 System.Console 换装层而非 Diag 内部；会话期诊断改环形缓冲 + 退出冲刷回 stderr -->
<!-- 原因: 直接 Console.Error 写入点分散（Diag/AsyncStderrWriter/McpPluginBootstrap），Diag 内部门禁漏拦截；换装层一次全覆盖且 CI 诊断捕获不受损 -->
<!-- 替代方案: 各调用点改走 Diag（漏网风险）+ Diag 加静默开关（仍漏直接 Console.Error 调用） -->
<!-- 验证: 契约测试 5/5 + E2E 流中 DIAG-ERR=0 + 错误帧完全对齐 ✅ -->
