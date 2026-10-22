# ADR 0148：/theme 双通道分工与 auto 唯一数据源收编

> 状态：proposed（2026-10-23；落地于 TASK065 S11 遗留#3 裁决）

## 背景

`/theme` 存在两条独立实现通道，随 TASK065 S11 接线而并存：

| 通道 | 位置 | 宿主形态 | 特性 |
|------|------|---------|------|
| 引擎侧命令 | `modules/composition/slash/guard/config/ThemeCommand.cs` | CLI 经典 REPL / headless `-p` / `--selftest-commands` 探针 | 交互式 Selector 选择器、`TerminalHelper.ClearScreen()` 刷新、`Console.*Color` 全局控制台色、7 名清单（含 auto） |
| TUI 拦截 | `native_tui/.../ReplShell.HandleThemeCommand` | native_tui Host（TUI 全屏渲染） | 本地渲染色板（Theme 对象）即时生效、不清屏（ClearScreen 会破坏 TUI 渲染流）、通知反馈、持久化委托 |

裁决前缺口：①TUI 侧不认识 `auto`（引擎侧支持 — 用户在 TUI 输 `/theme auto` 报"未知主题"）；②auto 小时解析逻辑散落三处（引擎 `ApplyTheme` / Host `LoadThemeNameAsync` 各写一遍 `6-18` 判定）。

## 决策

**分工明确而非二选一** — 两条通道各服务其宿主形态（CLI 通道的 Selector/清屏语义在其宿主正确；TUI 通道的本地色板/不清屏语义在其宿主正确），同时消除跨通道不一致：

1. **主题名集合双通道一致**：`ThemeSelection.Names` 收编为七名清单（auto/dark/light/dark-daltonized/light-daltonized/dark-ansi/light-ansi，对齐引擎侧 `ThemeCommand` 枚举序）— TUI 拦截自动获得 `auto`
2. **auto 解析唯一数据源**：`ThemeSelection.ResolveAutoName(hour)`（6-18 点含 6 不含 18 → light，其余 dark）— 引擎 `ApplyTheme` 与 Host `LoadThemeNameAsync` 的重复小时判定收编于此（消费方禁重写）
3. **持久化写原名**：TUI 侧 `/theme auto` 持久化写 `auto` 字面量（非解析值）— 与引擎侧 `SetAsync("theme","auto")` 语义一致，重启后由加载方再按时段解析

## 替代方案（考虑后放弃）

- **二选一（删引擎侧命令，TUI 拦截为唯一入口）**：被否 — CLI/headless 宿主无 TUI 渲染流，引擎侧命令是其唯一主题切换通道（Selector 交互与清屏语义在该宿主成立）
- **统一到引擎侧（TUI 拦截转发引擎命令）**：被否 — 引擎命令的 `ClearScreen` 破坏 TUI 渲染流（S11 实证），且 TUI 需要本地 Theme 色板对象即时换帧（引擎通道只设 `Console.*Color`）
- **auto 判定保持三处各写**：被否 — 违反"唯一数据源 + 委托消费"规范（AGENTS.md 硬编码变委托），三处漂移风险已现（TUI 缺 auto 即为漂移实例）

## 影响

- TUI 用户获得 `/theme auto`（时段自适应）
- Host 启动加载与 TUI 切换共用 `ResolveAutoName` — 修改时段规则只需改一处
- 引擎侧 `ThemeCommand.ApplyTheme` 内联 auto 判定保留（引擎域自治；若后续统一需引 native_tui 包不可行——Core 依赖方向相反，接受双实现但规则由测试锚定一致）
