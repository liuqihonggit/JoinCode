# 窗口震动通知与子代理 bot 中文名 — 实现设计

> ADR: [0109](../adr/0109-window-shake-notification-and-bot-name.md)
> 日期: 2026-09-16
> 方案: 改造为主（方案 A）

## 一、实现顺序（按依赖关系）

```
① BotNameGenerator（无依赖）
  ↓
② 配置开关（SettingsJson 改造）
  ↓
③ ShakeAnimationHelper（从 PermissionDialog 提取）
  ↓
④ FlashWindowEx P/Invoke（User32NativeMethods 改造）
  ↓
⑤ WindowShakeCoordinator（依赖 ②③④）
  ↓
⑥ ShakeWindowToolHandlers MCP 工具（依赖 ②⑤）
  ↓
⑦ ContextSetupMiddleware 改造（依赖 ①）
  ↓
⑧ TeamManager 聊天室语义（依赖 ⑥）
```

## 二、组件详细设计

### ① BotNameGenerator — 随机中文名生成器

**位置**: `lib/abstractions/abs_core/core_utils/core/BotNameGenerator.cs`

**职责**: 生成 `bot{中文名}` 格式的随机显示名，进程内去重

**设计**:
```csharp
public static class BotNameGenerator
{
    // 30+ 个中文小名
    private static readonly string[] s_chineseNames = {
        "小明", "小红", "小虎", "小薇", "阿杰", "阿宝",
        "小雪", "小雷", "小风", "小云", "小龙", "小凤",
        "阿伟", "阿芳", "小刚", "小娟", "小强", "小燕",
        "阿明", "阿华", "小亮", "小玲", "小波", "小静",
        "阿军", "阿丽", "小涛", "小敏", "小鹏", "小霞",
        "阿超", "阿秀", "小宇", "小婷", "小辉", "小悦"
    };

    // 进程内已用名称（去重）
    private static readonly ConcurrentDictionary<string, byte> s_usedNames = new();
    private static readonly Random s_random = new();

    public static string Generate()
    {
        // 随机选择 + 去重，最多尝试 36 次
        for (int i = 0; i < s_chineseNames.Length; i++)
        {
            var name = s_chineseNames[s_random.Next(s_chineseNames.Length)];
            var botName = $"bot{name}";
            if (s_usedNames.TryAdd(botName, 0))
                return botName;
        }
        // 全部冲突（极端情况），加数字后缀
        var fallback = $"bot{s_random.Next(1000, 9999)}";
        s_usedNames.TryAdd(fallback, 0);
        return fallback;
    }

    public static void Release(string name) => s_usedNames.TryRemove(name, out _);
}
```

**测试**: 
- `Generate()` 返回 `bot` 前缀
- 多次调用不重名
- `Release()` 后名称可复用

### ② 配置开关 — SettingsJson 改造

**位置**: `lib/guard/configuration/configuration2/core/mapping/SettingsJson.cs`

**改动**: 在 `CurrentSettings` 类中添加两个 bool 属性

```csharp
// 在 CurrentSettings 类中添加
/// <summary>窗口震动通知开关（默认 true）</summary>
public bool WindowShakeEnabled { get; set; } = true;

/// <summary>聊天室模式开关（默认 true）</summary>
public bool ChatRoomEnabled { get; set; } = true;
```

**热重载**: 自动获得（`IConfigChangeNotifier` 已监控 settings.json）

### ③ ShakeAnimationHelper — 震动动画公共类

**位置**: `app/gui/views/core/ShakeAnimationHelper.cs`

**职责**: 从 `PermissionDialog.StartShakeAnimation` 提取为公共静态类，可对任意 Avalonia `Window` 震动

**设计**:
```csharp
public static class ShakeAnimationHelper
{
    private static readonly int[] s_offsets = { -8, 8, -6, 6, -4, 4, -2, 2, 0 };

    public static void Shake(Window window, CancellationToken cancellationToken = default)
    {
        var transform = new TranslateTransform();
        window.RenderTransform = transform;
        var index = 0;
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
        timer.Tick += (_, _) =>
        {
            if (cancellationToken.IsCancellationRequested || index >= s_offsets.Length)
            {
                timer.Stop();
                transform.X = 0;
                return;
            }
            transform.X = s_offsets[index];
            index++;
        };
        timer.Start();
    }
}
```

**改造 `PermissionDialog`**: `StartShakeAnimation` 改为调用 `ShakeAnimationHelper.Shake(this)`

