# 交接文档 03: Desktop 高级工具

> 开一个AI窗口处理本文档。逐个执行测试命令，遇到任何不适都修复。

## 工具列表（10个）

1. start_recording - 开始录制桌面操作宏
2. stop_recording - 停止录制并保存宏
3. play_macro - 从文件加载宏并回放
4. list_macros - 列出宏文件
5. start_observation - 开始观察用户演示
6. learn_from_observation - 停止观察并抽象操作逻辑
7. reproduce_from_logic - 从逻辑生成操作序列并执行
8. optimize_steps - 分析操作逻辑并优化
9. show_desktop_overlay - 桌面显示高亮框
10. show_desktop_pulse - 桌面显示脉冲圆动画

## 测试命令

### 1. start_recording
```powershell
jcc mcp_schema start_recording
jcc mcp_call start_recording --% "{\"output_file\":\"test-macro.json\"}"
```
预期：开始录制，返回确认

### 2. stop_recording
```powershell
jcc mcp_schema stop_recording
jcc mcp_call stop_recording --% "{}"
```
预期：停止录制，保存宏文件

### 3. play_macro
```powershell
jcc mcp_schema play_macro
jcc mcp_call play_macro --% "{\"macro_file\":\"test-macro.json\",\"speed\":1.0}"
```
预期：回放宏或友好报错"文件不存在"

### 4. list_macros
```powershell
jcc mcp_schema list_macros
jcc mcp_call list_macros --% "{\"directory\":\".\"}"
```
预期：列出当前目录的宏文件

### 5. start_observation
```powershell
jcc mcp_schema start_observation
jcc mcp_call start_observation --% "{}"
```
预期：开始观察模式

### 6. learn_from_observation
```powershell
jcc mcp_schema learn_from_observation
jcc mcp_call learn_from_observation --% "{}"
```
预期：停止观察，返回抽象逻辑

### 7. reproduce_from_logic
```powershell
jcc mcp_schema reproduce_from_logic
jcc mcp_call reproduce_from_logic --% "{\"logic\":\"click start button\"}"
```
预期：从逻辑生成操作并执行

### 8. optimize_steps
```powershell
jcc mcp_schema optimize_steps
jcc mcp_call optimize_steps --% "{\"steps\":[]}"
```
预期：返回优化建议

### 9. show_desktop_overlay
```powershell
jcc mcp_schema show_desktop_overlay
jcc mcp_call show_desktop_overlay --% "{\"x\":100,\"y\":100,\"width\":200,\"height\":200,\"duration_ms\":3000}"
```
预期：桌面显示高亮框3秒

### 10. show_desktop_pulse
```powershell
jcc mcp_schema show_desktop_pulse
jcc mcp_call show_desktop_pulse --% "{\"x\":500,\"y\":500,\"duration_ms\":3000}"
```
预期：桌面显示脉冲圆3秒

## 验收标准

- 录制/回放能正确保存和加载
- 观察模式不卡死
- 桌面标注有超时自动清除
- 超过30s的工具必须备注
