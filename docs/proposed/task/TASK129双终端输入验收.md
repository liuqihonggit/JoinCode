# TASK129 — #52 Windows Terminal / conhost 输入验收

状态：采样入口与输出用例已编译通过（`NativeTui.Host.Tests` 4/4 绿，重定向守卫真机返回 1）；采集卡与空白判定表已就绪（§3），矩阵真机采样待人工执行。来源：[TASK121](TASK121-DSG042-P3输入泵默认与退役复核.md)、[TASK122](TASK122-未完成事项交接-专项与人工验收.md)。

## 1. 先采样，再验 REPL

1. 从 Win+R 启动 `wt.exe -w new powershell.exe -NoProfile`；另在经典 **Windows Console Host** 中启动 PowerShell（显式 `conhost.exe powershell.exe -NoProfile`，或在系统设置中选 Windows Console Host 后新开）。用可见窗口确认，不能仅凭 `WT_SESSION` 或存在 conhost 进程。
2. 两窗使用同一个新产物。PowerShell 中由仓库根运行 `Get-FileHash .\artifacts\bin\JoinCode\Debug\net10.0\jcc.exe`，再执行 `& .\artifacts\bin\JoinCode\Debug\net10.0\jcc.exe --probe-terminal-input`。不要通过管道运行，不用 `JCC_FORCE_TTY`，只输入测试字符。`Ctrl+Q` 结束，超过 3 分钟也会对称还原终端模式并退出。`wt-host` 和 `wt-session` 只是辅助证据。
3. 生产 REPL 以同一可执行文件无参数启动。用默认键位、非 Vim 模式，等 `/` 能弹出建议再测试，避免把启动早期输入缓冲当作 Raw 链证据。编辑测试不需要模型；历史和转录可提交本地 `/help`、`/version` 等命令，不需要真实密钥。要验实际模型回合，使用 [ADR 0077](../../implemented/adr/engineering_ci_test/0077-mockserver-jcc-joint-testing.md) 的本地 MockServer，使用临时应用数据目录和显式本地 endpoint，禁止用真实凭据。

| 检查项 | 输入采样应看到 | REPL 实际操作与期望 | Windows Terminal | conhost |
|---|---|---|---|---|
| ASCII、中文 IME、编辑 | `Char`，拼音候选确认后有 `U+4E2D`、`U+6587` | 输入 `ab中文cd`，按左两次、退格、输入 `X`、Delete 后为 `ab中Xd` | 待测 | 待测 |
| 方向键、Tab、Enter | 四方向分别上报；Tab/Return 不混淆 | `/` 下上下移动建议，Tab 只填入；Enter 才提交。空草稿上下翻阅已提交历史 | 待测 | 待测 |
| Ctrl 组合和 Esc | `Ctrl+←/→` 保留 Ctrl；Ctrl+U、Ctrl+_ 可区分；Esc 不产生残片 | `one two` 做词跳转，Ctrl+A/E 到两端，Ctrl+U/Y 剪切/恢复；Ctrl+R 搜索、Ctrl+O 切转录；草稿双 Esc 清空 | 待测 | 待测 |
| Shift+Tab | `BackTab`，`shift=True` | 无模态时连按两次，权限回执/界面档位变化，不插入 Tab | 待测 | 待测 |
| Home / End / PgUp / PgDn | 四个独立事件 | 单行 `abcd` 验首尾定位，无误提交 | 待测 | 待测 |
| F1–F12 | 每个 F 键独立记录；若终端截获，写明按键 | 默认无可见绑定，不应插字；无变化不能作为消费通过 | 待测 | 待测 |
| Bracketed paste | 用终端粘贴四行含中文、无尾换行，得到单个 `Paste` 和 UTF-16 长度，不回显剪贴板正文 | 显示 `[Pasted text #N +3 lines]`，无误提交或控制序列残片 | 待测 | 待测 |
| 左键、拖动、滚轮 | `Press/Release`、`Drag`、`WheelUp/Down` 和坐标；默认鼠标追踪打开 | 点击提示词详情入口；`Ctrl+O` 打开长转录，用滚轮移动应用内视图。QuickEdit 和系统滚动条移动不是应用消费 | 待测 | 待测 |
| Resize + 连续键 | 至少两次尺寸变化及连续序列，计数和顺序一致 | 留草稿 `前-0123456789-后`，缩窄再恢复，前后各追加编号、定位修改一字；无吞键、重字、错列 | 待测 | 待测 |
| 焦点与扩展键盘协议 | 本探针不主动开启焦点或 kitty 协议，不用它替代此项 | 在忙碌生产回合切出/切回；保留焦点变化证据。若宿主不发送可区分的 Shift+Enter 等序列，单记未上报，不算通过 | 待测 | 待测 |