### ④ FlashWindowEx P/Invoke

**位置**: `kit/hands/desktop/native/User32NativeMethods.cs`

**改动**: 添加 `FlashWindowEx` + `FLASHWINFO` 结构

```csharp
[StructLayout(LayoutKind.Sequential)]
public struct FLASHWINFO
{
    public uint cbSize;
    public IntPtr hwnd;
    public uint dwFlags;
    public uint uCount;
    public uint dwTimeout;
}

// FlashWindowEx 标志
public const uint FLASHW_ALL = 0x00000003;
public const uint FLASHW_TIMERNOFG = 0x0000000C;

[DllImport("user32.dll")]
[return: MarshalAs(UnmanagedType.Bool)]
public static extern bool FlashWindowEx(ref FLASHWINFO pwfi);
```

### ⑤ WindowShakeCoordinator — 1s 去抖协调器

**位置**: `kit/hands/desktop/services/WindowShakeCoordinator.cs`

**职责**: 进程内单例，1s 去抖合并多个子代理的震动请求

**设计**:
```csharp
[Register(typeof(IWindowShakeCoordinator), ServiceLifetime.Singleton)]
public sealed class WindowShakeCoordinator : IWindowShakeCoordinator
{
    private volatile int _lastShakeTick;  // Environment.TickCount（Interlocked 原子）
    private const int ShakeIntervalMs = 1000;

    public bool TryAcquireShakeSlot()
    {
        var now = Environment.TickCount;
        var last = _lastShakeTick;
        if (now - last < ShakeIntervalMs) return false;  // 1s 内已震动，跳过
        return Interlocked.CompareExchange(ref _lastShakeTick, now, last) == last;
    }

    public async Task ShakeAsync(bool flashTaskbar, CancellationToken ct)
    {
        if (!TryAcquireShakeSlot()) return;
        await Task.Delay(1000, ct).ConfigureAwait(false);  // 延迟 1s 再震动
        // 实际震动逻辑（GUI 路径 / CLI 路径 / 任务栏闪烁）
    }
}
```

**接口**: `lib/abstractions/abs_hands/desktop/IWindowShakeCoordinator.cs`

### ⑥ ShakeWindowToolHandlers — MCP 工具

**位置**: `kit/mcp/communication/ShakeWindowToolHandlers.cs`

**工具**:
- `shake_window` — 震动当前进程窗口（GUI/CLI 双路径）
- `flash_taskbar` — 任务栏闪烁
- `get_process_info` — 返回电脑名+PID（供 AI 回答用户）

**设计**:
```csharp
[McpToolDispatch(ToolCategory.Notification, Optional = true)]
public partial class ShakeWindowToolHandlers
{
    private readonly IWindowShakeCoordinator _coordinator;
    private readonly ISettingsProvider _settings;

    [McpTool("shake_window", "震动当前进程窗口以提醒用户", "notification")]
    public async Task<ToolResult> ShakeWindowAsync(
        [McpToolParameter("震动原因(可选)", Required = false)] string? reason = null,
        CancellationToken ct = default)
    {
        if (!_settings.Current.WindowShakeEnabled)
            return ToolResultBuilder.Success().WithText("窗口震动已关闭").Build();

        await _coordinator.ShakeAsync(flashTaskbar: false, ct);
        var pid = Environment.ProcessId;
        var machine = Environment.MachineName;
        return ToolResultBuilder.Success()
            .WithText($"已震动窗口（电脑:{machine}, PID:{pid}）")
            .Build();
    }

    [McpTool("get_process_info", "获取当前进程信息(电脑名+PID)", "notification")]
    public Task<ToolResult> GetProcessInfoAsync(CancellationToken ct = default)
    {
        return Task.FromResult(ToolResultBuilder.Success()
            .WithText($"电脑名:{Environment.MachineName}, 进程PID:{Environment.ProcessId}")
            .Build());
    }
}
```

### ⑦ ContextSetupMiddleware 改造 — bot 前缀命名

**位置**: `llm/agents/Services/Spawn/Unified/ContextSetupMiddleware.cs`

**改动**: 第 62 行附近，`DisplayName` 为空时调用 `BotNameGenerator.Generate()`

```csharp
// 改前
DisplayName = context.SpawnOptions.Name ?? context.SpawnOptions.Description,

// 改后
DisplayName = context.SpawnOptions.Name
    ?? context.SpawnOptions.Description
    ?? BotNameGenerator.Generate(),
```

