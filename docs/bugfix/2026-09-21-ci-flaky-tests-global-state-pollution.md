# Bug 日志:CI 偶发测试失败 — 全局静态状态并行污染

**日期**:2026-09-21
**PR**:#265
**触发**:用户要求排查 CI 第一次执行错误 + 同类问题 + 生成 bug 日志

---

## 已修复 Bug

### Bug 1: AsyncLock ScanHolds 竞态崩进程

- **CI**:run 35530611620,job `unit-tests / Unit - AsyncLock`,attempt 1
- **症状**:`Test host process crashed`,`InvalidOperationException: Nullable object must have a value` at `Core.Utils.LockRegistry.ScanHolds()`,Passed: 110, Total: Unknown,Test Run Aborted
- **根因**:`ScanHolds` 在后台 Timer 线程用 `HasValue`+`.Value` 两步式访问 `DateTimeOffset?` 字段(`AcquiredAt`/`WaitStartedAt`),锁释放线程在两步之间置 null,导致 `.Value` 抛异常,未处理异常冒到 Timer 顶终止进程
- **时序**:`LOCK-ACQUIRED`(54.098)→ `LOCK-RELEASED`(54.603,置 null)→ `ScanHolds` 抛异常(55.709,定时器回调触发)
- **修复**:三层纵深防御
  - 层1 根因:`ScanHolds` 改 `long` ticks 局部取值,消除两步式竞态窗口
  - 层2 兜底:Timer 回调包 `ScanHoldsSafe` try-catch,任何异常只记日志不崩进程(防进程崩的关键层)
  - 层3 原子:`DateTimeOffset?` → `long` ticks(0 哨兵),64位天然原子读写,消除16字节结构撕裂
- **commit**:`7c71453f7`
- **验证**:AsyncLock.Tests 183 通过 0 失败

### Bug 2: 6 处 Timer 回调缺 try-catch 兜底

- **CI**:同类排查(未导致失败但存在同类风险)
- **根因**:`System.Threading.Timer` 回调里任何未处理异常冒到 ThreadPool 顶 → 终止整个进程。6 处回调直接执行逻辑无 try-catch
- **修复**:6 处回调加 try-catch(异常只记日志不崩进程)
  1. `V1ReplBridgeTransport.FlushStreamEvents` — dispose 后 `_disposeCts.Token` 抛 `ObjectDisposedException`
  2. `FastModeService` cooldown — `Deactivate` 内 `TryLock` 超时抛 `TimeoutException` + 事件可抛
  3. `DebounceTracker` fire — 外部 `fireAction` 可抛(无 logger 用 `Console.Error.WriteLine`)
  4. `SystemActuatorCommandContext.HandleTimeout` — `Background` IO + `Backgrounded` 事件
  5. `SystemActuatorCommandContext` assistantTimer — 同 HandleTimeout 路径
  6. `SystemActuatorCommandContext` sizeWatchdog — `GetCurrentStdoutLength` dispose 后可抛
- **commit**:`d65e28864`
- **验证**:Guard.Config.Tests 1119 通过 + Hands.Tests 306 通过
- **排查结论**:竞态字段无同类高风险(LockRegistry 是唯一);Timer 回调 6 处同类缺口已全补,其余 19 处已兜底(TrySend Actor / 内部 try-catch / async-void try-catch)

### Bug 3: Host.Tests ApiKeySaveLoadTests 全局静态污染

- **CI**:run 560,job `unit-tests / Unit - Host`,attempt 1
- **症状**:`Execute_WithQuickAlias_Q_Should_Behave_Like_Quick` 偶发失败(`Expected boolean to be True, but found False`)
- **根因**:`ApiKeySaveLoadTests` 修改全局静态 `AppDataConstants.Paths`(有 finally 恢复),与 `InitCommandTests` 并行运行时(xUnit 默认 `CollectionPerClass` → 不同类并行),`InitCommand` 读到被污染的 `AppDataFolder`,导致创建的 jccDir 与测试断言的 expectedJccDir 不一致,`DirectoryExists` 偶发 false
- **修复**:注入式治本(非串行化)
  - `ConfigLoader.SaveApiKeyToJccAsync`/`LoadApiKeyFromJccAsync`/`LoadAuthFileAsync` 加 `AppDataPaths? paths = null` 可选参数(默认全局,现有调用方不改)
  - `ApiKeySaveLoadTests` 注入 paths,不再修改全局 `AppDataConstants.Paths`,消除并行测试静态状态污染源
- **commit**:`30e6a43ad`(含回滚 `275b5b1bf` 串行化方案)
- **验证**:Host.Tests 1040 通过 0 失败(2s,恢复并行)

---

## 同类风险(已排查,有恢复但并行窗口可污染)

| 位置 | 全局状态 | 恢复机制 | 风险 | 说明 |
|------|----------|----------|------|------|
| `test/unit/host.tests/.../BridgeMainCommandGuardIntegrationTests.cs` | 环境变量 `SessionAccessToken`/`OAuthToken` | finally 恢复 | **中** | host.tests 并行,窗口内可污染其他读环境变量的测试 |
| `test/unit/host.tests/.../ExecuteCommandTests.cs:52` | `Console.SetOut` | finally 恢复(73行) | **中** | Console 是进程级全局,并行测试输出可能交错 |
| `test/unit/infra.tests/process/GitHubApiClientTests.cs:59,70` | 环境变量 `JCC_GITHUB_TOKEN`/`GITHUB_TOKEN` | EnvVarScope + Dispose | **中** | 方法内直接改不进 EnvVarScope,Dispose 统一清理 |
| `test/unit/hands.tests/desktop/DesktopEnvironmentGuardTests.cs` | 环境变量 `CI`/`GITHUB_ACTIONS` | finally 恢复 | **低** | 只读环境变量不写文件,后果有限 |
| `test/mock/sync.integration.tests/` | 环境变量 `AppDataFolder` | try-finally | **低** | 集成测试并行度低 |
| `test/integration/integration.tests/` | 环境变量 `JCC_REPL_MODE`/`AppDataFolder` | try-finally | **低** | 集成测试并行度低 |

---

## 修复策略对比

| 方案 | 手法 | 优缺点 |
|------|------|--------|
| **注入式治本(采用)** | 给生产方法加 `AppDataPaths?` 可选参数,测试注入不改全局 | 治本,符合可插拔设计;现有调用方不改(默认全局) |
| 串行化整个程序集 | `[assembly: CollectionBehavior(CollectionPerAssembly)]` | 简单但过度,无静态依赖的测试也被串行 |
| 精准 Collection | 只把读/写全局状态的测试放同一 `[Collection]` | 精准但需找全所有读方,漏标仍有风险 |

---

## 建议

1. **已修复(高优先级)**:Bug 1/2/3 已通过注入式 + 纵深防御修复,PR #265
2. **中风险(建议后续)**:`BridgeMainCommandGuardIntegrationTests`/`ExecuteCommandTests`/`GitHubApiClientTests` 可用同模式注入式修复,或标记 `[Collection]` 串行
3. **低风险(可接受)**:`DesktopEnvironmentGuardTests` + 集成测试有恢复机制,暂可接受
4. **架构级(长期)**:`AppDataConstants.Paths` 全局可变静态状态是根因,理想改为注入式配置(`IOptions<AppDataPaths>`),消除所有静态状态依赖
