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

## 四、修复计划（登记后逐个守卫修复）

- [ ] F1（断层1+实锤10）：/exit /quit /logout 退出链（ShouldContinue 消费 + 退出反馈）
- [ ] F2（断层9）：LocalToolRegistry 过早释放根因修复（TUI 模块链差异调查）
- [ ] F3（断层2）：交互回调接 TUI 权限对话框桥（Confirm/Prompt 最小面）
- [ ] F4（断层3）：/clear 屏幕清（ClearScreen 回调接 ReplShell 清滚动区）
- [ ] F5（断层4）：ChatCommandRegistry 引擎 DI 单例注册（消除每次重建）
- [ ] F6-F8：按优先级排期
- [ ] 修后探针复跑全绿 + 扩全命令集 + 用户手工验证清单

## 五、验收表

| 基建实现 | 消费点 | 已实现 | 已验收 |
|---------|--------|--------|--------|
| 三代理调研报告 | 本文档 | ✅ | — |
| --selftest-commands 批量探针 | 真实链路逐命令实测 | 待做 | 待做 |
| 断层 F1-F8 修复 | 各命令 | 待做 | 待用户手工验证 |
