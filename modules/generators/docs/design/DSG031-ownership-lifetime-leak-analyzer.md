# DSG031 — Rust 风格所有权 + 生命周期泄露分析器设计

> 📍 **导航**: [docs/design/](README.md) › DSG031
> 🔗 **相关**: JCC9305（局部变量泄露，已落地 P0）、`gen/aot_safety.generator/rules/memory_leak/`
> 📌 **状态**: accepted（规划已确认；决策 7.1 = 方案 D「写法即语义」）

## 0. 缘起与硬约束

用户原话：「**不允许任何的抑制,而是像 Rust 一样计算生命周期,如果生命是被容器持有的话,就需要继续查看这个容器是否让 task 数组阻塞全部释放**」「**复杂可以通过状态机、规则引擎等等方式化解**」。

由此导出三条不可协商的硬约束：

| # | 约束 | 推论 |
|---|------|------|
| C1 | **禁止任何抑制** | 不接受 `#pragma warning disable` / `[SuppressMessage]` / `.editorconfig` 关规则作为"修复"。命中即真问题,要么改代码要么改分析器(分析器漏报=分析器 bug)。 |
| C2 | **Rust 风格生命周期计算** | 不是"方法内是否调 Dispose"的局部判断,而是**所有权 + 借用 + 转移 + 容器持有**的全链追踪。变量被容器持有时,继续追踪容器的释放完备性。 |
| C3 | **复杂度用状态机 + 规则引擎化解** | 不写一个巨型分析器,而是拆成多条规则 + 一个共享所有权事实库 + 每实例一个状态机。规则引擎编排,状态机驱动状态迁移。 |

## 1. 目标与非目标

### 1.1 目标
1. **零误报零漏报**(对资源泄露而言):每个 IDisposable/IAsyncDisposable 实例的归宿被精确判定 — 释放、转移、或被容器完备释放。误报=分析器 bug,漏报=分析器 bug。
2. **容器释放完备性**:持有 IDisposable 元素的字段/集合,其 Dispose/DisposeAsync 必须**完备释放**所有元素(逐一 Dispose 或 await Task.WhenAll 阻塞全部完成)。
3. **借用不报**:从字段/参数/共享源(GetXxx/Pool/Cache)读取的引用是借用,不要求释放。
4. **可扩展**:新场景(如新容器类型、新借用源)通过新增规则实现,不改核心引擎。

### 1.2 非目标
- 不做全程序跨程序集分析(单类型 + 同编译单元字段/方法 + 已知容器类型足够,跨程序集契约用注解声明)。
- 不替代运行时泄露检测(GC finalizer / SafeHandle),而是编译期静态防护层。
- 不做并发竞态分析(那是 ConcurrencyRules 职责),只做所有权/生命周期。

## 2. 当前问题(P0 JCC9305 的局限,用真实命中举证)

P0 已落地的 `LocalDisposableLeakRule`(JCC9305)做"方法内局部判断",在 `server/code_index` 上跑出 31 处命中,分三类:

### 2.1 真泄露(规则正确,代码需修)
| 位置 | 模式 | 归宿 |
|------|------|------|
| `BashAstParser.cs:33` `var tree = _parser.Parse(cmd); return tree?.RootNode;` | 读取成员后丢弃 tree | tree 遗漏释放 → **真泄露** |
| `ArgumentTypeCoercer.cs:131/139` `var arr = JsonDocument.Parse(str); return (arr.RootElement.Clone(), true);` | JsonDocument 未 using | **真泄露**(`.Clone()` 转移数据,arr 应 Dispose) |
| `ChunkDownloader.cs:46` `var fileStream = ...` | 需确认 | 待分类 |
| `EmbeddingModelDownloader.cs:104` `var client = ...` | HttpClient 局部 new | 待分类(可能真泄露) |

### 2.2 误报:借用被当拥有(规则需增强 — 本设计核心)
| 位置 | 模式 | 为何误报 |
|------|------|---------|
| `DeferredMailService.cs:22/39/66/91` `var lk = GetLock(mail.To);` | GetLock 返回字段持有的共享锁 | `lk` 是**借用**,不拥有,不应释放。JCC9305 把"调用返回 IDisposable"一律当拥有 → 误报 |
| `IntentCollector.cs` `var lk = ...` | 同上 | 借用 |
| `IOThrottleService.cs:78/121` `var lockObj = ...` | 同上 | 借用 |
| `TokenBucket.cs:72` `var guard = ...` | 需确认借用源 | 待分类 |