### ⑧ TeamManager 聊天室语义

**位置**: `llm/agents/Coordinator/Team/core/TeamManager.cs`

**改动**: 添加 `GetChatRoomInfoAsync()` 便捷方法（轻量，复用现有团队数据）

```csharp
public ChatRoomInfo GetChatRoomInfo(string teamId)
{
    var team = GetTeam(teamId);
    return new ChatRoomInfo
    {
        RoomName = team.TeamName,
        Members = team.Members.Select(m => m.DisplayName).ToList(),
        MemberCount = team.Members.Count,
    };
}
```

**新模型**: `lib/abstractions/abs_core/models/models_agent/agent/ChatRoomInfo.cs`

## 三、文件清单

### 新建文件（6 个）

| 文件 | 层 |
|------|-----|
| `lib/abstractions/abs_core/core_utils/core/BotNameGenerator.cs` | ② Foundation |
| `lib/abstractions/abs_hands/desktop/IWindowShakeCoordinator.cs` | ② Foundation |
| `app/gui/views/core/ShakeAnimationHelper.cs` | ⑦ App |
| `kit/hands/desktop/services/WindowShakeCoordinator.cs` | ④ Core |
| `kit/mcp/communication/ShakeWindowToolHandlers.cs` | ⑤ Services |
| `lib/abstractions/abs_core/models/models_agent/agent/ChatRoomInfo.cs` | ② Foundation |

### 改造文件（5 个）

| 文件 | 改动 |
|------|------|
| `lib/guard/configuration/configuration2/core/mapping/SettingsJson.cs` | +2 个 bool 属性 |
| `kit/hands/desktop/native/User32NativeMethods.cs` | +`FlashWindowEx` P/Invoke |
| `app/gui/views/core/PermissionDialog.axaml.cs` | `StartShakeAnimation` 改调 `ShakeAnimationHelper` |
| `llm/agents/Services/Spawn/Unified/ContextSetupMiddleware.cs` | `DisplayName` 空时生成 bot 名 |
| `llm/agents/Coordinator/Team/core/TeamManager.cs` | +`GetChatRoomInfo` 方法 |

### 测试文件（3 个）

| 文件 | 测试内容 |
|------|----------|
| `lib/abstractions/abs_core.tests/core_utils/BotNameGeneratorTests.cs` | 命名生成+去重 |
| `kit/hands.desktop.tests/services/WindowShakeCoordinatorTests.cs` | 1s 去抖 |
| `kit/mcp.tests/communication/ShakeWindowToolHandlersTests.cs` | 工具调用+配置开关 |

## 四、实现计划（渐进式，每步编译+测试+提交）

| 步骤 | 内容 | 依赖 |
|------|------|------|
| 1 | `BotNameGenerator` + 测试 | 无 |
| 2 | `SettingsJson` 添加 2 个开关 | 无 |
| 3 | `ShakeAnimationHelper` 提取 + `PermissionDialog` 改造 | 无 |
| 4 | `User32NativeMethods` 添加 `FlashWindowEx` | 无 |
| 5 | `IWindowShakeCoordinator` + `WindowShakeCoordinator` + 测试 | ②④ |
| 6 | `ShakeWindowToolHandlers` + 测试 | ②⑤ |
| 7 | `ContextSetupMiddleware` bot 前缀改造 | ① |
| 8 | `ChatRoomInfo` + `TeamManager.GetChatRoomInfo` | ⑥ |
| 9 | 全量编译 + 集成验证 | 全部 |

## 五、验收标准

- [ ] 子代理 spawn 时 `DisplayName` 为空 → 自动生成 `bot{中文名}`
- [ ] MCP 工具 `shake_window` → 窗口实际震动
- [ ] MCP 工具 `get_process_info` → 返回电脑名+PID
- [ ] 1s 内多次 `shake_window` → 只震动一次
- [ ] `WindowShakeEnabled=false` → 工具返回文字，不震动
- [ ] 全量编译通过（`--no-incremental`）
- [ ] 单元测试全部通过

<!-- 🤖 Auto Decision: 2026-09-16 -->
<!-- 决策: 选择方案A(改造为主),复用现有邮箱/窗口API/配置热重载 -->
<!-- 原因: 现有基础设施完善,复用符合八荣八耻和万物皆插件原则 -->
<!-- 替代方案: 方案B(新建独立模块),因重复造轮子放弃 -->
<!-- 验证: 待实现后验证 -->
