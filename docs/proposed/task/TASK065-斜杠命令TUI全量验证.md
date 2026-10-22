# TASK065 — 斜杠命令 TUI 全量传递与应用验证

> 状态：in_progress（2026-10-22 三子代理调研完成 + 批量探针实测中）
> 用户指令："开几个子代理，看引擎有多少斜杠命令实现完了、多少实现了没连接到 TUI、没验证过的，列一个表逐个验证，有些许错误都不行，最后用户手工验证"

## 一、调研结论（三子代理并行）

| 维度 | 结论 |
|------|------|
| 引擎命令总量 | **98 个全部实现**（modules/composition/slash/，[ChatCommand] 特性一一对应，无遗漏） |
| 调 LLM 命令 | 13 个（btw/init/ultraplan/install/review/security-review/compact/insights/??/goal/falv/generate/analyze；plan fallback 间接） |
| 需交互命令 | 18 个（Confirm/ReadPassword/Selector/ReadLine/ReadKey） |
| 退出命令 | 3 个（exit 确认后/quit 直接/logout 登出全部后） |
| 隐藏命令 | 14 个 |
| 测试覆盖 | **44/98 有测试，54 零测试**（重灾区：guard 域 11/15 空、ai 域 10/16 空、system 域 11/19 空） |
| TUI 连接面 | 命令分发链已通（TASK064 SlashCommandRunner 分流）但**控制语义未消费、交互回调全 null** |

## 二、断层登记（TUI 数据链，子代理 2 调研）

| # | 级别 | 断层 | 位置 | 影响 |
|---|------|------|------|------|
| 1 | **P0** | /exit /quit /logout 敲了**不退出且无任何反馈**（空回合） | EngineSession.DispatchSlashCommandAsync 未传 onExitRequested、忽略 ShouldContinue；ExitCommand confirm null+非交互→直接 Exit() 无输出；程序注释宣称"退出路径 Ctrl+D 或 /exit"与实际不符 | 用户敲 /exit 毫无反应 |
| 2 | **P0** | 交互回调全 null（confirm/prompt/readPassword/clearScreen） | EngineSession.cs:142-147；SlashCommandRunner 注释明示"TUI 传权限对话框 Confirm"设计意图但未实现 | /commit 确认全拒无法提交、/login 无法输 key、/resume 选择器取消、/worktree clean 全拒 |
| 3 | P1 | /clear 清引擎历史但 TUI 滚动区不清 | ClearCommand ClearScreen 未传 | 提示"已清空"但屏幕满是旧消息 |
| 4 | P1 | 命令注册表每次执行全量重建；清单 DI 永远 null 走静态兜底（注释误导） | SlashCommandRunner.cs:44-49、SlashCommandSource.cs:15 | 功能正确纯开销；动态注册命令不进两面板 |
| 5 | P2 | IsEnabled 运行时动态门控在补全面失效 | SlashCommandSource.cs:18 过滤的是静态特性值 | 动态禁用命令仍出现在建议列表 |
| 6 | P2 | /help 长输出 Markdown 重排风险（4 空格缩进→代码块、表格错位）；命令 stderr 直出与渲染交错 | EngineSession.cs:149、InProcessEventConsumer | 帮助文本对齐可能错乱 |
| 7 | P3 | /model 双路径分裂（无参走 TUI 选择器、带参走引擎命令、补全 hint 诱导带参） | Program.cs:142-151 | 行为不一致 |
| 8 | P3 | SlashSuggester 命令名精确匹配大小写敏感 vs 别名不敏感 | SlashSuggester.cs:226/230 | /EXIT 不精确匹配 exit |

## 三、批量探针实测（--selftest-commands，2026-10-22）

33 条安全命令走 TUI 真实链路：**30 OK / 3 EMPTY / 0 ERROR / 0 TIMEOUT**。

### 实测发现（新增断层）

| # | 级别 | 发现 | 证据 |
|---|------|------|------|
| **9** | **P0** | **LocalToolRegistry 在 TUI 会话中被过早释放** — /tools /mcp list 报"命令执行失败: 锁 'AsyncLock#2' 已释放"；/nosuchcmd 未知命令的 MCP 工具查找分支同样中招（设计诱导文案被掩盖） | 探针三条全报同锁错；已知命令斜杠注册表分支命中不碰工具表所以 OK |
| 10 | — | 退出类零输出实锤（断层 1 探针验证） | /quit /exit /q 三条 EMPTY |
| — | — | 其余 30 条全部正常（/help 5736 字符 /doctor 1861 /env 2991 /agents 858 /cost $2.13 等） | 探针输出 |

### 30 条 OK 命令（内容正常）

status/version/help/context/doctor/env/usage/stats/cost/config list/tasks/history/files/tag list/trust list/privacy-settings show/theme/effort/vendor/model info/agents/summary/worktree list/branch list/session list/debuglog/?（别名）

## 四、修复计划（登记后逐个守卫修复 — 全部完成）