### 2.3 误报:容器持有被当拥有(规则需增强 — 用户 C2 核心诉求)
| 位置 | 模式 | 为何误报 |
|------|------|---------|
| `AsyncLockedDictionary.cs:47` `var keyLock = _keyLocks.GetOrAdd(key, _ => new AsyncLock(...));` | keyLock 存入 `_keyLocks` 字段(容器) | keyLock 所有权转给 `_keyLocks`。是否泄露取决于 `_keyLocks` 容器的 Dispose 是否完备释放所有 AsyncLock。JCC9305 看不到容器层 → 误报 |
| `EnvironmentSnapshot.cs:141/142` `var stdoutTask = process.StandardOutput.ReadToEndAsync(ct);` | Task 由 process 持有 | process `using var` 释放时,Task 的阻塞语义需追踪(用户原话"task 数组阻塞全部释放") |

### 2.4 结论
JCC9305 的"方法内 + 调用返回即拥有"启发式**同时产生漏报(容器持有后不再追踪)和误报(借用当拥有)**。本设计用所有权模型替代启发式,根除两类问题。

## 3. 核心概念(对齐 Rust,适配 C#)

### 3.1 所有权三态 + 生命周期
每个 IDisposable/IAsyncDisposable **实例**有一个所有者(owner),实例处于四态之一:

```
            new / Create / 接管返回值
   ┌──────────────────────────────────────┐
   │                                      ▼
┌────────┐  release     ┌─────────┐  move    ┌────────┐
│Allocated│ ──────────▶ │Released │ ───────▶ │ Moved  │
└────────┘  (Dispose/   └─────────┘          └────────┘
   │       DisposeAsync)   ▲                     │
   │                       │                     │
   │ escape into container  │                     │
   ▼                       │                     │
┌──────────────┐ 容器完备释放 │                     │
│ContainerHeld │ ────────────┘                     │
└──────┬───────┘                                   │
       │ 容器 Dispose 不完备                         │
       ▼                                           │
   ┌──────┐                                        │
   │ Leak │ ◀──────────────────────────────────────┘
   └──────┘  (Moved 后新所有者未尽责,由其规则追)
```

- **Allocated(拥有)**:当前作用域持有唯一所有权,退出前必须 release / move / escape。
- **Released(已释放)**:Dispose/DisposeAsync 已调用,归宿完成。
- **Moved(已转移)**:所有权转给新所有者(`return x` / `_field = x` / `TakeOwnership(x)`),原作用域不再负责。新所有者由其所在规则继续追(字段→容器规则,参数→契约规则)。
- **ContainerHeld(容器持有)**:所有权转给容器(字段/集合/数组),由**容器释放完备性规则**追容器 Dispose 是否释放该实例。
- **Leak(泄露)**:Allocated 退出作用域未归宿,或 ContainerHeld 的容器未完备释放 → 报诊断。

**生命周期**:每个所有者有生命周期区间。容器生命周期必须 ≥ 其持有任意元素的生命周期(Rust: `'container: 'element`)。容器先于元素释放 = 泄露。

### 3.2 借用(Borrowed)— 不进入状态机
`var lk = GetLock(...)` 中 `lk` 是借用:不拥有,不要求释放,不进入所有权状态机。借用识别见 §5 L4。

### 3.3 容器释放完备性(用户 C2 核心)
容器(字段/集合/数组)持有 IDisposable 元素时,容器 Dispose 必须**完备释放**:

| 容器形态 | 完备释放要求 | 检测规则 |
|---------|------------|---------|
| `IDisposable _field` | Dispose 中 `_field.Dispose()` | JCC9301(已有) |
| `List<IDisposable>` / `IDisposable[]` | Dispose 中 `foreach(x) x.Dispose()` 或等价 | **JCC9306 新** |
| `List<Task>` / `Task[]` | DisposeAsync 中 `await Task.WhenAll(tasks)`(阻塞全部完成)再释放底层 | **JCC9307 新** |
| `Dictionary<K, IDisposable>` | Dispose 中遍历 `Values` 逐一 Dispose | JCC9306 |
| `ConcurrentDictionary<K, AsyncLock>` | Dispose 中遍历 `Values` 逐一 Dispose | JCC9306 |

