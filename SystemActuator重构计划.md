# SystemActuator 统一命名重构计划（极简版）

> **目标**: 把 bash/shell/cmd/python 执行器统一为 `SystemActuator` 体系，**两个文件夹**（Abstractions + Instances），砍掉 8 个中间层，多态调用。

## 决策记录（用户确认）

| 决策点 | 选择 |
|--------|------|
| 文件夹位置 | `core/execution/Hands/src/SystemActuator/{Abstractions,Instances}` |
| 执行服务 | 全部合并到基类（ShellExecutionService/ShellCommandContext/ShellBackgroundTaskService） |
| 能力检测 | 合并到实例类静态缓存（砍掉 CapabilityProvider+Cache+Factory） |
| 工具 Handler | 保持原位（通过 DI 注入 ISystemActuatorRegistry 多态调用） |
| 消费者注入 | 注入 `ISystemActuatorRegistry` 按 Kind 查找 |
| 后台任务 | 静态字典统一管（跨执行器） |
| 基类胖度 | 接受胖基类（500+ 行） |
| ShellType 枚举 | 完全删除，用 SystemActuatorKind 类替代 |
| Cmd 实现 | 补 CmdSystemActuator |

## 最终目录结构

```
core/execution/Hands/src/SystemActuator/
  Abstractions/
    SystemActuatorKind.cs        # 类型标识（替代 ShellType 枚举）
    ISystemActuator.cs           # 接口：能力+命令构建+执行+后台
    SystemActuatorBase.cs        # 抽象基类：通用执行/进程/后台（胖基类）
    ISystemActuatorRegistry.cs   # 注册表接口：按 Kind 查找 + 后台统一管理
    SystemActuatorRegistry.cs    # 注册表实现：静态字典
  Instances/
    BashSystemActuator.cs
    PowerShellSystemActuator.cs
    CmdSystemActuator.cs         # 新增
    PythonSystemActuator.cs
```

## 砍掉的层（8 个）

| 旧类 | 去向 |
|------|------|
| `ShellCapability` | 合并到 `SystemActuatorBase` 字段 |
| `ShellCapabilityCache` | 合并到 `SystemActuatorRegistry` 静态字典 |
| `ShellCapabilityProvider` | 合并到实例类（静态缓存检测） |
| `ShellProviderFactory` | 合并到 `SystemActuatorRegistry` |
| `ShellExecutionService` | 合并到 `SystemActuatorBase.ExecuteAsync` |
| `ShellCommandContext` | 合并到 `SystemActuatorBase` 内部进程管理 |
| `ShellBackgroundTaskService` | 合并到 `SystemActuatorRegistry` 静态字典 |
| `ShellCapabilityInitializer` | 合并到 `SystemActuatorRegistry` 静态初始化 |

## 接口草图

```csharp
// Abstractions/SystemActuatorKind.cs — 替代 ShellType 枚举
public sealed class SystemActuatorKind
{
    public string Id { get; }           // "bash", "powershell", "cmd", "python"
    public string DisplayName { get; }  // "Bash", "PowerShell", "CMD", "Python"

    public static readonly SystemActuatorKind Bash = new("bash", "Bash");
    public static readonly SystemActuatorKind PowerShell = new("powershell", "PowerShell");
    public static readonly SystemActuatorKind Cmd = new("cmd", "CMD");
    public static readonly SystemActuatorKind Python = new("python", "Python");

    public static SystemActuatorKind? FromId(string? id) => ...;
    public static IReadOnlyCollection<SystemActuatorKind> All => ...;
    // Equals/GetHashCode/==/!= + 别名解析（pwsh/python3/py）
}

// Abstractions/ISystemActuator.cs — 执行器接口
public interface ISystemActuator
{
    SystemActuatorKind Kind { get; }
    string ShellPath { get; }
    string DisplayName { get; }
    string Version { get; }
    bool Detached { get; }
    Encoding OutputEncoding { get; }
    Encoding ErrorEncoding { get; }

    // 命令构建
    Task<SystemActuatorExecCommandResult> BuildExecCommandAsync(
        string command, SystemActuatorExecOptions options, CancellationToken ct = default);
    string[] GetSpawnArgs(string commandString);
    Task<IReadOnlyDictionary<string, string>> GetEnvironmentOverridesAsync(
        string command, CancellationToken ct = default);

    // 执行（原 ShellExecutionService.ExecuteAsync）
    Task<SystemActuatorExecutionResult> ExecuteAsync(
        string command, int? timeout = null, string? workingDirectory = null,
        bool disableSandbox = false, CancellationToken ct = default);

    // 后台执行（原 StartWithBackgroundSupportAsync）
    Task<ISystemActuatorCommandContext> StartWithBackgroundSupportAsync(
        string command, int? timeout = null, string? workingDirectory = null,
        bool shouldAutoBackground = true, bool disableSandbox = false, CancellationToken ct = default);
}

// Abstractions/ISystemActuatorRegistry.cs — 注册表 + 后台统一管理
public interface ISystemActuatorRegistry
{
    ISystemActuator Get(SystemActuatorKind kind);
    IReadOnlyCollection<SystemActuatorKind> RegisteredKinds { get; }
    IReadOnlyDictionary<SystemActuatorKind, SystemActuatorInfo> GetAllInfos();

    // 后台任务统一管理（原 ShellBackgroundTaskService）
    Task<ISystemActuatorCommandContext> GetTaskAsync(string taskId, CancellationToken ct = default);
    Task<IReadOnlyList<SystemActuatorBackgroundTaskInfo>> ListTasksAsync(...);
    Task CancelTaskAsync(string taskId, CancellationToken ct = default);
    Task KillAllRunningAsync(CancellationToken ct = default);
}

// Abstractions/SystemActuatorBase.cs — 胖基类
public abstract class SystemActuatorBase : ISystemActuator
{
    // 静态缓存能力检测结果（首次检测，后续返回缓存）
    // 通用执行逻辑（进程启动、stdout/stderr 捕获、超时）
    // 环境变量注入
    // 子类重写：BuildExecCommandAsync, GetSpawnArgs, ResolveShellPath, DetectVersion
}
```

