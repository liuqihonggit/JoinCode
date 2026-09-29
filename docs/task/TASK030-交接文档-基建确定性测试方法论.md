# 基建确定性测试补充 — 交接文档(方法论+提示词模板)

> **用途**:本文档记录"并行多子代理检测+拆分项目+分阶段执行"的完整方法论,含可复用的子代理提示词模板。其他分支/项目可据此复用,进行大规模测试补充+重构。
>
> **来源**:TASK030 基建确定性测试补充(7模块,1566测试+13bug修复,2个PR)
>
> **创建时间**:2026-09-29

---

## 一、总体方法论(5步循环)

```
①检测(并行explore) → ②任务文档 → ③分阶段执行(并行developer-test-agent) → ④提交+PR → ⑤循环下一阶段
```

### 步骤详解

| 步骤 | 工具 | 并行度 | 产出 |
|------|------|--------|------|
| ①检测 | Task(subagent_type=explore) | 8-10个 | 各模块报告:过长方法/缺测试/可拆internal |
| ②任务文档 | Write | 1 | TASK文档:三阶段+验收标准+执行顺序 |
| ③执行 | Task(subagent_type=developer-test-agent) | 2-3个(不同模块) | 新测试+bug修复+拆分 |
| ④提交 | Bash(git) | 主代理统一 | commit+PR(auto-merge squash) |
| ⑤循环 | — | — | 下一阶段或下一批模块 |

### 关键原则

1. **检测与执行分离**:先并行explore全面检测(只读),再并行developer-test-agent执行(写代码)
2. **按模块并行**:不同测试项目的模块可并行(不冲突),同测试项目的串行
3. **主代理统一提交**:并行子代理禁止git commit,由主代理统一add+commit
4. **渐进式**:每完成一个子任务编译+测试+提交,持续推进不中断
5. **bug立即修复**:发现bug停下任务流程先修复(AGENTS.md铁律)

---

## 二、检测阶段:explore 子代理提示词模板

### 模板

```
检测 {模块路径} 模块({模块职责})。这是研究任务,不要写代码。

请 thorough 地分析:
1. 列出所有 .cs 源文件(非 tests)
2. 找出过长的方法(>{N}行),给出 文件:行号:方法名:行数
3. 找出复杂逻辑方法(含多分支/循环/状态转换),确定性测试候选
4. 检查对应测试项目 {测试项目路径},找出哪些 public/internal 方法缺乏单元测试覆盖
5. 识别可拆分为 internal 方法的中间逻辑(长方法中的纯计算片段,不依赖时序/IO/异步等待)
6. 特别关注: {模块特定关注点}

返回结构化报告:
- 模块概览(文件数,主要类)
- 过长方法清单(按行数降序,前20个)
- 缺测试覆盖的关键方法
- 建议拆分为 internal 的中间逻辑(含所在方法+拆分点+理由)
- 已有确定性测试 vs 缺失确定性测试 的对比
```

### 实际使用(8个并行explore)

|.模块 | 路径 | 关注点 |
|------|------|--------|
| structura | lib/structura/ | DAG拓扑排序/环检测/RingBuffer读写 |
| async_lock | lib/async_lock/ | 锁状态计算/等待图构建/死锁判定 |
| scheduling | lib/scheduling/ | 调度计算/优先级排序/时间窗口 |
| guard | lib/guard/ | 规则匹配/权限判定/拦截链 |
| vault | lib/vault/ | 路径计算/密钥解析/元数据构造 |
| transport.impl | lib/transport.impl/ | 协议握手/消息编解码/会话管理 |
| plugins.infrastructure | lib/plugins.infrastructure/ | 插件加载/依赖解析/生命周期 |
| clock+infra+abstractions | 多模块 | ActorBase专项/GoalGraph/AhoCorasick |

---

## 三、执行阶段:developer-test-agent 提示词模板

### 核心规则

**必须传用户原始查询 verbatim**:
```
用户原始查询(verbatim):{用户原始查询原文}
```

然后加当前子任务边界:
```
## 当前执行子任务边界(阶段{X.Y} {模块名})

任务文档:{文档路径}。执行阶段{X.Y}:{模块名} {任务描述}。
```

### 模板:阶段1(补测试,不改生产代码或只改private→internal)

```
用户原始查询(verbatim):{原文}

## 当前执行子任务边界(阶段1.X {模块})

任务文档:{路径}。执行阶段1.X:{模块} 确定性测试补充。

### 具体任务
{逐个列出需补测试的方法,含文件路径、方法名、测试场景}

### 执行要求
1. 先读代码确认方法签名、可见性
2. 遵循现有测试风格(读现有测试文件)
3. GlobalUsings:.cs 文件内禁止写 using
4. 确定性:不依赖时序/IO/异步,给定输入→断言输出
5. private→internal 只改可见性不改逻辑,确认 InternalsVisibleTo
6. 禁止删除文件
7. 并行期间禁止 git commit
8. 遇到 IO 依赖难确定性测试的方法:跳过并注释说明

### 验证命令
{dotnet build + dotnet test 命令}

### 完成后返回
1. 新建/修改文件清单
2. 新增测试方法数量
3. 编译+测试结果
4. 跳过的方法及原因
5. 遇到的bug(若发现边界bug,修复并说明)
```

### 模板:阶段2(拆分长方法为internal+补测试)

