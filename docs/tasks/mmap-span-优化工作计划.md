# mmap + PLINQ + Span 优化工作计划

> ADR: [0072](../adr/0072-mmap-plinq-span-proliferation.md)
> 扫描时间: 2026-09-07
> 状态: 进行中

## 已完成（PR 待合并）

| 改造 | 技术 | commit | 测试 |
|------|------|--------|------|
| MappedFileReader 封装 | IDisposable + using var | `c62c2864e` | 9 ✅ |
| P0: PhysicalFileSystem | mmap + LineSpanIndexer | `f882b9b86` | 136 IO ✅ |
| P1: SnipLogic | mmap + Span 行遍历 | `d7b785bfe` | 265 Hands ✅ |
| P1: FileEditLogic | 辅助方法统一两处读行 | `f3d59c3f5` | 265 Hands ✅ |
| P1: FileEditor | mmap + Span 行遍历 | `7e93973c9` | 136 IO ✅ |
| P1: FileReader | `_fs.ReadAllTextAsync` 自动 mmap | `1665cf7af` | 136 IO ✅ |
| P2: ContextCollapseService | LineSpanIndexer 替代 3处 Split | `c6311dc71` | 编译 ✅ |
| P2: ApplyPatchLogic | LineSpanIndexer 替代 Split | `edfb44133` | 265 Hands ✅ |
| P2: ProgressiveDisclosureService | Task.WhenAll 并行读3个源文件 | `b83b9875f` | 编译 ✅ |
| ADR 0072 accepted | 记录全部完成改造 | `7db444e3c` | - |

---

## 待做：类型2 串行 IO 并行化（收益最高，改动小）

参考已有并行模式：`ProjectRulesLoader` / `AgentDefinitionProvider`（`Task.WhenAll`）

### T2-1: SessionScanner — 串行读多个会话 JSONL 文件【高收益】

- **文件**: `core/safety/Vault/src/Memdir/Services/SessionScanner.cs:59-76`
- **热点**: `foreach` + `await ExtractSessionMetaAsync(file, ct)` 串行读几十~几百个会话文件
- **优化**: `Task.WhenAll` 并行读取，每个 `ExtractSessionMetaAsync` 独立（ReadAllLinesAsync + JSON 解析）
- **收益**: **高** — 会话历史文件数量可达几十到几百个，用户查看会话列表时的热路径

### T2-2: GoalStateStore — 串行读多个目标 JSON 文件【中收益】

- **文件**: `composition/Clock/src/Goal/Infrastructure/GoalStateStore.cs:73-87`
- **热点**: `foreach` + `await _fs.ReadAllTextAsync(file, ct)` 串行读多个目标文件
- **优化**: `Task.WhenAll` 并行读取
- **收益**: **中** — 目标文件数量通常较少（几个到十几个），获取未完成目标时触发

### T2-3: ToolTemplateService — 串行读多个模板 JSON 文件【中收益】

- **文件**: `core/execution/McpToolDispatch/src/Core/Execution/ToolTemplateService.cs:42-68`
- **热点**: `foreach` + `await _fs.ReadAllTextAsync(file, ct)` 串行读模板文件
- **优化**: `Task.WhenAll` 并行读取
- **收益**: **中** — 启动时加载，有 `_cache` 字段表明是初始化路径

### T2-4: FileBasedReflexionMemory — 串行读反思记忆 JSON 文件【中收益】

- **文件**: `core/ai/Agents/src/Doctor/Reflexion/FileBasedReflexionMemory.cs:70-85, 119-140`
- **热点**: 两处 `foreach` + `await _fs.ReadAllTextAsync(file, ct)` 串行读反思记忆
- **优化**: 两处均改 `Task.WhenAll`；`GetStatisticsAsync` 外层还有目录遍历，可考虑双层并行
- **收益**: **中** — Doctor 诊断时调用，文件数 = 规则数 × 每规则尝试次数

---

## 待做：类型1 大文件 Split → LineSpanIndexer（收益高）

