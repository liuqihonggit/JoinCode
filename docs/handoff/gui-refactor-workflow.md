# GUI 重构与调试交接文档

## 一、工作流程（截图→分析→修改→编译→验证循环）

### 核心循环

```
用户反馈 → 截图 → analyzeImage 分析 → 修改代码 → 编译 → 截图验证 → 提交 → ask_user 确认
```

### 1. 截图脚本

脚本位置：`.xxx/launch_and_capture.ps1`

功能：启动 GUI exe → 前置窗口（HWND_TOPMOST）→ 截图保存到 `.xxx/gui_refactored.png`

```powershell
powershell -File "D:\project\w2\.xxx\launch_and_capture.ps1"
```

关键点：
- **必须先杀掉旧进程**（DLL 锁）：`Get-Process -Name 'JoinCode.Gui' -ErrorAction SilentlyContinue | Stop-Process -Force`
- 脚本用 `SetWindowPos(HWND_TOPMOST)` 前置窗口，避免其他窗口遮挡导致截图错误
- 截图保存后用 `analyzeImage` 工具分析，不要用肉眼判断

### 2. 编译命令

```bash
# 杀进程（必须）
powershell -Command "Get-Process -Name 'JoinCode.Gui' -ErrorAction SilentlyContinue | Stop-Process -Force"

# 增量编译（3-7秒）
dotnet build app/gui/JoinCodeGui.csproj -c Debug

# 全量重建（新增 Register 类时必须，2-3分钟）
dotnet build app/gui/JoinCodeGui.csproj -c Debug --no-incremental
```

### 3. 截图分析

```python
# 调用 analyzeImage 工具，传入具体的问题清单
analyzeImage(
    imagePath=["D:\\project\\w2\\.xxx\\gui_refactored.png"],
    task="分析底部输入栏区域：1)xxx 2)xxx 3)xxx 逐一描述实际情况"
)
```

**要点**：问题必须具体、可检查，逐条列出。不要问"看起来怎么样"这种模糊问题。

---

## 二、项目架构与关键文件

### GUI 项目结构

```
app/gui/
├── JoinCodeGui.csproj              # 项目文件
├── views/                          # XAML 视图
│   ├── InputBarView.axaml          # 底部输入栏（主要重构对象）
│   ├── MainWindow.axaml            # 主窗口布局
│   ├── StatusBarView.axaml         # 状态栏（已从主窗口移除）
│   ├── SidebarView.axaml           # 左侧会话列表
│   └── core/
│       ├── InputBarView.axaml.cs   # 输入栏 code-behind
│       └── MainWindow.axaml.cs     # 主窗口 code-behind
├── view_models/                    # ViewModel（partial class 拆分）
│   └── main_view_model/
│       ├── core/                   # 核心：Lifecycle/Preferences/Theme
│       ├── panels/                 # 布局：Layout/Panel/Editor
│       ├── features/               # 功能：Interceptors/ModelConfig/Tools
│       └── messages/               # 消息：Send/Messages/Export
├── persistence/                    # 持久化
│   ├── GuiPreferences.cs           # GUI 偏好数据模型
│   └── GuiPreferencesStore.cs      # 读写 gui-preferences.json
└── theming/                        # 全局样式
    └── GuiControlStyles.axaml      # TextBox/Button 等全局样式
```

### 关键文件说明

| 文件 | 作用 | 重构时注意 |
|------|------|-----------|
| `InputBarView.axaml` | 底部输入栏布局 | QQ 风格三行：状态条/工具栏/输入框/发送区 |
| `MainWindow.axaml` | 主窗口四行布局 | RowDefinitions="*,Auto,Auto"（消息区/补全/输入栏） |
| `GuiControlStyles.axaml` | 全局控件样式 | TextBox:focus 蓝色边框在此定义，局部覆盖用 class |
| `GuiPreferences.cs` | 偏好数据模型 | 加新字段后 SavePreferencesAsync 要保留（先 Load 再更新） |
| `MainViewModel.Layout.cs` | 布局属性 | InputAreaHeight/IsStatusLogExpanded/StatusLogEntries 在此 |

---

## 三、调试技巧

### 1. 蓝色聚焦边框问题

**现象**：TextBox 聚焦时出现蓝色边框，用户不想要。

**根因**：全局样式 `GuiControlStyles.axaml` 定义了：
```xml
<Style Selector="TextBox:focus /template/ Border#PART_BorderElement">
  <Setter Property="BorderBrush" Value="{DynamicResource GuiAccentText}" />
</Style>
```

**解决**：在 InputBarView 局部样式覆盖，给 TextBox 加 class：
```xml
<Style Selector="TextBox.noFocusRing:focus /template/ Border#PART_BorderElement">
  <Setter Property="BorderBrush" Value="Transparent" />
</Style>
```
```xml
<TextBox Classes="noFocusRing" ... />
```

### 2. Avalonia 样式选择器

- `Border.composer:focus-within` — Border 内有控件聚焦时触发
- `TextBox.noFocusRing:focus /template/ Border#PART_BorderElement` — 穿透模板修改内部 Border
- 局部样式（UserControl.Styles）优先级高于全局样式（GuiControlStyles）