**不完备 = 泄露**:容器 Dispose 漏掉任一元素、或 Task 数组未 await WhenAll(任一 Task 未阻塞完成就释放底层资源)→ 报 JCC9306/9307。

## 4. 架构:状态机 + 规则引擎 + 所有权事实库

```
┌─────────────────────────────────────────────────────────────┐
│              OwnershipFacts (所有权事实库,编译期累积)         │
│  ┌─────────────┐  ┌──────────────┐  ┌────────────────────┐  │
│  │ Var→State   │  │ Container→   │  │ DisposeChain→      │  │
│  │ (状态机实例)│  │ Elements     │  │ ReleasedElements   │  │
│  └─────────────┘  └──────────────┘  └────────────────────┘  │
└──────────────────────────┬──────────────────────────────────┘
                           │ 读/写事实
┌──────────────────────────▼──────────────────────────────────┐
│                 RuleEngine (规则引擎,编排)                   │
│  按优先级调度规则,每规则读事实→推断→写事实→报诊断            │
│  多趟:收集事实(T1) → 推断状态(T2) → 验证完备性(T3)         │
└──────────────────────────┬──────────────────────────────────┘
                           │
   ┌───────────┬───────────┼───────────┬───────────┬─────────┐
   ▼           ▼           ▼           ▼           ▼         ▼
┌────────┐ ┌────────┐ ┌────────┐ ┌────────┐ ┌────────┐ ┌────────┐
│L1 局部 │ │L2 容器 │ │L3 容器 │ │L4 借用 │ │L5 跨方 │ │L6 状态 │
│所有权  │ │持有    │ │释放完备│ │识别    │ │法契约  │ │机迁移  │
│JCC9305 │ │JCC9306a│ │JCC9306/│ │(启发/ │ │JCC9308 │ │(驱动   │
│增强    │ │        │ │9307    │ │注解)   │ │        │ │状态变迁)│
└────────┘ └────────┘ └────────┘ └────────┘ └────────┘ └────────┘
```

- **OwnershipFacts**:编译期共享的不可变事实库,规则间通过它通信(不直接互调)。三张表:变量状态、容器→元素集合、Dispose 链→已释放元素集合。
- **RuleEngine**:按优先级 + 多趟调度。T1 收集(扫字段/集合元素/Dispose 体),T2 推断(变量状态迁移),T3 验证(容器完备性)。多趟保证 L2 收集完容器元素后 L3 才验证。
- **状态机**:每个 IDisposable 实例一个状态机对象,规则通过事件(Release/Move/Escape)驱动状态迁移,迁移到 Leak 时报诊断。状态机封装迁移合法性,规则只发事件。

## 5. 规则分层与 ID 分配

| 层 | 规则 | ID | 职责 | 状态 |
|----|------|----|------|------|
| L1 | LocalOwnershipRule | JCC9305(增强) | 局部变量所有权判定:Owned→release/move/escape,借用不报。替代当前启发式 | ✅ P0 已落地,需重构为发事件而非直接报 |
| L2 | ContainerHeldRule | JCC9306a | 字段/集合/数组持有 IDisposable → 标记元素为 ContainerHeld,记录容器→元素 | 🆕 阶段1 |
| L3 | ContainerReleaseCompletenessRule | JCC9306 / JCC9307 | 容器 Dispose 是否释放/await 所有元素。Task 数组需 WhenAll 阻塞 | 🆕 阶段2 |
| L4 | BorrowInferenceRule | —(不发诊断) | 区分 GetShared(借用) vs Create(拥有):启发式 + 注解 | 🆕 阶段1 |
| L5 | CrossMethodContractRule | JCC9308 | 方法参数/返回值所有权契约([Owned]/[Borrow] 注解或签名启发) | 🆕 阶段3 |
| L6 | StateMachineDriver | —(不发诊断) | 驱动状态迁移,迁移到 Leak 报 JCC9305/9306/9307 | 🆕 阶段1 |

## 6. 分阶段实施计划

> 每阶段:红测试(复现误报/漏报) → 实现 → 编译 → 绿测试 → 在 `server/code_index` + `lib` 上评估命中 → commit。禁止抑制,命中即修代码或修规则。