### T1-高1: GitHubToolHandlers.Run FillMemoryCacheFromRaw — 解析 GitHub Actions 日志【高收益】

- **文件**: `services/Mcp/src/GitHub/GitHubToolHandlers.Run.cs:312`
- **热点**: `rawContent.Split('\n')` — 几万行日志 Split，分配几万个 string[]
- **优化**: `LineSpanIndexer.BuildLineRanges` 零分配遍历行
- **收益**: **高** — AGENTS.md 记载"失败日志动辄几万行"，`gh run view` 热路径

### T1-高2: DiagnosticLogWatcher — 读取诊断日志文件后 Split【高收益】

- **文件**: `core/ai/Agents/src/Doctor/DiagnosticLogWatcher.cs:92-95`
- **热点**: `ReadAllTextAsync` + `Split('\n')` — 持续监控，每次有新内容重新读整个文件 + Split
- **优化**: `MappedFileReader` + `LineSpanIndexer` 避免 ReadAllTextAsync 大字符串分配
- **收益**: **高** — Doctor 诊断引擎持续监控路径，日志文件不断增长，高频调用

### T1-高3: GitHubToolHandlers TruncateLines — 截断大日志输出【高收益】

- **文件**: `services/Mcp/src/GitHub/GitHubToolHandlers.cs:82`
- **热点**: `output.Split('\n')` — 几万行 Split 后只取前 N 行，浪费严重
- **优化**: `LineSpanIndexer` 遍历前 `maxLines` 行，避免分配完整数组
- **收益**: **高** — 输入是 `gh run view --log` 完整日志（几万行），注释明确"避免大日志撑爆 LLM 上下文"

### T1-高4: ContextFoldDecider — 压缩工具结果 Split【高收益】

- **文件**: `core/execution/Brain/src/ContextFold/ContextFoldDecider.cs:284`
- **热点**: `content.Split('\n')` — 工具结果 Split 后 `Take(head)` + `TakeLast(tail)`
- **优化**: `LineSpanIndexer` 零分配获取头 N 行 + 尾 M 行
- **收益**: **高** — 工具结果（文件内容/命令输出/搜索结果）可能很大，上下文折叠是对话热路径

---

## 待做：类型1 文件内容 Split → LineSpanIndexer（收益中）

### T1-中1: BundledSkillToolHandlers — 代码简化分析 Split

- **文件**: `core/execution/Hands/src/ToolHandlers/Handlers/SystemTools/BundledSkillToolHandlers.cs:453`
- **优化**: `LineSpanIndexer` 替代，`for` 循环顺序遍历
- **收益**: **中** — 代码文件几百~几千行，用户请求代码简化时

### T1-中2: SessionMemoryPromptTemplate — 分析 MEMORY.md 各部分大小

- **文件**: `core/execution/Brain/src/Prompts/Templates/Memory/SessionMemoryPromptTemplate.cs:100, 197`
- **优化**: `LineSpanIndexer` 替代，`foreach` 顺序遍历
- **收益**: **中** — MEMORY.md 可能较大（用户长期积累），每次会话开始时分析

### T1-中3: ShellSedInterceptMiddleware — 生成 diff 预览双 Split

- **文件**: `core/execution/Hands/src/ToolHandlers/Handlers/SystemTools/Shell/ShellSedInterceptMiddleware.cs:165-166`
- **优化**: 双 `LineSpanIndexer` 替代两个 Split，循环只取前 20 个变更
- **收益**: **中** — 文件编辑前后完整内容双 Split

### T1-中4: LspFileSync — 应用 LSP 文档变更 Split

- **文件**: `services/Eyes/src/Lsp/Internal/Core/LspFileSync.cs:219`
- **优化**: `LineSpanIndexer` 替代，需按行索引访问（`lines[startLine]`），LineSpanIndexer 支持按索引获取
- **收益**: **中** — LSP 文档同步高频操作（每次编辑触发）

### T1-中5: StructuredPatchGenerator — 生成结构化 patch Split