## 消费者改动

| 消费者 | 改动 |
|--------|------|
| `BridgeServer` | 注入 `ISystemActuatorRegistry` 替代 `IShellExecutionService` |
| `LocalShellTask` | 同上 |
| `ShellToolHandlers` | `registry.Get(SystemActuatorKind.Bash).ExecuteAsync(...)` |
| `PowerShellToolHandlers` | `registry.Get(SystemActuatorKind.PowerShell).ExecuteAsync(...)` |
| `BashCommandHookExecutor` | switch case 改 `SystemActuatorKind.FromId()` |
| `ShellInfoSection` | 遍历 `registry.GetAllInfos()` 多态格式化 |

## 不改名的（安全检查/MCP 契约/提示词）

- `BashSecurityRegex`/`BashSemanticChecker` 等 Bash 专用安全检查
- `ShellToolName` 枚举**值**（MCP 协议契约）
- `BashToolPrompt`/`PowerShellToolPrompt` 等提示词
- `ExecutorVariant`（Agent 变体，与 Shell 无关）

## 执行阶段（渐进式）

### 阶段 1：新建 SystemActuator 体系
- [ ] 1.1 新建 `SystemActuatorKind.cs`
- [ ] 1.2 新建 `ISystemActuator.cs` + 辅助类型（Info/Options/Result）
- [ ] 1.3 新建 `SystemActuatorBase.cs`（合并执行+进程逻辑）
- [ ] 1.4 新建 `ISystemActuatorRegistry.cs` + `SystemActuatorRegistry.cs`
- [ ] 1.5 新建 4 个实例类（Bash/PowerShell/Cmd/Python）
- [ ] 1.6 编译 Core.slnx

### 阶段 2：迁移消费者
- [ ] 2.1 改 `ShellToolHandlers`/`PowerShellToolHandlers` 用 Registry
- [ ] 2.2 改 `BridgeServer`/`LocalShellTask` 等消费者
- [ ] 2.3 改 `BashCommandHookExecutor` switch case
- [ ] 2.4 改 `ShellInfoSection` 多态格式化
- [ ] 2.5 编译 + 测试

### 阶段 3：删除旧层
- [ ] 3.1 旧文件移到 `.xxx/` 备份（不删除）
- [ ] 3.2 删除 `ShellType` 枚举引用
- [ ] 3.3 全量编译 + 测试

### 阶段 4：验证
- [ ] 4.1 编译 App.slnx 全量
- [ ] 4.2 单元测试 + E2E 测试
- [ ] 4.3 更新 AGENTS.md

<!-- 🤖 Auto Decision: 2026-08-05 -->
<!-- 决策: 极简两文件夹结构，砍掉 8 个中间层 -->
<!-- 原因: 用户要求做减法，一个抽象文件夹+一个实例文件夹，多态调用 -->
<!-- 替代方案: 保留原分层只改名（用户否决，分层过多）-->
<!-- 验证: 待执行 -->
