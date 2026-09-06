# MCP 工具测试计划 01: 桌面控制 DesktopControl

> 第二轮:脚本批量验证 — 快速定位坏点

## 概述

| 项目 | 内容 |
|------|------|
| 计划编号 | 01/12 |
| 涵盖分类 | DesktopControl |
| 工具总数 | 32 个 |
| 测试方式 | 脚本批量调用 `jcc.exe mcp_call` |
| 前置条件 | jcc.exe 已编译(Release) |

## 工具清单

### Category: DesktopControl (32 个)

| # | 工具名 | 说明 | 状态 |
|---|--------|------|------|
| 1 | `screenshot` | 截屏 | ⬜ |
| 2 | `mouse_click` | 鼠标点击 | ⬜ |
| 3 | `mouse_move` | 鼠标移动 | ⬜ |
| 4 | `mouse_drag` | 鼠标拖拽 | ⬜ |
| 5 | `multi_click` | 多次点击 | ⬜ |
| 6 | `right_click_menu` | 右键菜单 | ⬜ |
| 7 | `drag_with_hover` | 拖拽悬停 | ⬜ |
| 8 | `key_press` | 按键 | ⬜ |
| 9 | `type_text` | 输入文本 | ⬜ |
| 10 | `show_desktop_overlay` | 显示桌面叠加层 | ⬜ |
| 11 | `show_desktop_pulse` | 显示桌面脉冲 | ⬜ |
| 12 | `get_environment_state` | 获取环境状态 | ⬜ |
| 13 | `wait_for_idle` | 等待空闲 | ⬜ |
| 14 | `undo_last_action` | 撤销最后操作 | ⬜ |
| 15 | `get_operation_history` | 获取操作历史 | ⬜ |
| 16 | `start_recording` | 开始录制 | ⬜ |
| 17 | `stop_recording` | 停止录制 | ⬜ |
| 18 | `play_macro` | 播放宏 | ⬜ |
| 19 | `list_macros` | 列出宏 | ⬜ |
| 20 | `start_observation` | 开始观察 | ⬜ |
| 21 | `learn_from_observation` | 从观察中学习 | ⬜ |
| 22 | `optimize_steps` | 优化步骤 | ⬜ |
| 23 | `reproduce_from_logic` | 从逻辑复现 | ⬜ |
| 24 | `list_processes` | 列出进程 | ⬜ |
| 25 | `kill_process` | 终止进程 | ⬜ |
| 26 | `start_process` | 启动进程 | ⬜ |
| 27 | `detect_ui_elements` | 检测UI元素 | ⬜ |
| 28 | `find_element` | 查找元素 | ⬜ |
| 29 | `list_windows` | 列出窗口 | ⬜ |
| 30 | `focus_window` | 聚焦窗口 | ⬜ |
| 31 | `move_window` | 移动窗口 | ⬜ |
| 32 | `close_window` | 关闭窗口 | ⬜ |

## 测试脚本

### 批量验证脚本

```powershell
$jcc = "D:\project\w1\artifacts\bin\JoinCode\Release\net10.0\jcc.exe"
$tools = @(
    "screenshot", "mouse_click", "mouse_move", "mouse_drag",
    "multi_click", "right_click_menu", "drag_with_hover", "key_press",
    "type_text", "show_desktop_overlay", "show_desktop_pulse", "get_environment_state",
    "wait_for_idle", "undo_last_action", "get_operation_history", "start_recording",
    "stop_recording", "play_macro", "list_macros", "start_observation",
    "learn_from_observation", "optimize_steps", "reproduce_from_logic", "list_processes",
    "kill_process", "start_process", "detect_ui_elements", "find_element",
    "list_windows", "focus_window", "move_window", "close_window"
)
foreach ($t in $tools) {
    Write-Host "--- Testing: $t ---"
    & $jcc --trust --bypass mcp_call $t --args-file NUL 2>&1 | Select-Object -First 5
}
```

### 单工具深度测试

```powershell
$jcc = "D:\project\w1\artifacts\bin\JoinCode\Release\net10.0\jcc.exe"
# 截屏(无副作用,适合冒烟)
& $jcc --trust --bypass mcp_call screenshot
# 列出窗口(只读操作)
& $jcc --trust --bypass mcp_call list_windows
# 获取环境状态(只读)
& $jcc --trust --bypass mcp_call get_environment_state
```

## 验收标准

- [ ] 每个工具调用返回格式正确的 JSON
- [ ] `Error:false` 且有非空输出(空输出 = 隐患)
- [ ] 无崩溃/超时/死锁(超时返回 1234)
- [ ] 启动参数格式统一(`--trust --bypass mcp_call <tool> [args]`)
- [ ] 只读工具(screenshot/list_windows/get_environment_state/list_processes)可安全重复调用
- [ ] 副作用工具(mouse_click/type_text/key_press)在无参数时给出明确错误提示而非崩溃

## 风险提示

- **DesktopControl 工具操作真实桌面**,测试时注意不要误操作
- `kill_process` / `close_window` 可能影响系统稳定性,建议在虚拟机中测试
- `start_process` 可能启动任意程序,注意安全
- 部分工具依赖 Win32 API,在无桌面环境(如 SSH 会话)可能失败

## 测试结果 (2026-09-06)

### 批量测试汇总

| 状态 | 数量 | 说明 |
|------|------|------|
| OK | 7 | 正常返回有意义的结果 |
| EMPTY | 1 | 空输出(隐患) |
| ERROR | 24 | 缺参数错误(预期行为) |

### 通过的工具 (7个)

| 工具 | 输出摘要 |
|------|----------|
| `get_environment_state` | 光标状态: Normal, 弹窗检测, 撤销栈深度: 0 |
| `wait_for_idle` | 14 chars, 正常返回 |
| `undo_last_action` | 12 chars, 正常返回 |
| `get_operation_history` | 6 chars, 正常返回 |
| `list_macros` | 53 chars, 宏列表 |
| `list_processes` | 1015 chars, 50个进程 |
| `list_windows` | 591 chars, 12个窗口 |

### 坏点

| 工具 | 问题描述 | 根因 | 修复 |
|------|----------|------|------|
| `screenshot` | 空输出(仅"(无文本输出)") | 可能返回二进制base64但mcp_call未正确输出 | 待修复 |
| `detect_ui_elements` | "LLM 未返回有效识别结果" | 依赖LLM API,无API Key时失败 | 需配置API Key后重测 |

### 缺参数工具 (24个) — 预期行为

- `mouse_click` → "Missing required parameter: x" ✅
- `key_press` → "Missing required parameter: virtual_key" ✅
- `kill_process` → "必须提供 pid 或 name" ✅
- 其余21个工具均返回明确的缺参数错误提示,无崩溃 ✅

### 待修复问题

1. **`screenshot` 空输出** — 应返回 base64 PNG 数据,但 mcp_call 输出为空。需检查 screenshot 工具返回值与 mcp_call 输出链路
2. **错误输出格式** — 错误信息输出到 stderr 而非 JSON 格式,需确认是否为设计决策

## 交接说明

> 本计划由第三轮 AI 窗口处理。每次只手动执行一个命令测试,遇到任何不适(崩溃/格式错/空输出/超时)都需要改代码修复,面向笨蛋客户调整。