- **文件**: `infrastructure/Infrastructure/IO/Services/Diff/StructuredPatchGenerator.cs:141`
- **优化**: Myers 算法需随机访问行，可考虑 `LineSpanIndexer` + 按需 `ToString()`，或保留 Split 用 `ArrayPool<string>` 减少分配
- **收益**: **中** — diff 生成是代码编辑/比较热路径

### T1-中6: MemoryTruncator — 截断记忆内容 Split

- **文件**: `core/safety/Vault/src/Memdir/Memdir2/Operations/MemoryTruncator.cs:90, 132`
- **优化**: `LineSpanIndexer` 替代，`Take(config.MaxLines)` 只需前 N 行
- **收益**: **中** — 记忆文件写入时的热路径

### T1-中7: AgentMemoryService — 截断入口文件内容 Split

- **文件**: `core/ai/Agents/src/Services/Support/AgentMemoryService.cs:302`
- **优化**: `LineSpanIndexer` 替代，只需前 `MaxEntrypointLines` 行
- **收益**: **中** — MEMORY.md 入口文件，每次加载代理记忆时

### T1-中8: LlmCodePatchGenerator — 计算置信度双 Split

- **文件**: `core/ai/Agents/src/Doctor/SourceCode/LlmCodePatchGenerator.cs:133-134`
- **优化**: 双 `LineSpanIndexer` 替代，顺序遍历比较行
- **收益**: **中** — Doctor 自举路径，比较原始/补丁代码

### T1-中9: DefaultBootstrapGuard — 计算变更行数双 Split

- **文件**: `core/ai/Agents/src/Doctor/Bootstrap/DefaultBootstrapGuard.cs:95-96`
- **优化**: 双 `LineSpanIndexer` 替代，顺序遍历比较
- **收益**: **中** — Doctor 自举守卫

### T1-中10: TerminalCaptureService — 截断终端输出 Split

- **文件**: `infrastructure/Infrastructure/IO/Services/Terminal/Core/TerminalCaptureService.cs:298-302`
- **优化**: `MappedFileReader` + `LineSpanIndexer` 替代 `ReadAllText` + `Split`，只需最后 N 行
- **收益**: **中** — 终端捕获输出 `TakeLast`

### T1-中11: SendUserFileToolHandlers — 预览文件内容 Split

- **文件**: `services/Mcp/src/Communication/SendUserFileToolHandlers.cs:80-82`
- **优化**: `LineSpanIndexer` 替代，只需前 50 行（有 10KB 大小限制，收益有限）
- **收益**: **中偏低**

---

## 待做：类型1 低收益 Split（git/进程输出，批量改造）

以下为 git 命令输出或进程输出的 Split，通常不是大文件，数量多但单个收益小：

