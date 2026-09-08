# 交接文档 02: Desktop 交互工具

> 开一个AI窗口处理本文档。逐个执行测试命令，遇到任何不适都修复。

## 工具列表（12个）

1. screenshot - 截取屏幕/窗口/区域
2. detect_ui_elements - 截取屏幕并识别UI元素
3. find_element - 按自然语言查找UI元素
4. mouse_click - 鼠标点击
5. mouse_move - 移动光标
6. mouse_drag - 拖拽
7. multi_click - 多坐标点击
8. key_press - 按键
9. type_text - 输入文本
10. right_click_menu - 右键菜单
11. drag_with_hover - 拖拽悬停
12. wait_for_idle - 等待空闲

## 测试命令

### 1. screenshot
```powershell
jcc mcp_schema screenshot
jcc mcp_call screenshot --% "{\"target\":\"screen\"}"
```
预期：返回 base64 PNG 截图

### 2. detect_ui_elements
```powershell
jcc mcp_schema detect_ui_elements
jcc mcp_call detect_ui_elements --% "{}"
```
预期：返回UI元素列表（类型/坐标/状态）

### 3. find_element
```powershell
jcc mcp_schema find_element
jcc mcp_call find_element --% "{\"description\":\"任务栏开始按钮\"}"
```
预期：返回坐标或友好报错

### 4. mouse_click
```powershell
jcc mcp_schema mouse_click
jcc mcp_call mouse_click --% "{\"x\":100,\"y\":100,\"button\":\"left\"}"
```
预期：在(100,100)左键点击

### 5. mouse_move
```powershell
jcc mcp_schema mouse_move
jcc mcp_call mouse_move --% "{\"x\":500,\"y\":500}"
```
预期：移动光标到(500,500)

### 6. mouse_drag
```powershell
jcc mcp_schema mouse_drag
jcc mcp_call mouse_drag --% "{\"start_x\":100,\"start_y\":100,\"end_x\":200,\"end_y\":200}"
```
预期：从(100,100)拖拽到(200,200)

### 7. multi_click
```powershell
jcc mcp_schema multi_click
jcc mcp_call multi_click --% "{\"points\":[{\"x\":100,\"y\":100},{\"x\":200,\"y\":200}]}"
```
预期：依次点击多个坐标

### 8. key_press
```powershell
jcc mcp_schema key_press
jcc mcp_call key_press --% "{\"key\":\"Enter\"}"
```
预期：按下回车键

### 9. type_text
```powershell
jcc mcp_schema type_text
jcc mcp_call type_text --% "{\"text\":\"你好世界\"}"
```
预期：输入"你好世界"

### 10. right_click_menu
```powershell
jcc mcp_schema right_click_menu
jcc mcp_call right_click_menu --% "{\"x\":100,\"y\":100,\"menu_item\":\"属性\"}"
```
预期：右键点击并选择菜单项

### 11. drag_with_hover
```powershell
jcc mcp_schema drag_with_hover
jcc mcp_call drag_with_hover --% "{\"start_x\":100,\"start_y\":100,\"end_x\":200,\"end_y\":200,\"hover_ms\":500}"
```
预期：拖拽并悬停500ms

### 12. wait_for_idle
```powershell
jcc mcp_schema wait_for_idle
jcc mcp_call wait_for_idle --% "{\"timeout_ms\":5000}"
```
预期：等待桌面空闲或超时

## 验收标准

- 截图工具返回有效 base64
- 坐标参数有边界检查
- 键盘输入支持 Unicode
- 超过30s的工具必须备注
