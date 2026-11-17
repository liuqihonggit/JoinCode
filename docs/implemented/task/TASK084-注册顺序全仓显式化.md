# TASK084 — 注册顺序全仓显式化：清掉接口层残留的数字排序

> 状态：进行中
> 起点：TASK082 第二十九~三十一轮（TASK082 的显式顺序改造已把 `AppModule` / `PromptSection` 两处
> 特性排序改成「注册名 + YAML」，但只覆盖了**特性层**；接口层的 `Priority` / `Order` 数字排序仍在运行）。

## 一、为什么要做

上一轮用户的要求是「**清理掉所有的**特性的排序功能，因为特性用来反射特性或者源码生成器来收集数字进行排序
这种做法不直观。该用注册名称 + 写 yml 来显式设计」。

全仓复查结论（2026-11-18）：

| 层面 | 复查结果 |
|---|---|
| 特性上的排序参数（`[Xxx(Order=…)]` / `Priority=`） | **0 处** —— 上一轮已清干净 |
| 接口上的排序成员（`int Priority` / `int Order`） | **6 个接口、17 个实现类**仍在用硬编码数字驱动运行时排序 |

后者与前者是**同一种反模式**：靠一个数字表达「谁先谁后」，读代码的人必须把所有实现的数字横向比对一遍，
才知道实际顺序；而且**数字与注册名是两套标识** —— 改个类名要同步改数字，改个数字要重新确认全序。
八个命令守卫用了 30 / 50 / 100 / 200 / 500 / 800 / 900 / 1000 八个离散值，中间的差值毫无语义：
只有相对顺序有意义，数字本身既冗余又容易漂移。

## 二、本轮范围

### 2.1 迁移到 YAML 显式顺序的 6 个机制

| 机制 | 实现数 | 现状 | 迁移后 |
|---|---|---|---|
| `command_guards` | 8 | `OrderByDescending(Priority)` 1000→30 | YAML 显式顺序 |
| `command_interceptors` | 0 | 接口存在但无实现 | YAML 空列表 |
| `tool_fix_hooks` | 3 | `OrderByDescending(Priority)` 100→80→60 | YAML 显式顺序 |
| `agent_tool_middlewares` | 5 | `OrderBy(Order)` 100→500 | YAML 显式顺序 |
| `query_stop_hooks` | 0 | 无实现 | YAML 空列表 |
| `git_security_interceptors` | 1 | `Priority => 100`，**从未被任何代码排序** | 直接删成员（该列表因此不建） |

### 2.2 设计

- 唯一数据源：`resources/orders.yaml`，按「列表名 → 注册名列表」声明，**列表顺序 = 执行顺序**；
  最终 5 个列表 / 16 项 —— 只有**确实参与排序**的注册表才建列表，没有消费方的列表不建（死数据）。
- 契约层新增 `OrderIndex`（`abstractions.primitives.utils`），内嵌 `orders.yaml`：
  - `Require(list, name)`：未登记直接抛异常，错误信息说明「哪个列表少了哪个名字、怎么补」；
  - 未登记的实现 → **启动期即失败**，不允许静默按未知顺序执行（守卫链尤其不能）。
- 注册名统一用**实现类名**（`GetType().Name`）；`IToolFixHook` / `IQueryStopHook` 本来就有 `Name`，值也等于类名。

### 2.3 明确不做

- `SearchProviderCoordinator`（provider 选择优先级）、`CompressionStrategyFactory`（策略权重）、
  `AgentPermissionMode` / `RemotePolicyService`（规则优先级）、`NodeRegistry`（来自 YAML 节点配置的 `Priority`）
  —— 这些是**领域数据权重**，不是注册顺序，语义上就该由数据决定，不在「注册顺序显式化」范围内。

## 三、验收（真机为准，单元测试不作为挡箭牌）

- [x] `OrderIndex` 契约层测试：已登记 / 未登记 / 空列表 / 重复名 / 列表非序列 / 缺 lists 映射。
- [x] 6 个接口的 `Priority`/`Order` 成员与 17 个实现的数字**全部改为由表派生**（其中 `IGitSecurityInterceptor` 的成员从未参与排序，直接删除），全仓 grep 字面量为 0。
- [x] 迁移前后**执行顺序完全一致**（守卫链 8 / hook 3 / 中间件 5，基线写在对账脚本里）。
- [x] 真机：跑真实 exe 触发守卫链，从调试日志读出实际执行顺序，与 YAML 逐项比对。
- [x] `resources/orders.yaml` ↔ 代码双向对账（每个实现都在表里；表里没有不存在的实现）。
- [x] 受影响工程全部编译通过 + 相关测试全绿。