### 3. PointerCapture 拖拽

**问题**：拖拽手柄 Height=6，鼠标移出手柄后 Moved 事件不触发。

**解决**：
1. XAML 中手柄只注册 `PointerPressed`
2. code-behind 构造函数里给 `InputBarRoot` 加 `PointerMoved`/`PointerReleased`
3. `PointerPressed` 里 `e.Pointer.Capture(InputBarRoot)` — capture 到根控件
4. 这样鼠标在整个窗口范围内移动都能收到事件

```csharp
// 构造函数
InputBarRoot.PointerMoved += OnInputResizePointerMoved;
InputBarRoot.PointerReleased += OnInputResizePointerReleased;

// Pressed
e.Pointer.Capture(InputBarRoot);

// Moved（检查标志位）
if (!_isResizingInput) return;
var delta = _resizeStartY - e.GetPosition(null).Y;  // 向上拖 = 正 = 增大
```

### 4. Popup 对齐状态条宽度

```xml
<Popup Width="{Binding #StatusStrip.Bounds.Width}"
       Placement="Top" PlacementTarget="{Binding #StatusStrip}" />
```

### 5. 窗口尺寸持久化

- `GuiPreferences.cs` 加 `WindowWidth/WindowHeight/WindowX/WindowY`
- `MainWindow.axaml.cs` 构造函数调 `RestoreWindowBounds()`
- `OnWindowClosed` 调 `SaveWindowBounds()`
- `SavePreferencesAsync` 改为**先 Load 再更新**，避免覆盖窗口尺寸字段为 0

---

## 四、本次重构完成的内容

| 序号 | 内容 | commit |
|------|------|--------|
| 1 | InputBarView 改为 QQ 风格三行布局 | ef50ad6bc |
| 2 | 工具栏全纯图标（🟢🎭Ⓘ🧠⚡） | c41946762 |
| 3 | 快捷按钮改为 ⚡ MenuFlyout 二次展开 | c1108b697 |
| 4 | 权限模式图标随模式变化 + MenuFlyout | fd8475702 |
| 5 | 模型选择器图标改为 🎭 | 895f0a32e |
| 6 | 模型 Popup 空白修复（fallback 读 settings.json） | 1b35dadd4 |
| 7 | 去掉二次包裹圆角 + 移除底部状态栏 + 输入框可拖拽调高 | 1243f9b66 |
| 8 | 拖拽手柄移到输入区最顶部 + 修复 PointerCapture | df3faf1ee |
| 9 | 去掉输入框聚焦蓝色边框 | 95fc626f0 |
| 10 | 悬浮系统日志面板（▲向上展开+可复制+对齐状态条） | 4dc33ded6 |
| 11 | GUI 默认尺寸改小 + 窗口尺寸/位置持久化 | a6040549d |

---

## 五、注意事项

### 禁止行为

1. **禁止删除文件** — 用移动到 `.xxx/` 代替（AGENTS.md 红线）
2. **禁止在 bash 中用 `2>nul`** — 用 `2>/dev/null`（git bash 中 nul 是普通文件名）
3. **禁止用 PowerShell 替换源码文件** — 编码问题（BOM/UTF-8/UTF-16 混乱），用 Python 脚本
4. **禁止 `git commit` 不带 `-m`** — 会打开编辑器卡住
5. **禁止跳过编译验证直接提交** — 必须编译通过 + 0 警告 0 错误

### 编译前必须

```powershell
Get-Process -Name 'JoinCode.Gui' -ErrorAction SilentlyContinue | Stop-Process -Force
```
GUI 进程锁 DLL，不杀掉编译会失败。

### Avalonia 常见坑

| 坑 | 解决 |
|----|------|
| WrapPanel 没有 Spacing | 用 StackPanel Orientation="Horizontal" |
| 空 catch 块 | 分析器 JCC3013 禁止，加具体异常处理 |
| if 嵌套超 2 层 | 分析器 JCC1009 禁止，用卫语句扁平化 |
| `Background="Transparent"` | Avalonia 中接收 hit test（不同于 null） |
| `Background="null"` | 不接收 hit test，PointerPressed 不触发 |
| TextBox MinLines | Avalonia 无此属性，用 MinHeight/Height 控制 |

### 截图脚本前置窗口

脚本已用 `SetWindowPos(HWND_TOPMOST)` 前置。如果仍有遮挡，检查：
1. 脚本里 `SW_RESTORE` → `HWND_TOPMOST` → `SetForegroundWindow` 三步顺序
2. 启动后等待 6 秒（`Start-Sleep -Seconds 6`）让窗口加载完成
3. 截图前再等 1 秒（`Start-Sleep -Milliseconds 1000`）让前置生效

---

## 六、后续可扩展方向

1. **日志数据源丰富** — 当前只在权限/模型切换时追加日志，可在更多操作处加 `AddStatusLog()`
2. **输入框高度持久化** — InputAreaHeight 保存到 gui-preferences.json
3. **日志面板宽度自适应** — 当前绑定状态条宽度，可改为可拖拽调节
4. **状态条 MarqueeText 滚动** — 当前静态显示，可加滚动动画（StatusBarView 有 MarqueeTextBlock 组件可复用）
