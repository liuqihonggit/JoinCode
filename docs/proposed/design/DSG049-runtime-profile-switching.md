# DSG049: 运行期 Profile 动态切换

> 📍 **导航**: [docs/](../README.md) › [design/](README.md) | **前置**: [DSG048](DSG048-node-plugin-inheritance-architecture.md)
> 状态: proposed
> 创建: 2026-11-14
> 关联: [DSG048](DSG048-node-plugin-inheritance-architecture.md) 阶段5 已知限制2

---

## 1. 问题

当前 `ResolvedProfile` 是 DI 单例，管道在 DI 容器构建阶段一次性创建（中间件列表固定）。运行期无法切换 Profile，`NodeRegistry.Register` 抛 `NotSupportedException`。

**已知限制**（来自 DSG048 验收报告）：
- P5 运行期回滚未验证 — 当前不支持运行期 Profile 动态切换，可逆注册的回滚行为通过单元测试覆盖

## 2. 目标

- 运行期可通过 API/命令切换 Profile（如从 `base` 切换到 `headless`）
- 切换后管道使用新 Profile 的中间件列表
- 切换过程保证状态一致性（旧管道完成当前执行后再切换）
- 无锁设计（`volatile` 引用原子替换），符合项目配置读写规范

## 3. 设计

### 3.1 IProfileManager — Profile 运行期管理

```csharp
namespace JoinCode.Abstractions.Composition;

public interface IProfileManager {
    ResolvedProfile Current { get; }
    Task SwitchAsync(string profileName, CancellationToken cancellationToken = default);
    event Func<ResolvedProfile, ResolvedProfile, ValueTask>? ProfileSwitching;
}
```

- `Current` — 返回当前活跃的 ResolvedProfile（`volatile` 读）
- `SwitchAsync` — 切换到新 Profile（原子替换引用）
- `ProfileSwitching` — 切换前事件（允许消费方做清理/准备）

### 3.2 ProfileManager 实现

```csharp
public sealed class ProfileManager : IProfileManager {
    private volatile ResolvedProfile _current;
    private readonly NodeProfileResolver _resolver;
    private readonly ILogger<ProfileManager>? _logger;

    public ResolvedProfile Current => _current;

    public event Func<ResolvedProfile, ResolvedProfile, ValueTask>? ProfileSwitching;

    public ProfileManager(NodeProfileResolver resolver, ResolvedProfile initial, ILogger<ProfileManager>? logger) {
        _resolver = resolver;
        _current = initial;
        _logger = logger;
    }

    public async Task SwitchAsync(string profileName, CancellationToken cancellationToken = default) {
        var newProfile = _resolver.Resolve(profileName);
        if (ProfileSwitching is not null) {
            foreach (var handler in ProfileSwitching.GetInvocationList()) {
                await ((Func<ResolvedProfile, ResolvedProfile, ValueTask>)handler)(_current, newProfile);
            }
        }
        _current = newProfile; // volatile 写，原子替换
    }
}
```

### 3.3 管道改为从 IProfileManager 获取 Profile

**当前**：管道构建时从 DI 获取 `ResolvedProfile` 单例，中间件列表固定。

**改造后**：管道每次执行时从 `IProfileManager.Current` 获取当前 Profile，动态解析中间件列表。

**关键决策**：管道本身仍是 DI 单例（PipelineBuilder.Build() 返回的管道对象不变），但管道内部的中间件列表改为 `Lazy` 模式 — 首次执行时从 Profile 解析，Profile 切换后重新解析。

### 3.4 改造范围

| 组件 | 改造内容 |
|------|---------|
| `DataDrivenPipelineComposition` | 管道构建改为从 `IProfileManager.Current` 获取 Profile |
| `ResolvedProfile` DI 注册 | 从单例改为通过 `IProfileManager.Current` 获取 |
| `NodeRegistry` | 不变（节点列表始终不变） |
| `NodeProfileResolver` | 不变（仍用于解析继承链） |

### 3.5 渐进式实现

**阶段1**：引入 `IProfileManager` + `ProfileManager`，`ResolvedProfile` 从 DI 单例改为通过 `IProfileManager` 获取
**阶段2**：管道改为每次执行时从 `IProfileManager.Current` 获取 Profile
**阶段3**：提供 CLI 命令 `/profile <name>` 运行期切换 Profile

## 4. 验收

| 验收项 | 方法 |
|--------|------|
| Profile 切换不崩溃 | exe 启动后切换 Profile，对话正常 |
| 中间件列表随 Profile 变化 | 切换后 `--dump-profile` 输出正确 |
| 状态一致性 | 旧管道完成当前执行后再切换 |
| 无锁设计 | `volatile` 引用原子替换，无 `lock` |
