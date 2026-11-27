# TASK139 — Computer Use 实际启动 jcc 交互验收

- 状态：已完成（4 项真机缺陷已修复，GUI 矩阵与受影响域回归全部通过）
- 日期：2026-11-27
- 目标：通过真实桌面截图、键盘和鼠标操作启动并使用 `jcc.exe`，修复主路径上实际出现的不适或缺陷；不以管道输出、单元测试或源码推断替代真机验收。

## 范围

1. 使用 Debug 产物，在隔离的应用数据目录中启动。
2. 覆盖启动首帧、中文与 ASCII 输入编辑、方向键/Tab/Enter、斜杠建议、本地 `/help` 与 `/version`、会话选择器、窗口缩放、退出后的终端状态恢复。
3. 不调用真实模型、不读取真实凭据、不修改用户全局配置。模型回合如需验证，使用本地 MockServer。
4. 只修复实测复现的问题；实现差异不擅自对齐，单独记录。

## 环境与证据

- 工作区：`D:\project\w1`
- 产物：`artifacts/bin/JoinCode/Debug/net10.0/jcc.exe`
- 桌面：Windows 10，单屏 1920×1080。
- 截图、日志和采样输出统一放 `.xxx/`。
- 隔离配置：`.xxx/cu-accept-20261127/settings.json` 内容为 `{}`，API Key 仅使用 `dummy-not-a-real-key`。
- 桌面操作入口：jcc 自带 `start_process` / `focus_window` / `screenshot` / `type_text` / `key_press` / `mouse_click` / `move_window` / `list_processes`。
- 首轮阻碍已解除：Debug 产物重建完成；全仓编译首次因解决方案资产文件缺失失败，已执行 `dotnet restore build/sln/JoinCode.slnx` 后重跑。

## 验收清单

| 场景 | 操作 | 期望 | 实测 | 证据 |
|---|---|---|---|---|
| 启动首帧 | 可见终端无参数启动 | 正常进入 REPL，无崩溃/乱码/残留光标 | Hub 自动拉起成功，`/health` 返回 `agents=1/providers=6`，首帧无异常 | `.xxx/cu-accept-start-final-20261127.png`、`.xxx/cu-accept-hub-fixed-20261127.png` |
| ASCII/中文编辑 | 输入、左移、退格、Delete、Home、End | 文本与光标一致，中文宽度正确 | `ab中文cd` 经左移/退格/Delete 后为 `ab中文`，Home/End 后光标正确 | `.xxx/cu-edit-before-final-20261127.png`、`.xxx/cu-edit-after-final-20261127.png` |
| 方向键/Tab/Enter | `/` 建议、上下选择、Tab 补全、Enter | 选择与提交符合提示，不插入控制字符 | 建议列表可见；上下选择后 Tab 补全为 `/version`，Enter 正常提交 | `.xxx/cu-slash-suggestions-final-20261127.png`、`.xxx/cu-slash-tab-final-20261127.png` |
| 本地命令 | `/help`、`/version`、`/resume` | 未登录也可执行，输出完整且不重叠 | `/version` 输出版本/运行时/架构/OS；`/help` 完整；`/resume` 显示无可恢复会话 | `.xxx/cu-version-screen-20261127.png`、`.xxx/cu-help-final-20261127.png`、`.xxx/cu-resume-final-20261127.png` |
| 会话选择器 | `/session`、Esc | 空列表提示正确，Esc 恢复输入 | 隔离数据下显示“没有可恢复的会话”；Esc 返回输入态并可继续输入（无历史会话，搜索路径不适用） | `.xxx/cu-session-final-20261127.png`、`.xxx/cu-session-esc2-focused-20261127.png` |
| Resize | 缩窄再恢复，保留草稿 | 不吞键、不错列、草稿保留 | 1011×635 → 700×500 → 1011×635，`resize草稿` 始终保留，布局重排正常 | `.xxx/cu-resize-narrow-final-20261127.png`、`.xxx/cu-resize-restored-final-20261127.png` |
| 退出恢复 | `/exit` | 回到 shell，光标/鼠标/粘贴状态恢复 | `/exit` 输出“退出 jcc”，GUI 进程消失，终端回到 `PS D:\project\w1>`；鼠标/光标无残留异常 | `.xxx/cu-exit-final-20261127.png` |

## 问题台账