### 阶段 0(已完成)
- ✅ JCC9305 P0:局部变量方法内泄露检测。11 测试绿,150 套件全绿。

### 阶段 1:借用识别 + 容器持有事实库(消除 §2.2/§2.3 误报)
**目标**:JCC9305 不再误报借用(lk/keyLock)和容器持有(keyLock 存入 _keyLocks)。

- 引入 `OwnershipFacts` + `StateMachineDriver` 基建。
- L4 BorrowInferenceRule:方法调用返回 IDisposable 时,按 §7.1 决策识别借用/拥有。借用变量不进状态机。
- L2 ContainerHeldRule:变量赋值给字段/插入集合(`_field = x` / `coll.Add(x)` / `_dict[k] = x`)→ 标记 ContainerHeld,记录容器→元素。
- L1 重构:不再直接报诊断,改为发事件(Release/Move/Escape)给状态机;状态机迁移到 Leak 才报。
- **验收**:§2.2/§2.3 的 8 处误报全部消除;§2.1 真泄露(BashAstParser/ArgumentTypeCoercer)仍报。

### 阶段 2:容器释放完备性(JCC9306/9307 — 用户 C2 核心)
**目标**:持有 IDisposable 的容器,Dispose 不完备释放 → 报。

- L3 ContainerReleaseCompletenessRule:
  - 集合容器:Dispose 体遍历元素逐一 Dispose(识别 `foreach(x in c) x.Dispose()` / `c.Values` 遍历)。
  - Task 数组/列表:DisposeAsync 体含 `await Task.WhenAll(tasks)`(阻塞全部完成)。缺 WhenAll 或仅 await 部分 → JCC9307。
  - 不完备 → 报 JCC9306(集合)/JCC9307(Task)。
- **验收**:构造反例(容器 Dispose 漏元素 / Task 数组缺 WhenAll)→ 报;正例(完备)→ 不报。在真实代码上找容器持有点验证。

### 阶段 3:跨方法契约 + 精化(JCC9308 — 已完成)
**目标**:方法参数/返回值所有权契约,消除跨方法转移的漏报。

方案 D 落地(无注解,分析方法体判定):
- `CrossMethodContractAnalyzer` 辅助类:JCC9305 传参时调用,区分转移(不报)与借用(仍需释放)。
- **转移契约判定**(项目内方法):分析方法体,参数赋值字段/集合/return/调 Dispose = 转移;只读取 = 借用。跨语法树用 `compilation.GetSemanticModel(syntaxTree)`。
- **BCL 方法分类**:集合 Add/Insert/Push/Enqueue = 转移;Task.FromResult/Interlocked.Exchange/Options.Create = 转移(包装返回/存字段);其他 BCL 方法 = 借用(只读取)。
- **未知外部方法**(无源码):保守转移(避免误报)。
- **JCC9305 增强**:`IsFieldReceiverBorrowCall` 加 Try 前缀(字段接收者 TryAcquire/TryGet = 借用);`ContainerElementAccessMethods` 加 GetService/GetRequiredService(DI 获取 = 借用)。
- **JCC9104 修复**:双实现 IDisposable+IAsyncDisposable 类型(MemoryStream 等)用 `using var` 合法,规则只对"仅 IAsyncDisposable"报。
- **验收**:9 单元测试全绿(借用方法报/转移方法不报/BCL 借用报/集合 Add 不报);全量编译 0 命中;20+ 处真实漏报修复(using var/await using var)。

### 阶段 4(可选):全程序调用图 + 容器嵌套 — ✅ 已完成
- **JCC9301 状态机重写**:用 `Ownership` 状态机(Unknown→Owned/Borrowed/Skip→Released/Leaked)驱动字段所有权分析,替代旧版启发式。`ClassifyExpression` 判定 `new`/`ImplicitNew`/工厂返回 IDisposable=Owned,构造函数参数=Borrowed,null=Skip。
- **DI 容器获取识别为借用**:`GetService`/`GetRequiredService`/`GetKeyedService`/`GetRequiredKeyedService` 返回对象由 DI 容器管理生命周期,判定为 Borrowed 而非 Owned。
- **释放方法名扩展**:`PostStop`/`PostStopAsync`(ActorBase 钩子)+ `OnResourceDispose`(PluginResourceBase 钩子)加入 `IsDisposeMethodName`,让 `CheckInDisposeCallChain` BFS 追到这些释放路径。
- **`IsFieldReference` 支持 `self.field` 模式**:`MethodBodyNullsField` 原只检查 `_field = null`(IdentifierName),现也检查 `self._field = null`(MemberAccess),覆盖 lambda/ContinueWith 内的置 null。
- **源码修复**:全量编译暴露 50+ 处真实泄露(Owned 字段未释放/JCC9302 释放后未置 null),覆盖生产代码(Infrastructure/Guard/Scheduling/Bridge/CodeIndex/Hands/Clock/Agents)+ 测试代码,全部修复。
- **验收**:199 分析器测试全绿;全量编译 0 命中(0 JCC9301 + 0 JCC9302)。