每格记录 `应用已消费`、`仅解码通过`、`宿主截获/未上报` 或 `未测`，附窗口身份、产物哈希、按键及最小诊断输出。两端结果回填 TASK066 B6 与 TASK121，不能用 Demo（默认 Win32 回退泵）替代生产 Windows 的 Raw 路径。

## 3. 一次性采集卡（AI 生成，人工执行）

> 目的：让 §1 的 10×2 矩阵每格都有"窗口身份 + 产物哈希 + 按键 → 采样输出 → REPL 行为"的可复核证据。
> 纪律：探针只采样、不判分；矩阵结论只由人按实测填写，**未测就写未测**，禁止用"探针能跑"代替"应用已消费"。

### 3.0 产物基线

本轮 Debug 产物（2026-11-25 构建）SHA-256：`1E2D021C5B0185D9C91762AD2CCC0EB5094EFD5F8B53FE844E75E7F972B0940C`。
执行时以窗口内 `Get-FileHash` 的实测值为准；两窗必须是同一哈希，否则作废重来。

### 3.1 开窗与登记（每个窗口各做一次）

```powershell
# 在仓库根执行；wt 窗口用 wt，conhost 窗口把 $out 改成 .xxx\logs\b1\conhost
$exe = ".\artifacts\bin\JoinCode\Debug\net10.0\jcc.exe"
$out = ".xxx\logs\b1\wt"
New-Item -ItemType Directory -Force $out | Out-Null
(Get-FileHash $exe -Algorithm SHA256).Hash | Set-Content "$out\exe-sha256.txt"
"wt_session=[$env:WT_SESSION] host=$((Get-Process -Id $PID).Path)" | Set-Content "$out\window.txt"
```

- WT：Win+R 运行 `wt.exe -w new powershell.exe -NoProfile`，窗口内 `$env:WT_SESSION` 非空。
- conhost：从资源管理器或 Win+R 运行 `conhost.exe powershell.exe -NoProfile`（**不要**从 WT 里再开，否则会继承 `WT_SESSION`），窗口内 `$env:WT_SESSION` 应为空；同时用可见窗口确认经典滚动条/标题栏形态。

### 3.2 输入采样（每窗一次，覆盖下表 10 行）

```powershell
& $exe --probe-terminal-input    # 不接管道、不设 JCC_FORCE_TTY；Ctrl+Q 结束，3 分钟自动退出
```

按 §1 表逐行输入：ASCII/中文 IME、四方向与 Tab/Enter、Ctrl 组合与 Esc、Shift+Tab、Home/End/PgUp/PgDn、F1–F12、四行中文粘贴、左键/拖动/滚轮、两次 resize、忙时切出切回。每行至少产生一次可辨认的 `[n] KeyKind ...` 行。

证据归档（`$out` 下）：

- `probe.txt`：把探针 scrollback 全选复制保存（WT 选区后 Ctrl+Shift+C；conhost 用「标记→复制」），必须包含开头 `[terminal-input] wt-host=...` 与服务结束行。
- `probe-*.png`：关键行（中文 IME、Shift+Tab、F 键、paste、鼠标、resize）各一张截图；文本复制不完整时以截图为准。
- 探针拒绝 stdout 重定向，**不要**用 `| Tee-Object` / `> file` 采集，否则守卫直接 exit=1。

### 3.3 生产 REPL（同窗同产物）