| 编号 | 现象 | 复现步骤 | 根因 | 修复 | 验证 |
|---|---|---|---|---|---|
| CU-1 | `focus_window` 对最小化窗口返回成功，但窗口仍停在 `(-32000,-32000)` | 最小化 Windows Terminal 后调用 `focus_window` | 激活前未检查/恢复 `IsIconic` 状态 | `User32NativeMethods.IsIconic` + `ShowWindow(SW_RESTORE)` | 集成测试红→绿；真实窗口恢复到 `(894,67) 1011×635`，证据 `.xxx/cu-focus-restored-20261127.png` |
| CU-2 | 隔离 `settings.json={}` 时 Hub 崩溃 | 空配置启动 `jcchub.exe` | 源码生成 JSON 反序列化后 `Vendor=null`，启动代码直接读取 `Count` | `ResolveSettings` 用 `Vendor is not { Count: > 0 }` 加载嵌入默认供应商 | 3/3 定向 + 40/40 Hub 全绿；真实 `/health` 返回 6 个供应商 |
| CU-3 | CLI 单项目构建有 `jcchub.exe` 但无 `jcchub.dll`，自动拉起 6 秒超时 | 构建 CLI 后从同目录启动 Hub | build-only `ProjectReference` 只复制 apphost/config，不复制目标程序集 | `CopyHubRuntimeFiles` 构建目标复制完整 Hub 输出树 | 回归测试红→绿；真实自动拉起成功，证据 `.xxx/cu-accept-hub-fixed-20261127.png` |
| CU-4 | 未登录时 `/version` 被 API Key 门禁吞掉 | 隔离空配置输入 `/version` | 登录门只放行 `/exit` 与 `/login`，本地诊断/会话命令没有声明式可用性 | 新增 `IsAvailableWithoutLogin` 元数据，生成目录透传，TUI 登录门按元数据放行 | 登录门红测 6 失败→18/18 绿；Host 元数据 9/9 绿；真机 `/version`、`/help`、`/resume` 均通过，普通提示仍被拦截 |
| CU-5 | `screenshot scope=window` 按动态标题找不到 Windows Terminal | 窗口列表可见但 `title=JoinCode` 截图失败 | 动态标题不稳定；按进程名匹配时可能命中其他终端窗口 | 暂不改产品代码，记录工具使用限制 | 使用 `scope=screen` 取得确定证据；`scope=window` 的命中不确定性已记录 |

## 自动化验证证据

| 验证项 | 结果 | 日志 |
|---|---|---|
| Hub 空供应商修复定向测试 | 3/3 通过 | `.xxx/logs/hub-empty-vendor-green-20261127.log` |
| Hub 全量测试 | 40/40 通过 | `.xxx/logs/hub-full-green-20261127.log` |
| CLI Hub 运行时回归 | 红：缺 `jcchub.dll`；绿：7/7 | `.xxx/logs/hub-runtime-red-20261127.log`、`.xxx/logs/hub-runtime-green-20261127.log` |
| 登录门本地命令回归 | 红：6 失败；绿：18/18 | `.xxx/logs/login-gate-local-command-red-20261127.log`、`.xxx/logs/login-gate-local-command-green-20261127.log` |
| 命令目录元数据透传 | 9/9 通过 | `.xxx/logs/slash-source-login-availability-green-20261127.log` |
| CLI Debug 重编 | 0 警告、0 错误 | `.xxx/logs/cu-jcc-build-login-availability-20261127.log` |
| 跨域契约全仓编译 | 16 域 + native_tui/native_gui 全部通过 | `.xxx/logs/build-all-domains-login-availability-20261127.log` |
| 最终 CLI 增量构建 | 0 警告、0 错误 | `.xxx/logs/cu-jcc-build-final-20261127.log` |
| NativeTui.Tests 全量 | 3364/3364 | `.xxx/logs/native-tui-full-green-20261127.log` |
| NativeTui.Host.Tests 全量 | 183/183 | `.xxx/logs/native-tui-host-full-green-20261127.log` |
| Composition.Tests 全量 | 1177/1177 | `.xxx/logs/composition-full-green-20261127.log` |
| Hub.Tests 全量 | 40/40 | `.xxx/logs/hub-full-green-20261127.log` |
| Integration.Desktop.Tests 全量 | 10/10 | `.xxx/logs/desktop-integration-full-green-20261127.log` |
| Host.Tests 全量 | 1423/1423 | `.xxx/logs/host-full-green-20261127.log` |
| PR CI 修复后 Host.Tests（CI 同过滤器） | 1419/1419 | `.xxx/logs/pr756-host-full-green-20261127.log` |
| 显式模块顺序脚本真机复验 | 30 秒内完成；段落 49/68、模块 9/9 | `.xxx/logs/pr756-explicit-order-green2-20261127.log` |
| Module order job 超时收紧 | 25 → 15 分钟；子命令 25 → 12 秒并消除管道 EOF 挂死 | `.github/workflows/ci-unit-tests.yml`、`non_deliverables_tools/scripts/Verify-ExplicitOrder.py` |
| 受影响域回归合计 | 6197/6197，0 失败、0 跳过 | 同上六组日志 |

## 四列验收表

| 基建实现 | 消费点 | 已实现 | 已验收 |
|---|---|---|---|
| `Win32WindowManagementService.FocusAsync` 恢复最小化窗口 | 桌面 `focus_window` 工具 | ✅ | ✅ 真实最小化/激活，`.xxx/cu-focus-restored-20261127.png` |
| `Hub.Program.ResolveSettings` 空供应商回退 | Hub 启动链路 | ✅ | ✅ 真实 `/health` 返回 6 个供应商 |
| `JoinCode.csproj/CopyHubRuntimeFiles` | CLI 单项目构建与 Hub 自动拉起 | ✅ | ✅ 真实自动拉起，`.xxx/cu-accept-hub-fixed-20261127.png` |
| `IsAvailableWithoutLogin` 特性→生成目录→TUI 登录门 | `/help`、`/version`、`/session`、`/resume` | ✅ | ✅ 真实未登录命令全部通过；普通 `hello` 仍被登录门拦截，证据 `.xxx/cu-version-screen-20261127.png`、`.xxx/cu-login-gate-final-20261127.png` |

手动 exe 验收规范：[ADR 0080](docs/implemented/adr/engineering_ci_test/0080-manual-exe-testing-guide.md)。