- [x] F1 退出链：/exit /quit /logout ShouldContinue 消费（上游 M8-5 TuiCommandCallbacks 架构 + 本侧命令名提示行融合，rebase 已收）
- [x] F2 registry 过早释放：8 处命令 await using DI 单例违规根因修复（is-pattern 借用 + ToolRegistryAdapter 非 owner 零转发 + 守卫测试）
- [x] F2b 交互命令模态化：confirm/prompt 接 TUI 对话框（CommandDialogCoordinator TCS 桥，照抄 TuiPermissionBridge 先例；ReadPassword 降级遗留）
- [x] F3 /clear 屏幕清：上游 M8-5 ClearMessagesExceptBannerAndSpinner（rebase 融合收编）
- [x] F4 registry DI 单例：引擎域 SlashCommandModule(Order=50) 注册（CLI 链由 CliModule 自有注册职责分离）+ Runner DI 优先
- [x] F5 IsEnabled 动态门控：SlashCommandSource 运行时过滤（registry 缺席降级静态值）
- [x] F6 命令输出纯文本渲染：InProcessEventConsumer.BeginCommandTurn 纯文本模式（Markdown=null 跳过投影，/help 不再重排）
- [x] F7 /model hint 失配：Host 装配层 ApplyPickerHints（model → "（无参打开选择器）"）
- [x] F8 大小写统一：SlashSuggester 四处 OrdinalIgnoreCase（/EXIT 可匹配）
- [x] 生产 bug：EffortCommand max + ModelCatalog 缺失未捕获崩溃 → 保守降级 high（代理 C 登记 + 红绿修复）
- [x] 探针复跑全绿 33/33

### 三代理并行批次（2026-10-22 第二波）

| 代理 | 任务 | 交付 |
|------|------|------|
| A | F2b 交互模态化 + F6 纯文本 | CommandDialogCoordinator/Item + ReplShell 桥门面 + 30 测试（含 xunit 并行互踩真缺陷修复：slash-command-serial Collection） |
| B | F4/F5/F7/F8 | SlashCommandModule + Runner 测试锁定 + SlashCommandSource 门控 + ApplyPickerHints + 大小写 4 处，4+3+3+38 测试 |
| C | 零测试命令补测 P1+P2 | 13 命令 129 测试（login/logout/trust/permissions/reset-config/privacy-settings/help/stats/quit/fork/effort/fast/version）+ 3 生产隐患登记（effort 已修；UsageTracker 可空契约、Dialog 静态不可注入待排期） |

## 五、验收表

| 基建实现 | 消费点 | 已实现 | 已验收 |
|---------|--------|--------|--------|
| 三代理调研报告 | 本文档 | ✅ | — |
| --selftest-commands 批量探针 | 真实链路逐命令实测 | ✅ 33/33 两轮全绿 | ✅ ADR 0080 探针实证 |
| 断层 F1-F8 修复 | 各命令 | ✅ | 待用户手工验证 |
| 测试基线 | composition 408 / NativeTui 1265 / Host 99 / host.tests 1419 | ✅ 全绿 | ✅ |

## 六、用户手工验证清单（真机 Host.exe）

| # | 命令 | 预期 |
|---|------|------|
| 1 | /exit | 弹 y/N 确认对话框（F2b 模态）→ y 后显示 "⎿ /exit — 退出 jcc" 退出 |
| 2 | /help | 对齐纯文本不重排（F6：缩进不被 Markdown 解释） |
| 3 | /model | 补全提示 "（无参打开选择器）"（F7），回车弹选择器 |
| 4 | /EXIT（大写） | 补全列表命中 exit（F8） |
| 5 | /effort max | sensenova 模型降级 high 提示不崩溃（生产 bug 修复） |
| 6 | /mcp list 后再 /tools | 无"锁已释放"（F2 registry 存活） |
| 7 | /commit（有暂存时） | 弹确认对话框（F2b）而非静默拒绝 |

<!-- 🤖 Auto Decision: 2026-10-22 -->
<!-- 决策: 三代理文件域零重叠并行（A 交互模态 / B DI 化+补全层 / C 补测），禁 commit 由主代理统一收尾 -->
<!-- 原因: 用户指令"后续任务能并行多个子代理去实现吗"；撞车风险已先行排查（上游 M8 八批收口无新批次在飞） -->
<!-- 替代方案: 串行（慢三倍且三域独立无依赖） -->
<!-- 验证: 四域 408+1265+99+1419 全绿 + 双探针 33/33 + effort 崩溃红转绿 17/17 ✅ -->

### 第三波三代理并行批次（2026-10-22 晚）

| 代理 | 交付 |
|------|------|
| A | ai 域核实（10 命令中 5 不存在：model-list/output-style/thinking/mood/temperature 均非独立命令——能力分别在 /model 无参、/config 键、/sampling 参数）+ 5 实存命令 62 测试（compact/context/model/vendor/memory） |
| B | system 域 10 命令 134 测试（config/status/mcp/theme/doctor/upgrade/export/agents/plugin/hooks；claude 不存在）+ MCP add bug 锁定 |
| C | Dialog 注入化（/logout all、/permissions clear 确认走 ChatCommandContext.Confirm 双形态——对齐 ExitCommand 先例，null 降级保生产交互）+ UsageTracker 契约去误防御（实现从不返回 null）41 测试 |

**主代理修复（登记 bug 闭环）**：
- catalog 崩溃 ×3（Context 降级 128k 窗口/Model 别名透传直切/Vendor 降级切换不跟随默认模型——快照测试全部翻转为降级断言）
- MCP add 不识别（CrudAction 枚举补 Add 值 Unix 习惯 + switch 补 case——测试翻转 Times.Once）

**遗留登记（下轮候选）**：CompactCommand PTL attempt=2 边界异常冒泡、MemoryCommand --type 小写剥离残留、ExportCommand sanitize CJK 与 TS 偏差、交互式 Picker/Selector 分支（测试环境天然不可达）。

基线：composition 408 → **608**（+200）；host.tests 1419；探针 33/33；cli/Host 全量重建 0 错误。
