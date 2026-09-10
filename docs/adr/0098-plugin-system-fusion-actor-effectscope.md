# 0098. 插件系统融合:Actor 串行 + EffectScope + 弱引用事件

- 状态：proposed
- 日期：2026-09-10
- 决策者：AI + 用户确认

## 背景

项目已有一套 Cordis 风格插件系统（`PluginManager` + `WorkflowPluginHost` + `PluginContext` + `PluginFiber` + `ResourceReferenceGraph`），采用"撤销链 + 引用计数 + 心跳检测 + ConcurrentDictionary+AsyncLock"路线。

需求文件（`D:\Users\54076\Desktop\3插件系统.txt`）描述了另一套"EffectScope + WeakEventBroker + WeakServiceProxy + ServiceRegistry + Actor+Channel"路线。

逐维度对比评分（1-5 分）后：

| 维度 | 现有 | 需求 | 胜方 |
|------|:---:|:---:|:---:|
| 状态机 | 4 | 4 | 平 |
| 副作用管理 | 3 | 4 | 需求 |
| 线程安全模型 | 3 | 5 | 需求 |
| 服务注册/访问 | 4 | 2 | 现有 |
| 死亡检测 | 4 | 2 | 现有 |
| 依赖图/连带卸载 | 3 | 4 | 需求 |
| 事件订阅 | 3 | 4 | 需求 |
| 卸载流程 | 4 | 4 | 平 |
| 诊断 | 3 | 4 | 需求 |
| 插件接口 | 4 | 2 | 现有 |
| ALC 隔离 | 2 | 4 | 需求 |
| 后台任务 | 1 | 5 | 需求 |

现有总分 38 / 需求总分 46。

**致命硬伤**：需求 `WeakServiceProxy<T>` 用 `DispatchProxy`（反射 emit），**不兼容 NativeAOT**（项目强制 `PublishAot` + `TrimMode=full`，见 AGENTS.md 关键约束）。纯替换会导致编译失败。

## 决策

采用**融合方案**：以需求的 Actor+EffectScope+动态拓扑+弱事件+结构化诊断+ALC+后台任务为骨架，保留现有的标准 DI+心跳检测+IWorkflowPlugin+ObjectId 扫描+黑名单。

### 被否决的替代方案

1. **纯替换**（否决）：`WeakServiceProxy` 的 `DispatchProxy` 不兼容 NativeAOT，直接替换编译失败。且丢失标准 DI 生态、心跳检测纳秒级优势。
2. **新建独立系统**（否决）：两套插件系统并存增加维护负担，调用方需选择用哪套，心智负担高。
3. **只做部分维度**（否决）：Actor 串行与 EffectScope 强耦合（Actor 保证串行才能无锁访问 EffectScope），拆开做无法保证一致性。

### 架构

```
PluginHost (Actor + Channel<Func<Task>> mailbox)
├── 状态机: PluginFiber (复用 StateMachine<T>, 加 Activating/Unloaded/Faulted重试)
├── EffectScope (Stack<(Revert,Desc)> + onRevertFailed 上报 + 异步撤销链)
├── ServiceRegistry (IServiceCollection+ServiceProvider 标准 DI, + ServiceLookup 枚举)
├── DependencyGraph (插件→服务类型声明, 动态 providerResolver 拓扑)
├── WeakEventBroker (ConditionalWeakTable 弱引用事件, AOT 兼容)
├── PluginDiagnostic (Kind/Message/Suggestion 结构化诊断)
├── RunBackgroundTask (绑定 Shutdown, 卸载等待退出)
├── ALC 隔离 (AssemblyLoadContext + GC×10 + WeakReference 验证)
├── ResourceReferenceGraph (保留: 资源级引用图 + 引用计数)
├── PluginResourceScanner (保留: ObjectId 泄漏扫描 + 黑名单)
└── EnsureAlive + PluginDeadException (保留: 心跳死亡检测)
```

### 核心设计

1. **Actor 串行无锁** — `PluginHost` 用 `Channel<Func<Task>>` mailbox + 单 Actor 循环，所有可变状态（_plugins/_services/_graph）只在 Actor 循环里访问，彻底去掉 `ConcurrentDictionary`+`AsyncLock` 多锁。复用项目已有 `ActorBase` 基建（ADR 0074/0091）。

2. **EffectScope 作用域** — `Stack<(Action Revert, string? Desc)>`，`Add(apply, revert, desc)`，`Dispose` 逆序 Pop。revert 异常通过 `onRevertFailed` 回调上报 `PluginDiagnostic`（不静默吞）。保留异步撤销链 `Stack<IAsyncDisposable>`（现有优势），异步先于同步执行。

3. **状态机枚举改造** — `PluginFiberState` 加 `Activating`（区分开始激活/激活完成）、`Unloaded`（终态，区别 Unloading 中间态）。转换表加 `Faulted→Activating`（允许重试激活）、`Faulted→Unloading`。复用 `StateMachine<TState>` 基建 + FrozenDictionary（AOT 最优）。

