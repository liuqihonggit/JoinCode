# Terminal.Gui v2 CanFocus 陷阱 — 键盘输入无法到达 TextField

## 问题

jcctui.exe 启动后，TextField 渲染正常（背景色变深表示有焦点），但**键盘输入打不进字**。鼠标悬停按钮也卡顿（1秒以上才响应）。

## 根因

**Terminal.Gui v2 中，从 Toplevel（Window）到 TextField 的整个父视图链中，每个中间 View 都必须设置 `CanFocus = true`，否则键盘事件无法传递到 TextField。**

```
Window (CanFocus 隐式 true)
  └─ RootView (View, CanFocus 必须设 true)       ← 断点1
       └─ _promptArea (View, CanFocus 必须设 true)  ← 断点2
            └─ _container (View, CanFocus 必须设 true) ← 断点3
                 └─ TextField (CanFocus 默认 true)
```

任何一个中间 View 的 `CanFocus = false`（View 的默认值），都会**截断键盘事件传递链**，TextField 收不到字符输入。

## 排错过程

### 第1步：怀疑焦点未设置

最初认为 TextField 没获取焦点，用 `app.Iteration` 事件在首次迭代时调 `SetFocus()`。用户反馈"背景色变深了"说明**有焦点**，但打不了字。排除焦点问题。

### 第2步：怀疑 async Main 线程问题

怀疑 `async Task<int> Main` 没有 SynchronizationContext，`app.Run` 在线程池线程而非主线程运行。改为同步 `int Main` + `.GetAwaiter().GetResult()`。用户反馈没区别。排除线程问题。

### 第3步：怀疑 KeyDown 事件订阅干扰

去掉所有 `KeyDown` 事件订阅（root 的 F1-F5 + TextField 的 Enter）。用户反馈没区别。排除 KeyDown 干扰。

### 第4步：最小化控件测试（关键突破）

**逐步剥离组件，用最小复现定位问题：**

| 测试版本 | 结构 | 结果 |
|----------|------|------|
| 裸 TextField | `Window → TextField + Label` | ✅ 能输入 |
| +RootView +PromptView | `Window → RootView → _promptArea → _container → TextField` | ❌ 无法输入 |
| 去掉 RootView | `Window → _container → TextField` | ❌ 无法输入 |
| 去掉 _container | `Window → Label + TextField` | ✅ 能输入 |
| _container CanFocus=true | `Window → container(CanFocus=true) → TextField` | ✅ 能输入 |

**结论：View 作为中间容器时，`CanFocus` 默认 `false` 会截断键盘事件传递。设 `CanFocus = true` 即可修复。**

### 第5步：应用到完整版本

把所有中间 View 设 `CanFocus = true`：
- RootView 自身：`CanFocus = true`
- RootView 的 5 个区域 View：只有 `_promptArea` 需要 `CanFocus = true`（其他区域不需要焦点）
- PromptView 的 `_container`：`CanFocus = true`

**注意：兄弟 View 不需要都设 `CanFocus = true`，只有 TextField 所在的焦点路径需要。设太多会导致焦点在兄弟间跳转而非进入 TextField。**

## 排错方法论

### 最小化控件测试法

1. **先确认最简单的 case 能工作** — 只有 TextField + Label 直接加到 Window
2. **逐步添加嵌套层级** — 每加一层 View 容器就测试一次
3. **二分法定位断点** — 去掉 RootView 测试、去掉 _container 测试，快速缩小范围
4. **每次只改一个变量** — 不要同时改多个属性，否则无法确定哪个修复生效

### 让用户真实测试

- **sandbox 无法测试交互式 TUI** — 无法模拟键盘输入，必须用户在真实终端测试
- **每次编译后让用户验收** — 用 `question` 工具问用户"能打字吗？"
- **不要猜测，用实验证明** — 每个假设都用最小复现验证

## 修复清单

| 文件 | 改动 | 原因 |
|------|------|------|
| `RootView.cs` | `CanFocus = true`（RootView 自身） | 中间层不能截断焦点链 |
| `RootView.cs` | `_promptArea.CanFocus = true` | TextField 所在区域的父容器 |
| `PromptView.cs` | `_container.CanFocus = true` | TextField 的直接父容器 |

## 避坑指南

1. **Terminal.Gui v2 的 View 默认 `CanFocus = false`** — 与 v1 不同，v2 要求显式设置
2. **整条焦点路径都要 `CanFocus = true`** — 不是只设最近的父容器，是从 Window 到 TextField 的每一层
3. **兄弟 View 不需要都设** — 只有 TextField 所在路径需要，其他兄弟设了反而干扰焦点导航
4. **`app.Iteration` 设焦点** — 用 `app.Iteration` 事件在首次迭代时调 `SetFocus()`，因为此时视图已加入可视树
5. **不要用 `async Main`** — 改为同步 `int Main` + `.GetAwaiter().GetResult()`，确保 `app.Run` 在主线程
