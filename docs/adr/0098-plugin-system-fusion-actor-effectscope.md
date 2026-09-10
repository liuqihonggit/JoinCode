# 0098. 插件系统融合:Actor 串行 + EffectScope + 弱引用事件

- 状态：accepted
- 日期：2026-09-10
- 决策者：AI + 用户确认
- 验证：代码已完整实现并测试通过（PluginManager Actor 化 + EffectScope + WeakEventBroker + PluginDiagnostic + RunBackgroundTask + PluginFiberState 含 Activating/Unloaded/Faulted 重试 + PluginAlc ALC 隔离）

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

## DSH/Cordis 功能对齐补全（2026-09-11 扩写）

对照 DSH（DeepSeek Harness，基于 Cordis 内核）插件系统，ADR 0098 原 12 维度已全部落地（Actor+Channel、EffectScope、PluginFiber 状态机、DependencyGraph、ResourceReferenceGraph、ALC 隔离、RunBackgroundTask、PluginDiagnostic、WeakEventBroker、ServiceLookup、标准 DI、心跳检测）。DSH 另有 11 项功能未覆盖，经 .NET NativeAOT 可行性评估，全部纳入本 ADR（#8 #9 因 AOT 限制降级）。不新建 ADR，统一扩写至此。

### 补全项清单

| # | 功能 | AOT | 难度 | 价值 | 依赖 |
|---|------|:---:|:---:|:---:|:----:|
| 1 | 5 种事件分发模式 | ✅ | 中 | 高 | - |
| 2 | 运行时不变量自检 | ✅ | 中 | 高 | - |
| 3 | 插件审批机制 | ✅ | 低 | 中 | - |
| 4 | SessionEvent 事件溯源 | ✅ | 高 | 高 | - |
| 5 | Trajectory + fork/replay | ✅ | 高 | 高 | #4 |
| 6 | intercept 配置覆写 | ✅ | 中 | 中 | - |
| 7 | patch 层/Bundle/Profile 组合 | ✅ | 中 | 中 | - |
| 8 | 动态插件运行时（部分） | ⚠️ | 高 | 高 | #3 |
| 9 | capability 注入门禁（部分） | ⚠️ | 高 | 中 | - |
| 10 | Service.invoke 可调用服务 | ✅ | 中 | 低 | - |
| 11 | 多运行模式 | ✅ | 中 | 低 | - |

### 11.1 事件分发模式（5 种）

- `EventDispatchMode` 枚举：`Emit`/`Parallel`/`Serial`/`Bail`/`Waterfall`
- `Emit`：同步触发不等监听器
- `Parallel`：异步并行 `Task.WhenAll`
- `Serial`：串行依次
- `Bail`：首个 bail 结果即停
- `Waterfall`：监听器连成 `next()` 链，漏调 `next()` 短路整条链
- 改造 `ServiceMessageBus` 支持 `EventDispatchMode` 参数，`IAppEventBus` 加 `EmitWaterfall`/`EmitBail` 等扩展方法
- 典型应用：`tools/pre-execute` 门禁走 waterfall，`agent/error` 走 emit，`session/flush` 走 parallel
- AOT：✅ 纯 C# 实现，无反射 emit

### 11.2 运行时不变量自检

- `InvariantRegistry` 服务（挂成 `ctx.Invariants`），可配置注册表，不含产品检查
- 每个插件程序集暴露 `RegisterInvariants(IInvariantRegistry)` 配套入口（对齐 DSH `./invariant` 伴随入口）
- `InvariantError`：`code:'INVARIANT'`（稳定机器可读）+ `PackageName`，继承 `Exception`
- `package_allowlist`/`package_blocklist` 正则过滤（区分大小写、`new RegExp` 编译、blocklist 优先）
- 专用子 fiber 运行 installer，`fail(message)` 注入抛 `InvariantError`
- 启动 join：注册成功前不返回；失败原子 dispose 子 fiber
- disposer 归属：服务拥有每个注册 fiber，卸载任一侧都移除监听器
- AOT：✅

### 11.3 插件审批机制

- `PluginApprovalRequest`（复用现有 `PlanApprovalRequest` 模式扩展）
- `ApprovalRequestId` 自增铸造
- 首个回答者获胜：`ArmRequest`/`PeekRequest`/`ClaimRequest`/`DisarmRequest`/`PendingRequestFor`
- `approve(requestId, approveFutureVersions)` / `decline(requestId)`
- 动态插件运行时（#8）的激活前置依赖
- AOT：✅

### 11.4 SessionEvent 事件溯源

- `SessionEvent` 持久事件流：`seq` 单调递增 + `time` + `type` + `data`
- 格式版本闸门：`SessionFormatVersion`（当前 =3），日志首行 version 比当前新时拒绝加载
- `surfaceOp`：`append` 或 `{ op:'replace', startSeq, endSeq }`，仅 message/tool-result 合法
- `sourceEventSeqs`：引用的源事件 seq（压缩替换→被遮蔽条目）
- `ignorable`：未知类型跳过；缺失=必选，拒绝重建
- 升级 `TranscriptService` 为事件溯源（保留 append-only，加 seq/surfaceOp/格式闸门）
- 遥测/投影/恢复消费同一事件流
- AOT：✅

### 11.5 Trajectory + fork/replay