## 7. 关键决策点(需用户确认)

### 7.1 借用识别方式(L4 规则核心)
**问题**:`var lk = GetLock(...)` 的 `lk` 是借用还是拥有?C# 无语法标记,需选择识别策略。

| 方案 | 机制 | 优 | 劣 |
|------|------|----|----|
| A. 纯启发式 | 方法名前缀:`Get*`/`Acquire*`/`Borrow*`/`Shared*`/`Pool*`/`Cache*` → 借用;`new`/`Create*`/`Build*` → 拥有 | 零侵入,现有代码直接工作 | 启发式有边界,误判需改方法名 |
| B. 纯注解 | `[Borrow]`/`[Owned]` 标记方法返回/参数 | 精确无歧义 | 需大量标注现有代码,初始成本高 |
| C. 启发式 + 注解覆盖 | 默认用 A 启发式;`[Borrow]`/`[Owned]` 注解显式覆盖启发式 | 渐进:零侵入起步,边界 case 用注解钉死 | 两套机制需文档约定优先级(注解 > 启发式) |
| **D. 写法即语义(已选)** | 让写法本身区分:`new`/工厂返回 IDisposable=拥有;借用源返回非 IDisposable 句柄;字段读/已知容器 API=借用 | 零注解零启发式,语义在写法里 | 借用源 API 须改返回非 IDisposable 句柄(改主代码) |

**方案 D 落地(已确认)**:

| 写法 | 所有权 | 识别机制 |
|------|--------|---------|
| `new X()` | 拥有 | 语法(ObjectCreationExpression) |
| `Method()` 返回 IDisposable | 拥有(工厂创建转移所有权约定) | 返回类型实现 IDisposable |
| `Method()` 返回非 IDisposable 句柄(如 `LockRef`) | 借用 | 类型非 IDisposable,规则不触发 |
| `_field` 字段读 | 借用 | MemberAccess 到字段符号 |
| 已知容器 API(`GetOrAdd`/`TryGetValue`/索引器/`Add` 等) | 借用 | 有限已知集合,非启发式 |
| 参数 / 局部变量读 | 借用 | 符号种类 |

落地三步:① 规则增强识别字段读/容器 API 借用(不改主代码,先做) ② 改写自有借用 API 返回非 IDisposable 句柄(`GetLock`→`LockRef`,改主代码) ③ BCL 容器 API 由①的已知集合识别,无需改 BCL。

### 7.2 Task 数组"阻塞全部释放"的语义(JCC9307)
**问题**:用户说"task 数组阻塞全部释放"。需明确阻塞语义。

| 方案 | JCC9307 触发条件 |
|------|----------------|
| A. 严格阻塞 | 容器 DisposeAsync 必须含 `await Task.WhenAll(_tasks)`,缺即报。fire-and-forget 后台释放不算完备 |
| B. 阻塞或显式后台契约 | `await WhenAll` 或显式 `[FireAndForgetRelease]` 注解声明后台释放契约。缺两者即报 |

### 7.3 分析范围
| 方案 | 范围 | 复杂度 |
|------|------|--------|
| A. 单类型内 | 字段 + 方法 + 同类容器 | 低,阶段1-2 |
| B. 同编译单元 | + 跨类型字段注入 | 中,阶段3 |
| C. 全程序调用图 | + 跨程序集 | 高,阶段4 |

## 8. 反例清单(规划阶段识别的最坏代码模式)