```
用户原始查询(verbatim):{原文}

## 当前执行子任务边界(阶段2.X {模块} 拆分长方法)

### 核心原则
**拆分时保持行为不变**(原方法调用结果完全一致),拆出 internal 子方法后补测试。每步编译+全量测试验证无回归。

### 具体任务
{逐个列出需拆分的长方法,含当前行数→目标行数、拆出哪些子方法}

### 执行要求
1. 先读完整长方法理解逻辑,再拆分
2. **行为不变**:全量回归
3. 拆出子方法用 internal static(纯计算)或 internal
4. 为子方法补确定性测试
{...其余同阶段1}
```

### 模板:阶段3(mock测试)

```
用户原始查询(verbatim):{原文}

## 当前执行子任务边界(阶段3.X {模块} mock测试)

### 核心原则
用 mock 消除 IO/异步依赖,使测试确定性。若某方法即使mock仍涉及时序,跳过并注释。

### 具体任务
{列出需mock测试的方法,含mock策略}

### 跳过(异步编排时序依赖)
{列出不可确定性测试的方法及原因}
```

---

## 四、任务拆分策略

### 按阶段分(收益/风险比降序)

| 阶段 | 内容 | 风险 | 收益 |
|------|------|------|------|
| 1 | P0纯逻辑零测试(只改private→internal) | 低 | 最高 |
| 2 | 拆分长方法为internal子方法 | 中 | 高 |
| 3 | 补测IO/异步可mock方法 | 中 | 中 |

### 按模块并行(不冲突规则)

- **可并行**:不同测试项目的模块(如 abstractions+plugins+vault)
- **不可并行**:同测试项目的子任务(如 guard的1.3+1.3b)
- **并行限制**:最多3个developer-test-agent并行(上下文管理)

### 按风险排序

1. 核心算法零测试(structura Dag/ConcurrentDag) — 最高风险优先
2. 安全网零测试(LockRegistry死锁检测、BashAstSecurityWalker) — 高
3. 协议编解码零测试(SseStreamParser、HttpRequestSerializer) — 高
4. 纯计算未测(JsonRepairPipeline、MemoryPaths) — 中
5. 长方法拆分 — 中(重构风险)
6. mock测试 — 低(IO/异步依赖)

---

## 五、验证流程(每步必须)

```
①编译:dotnet build {csproj} -c Debug  → 0警告0错误
②测试:dotnet test {csproj} -c Debug --no-build → 全绿
③提交:git add + git commit(主代理统一)
④PR前:dotnet build build/sln/JoinCode.slnx -c Debug → 0警告0错误
⑤PR:gh pr create + gh pr merge {N} --auto --squash
```

### Git commit 消息格式

```
类型: 描述 | 决策: [做了什么选择,为什么]
类型:feat/fix/refactor/test/docs/chore
禁止:分支名、PR编号、$、反引号、三引号
```

---

## 六、注意事项(避坑)

### 必须遵守

1. **传用户原始查询verbatim给developer-test-agent**:不重写、不扩展、不分解
2. **并行子代理禁止git commit**:由主代理统一提交
3. **private→internal只改可见性不改逻辑**:除非是bug修复
4. **bug立即修复**:发现bug停下先修复(AGENTS.md铁律)
5. **PR前编译JoinCode.slnx**:禁止带错提PR
6. **InternalsVisibleTo确认**:internal方法所在项目已对测试项目暴露
7. **GlobalUsings**:.cs文件内禁止写using
8. **禁止删除文件**:移到.xxx/目录

### 常见跳过原因

| 原因 | 示例 |
|------|------|
| 时序依赖 | InteractiveHandler异步编排、SwarmWorkerHandler超时 |
| IO不可mock | VpnRouteGuard.DetectVpn(Process/Env) |
| 高风险大重构 | SwissTable SIMD去重(核心哈希表) |
| 与他人冲突 | async_lock(ActorBase改造) |
| 依赖不存在 | MemoryAgeInfo类不存在 |

### 发现的bug模式

| 模式 | 案例 |
|------|------|
| 集合操作结果丢弃 | PermissionChecker.RemoveFromAutoApproved(ImmutableHamT.Remove返回新集合未赋值) |
| 循环未跳出 | BashSafeWrapperStripper(else break未跳出for) |
| 边界参数 | GetPipeTargetCommands(Enumerable.Range(0,-1)) |
| 实现与参考不同步 | BashAstSecurityWalker 9个bug(对齐TS ast.ts) |

---

## 七、扩展到其他分支的建议

1. **先检测**:并行8-10个explore子代理,覆盖所有基建模块
2. **写任务文档**:整合检测报告为TASK文档,三阶段+验收标准
3. **按风险排序执行**:核心算法零测试优先,长方法拆分次之,mock测试最后
4. **并行2-3个developer-test-agent**:不同模块不冲突,同模块串行
5. **每阶段PR**:阶段完成即PR(auto-merge squash),不堆积
6. **复用提示词模板**:本文档第三节的模板可直接复用,替换模块名/路径/方法名

### 可复用的检测维度

- 过长方法(>40行)
- 复杂逻辑(多分支/循环/状态转换)
- 缺测试覆盖(public/internal无测试)
- 可拆internal的纯计算片段(不依赖时序/IO/异步)
- 已有确定性测试 vs 缺失确定性测试

---

<!-- 🤖 Auto Decision: 2026-09-29 -->
<!-- 决策: 写交接文档记录方法论+提示词模板,供其他分支复用 -->
<!-- 原因: 用户发现并行子代理+提示词方式有效,要扩展到其他分支 -->
<!-- 替代方案: 口头说明(不可复用,信息丢失) -->
<!-- 验证: 文档包含完整5步循环+3类提示词模板+任务拆分策略+避坑清单 ✅ -->