| 文件 | 行号 | 代码片段 |
|------|------|----------|
| `infrastructure/Infrastructure/IO/Process/GitCommandRunner.cs` | 83, 115 | git merge/grep 输出 |
| `infrastructure/Infrastructure/IO/Process/GitHubCommandRunner.cs` | 390, 432 | PR URL/列表 |
| `core/safety/Guard/src/Security/Services/GitDiffProvider.cs` | 24 | git diff 文件名 |
| `core/safety/Guard/src/Security/Scanners/GitSecretScanner.cs` | 51 | git diff 密钥扫描（diff 可能大） |
| `composition/Composition/src/Commands/hands/Git/CommitCommand.cs` | 55, 149 | git status |
| `composition/Composition/src/Commands/hands/Code/DiffCommand.cs` | 251, 264, 277 | diff 输出 |
| `infrastructure/Infrastructure/IO/Process/PhysicalProcessService.cs` | 161, 171 | 进程输出 |
| `core/execution/Hands/src/Build/BuildQueueService.cs` | 240 | 构建输出 |
| `core/ai/Agents/src/Services/Support/AgentWorktreeService.cs` | 616, 630 | git 输出 |
| `core/ai/Agents/src/Services/Worktree/Pipeline/Middleware/WorktreeConfigMiddleware.cs` | 89, 103, 141 | git 输出 |
| `core/ai/Agents/src/Doctor/SourceCode/BootstrapWorktreeManager.cs` | 101, 153 | git 输出 |
| `core/execution/McpToolDispatch/src/CodeTools/LspToolHandlers.cs` | 457 | git check-ignore |
| `infrastructure/Infrastructure/IO/Process/PrBodyGenerator.cs` | 42 | PR body |
| `services/Mcp/src/GitHub/GitHubToolHandlers.Pr.cs` | 57 | PR 输出 |
| `services/Mcp/src/GitHub/GitHubToolHandlers.Run.cs` | 140, 409 | 日志输出 |
| `core/execution/Brain/src/Context/Compact/Guard/CompactOutputGuard.cs` | 139 | 压缩摘要 Split |
| `core/safety/Guard/src/Hooks/Execution/HookExecutors/HookExecutorBase.cs` | 148 | hook stdout |
| `core/safety/Guard/src/Hooks/Execution/AsyncHookRegistry.cs` | 347 | hook stdout |
| `core/ai/Agents/src/Services/Support/WorktreeMergeService.cs` | 207 | diff 输出 |

---

## 执行顺序建议

1. **先做 Actor 改造**（P0 数据丢失风险最高）：A-P0-1 → A-P0-2 → A-P0-3
2. **再做类型2并行化**（收益最高，改动小）：T2-1 → T2-2 → T2-3 → T2-4
3. **再做类型1大文件 Split**（收益高）：T1-高1 → T1-高2 → T1-高3 → T1-高4
4. **后做类型1文件内容 Split**（收益中）：T1-中1 ~ T1-中11
5. **最后做类型1低收益**（git/进程输出 Split，可批量改造）

---

## 待做：Actor 模型改造（并发安全，消除锁竞争/死锁）

> 参考已有 Actor 先例：`infrastructure/Infrastructure/IO/PersistencePipeline.cs`（Actor 单消费者 Channel 串行写，无锁无死锁）
> 核心原则：**读取（mmap）不需要 Actor**（只读无冲突），**写入/读-改-写需要 Actor**（有并发冲突）

### A-P0-1: FileEditLogic 全部方法 — 完全无锁读-改-写【P0 数据丢失风险】

- **文件**: `core/execution/Hands/src/ToolHandlers/Handlers/Core/Logic/FileEditLogic.cs`
- **涉及方法**: `EditWithRegexAsync`(L16)、`InsertLinesAfterAsync`(L73)、`DeleteLinesAsync`(L121)、`BatchEditAsync`(L171)
- **问题**: 全部方法读-改-写**完全无锁**（连 IOThrottleService 都没有），并发编辑直接丢数据
- **改造**: per-file `FileWriteActor` 串行化读-改-写，或补 `FileLockService` 锁保护
- **风险**: **P0** — Hands 工具核心编辑逻辑，并发编辑数据丢失

### A-P0-2: ApplyPatchLogic.ApplyAsync — 完全无锁读-改-写【P0 数据丢失风险】

- **文件**: `core/execution/Hands/src/ToolHandlers/Handlers/Core/Logic/ApplyPatchLogic.cs:13-88`
- **问题**: 多文件 patch 读-改-写**完全无锁**，patch 应用期间文件被外部修改导致 context mismatch 或覆盖
- **改造**: per-file `FileWriteActor` 串行化，或补 `FileLockService`
- **风险**: **P0**

### A-P0-3: ThrottledFileService.EditFileAsync/EditByLineRangeAsync — 无锁读-改-写【P0 数据丢失风险】

- **文件**: `infrastructure/Infrastructure/IO/Services/FileOps/ThrottledFileService.cs:145-279`
- **问题**: `EditFileAsync`(L145) 和 `EditByLineRangeAsync`(L219) 无锁读-改-写，同文件 `WriteFileAsync`(L83) 有锁但 Edit 遗漏
- **改造**: 补 `FileLockService`（对齐 `WriteFileAsync`），或 per-file Actor
- **风险**: **P0**