| 反例 | 当前 JCC9305 | 本设计后 |
|------|-------------|---------|
| `var x = new D();` 无释放 | ✅ 报 | ✅ 报(状态机:Allocated→Leak) |
| `var x = GetShared();` 借用 | ❌ 误报 | ✅ 不报(L4 识别借用) |
| `var x = new D(); _field = x;` 转字段 | ✅ 不报(误打误撞) | ✅ 不报(Move,字段由 JCC9301 追) |
| `var x = new D(); _list.Add(x);` 转集合 | ❌ 漏报(集合未追) | ✅ 由 JCC9306 追集合完备性 |
| `var x = new D(); return x?.Foo;` 读取成员后丢 | ✅ 报 | ✅ 报(读取非转移) |
| `var x = new D(); return x ?? throw;` coalesce 转移 | ✅ 不报(P0 已修) | ✅ 不报(Move) |
| 容器 Dispose 漏元素 | ❌ 漏报 | ✅ 报 JCC9306 |
| `Task[]` 容器 DisposeAsync 缺 WhenAll | ❌ 漏报 | ✅ 报 JCC9307 |
| `var x = new D(); borrowMethod(x);` 传参给借用方法 | ❌ 漏报(传参当转移) | ✅ 报 JCC9305(跨方法契约判定) |

## 9. 验收表(AGENTS.md 四列强制)

| 基建实现 | 消费点 | 已实现 | 已验收 |
|---------|--------|--------|--------|
| `LocalDisposableLeakRule` JCC9305 (`gen/aot_safety.generator/rules/memory_leak/LocalDisposableLeakRule.cs`) | MemoryLeakRules 入口;11 单测 | ✅ | ✅ 单测全绿;`server/code_index` 31 命中(含真泄露 BashAstParser/ArgumentTypeCoercer) |
| `OwnershipFacts` 事实库 | L1-L5 规则读写 | ❌ 阶段1 | ❌ |
| `StateMachineDriver` 状态机 | L1 发事件→迁移→Leak 报 | ❌ 阶段1 | ❌ |
| `BorrowInferenceRule` L4 | L1 查借用判定 | ❌ 阶段1 | ❌ |
| `ContainerHeldRule` L2 (JCC9306a) | L1 escape 事件→标记 ContainerHeld | ❌ 阶段1 | ❌ |
| `ContainerReleaseCompletenessRule` L3 (JCC9306/9307) | 容器 Dispose 完备性验证 | ❌ 阶段2 | ❌ |
| `CrossMethodContractAnalyzer` L5 (JCC9308) (`gen/aot_safety.generator/rules/memory_leak/CrossMethodContractAnalyzer.cs`) | JCC9305 传参时调用,区分转移/借用 | ✅ | ✅ 9 单测全绿;20+ 处真实漏报修复(using var/await using var) |

## 10. 风险与缓解

| 风险 | 缓解 |
|------|------|
| 所有权分析复杂度爆炸 | 状态机 + 规则引擎拆解;每规则单职责;多趟固定调度 |
| 启发式借用识别边界 case 误判 | 决策 7.1C:注解覆盖;边界 case 用 `[Borrow]` 钉死 |
| 真实代码命中量大,修复阻塞 | 渐进式:阶段1 先消除误报(不改主代码),阶段2 起真泄露逐个修;每阶段独立 commit |
| 分析器自身性能(多趟+事实库) | 事实库用 ConcurrentDictionary + 符号键;规则按需注册;CompilationEnd 汇总 |
| TreatWarningsAsErrors 导致新规则阻断编译 | 新规则先在测试项目验证;主项目启用前先跑全量评估,真泄露先修;规则默认 Warning,经评估后可升 Error |

---

<!-- 🤖 Auto Decision: 2026-10-06 -->
<!-- 决策: 采用 Rust 风格所有权+生命周期模型,而非局部启发式 -->
<!-- 原因: 用户明确要求"像 Rust 一样计算生命周期"+ 禁止抑制;局部启发式同时产生误报(借用)和漏报(容器持有),根因是缺乏所有权模型 -->
<!-- 替代方案: 1) 扩展启发式逐 case 打补丁(治标,复杂度无界) 2) 引入 IL 数据流分析(运行期,非编译期,偏离分析器定位) -->
<!-- 验证: 规划文档待用户确认;P0 JCC9305 已落地验证局部可行性 -->
