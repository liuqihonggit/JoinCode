# 0046. [Register] 特性 DI 自动注册模式

- 状态：proposed
- 日期：2026-08-29
- 决策者：项目架构组

## 背景

项目有 752 处 DI 注册，通过 `[Register(typeof(IXxx), ServiceLifetime.Singleton)]` 特性 + 源码生成器自动生成 DI 注册代码。但部分注册模式不统一：

1. **重复注册同一接口**：`SwarmPermissionCallbackService` 注册了两次 `ISwarmPermissionCallbacks`
2. **注册具体类而非接口**：部分注册 `typeof(XxxClass)` 而非 `typeof(IXxx)`，消费方无法通过接口解耦
3. **生命周期不统一**：大部分是 Singleton，少数 Scoped/Transient 未文档化选择理由

## 决策

### 1. 注册接口优先

```csharp
// ✅ 正确 — 注册接口，消费方通过接口解耦
[Register(typeof(IToolService), ServiceLifetime.Singleton)]
public sealed class ToolService : IToolService { }

// ❌ 禁止 — 注册具体类，消费方耦合实现
[Register(typeof(ToolService), ServiceLifetime.Singleton)]
public sealed class ToolService : IToolService { }
```

例外：配置选项类（如 `SkillOptions`）、内部辅助类（无接口）可注册具体类。

### 2. 禁止重复注册同一接口

同一接口在同一程序集内只能注册一次。跨程序集注册同一接口时，需用 `[Register(typeof(IXxx), ServiceLifetime.Singleton, "qualifier")]` 限定名区分。

### 3. 生命周期选择规则

| 生命周期 | 适用场景 | 示例 |
|----------|----------|------|
| Singleton | 无状态/线程安全/全局唯一 | IToolService、IConfigLoader |
| Scoped | 每请求/每会话一个实例 | IRequestContext、ISessionState |
| Transient | 轻量无状态/每次新建 | IMapper、IValidator |

默认用 Singleton，需 Scoped/Transient 时在注释中说明理由。

## 替代方案

1. **手动 DI 注册**：放弃。752 处手动注册易遗漏，且无法编译期检查。
2. **Scrutor 程序集扫描**：放弃。运行时扫描启动慢，且无法 AOT 友好。
3. **不约束注册模式**：放弃。重复注册和具体类注册导致 DI 容器行为不确定。

## 后果

- 正面：编译期生成注册代码，AOT 友好；接口注册消费方解耦；重复注册编译期检测
- 负面：配置选项类等例外需显式说明；Scoped/Transient 需注释理由
- 中性：源码生成器位于 generators/ 第一层（见 ADR 0001）