- `SessionTrajectory`：从事件流重建完整 run（按 source 分组 inspect）
- `ReplaySession`：回放事件流到指定 seq
- `ForkSession`：从某 seq 分叉新会话（复制事件流前缀）
- 依赖 #4 SessionEvent 事件溯源
- 复用现有 `SessionResumeStep` 扩展
- AOT：✅

### 11.6 intercept 配置覆写

- `ServiceIntercept` 层：`ctx.Intercept(serviceName, config)`
- 流入 `Service.ResolveConfig` 合并祖先 intercept 配置
- 允许插件覆写服务配置而不改服务实现
- AOT：✅

### 11.7 patch 层 / Bundle / Profile 组合

- `PluginPatch`：YAML 顶层数组，每条目两种操作——`insert`（按 id 追加）/ 按 id 覆盖整行
- `PluginBundle`：自带 patch 层的插件包，作为一层加入 profile
- `PluginProfile`：`bundles` 依赖声明
- 多层拍平应用：`profile.bundles` → profile patch → 全局 patch → `--patch` overlays
- `config` 整行替换非深合并；`name` 不符 warn 后跳过；无 `replace`/`ignore` 动词
- reconcile 按已安装状态（非依赖 diff），`update` 能激活新版本才多了 `dsh.bundle` 声明的包
- AOT：✅

### 11.8 动态插件运行时（部分，AOT 降级）

- `DynamicPluginRegistry`：进程内存 `Map`，不可变 `Package` + `currentPackageId`/`nextPackageId`
- `DefinePlugin`/`RunPlugin`/`UpdatePlugin`/`StopPlugin`/`UndefinePlugin`
- **运行时加载已编译程序集**（复用 `PluginAlc`）+ 版本 + 审批（依赖 #3）
- **不支持源码求值**：Roslyn 编译器不兼容 NativeAOT，只支持已编译 DLL
- `stop` 只停运行、`undefine` 永久删除；重启全部丢失（纯内存）
- `inspect provider`（运行时自省）：`CordisInspectRegistryService` 挂成 `ctx.CordisInspect`
- invoke handler 表：`host.call(method, args)` 路由，4 类失败码（plugin-not-running/stale-run/method-not-found/handler-error）
- AOT：⚠️ 降级（无源码求值，只有已编译程序集加载）

### 11.9 capability 注入门禁（部分，AOT 适配）

- **源码生成器编译时检查**：扫描 `[Inject]` 特性，校验 `inject` 声明完整性，未声明访问编译报错
- **运行时 `ServiceLookup` 校验**：已声明才放行，未声明抛 `ServiceNotDeclaredException`（带修复提示）
- **不用 `DispatchProxy`**（AOT 不兼容，ADR 0098 已否决 `WeakServiceProxy`）
- 对齐 DSH capability-based Proxy 语义，但实现路线是源码生成器 + 运行时校验
- AOT：⚠️ 源码生成器路线

### 11.10 Service.invoke 可调用服务

- `[ServiceInvoke]` 特性标记可调用方法
- 源码生成器生成调用包装（把服务包成可调用委托）
- 消费方 `ctx.MyService(args)` 直接调用（对齐 DSH `ctx.logger()` 式）
- AOT：✅

### 11.11 多运行模式

- `RunMode` 枚举：`Standard`/`Code`/`Minimal`/`Creator`
- 映射现有 `Interactive`/`NonInteractive`/`Doctor`/`Tui`/`Json`/`Headless`
- `Standard`：完整工具集（文件编辑+shell+搜索+技能+规划+子代理+工作流）
- `Code`：SDK 编排（多步操作合并为一个程序）
- `Minimal`：双工具（shell + str_replace_editor，基准测试用）
- `Creator`：自省 + 插件实验 + 预设编写
- AOT：✅

### 实现顺序（依赖链）

```
#3 审批 → #1 分发模式 → #2 不变量 → #6 intercept → #4 事件溯源 → #5 Trajectory → #7 patch 层 → #9 capability → #10 Service.invoke → #11 多模式 → #8 动态运行时
```

### 组件处置清单（新增）

| 组件 | 处置 | 理由 |
|------|------|------|
| `EventDispatchMode` 枚举 | 新增 | 5 种分发模式 |
| `InvariantRegistry`+`InvariantError` | 新增 | 运行时不变量自检 |
| `PluginApprovalRequest` | 新增 | 复用 PlanApproval 模式 |
| `SessionEvent` 事件流 | 新增（升级 TranscriptService） | 事件溯源 |
| `SessionTrajectory`+`ReplaySession`+`ForkSession` | 新增 | 事件流重建 |
| `ServiceIntercept` | 新增 | 配置覆写层 |
| `PluginPatch`/`PluginBundle`/`PluginProfile` | 新增 | 配置组合 |
| `DynamicPluginRegistry` | 新增 | 运行时动态插件 |
| `CapabilityGate`（源码生成器） | 新增 | 编译时 inject 检查 |
| `ServiceInvokeGenerator` | 新增 | 可调用服务包装 |
| `RunMode` 枚举 | 新增 | 多运行模式 |

### 补全验证

- 每项走 TDD 红绿循环（AGENTS.md TDD 铁律）
- 每项编译+单元测试+提交（渐进式开发）
- #8 #9 的 AOT 限制单独 Release 编译验证 ✅（Abstractions + Guard.Config.Tests Release 编译 0 警告 0 错误）
- #4 事件溯源的格式闸门用版本迁移测试验证
- 全部 11 项已完成，333 个插件测试全绿