```powershell
& $exe      # 无参数进入生产 REPL；编辑类操作不需要模型
```

按 §1 表右列逐项操作；`/help`、`/version` 等本地命令不需要密钥。需要真实回合的"忙时焦点切换"一格，按 [ADR 0077](../../implemented/adr/engineering_ci_test/0077-mockserver-jcc-joint-testing.md) 起本地 MockServer + 临时应用数据目录，**禁止使用真实凭据**；没有 MockServer 条件就记「未测」。

### 3.4 每窗判定卡（复制后逐格填写）

| 检查项 | 采样实测（贴 `[n]` 行） | REPL 实测（贴行为） | 判定 | 证据文件 |
|---|---|---|---|---|
| ASCII、中文 IME、编辑 | | | 已消费 / 仅解码 / 宿主截获 / 未测 | |
| 方向键、Tab、Enter | | | | |
| Ctrl 组合和 Esc | | | | |
| Shift+Tab | | | | |
| Home / End / PgUp / PgDn | | | | |
| F1–F12 | | | | |
| Bracketed paste | | | | |
| 左键、拖动、滚轮 | | | | |
| Resize + 连续键 | | | | |
| 焦点与扩展键盘协议 | | | | |

判定口径：`已消费` = 应用行为按期望变化；`仅解码` = 采样有事件但应用无对应行为；`宿主截获` = 终端自己处理了该键（写明按键与宿主行为）；`未测` = 没有窗口证据。两窗填完后把 20 格结论合并回 §1 表与 TASK066 B6、TASK121。

### 3.5 自动化采样记录（2026-11-25，AI 执行，非人工判定）

> 本节只记录已经采到的原始事实，**不替代 §3.4 的人工判定卡**。

- 产物基线：`jcc.exe` SHA-256 `3305FB99E73CEA2044D24DD7B58FFB917B42C37BB7DBAD01C32709DD56C94787`（2026-11-25 构建，§3.0 的旧 hash 已失效）。
- WT：`WT_SESSION` 非空（`40cbbfb4-...`），探针 53 事件；`probe.txt` 完整，含 F11 被 WT 截获全屏并触发 `[resize] cols=209 rows=56`；鼠标按下/释放/滚轮与 Alt-Tab 回切均有事件。证据目录 `.xxx/logs/b1/wt/`。
- conhost：`WT_SESSION` 为空，探针 83 事件并有截图（含 `[resize] cols=190 rows=63`、鼠标拖动），但 `probe.txt` 未取得（`AttachConsole` dump 失败、UIA TextPattern 不支持 `ConsoleWindowClass`）。证据目录 `.xxx/logs/b1/conhost/`。
- 结论：WT 行可供人工判定；conhost 行只能算部分采样，判定卡中按“未测/待补采样”处理。真实 IME、生产 REPL 行为、忙时真实模型回合均未自动覆盖。

## 2. 已有验证和边界

- Git Bash 下对已有 Debug 产物运行 `printf 'ab\025\033[D\033[1;5D\033[200~中文粘贴内容\033[201~\037你\r' | ./artifacts/bin/JoinCode/Debug/net10.0/jcc.exe --selftest-input`，返回 `INPUT SELFTEST OK`。此前直接运行未喂 stdin，预期收 9、实际 0，补齐输入后通过。产物 SHA-256：`7f8849c41523dbc66013994202a6e535366b0b41c00a8a17c0351ccac6263eb0`。这是已有二进制的管道测试，不含本次新探针，也不证明双终端实测。
- `TerminalInputProbeTests` 已加码点/修饰、粘贴脱敏、鼠标载荷修饰断言，**已编译并运行 4/4 绿**（`native_tui/tests/NativeTui.Host.Tests`）。`--probe-terminal-input` 的重定向守卫真机验证：`echo hi | jcc --probe-terminal-input` → `exit=1` 且 stderr 给出「需要 Windows 交互终端…请用 --selftest-input」。同一产物 `--selftest-input` 按上文原始字节流仍返回 `INPUT SELFTEST OK`（新增入口未改动既有泵）。真实双终端矩阵全部保持待测。