## 四、真机证据

### 4.1 守卫链实际装配顺序（`JCC_DEBUGLOG=1` 真机）

```
$ JCC_DEBUGLOG=1 JCC_ALLOW_DIRECT_FALLBACK=1 jcc mcp_call bash '{"command":"echo order-probe"}'
[ORDER] command_guards: GitCommitGuard > DriveProtectionGuard > CmdIndirectCallGuard > RobocopyMirrorGuard > HeredocGuard > GhPrBodyGuard > GhTimeoutGuard > VpnRouteGuard
[ORDER] command_interceptors:
```

与 `resources/orders.yaml` 的 `command_guards` 列表、与迁移前 `Priority` 降序基线**逐项一致**。

打点位置：`CommandInterceptionDispatcher` 构造函数（守卫链顺序直接决定谁能先拒绝命令，
而顺序只存在于 YAML 里，日志是唯一的现实核对口）；`ToolFixHookRegistry.TryFixAsync`；
`AgentWorktreeService` 建管线时。三处都受 `JCC_DEBUGLOG` 开关控制。

### 4.2 对账脚本

```
$ python non_deliverables_tools/scripts/Verify-RegistrationOrder.py
注册顺序表 5 个列表 / 16 项
orders.yaml 与源码实现双向一致，位次全部由表派生，无字面量数字。
```

已接进 CI：`ci-unit-tests.yml` 新增 `registration-order` job。

### 4.3 编译与测试

| 范围 | 结果 |
|---|---|
| `Abstractions.Primitives.Utils` / `Abstractions.Guard` / `Guard.Security` / `Guard.Hooks` / `Hands.Core` / `Brain.Query` / `Agents.Services.Support` / `JoinCode` | 全部 0 警告 0 错误 |
| `Guard.Hooks.Tests` | 598 通过（原 597，断言从「字面量数字」改为「由表派生 + 相对次序不变」） |
| `Brain.Other.Tests` | 426 通过 |
| `McpToolDispatch.Tests` | 524 通过 |
| `Host.Tests` | 1298 通过（原 1288，新增 `OrderIndexTests` 10 个） |

## 五、关键设计决策与理由

### 5.1 为什么接口保留 `Priority` 成员，而不是彻底删掉

彻底删掉成员会更「纯」，但代价是：

- 6 个接口里有 3 个是**插件可扩展的注册表**（`IToolFixHook` 由 `RegisterDefaultFixHooks()` 填充、
  `IQueryStopHook` 由 `RegisterStopHook` 填充）。删掉成员后，插件怎么声明自己的先后？
  只能再加一张表 —— 那就变成两套机制。
- 仓库里 `Priority` 有**两种相反语义**：守卫链是「数字大 = 先」，中间件与停止 hook 是「数字小 = 先」。
  同一个名字两种含义，正是数字排序弊病最直接的证据。删成员要同时统一所有消费方，
  会把 4 个注册表的排序逻辑全部改写 —— 安全相关的守卫链也在其中。
- 测试替身（Moq mock、fake guard）大量直接提供 `Priority`；彻底删成员要重写 6 个测试文件，
  而这些测试断言的是**注册表机制**（链式改写、拒绝短路、fail-closed），不是数字本身。

所以选择：**成员保留，值一律由表派生**。`OrderIndex.Default.PriorityOf("command_guards", nameof(X))`
让实现类里不再有作者手调的常量，YAML 成为唯一数据源；「不许再写死数字」由对账脚本守住
（`LITERAL_NUMBER` 正则扫描实现目录，有字面量即 CI 失败）。

### 5.2 同一份 YAML 的两种派生方向

- `PriorityOf` → 「数字大 = 先执行」（守卫链、拦截器链、工具修正 hook）
- `OrderOf` → 「数字小 = 先执行」（Agent 工具中间件、查询停止 hook）

两者都从同一张表出发，只是适配各自消费方既有的排序约定，**执行顺序因此保持逐项不变**。

### 5.3 未登记 = 启动期抛异常

`OrderIndex.Require` 对未登记的注册名直接抛异常，错误信息包含：列表名、注册名、
数据源路径、当前已登记的内容 —— 按项目规范第 9 条「错误提示必须有诱导方式」。
守卫链尤其不能「按未知顺序执行」：新守卫若没登记就静默排到最后，
可能被先执行的 `GitCommitGuard` 重定向掉，安全性静默下降。

## 六、记录

- 2026-11-18：实现、编译、真机验证、对账脚本与 CI 门禁全部完成。

