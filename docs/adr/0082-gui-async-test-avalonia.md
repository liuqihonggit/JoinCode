# 0082. GUI 异步 UI 测试与启动 exe 测试

- 状态：accepted
- 日期：2026-09-08
- 决策者：项目架构组

## 背景

Avalonia + CommunityToolkit.Mvvm 的异步命令在 xUnit 单线程上下文下容易死锁，需要专门的测试模板。GUI 桌面应用无控制台，崩溃诊断需要特殊处理。启动 exe 测试需要统一等待方式。

## 详细内容

### GUI / 异步 UI 测试（Avalonia + CommunityToolkit.Mvvm 适用）

- `[RelayCommand]` 生成的 `AsyncRelayCommand` **默认 `AllowConcurrentExecutions=false`**（命令运行中 `CanExecute` 返回 false、UI 自动禁用）。**不要再为"是否被 IsBusy 拦截"写并发测试**——那是框架内置能力，测它=减法思维的冗余测试，且在单线程上下文极难写好。
- **异步命令在 xUnit 单线程 `AsyncTestSyncContext` 下直接 `await` 会死锁**（命令续体被 post 回单线程队列，而测试方法正等命令完成，互等）。解法：命令调用包 `Task.Run(...)` + 对返回任务 `.WaitAsync(TimeSpan.FromSeconds(5))` 硬超时兜底，任何情况下 5 秒内必结束测试。
- 测试标配模板：
  ```csharp
  var vm = new MainViewModel();
  vm.InputText = "hello";
  await Task.Run(() => vm.SendCommand.ExecuteAsync(null)).WaitAsync(Timeout);
  ```
- 分析器铁律：`JCC5002` — 循环内禁止 `+=` 拼字符串，流式追加用 `StringBuilder`。
- 分析器铁律：`JCC9006` — `FileStream` 构造必须用 `FileShare.ReadWrite`（避免跨进程读写冲突），`PhysicalFileSystem`/`SafeFileIO` 已豁免。
- 命令本身不检查 `CanExecute` 就执行命令体（`execute(parameter)` 无条件调用）——So 若需在命令内拦截"运行中"，应在命令体开头显式 `if (IsBusy) return;`。

#### Avalonia XAML 专属坑（2026-08-06 实战）

| 坑 | 错误写法 | 正确写法 |
|----|----------|----------|
| **引用资源** | `<StaticResource x:Key="Foo" />`（会导致运行时 `StaticResourceExtension.ResourceKey must be set` 崩溃） | 绑定处直接用 `{StaticResource Foo}`（App.axaml 注册后全树可见） |
| **ToolTip** | `ToolTip="..."`（AVLN2000） | `ToolTip.Tip="..."`（附加属性） |
| **StackPanel Padding** | `Padding="12,14"`（AVLN2000，StackPanel 无 Padding） | 用 `Margin` 或外包 `Border Padding` |
| **DataTemplate 绑定** | 无 `x:DataType`（AVLN2000 无法解析属性） | `<DataTemplate x:DataType="vm:ChatUiMessage">` |
| **ThemeVariant** | `Avalonia.Themes.Fluent.ThemeVariant`（不存在） | `Avalonia.Styling.ThemeVariant.Dark/Light`，赋给 `RequestedThemeVariant` |
| **转义字符** | XAML 属性里直接用 `<` `>` | 用 `&lt;` `&gt;` 或 `StringFormat` 单引号包裹 |
| **ScrollChanged 首帧 NRE** | `ScrollChanged` 在窗口首次布局时先于 code-behind 命名字段赋值触发（`BackToBottomButton` 为 null，报 0xC0000005） | handler 内判空 `if (BackToBottomButton is not null)` |

**GUI 崩溃诊断**（Avalonia 桌面无控制台，CLI 的 `--await`/stderr 方案不适用）：
- 进程退出码 `-532462766` = `0xE0434352` = .NET CLR 未处理异常
- 在 `App.OnFrameworkInitializationCompleted` 挂 `AppDomain.CurrentDomain.UnhandledException` + `TaskScheduler.UnobservedTaskException`，异常写 `dumps/crash_*.log`（BaseDirectory 下），冒烟崩溃后读该文件定位
- 注意：冒烟启动后可能弹出"是否调试"对话框卡死进程，用 `Start-Process -PassThru` + 定时 `Stop-Process` 兜底

### 启动 exe 测试

当用户要求启动 exe 进行测试时，使用 `Start-Process -Wait` 等待程序结束：

```powershell
Start-Process -FilePath "{当前项目}\artifacts\bin\JoinCode\Release\net10.0\jcc.exe" -ArgumentList "<args>" -Wait
```

示例：
- 启动主程序：`Start-Process -FilePath "{当前项目}\artifacts\bin\JoinCode\Release\net10.0\jcc.exe" -Wait`
- 带参数启动：`Start-Process -FilePath "{当前项目}\artifacts\bin\JoinCode\Release\net10.0\jcc.exe" -ArgumentList "/reset-config" -Wait`

## 替代方案

无。`Task.Run` + `WaitAsync` 硬超时是 xUnit 单线程上下文下唯一可靠的异步命令测试方式。