### A-P1-1: FileEditor.EditFileAsync — 读锁/写锁分离 TOCTOU【P1 时间窗口风险】

- **文件**: `infrastructure/Infrastructure/IO/Services/FileOps/FileEditor.cs:23-221`
- **问题**: 读取(L101)加锁释放→内存修改→写入(L196)重新加锁，中间存在 TOCTOU 时间窗口
- **改造**: 合并为单次锁内读-改-写，或单 Actor 消息
- **风险**: **P1** — 有 `_fileStateCache` 时间戳辅助检测，风险降低

### A-P1-2: PlanModeManager 状态文件 — 跨进程无锁写【P1 覆盖风险】

- **文件**: `core/execution/Brain/src/Planning/Planning2/ToolHandlers/PlanModeManager.cs:876-900`
- **问题**: 跨进程状态文件 `.active_plan_state.json` 无锁写入，多进程并发覆盖
- **改造**: 文件锁或单写者 Actor
- **风险**: **P1**

### A-P1-3: ConfigLoader.SaveSettingsJsonAsync — 静态无锁写【P1 覆盖风险】

- **文件**: `core/safety/Guard/src/Configuration/Configuration2/Core/Loading/ConfigLoader.cs:161-172`
- **问题**: 静态方法无锁写 settings.json，配置热重载 + CLI 多实例并发覆盖
- **改造**: 引入配置持久化 Actor
- **风险**: **P1**

### A-P2-1: HookConfigurationManager.AddHook/RemoveHook — 读-改-写无锁【P2 丢失更新】

- **文件**: `core/safety/Guard/src/Hooks/Configuration/HookConfigurationManager.cs:345-409`
- **问题**: `JsonFileHookConfigurationProvider` 的 AddHook/RemoveHook 读-改-写无锁
- **改造**: 补锁或 Actor
- **风险**: **P2**

### A-P2-2: TeamMemorySyncService.PullFromRemoteAsync — 并行同步无锁【P2 覆盖风险】

- **文件**: `core/safety/Vault/src/Memdir/Sync/TeamMemorySyncService.cs:503-547`
- **问题**: `SyncAllFilesAsync` 用 `Task.WhenAll` 并行同步多文件，`PullFromRemoteAsync` 直接 `_fs.WriteAllTextAsync` 无锁
- **改造**: per-file 锁
- **风险**: **P2**

### A-P2-3: ThrottledFileService.WriteFileWithEncodingAsync — 无文件锁【P2 覆盖风险】

- **文件**: `infrastructure/Infrastructure/IO/Services/FileOps/ThrottledFileService.cs:561-596`
- **问题**: tmp+move 原子但并发写同一目标文件的多个 tmp move 可能交错
- **改造**: 对齐 `WriteFileAsync` 补 `FileLockService`
- **风险**: **P2**

---

## 已有正确并发控制（无需改造）

| 文件 | 机制 | 说明 |
|------|------|------|
| `PersistencePipeline` | Actor 单消费者 | 无锁无死锁，最佳实践 |
| `TranscriptFileWriter` | AsyncLock 锁内读-改-写 | 原子 |
| `TeammateMailboxService` | per-agent 分片锁 | 不同 agent 不互斥 |
| `TeamManager` | AsyncLock 锁内写 | 内存状态一致 |
| `GraphPersistence` | ReaderWriterLockSlim + Actor | 读锁+Actor 写 |
| `PhysicalFileSystem` 读取 | mmap 只读 | FileShare.ReadWrite，无冲突 |

---

<!-- 🤖 Auto Decision: 2026-09-07 -->
<!-- 决策: 创建工作计划文档记录全部待做优化点 -->
<!-- 原因: 用户要求全部记到工作计划后先PR，便于后续按优先级渐进式改造 -->
<!-- 验证: 文档创建完成，待 commit -->