4. **动态拓扑依赖图** — `DependencyGraph` 记录"插件→依赖的服务类型"声明，卸载时通过 `providerResolver: Type→string?` 动态解析当前提供者，DFS 拓扑排序。服务热替换（A 卸载后 C 重新 Provide）自动生效。保留 `ResourceReferenceGraph` 资源级引用图（ObjectId 粒度）+ 引用计数 + 两阶段 Prepare。

5. **弱引用事件** — `WeakEventBroker` 用 `ConditionalWeakTable<object, Dictionary<string, IWeakSlot>>`（AOT 兼容），事件源侧弱引用槽 `WeakSlot<TArgs>`，订阅者死亡自动回收，所有订阅者死亡时从事件源自注销。`PluginContext.WeakSubscribe` 作为 `IAppEventBus` 弱引用补充。

6. **结构化诊断** — `PluginDiagnostic`（PluginId/Kind/Message/Suggestion/Timestamp）+ `OnDiagnostic` 事件。Kind 分类：RevertFailed/UnloadTimeout/AlcLeak/AlcNotCollectible/ActivationFailed/EmptyRegistration。保留 `PluginResourceScanner` ObjectId 扫描。

7. **后台任务管理** — `PluginContext.RunBackgroundTask(work, waitOnUnload)` 自动绑定 Shutdown 令牌，revert 里 `task.Wait(wait)` 等待退出（带超时，不阻塞 Actor 过久）。补现有"裸 Task.Run 不受令牌约束"缺陷。

8. **ALC 隔离** — `PluginEntry.Context = AssemblyLoadContext?`，`UnloadCoreAsync` 里 `ALC.Unload()` + `GC.Collect()`×10 + `WeakReference` 验证回收。需验证 .NET10 NativeAOT 下 `AssemblyLoadContext.IsCollectible` 可用性；不可用时降级为诊断警告（AlcNotCollectible）。

9. **保留标准 DI** — `IServiceCollection`+`ServiceProvider` 保留，废弃 `WeakServiceProxy`（DispatchProxy 不兼容 AOT）。服务死亡检测用 `EnsureAlive()` 心跳（AOT 兼容，纳秒级）。新增 `ServiceLookup` 枚举（Found/NotRegistered/ProviderDead）区分查询结果。

10. **保留 IWorkflowPlugin** — 元数据（Name/Version/Description）+ 分阶段（LoadAsync→InitializeAsync→Unload）+ async。需求 `IPlugin`（仅 Activate）过简，废弃。

### 组件处置清单

| 组件 | 处置 | 理由 |
|------|------|------|
| `PluginFiber`/`StateMachine<T>` | 保留基建+改枚举 | AOT 最优，补 Activating/Unloaded/Faulted重试 |
| `PluginContext.Effect` | 改为 EffectScope | apply/revert/desc+上报，保留异步 |
| `PluginManager` 锁模型 | 改为 Actor+Channel | 无锁串行，消除死锁风险 |
| `IServiceCollection`+`ServiceProvider` | 保留 | 标准 DI 生态，AOT 兼容 |
| `WeakServiceProxy`/`DispatchProxy` | 废弃 | AOT 不兼容 |
| `EnsureAlive`+`PluginDeadException` | 保留 | 心跳 AOT 兼容，纳秒级 |
| `ResourceReferenceGraph` | 保留 | 资源级引用图+引用计数 |
| `DependencyGraph` 动态拓扑 | 新增 | 替代快照 Cascade，服务热替换 |
| `WeakEventBroker` | 新增 | 弱引用事件，AOT 兼容 |
| `IAppEventBus` | 保留 | 强引用场景仍需要 |
| `PluginResourceScanner`+黑名单 | 保留 | ObjectId 扫描+泄漏防护 |
| `PluginDiagnostic` | 新增 | 结构化诊断+建议 |
| `IWorkflowPlugin` | 保留 | 元数据+分阶段+async |
| ALC 隔离+GC验证 | 新增 | 程序集卸载，验证 AOT |
| `RunBackgroundTask` | 新增 | 补裸 Task.Run 缺陷 |

## 后果

### 正面

- 消除多锁死锁风险（Actor 串行无锁）
- 副作用 revert 异常不再静默吞（结构化诊断）
- 服务热替换后依赖图自动更新（动态拓扑）
- 弱引用事件避免订阅者泄漏
- 后台任务受 Shutdown 约束，卸载时等待退出
- ALC 隔离实现程序集级卸载+GC 验证

### 负面

- Actor mailbox 所有操作过队列有微小延迟（可接受，插件操作非热路径）
- ALC 在 NativeAOT 下需验证 `IsCollectible` 可用性，不可用时降级
- 改动面大（PluginManager 669 行重写为 Actor），需充分测试

### 验证

- 每维度走 TDD 红绿循环（AGENTS.md TDD 铁律）
- 每维度编译+单元测试+提交（渐进式开发）
- 最后全量编译+集成测试
- ALC 的 AOT 兼容性单独验证（Release 编译）
