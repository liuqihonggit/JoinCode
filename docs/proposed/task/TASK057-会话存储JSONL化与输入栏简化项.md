# TASK057 — 会话存储 JSONL 化 + TUI 输入栏简化项三连

> 状态：implemented（2026-10-21 全部四任务落地；剩余用户真实终端手感验收）
> 前置：TASK056（斜杠补全+历史环）已完结验收
> 用户指令（2026-10-21）：①会话改为 jsonl 按需读取和追加（现在逻辑有问题）②推荐功能全部做

## 任务分解（渐进式：每步 编译 → 测试 → commit）

| # | 任务 | 优先级 | 状态 |
|---|------|--------|------|
| 1 | **transcript 存储改 JSONL**（按需读取+纯追加，消除整存整取） | P0 用户指令 | ✅ 49f3b6e+0160ed8（86+5+2+5 测试，真实存量会话迁移验收 OK，ADR 0144 accepted） |
| 2 | 参数补全（/model xxx Argument 模式） | P1 | ✅ b6f4309（生成器补 ArgumentHint 提取 + Host 静态清单 theme/effort/config + Demo 演示接线） |
| 3 | 幽灵文本（mid-input /xx 补全） | P2 | ✅ c41a369（GhostSuffix 前缀匹配 + RightArrow 接受 + dim 渲染光标覆盖首字符） |
| 4 | Ctrl+R 历史搜索 | P3 | ✅ 061d4f2（HistorySearch 子串+去重+容量100 + 模态优先级链 + ghost 显示当前匹配） |

## 任务1 背景（调研待补充）

- 现状：`~/.jcc/sessions/{id}/transcript.json` 为 **JSON 数组整存整取**——写入是"读旧数组+追加+写回全量"（每次提交 O(n) 全量重写），读取是全量反序列化+逐条 paste-cache 还原（大会话性能灾难）
- 目标：JSONL 每行一条，追加 O(1)，读取按需流式（分块倒序 — TS readLinesReverse 4KB 先例）
- 关键资产：`TranscriptFileWriter`（Actor 串行写）、`AgentTranscriptService`（subagents/agent-*.jsonl 已是 JSONL 先例）、`MigrateLegacyAsync`（历史迁移）
- **历史疑点**：旧扁平 {id}.jsonl 曾被迁移为 JSON 数组——需查 git 历史确认当初迁移动机，避免踩回旧坑（可能是 paste-cache 引用/原子性/损坏容错问题）

<!-- 🤖 Auto Decision: 待调研后补充 -->

## 最终基线

- NativeTui.Tests：698 → **751**（+53：参数补全 11 + 幽灵文本 12 + 历史搜索 17 + Demo 引擎 5 + Host fake 修复等）
- NativeTui.Host.Tests：43 → **45**
- Demo selftest + bridge-selftest 双通过
- 历史存储：vault.other.tests 88/88 + 集成 2/2 + GUI 571 + 真实存量会话迁移验收（ADR 0144 accepted）

<!-- 🤖 Auto Decision: 2026-10-21 -->
<!-- 决策: 搜索态/ghost/参数补全三功能全部走 Core 零依赖纯逻辑组件 + ReplShell 模态拦截链 -->
<!-- 原因: 对齐权限对话框/模型选择器既有模态先例,优先级链 权限>模型选择器>搜索>补全>历史 -->
<!-- 替代方案: 每功能独立浮层组件(渲染侵入大,复用 ghost/建议区渲染更轻) -->
<!-- 验证: 751+45 全绿 + Demo 双自检 ✅ -->

## 验收缺陷修复（2026-10-21 用户真机验收反馈）

| # | 缺陷 | 根因 | 修复 | 提交 |
|---|------|------|------|------|
| 1 | `/theme d` 无效果（值域缺失） | TUI 硬编码 theme=3值 vs 引擎 `[ChatCommandArg]` 真值 7 值 — 参数值域双源 | `SlashCommandMetadata.Arguments` 字段 + 生成器从 `[ChatCommandArg].Enum` 提取注入，TUI 删硬编码改消费元数据 | 0e19963c |
| 2 | `/model` 切换后 banner 模型名不更新 | session_info 只在启动发一次，`ApplyModel` 后无人重发 | `EngineSession` 持有 checker/fastMode 字段 + `RefreshSessionInfo()` 重发；Program 的 applyModel 委托组合 ApplyModel+刷新 | 041013bd3 |

- Bug2 测试：`EngineSessionTests.BuildSessionInfoEvent_AfterModelSwitch_ReflectsNewModel`（真实 FastModeService，FIFO 确定性）
- 踩坑：QuerySync 在 Actor Dispose 后 TrySend 静默返回 false → 永久阻塞（测试必须 Dispose 前完成查询，用 `await using var`）
- 基线更新：NativeTui.Host.Tests 45 → **47**；Host exe 已 `--no-incremental` 全量重建供复验

<!-- 🤖 Auto Decision: 2026-10-21 -->
<!-- 决策: applyModel 委托组合（ApplyModel + RefreshSessionInfo）而非让 ModelPickerEngine 依赖宿主刷新 -->
<!-- 原因: ModelPickerEngine 属 Core 零依赖层,刷新 session_info 需 EngineSession 内部状态,委托组合保持分层 + ADR 0140 零依赖 -->
<!-- 替代方案: ModelPickerEngine 直接回调宿主接口(引入 Core→Host 反向依赖,破坏零依赖) -->
<!-- 验证: EngineSession 6/6 + 双项目全量 751+47 全绿 + Host exe 全量重建 ✅ -->

