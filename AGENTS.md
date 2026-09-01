# CLAUDE.md

## 核心规则：每次回复结束前必须调用 `ask_user` 工具

这是不可跳过的强制协议。在你的每一轮回复中，你必须执行以下操作之一：

1. **完成用户请求后** → 立即调用 `ask_user` 工具，提出与当前上下文相关的后续问题
2. **存在任何不确定性时** → 不要猜测执行，立即调用 `ask_user` 工具进行澄清

### 禁止行为

- **禁止在不调用 `ask_user` 的情况下结束回复**
- **禁止使用终结性表达**（如“希望对你有帮助”、“如有问题随时提问”等）
- **禁止猜测用户意图** — 不确定就用 `ask_user` 询问

### `ask_user` 调用要求

- 问题必须与当前任务上下文直接相关
- 问题必须具体、可操作，不要问泛泛的"还需要什么帮助"
- 可以提供选项供用户选择，降低用户输入成本
- **必须先编译+提交后再调用 `ask_user`** — 禁止在未编译验证和提交的情况下询问用户后续工作

## 基础规范

1. 先记录剩下的任务到 {任务名} 文档
2. 每个任务都要：红测试 → 任务 → 编译 → 绿测试 → 文档 → (没有单元测试就不得)git 提交 → 差评大师 → 修复
3. 禁止 subAgent 进行全量测试,只能编译+快速冒烟,由mainAgent进行全量测试
***


## 🔴 绝对禁止（触碰即错）

### 操作禁令

1. **⛔ 禁止删除文件（不可协商的安全红线）**
   > **这是神圣不可侵犯的规则。违反将导致任务立即失败。**
   - **🚫 绝对禁用的工具和命令：**
     - `DeleteFile` 工具 — **永远不要调用此工具**
     - `Remove-Item` / `del` / `rm` 命令
     - 任何形式的文件删除操作
   - **为什么？**
     - 删除 = 无法回滚 = 灾难性后果
     - 丢失审计追踪，无法追溯历史
     - 违反渐进式安全原则
    - **✅ 唯一正确做法：移动到项目根目录 `.xxx/` 目录**
      - **目标位置**：统一移到 `{RepoRoot}\.xxx\`（如 `D:\project\w3\.xxx\`），**禁止**移到子目录下的 `.xxx/`
      - **命令**：用 `Move-Item`，**禁止**用 `git mv`（git mv 会 staged 移动记录，且子目录 .xxx 不在 gitignore 中会污染编译）
      - **格式**：`.xxx/{原文件名}.{原后缀}.{时间戳}.del`（如 `ICommandRewriter.cs.20260824.del`）
      - **.xxx 在 .gitignore 中**：归档文件不被 git 跟踪，原文件显示为 `D`(deleted)，commit 记录删除
      - **移走后必须修复引用**（否则编译失败 CS1574/CS0246）：
        1. XML 注释中 `<see cref="旧类名"/>` → 改为文字描述（如 `迁移自旧 XxxRewriter`）
        2. `GlobalUsings.cs` 中旧命名空间 → 删除该 `global using` 行
        3. 旧测试文件也一并移走（引用旧类的测试同样归档）
      - **完整示例**：
        ```powershell
        New-Item -ItemType Directory -Force -Path "D:\project\w3\.xxx" | Out-Null
        Move-Item "core/safety/Guard/src/Hooks/Execution/ICommandRewriter.cs" "D:\project\w3\.xxx\ICommandRewriter.cs.20260824.del"
        ```
   
2. **❌ 禁止使用命令行文本工具直接修改源码文件**
   
   - 原因: 可能导致文件损坏或编码问题
   - 正确: 使用 IDE 提供的 `SearchReplace` 工具修改文件内容

3. **❌ 禁止删除函数注释（XML 文档注释）**
   
   - **🚫 禁止**: 删除或清空函数/方法上的 `/// <summary>` 等 XML 文档注释
   - 原因: 函数注释是代码契约的一部分，删除会导致 IntelliSense 信息丢失、调用方无法理解意图
   - 原因: AI 生成代码时容易“顺手”删掉注释，这是不可逆的信息损失
   - **✅ 正确做法**:
     - 函数签名变更时，同步更新注释内容，而非删除
     - 注释内容过时时，更新为正确描述，而非清空
     - 新增函数时，必须编写 XML 文档注释
   
4. **❌ 禁止使用会卡住交互的命令**
   - 禁止: `more`, `less` 等分页命令
   - 禁止: `git commit` 不带 `-m` 参数（会打开编辑器）
   - 禁止: `npm init` 等交互式命令（使用 `-y` 跳过）

5. **❌ 禁止猜测用户意图/背景/业务场景**（规划任务期间）
   
   - 信息模糊时，基于行业最佳实践自主选择技术方案，优先保守安全.
   - 一旦过程中有对架构进行丰富调整的,推荐使用 `ask_user` 工具让用户确认.
   - 记录决策依据到工作文件末尾（遵循第3条对话偏好）

6. **⚠️ 分级交互控制**（执行任务期间）
   
   - **Level 1-2 禁止交互**: 自主决策技术方案，基于上下文选择最合理的实现方式
   - **Level 3 允许交互**: 穷尽所有手段（MCP记忆 → 项目代码 → 可用技能 → 互联网搜索）后仍无法解决时，使用 `ask_user` 工具请求用户决策
   
7. **⛔ 禁止并行子智能体期间提交 Git**
   
   - 每次指派子智能体时，必须告知子智能体当前处于**并行期间**，禁止执行 `git commit` / `git push`
   - 原因: 并行子智能体操作同一仓库，提交会导致冲突或覆盖他人工作
   - ✅ 正确做法: 并行任务全部完成后，由主智能体统一提交
   
8. **⛔ 禁止因时间/长度关系中断任务**
   - 禁止因为输出过长、执行时间过长而中途停止
   - 禁止主动询问用户“是否继续”——用户可以随时中断，不需要AI提醒
   - 长任务应持续执行直到完成或穷尽所有手段后请求用户决策

***

## ✅ 必须执行（遗漏即错）

### ADR 工作流（架构决策记录）

> 📖 规范详见 [docs/adr/README.md](docs/adr/README.md)

1. **新架构决策必须先写 ADR**：涉及跨模块、影响全局、或选择 A 放弃 B 的决策，先在 `docs/adr/` 写 ADR（`状态：proposed`）再实现
2. **实现后改状态**：决策落地并验证后，ADR 状态改为 `accepted`
3. **决策被取代**：旧 ADR 状态改为 `superseded by NNNN`，新 ADR 引用旧 ADR
4. **ADR 不可删除**：内容不可变，只改状态字段（见 ADR [0008](docs/adr/0008-archive-to-xxx-not-delete.md)）
5. **粒度**：架构级 + 组件策略级，函数级决策留在代码注释
6. **AGENTS.md 反向引用**：AGENTS.md 中对应规则处标注 `> ADR: [NNNN](docs/adr/NNNN-xxx.md)`

### 开发流程强制要求

0. 脚本替换规范
必须要先在一个文件或者一个项目上面验证成功,才可以用脚本推广到全部位置.
1. **✅ 必须采用渐进式开发方法**
   - **渐进式定义**：渐进式 ≠ 停下来问用户。渐进式 = 执行任务期间，每完成一个关键步骤就自动执行 `编译 → 测试 → commit`，持续推进任务，不中断、不等待、不询问。核心目的是：① 每步都有可验证的产物（编译产物/测试结果/git提交）② 任何时刻中断都能从最近的commit恢复 ③ 错误在萌芽阶段就被捕获，而非堆积到末尾
   - 每次只完成一个功能，编译，单元测试，git提交
   - 主工程编译成功后，测试用例也需编译成功
   - 一旦有疑问或发现错误，**立即停止**，先修复再继续
   - **⛔ 禁止中途暂停询问用户"是否继续"** — 渐进式要求持续推进，用户可随时中断，不需要AI提醒
2. **⚠️ 任务失败处理机制*
   - **核心原则**: 必须先评估错误级别，禁止遇到小错误就立即回滚
   - **总上限**: 3次重试机会（含自行修复和回滚重试）
   - **⚠️ 降级策略**: 错误累积过多时，禁止反复重试整个任务触发git回滚
     - 正确做法: 将当前任务拆成更小的子步骤，每步独立编译验证
     - 拆到多小？拆到单文件单函数级别，确保每步可独立编译
     - 禁止: 看到大量错误就整体回滚再重来（会陷入回滚-重试死循环）
3. **🔴 TDD 铁律1（双层测试驱动开发，强制）**
   - **循环**: 🔴E2E红 → 🔴单元红 → 🟢单元绿 → 🔵重构 → 🟢E2E绿
   - **E2E 层**（仅对外接口变更时）: 先写会失败的E2E测试 → 定义契约 → 独立exe进程级交互模拟，验证跨进程协议
   - **单元层**: E2E红灯下开始红绿循环 → 先写行为测试后写输入测试 → 逐步让代码通过使E2E变绿
   - **铁律**: 先E2E红灯→才允许单元测试→才允许生产代码；写完组件要聚合到主项目编译验证
   - **⛔ 禁止补测试**: 发现无测试的生产代码 → 先移到 `.xxx/` → 再从E2E失败测试开始
   - **例外**: 纯DTO/枚举无需TDD；纯内部重构可仅走 🔴单元红→🟢单元绿→🔵重构
4. **🔴 TDD 铁律2（缺陷驱动测试，修复 bug 时强制）**
   - **循环**: bug → 🔴E2E红(复现) → 🔴单元红(定位根因) → 🟢单元绿(修复) → 🟢E2E绿(验证集成)
   - **铁律**: 禁止直接改代码修bug → 先写E2E复现 → 确认失败 → 写单元定位根因 → 确认失败 → 才允许修复
   - **例外**: 纯内部bug可仅走单元测试复现→修复循环
5. **✅ Git 规范（强制）**

| 规则 | 说明 |
|------|------|
| 环境准备 | 开始任务前先备份一次 |
| 无分页模式 | `git --no-pager log/diff/status`，`git merge --no-edit` |
| 提交前验证 | 改动的 csproj 在 Debug 模式下编译通过即可提交，Release 全量编译由 CI 执行 |
| 禁止跳过 | 即使只改了一个注释，也必须走完整个流水线 |
| 禁止单元测试不通过 | 单元测试不通过 = 不允许提交 |
| 允许 push（非 main/master） | LLM 可 `git commit` + `git push` 到功能分支；禁止 force push 到 main/master |
| HEREDOC 禁令 | PowerShell 不支持 HEREDOC。`git commit` 用多个 `-m` 参数；`gh pr create --body` 用双引号多行字符串 `--body "line1\nline2"`；Shell命令中HEREDOC由 `HeredocRewriter` 自动检测并转换为双引号字符串（优先级200，无需手动处理） |
| 特殊字符禁令 | commit 消息禁止 `$`、反引号、三引号 |
| ⚠️ 源码生成器 + 增量编译 | `dotnet build` 默认增量编译，会缓存生成器输出。**新增/修改 `[Register]` 类后必须用 `--no-incremental` 全量重建**，否则生成器不会重新扫描新类型 |
| PR 两段式验证 | PR 通过 CI 后自动合并到 main → main 自动触发自身 CI 实现二次验证。创建 PR 时必须启用 auto-merge（squash 方式）。PR 目标分支统一为 main，无 dev 中间层 |
| gh 工具优先 | 操作 PR/Issue/Release 等 GitHub 资源时，优先使用 `gh` CLI，而非 PowerShell 脚本或手动操作 |

**Git commit 消息格式**：
- 标准：`类型: 描述`
- 含决策：`类型: 描述 | 决策: [做了什么选择，为什么]`
- 类型：feat / fix / refactor / docs / test / chore
- 示例：`git commit -m "feat: 添加工具搜索功能 | 决策: 优先查MCP记忆再查互联网"`
- **⛔ 禁止包含分支名**：commit 消息中禁止出现 W1/W2/feature-xxx 等分支标识，描述必须说明"做了什么"而非"在哪个分支"
- **⛔ 禁止包含无意义标记**：commit 消息禁止包含 PR/Issue 编号引用（会被 GitHub 自动关联）、纯序号、临时标记等

## 🔄 工作流程

### 经验复用机制（先查后做）

1. **查记忆（开始任务前必做）**
   - 搜同类问题、失败记录、解决方案（可联网）
   - 知识图谱：技术栈 → 问题 → 方案
   - 不要重复造轮子，避免重蹈覆辙
   - 搜索 MemoryCli 等工具
   - 执行记忆查询命令

2. **写记忆（解决问题后必做）**
   - 记录：问题场景、原因、方案、验证结果
   - 标记：【成功经验】/【避坑指南】
   - 要有对应错误原因（什么位置遇到，做过什么尝试不行）
   - 即使失败的经验也是可贵的

3. **注意事项**
   - 不要写项目名到记忆（记忆会越来越大，要保持通用性）
   - 先去检索有什么工具可以读写记忆

### 问题解决优先级链（遇到问题时按顺序执行）

> **原则**: 越靠前的手段成本越低、上下文越精准，禁止跳级查询

1. **🔍 查 MCP 工具**（尤其是记忆 MemoryCli）→ 搜同类问题、失败记录、解决方案
2. **📂 查项目代码** → .ps1 脚本、SearchCodebase、Grep 搜索现有实现模式
3. **🛠️ 查可用技能** → 检查 Skill 工具是否有相关能力（如性能优化、代码组织等）
4. **🌐 查互联网** → WebSearch/WebFetch（最后手段，成本最高、上下文最泛）
5. **❓ 穷尽以上仍无法解决** → 使用 `ask_user` 请求用户决策
5. 没有测试出来就不允许修复,你要复现用户问题,先定位到问题,避免日后出现重复错误,也避免你自以为修复了.

### 交付优先级原则（功能开发时遵循）

| 优先级 | 原则 | 说明 |
|--------|------|------|
| 🟢 | 可运行 > 完美 | 先让核心路径跑通，再优化边缘场景 |
| 🟡 | 质量底线不可妥协 | 编译通过、无运行时崩溃、核心测试通过 |
| 🔵 | 后续可优化项 | 性能优化、边缘场景覆盖、代码美化 |
| ⚠️ | 与 TDD 的协调 | TDD 循环仍需执行，但允许先覆盖核心路径测试，边缘测试后续补充 |

### 渐进式迁移策略（重构时必用）

1. 保证git环境干净，备份一次
2. 每次移动一个功能模块
3. 移动后立即编译验证
4. 编译成功后提交git
5. **禁止一次性大规模重构**

### 对话偏好补充

1. **涉及文件更改时要先列目录树**
2. **架构不合理要提出来，不要直接生成代码**
3. **✅ 渐进式成功后必须记录自主决策**
   - **时机**: 每完成一个功能点并编译成功后，立即记录
   - **位置**: 写到当前工作文件的末尾（不是CLAUDE.md）
   - **格式**: 使用 `<!-- 🤖 Auto Decision: [决策内容] -->` 注释格式
   - **内容**: 说明做了什么决策、为什么这样选择、替代方案是什么
   - **示例**:
     
     ```markdown
     <!-- 🤖 Auto Decision: 2026-04-30 -->
     <!-- 决策: 使用 FrozenDictionary 替代 switch-case -->
     <!-- 原因: 性能更优，符合NativeAOT要求，避免硬编码 -->
     <!-- 替代方案: 特性标记（复杂度较高，暂不采用）-->
     <!-- 验证: 编译通过，测试用例全部通过 ✅ -->
     ```
   - **⚠️ 重要**: 未编译成功的决策不得记录，必须先修复错误
   
4. **结束对话时有未完成的工作或缺陷，一定要⚠️emoji表情提醒**

5. 根据用户对话，自行决定是否采用脚本检测法，否则grep逐个检查太慢，尤其是大型重构
   - 脚本检测法：遍历用户项目代码，生成报告
   - 例如，用户需要你判断全部锁是否范围，重构锁，死锁问题

## 封装要求

> ADR: [0020](docs/adr/0020-encapsulation-requirements.md)（封装要求）、[0019](docs/adr/0019-enum-enumvalue-source-generator.md)（枚举扩展）

| 规则 | 说明 |
|------|------|
| API 粒度 | 尽可能少暴露公开接口，测试用 `internal` 类 |
| 字符串性能 | 用 Span 消除性能差异，用只读类型消除多线程不安全 |
| 类拆分 | 字段太多时拆成多个类，封装层次更清晰 |
| 枚举扩展 | 用 `[EnumValue]` + 源码生成器遍历特性收集函数，实现扩展 |

### 数据容器选型规范（AOT编译 + GC释放效率优先）

| 场景 | 选用容器 | 原因 |
|------|----------|------|
| **检索优先（无序）** | `Dictionary<K,V>` / `HashSet<T>` | O(1) 查找，GC释放效率最优 |
| **硬编码有序（如枚举转字典）** | `SortedList<K,V>` | 连续内存，查找 O(log n)，插入少 |
| **高频插入 + 有序** | `SortedDictionary<K,V>` | 红黑树，插入删除 O(log n) |
| **尾追加顺序写入** | `T[]` / `List<T>` | 最后才选择，连续内存 |
| **AOT不可变查找集** | `FrozenSet<T>` / `FrozenDictionary<K,V>` | AOT友好，不可变，O(1) 查找，GC零分配 |

**容器性能对比**：

| 操作 | `SortedDictionary` (红黑树) | `SortedList` (数组) |
|------|----------------------------|---------------------|
| 插入/删除 | O(log n) ✅ | **O(n)** ❌ (要挪动大量元素) |
| 查找/读取 | O(log n) | O(log n) (二分查找) |
| 内存占用 | 大（每个元素存指针） | **小**（连续内存） |

**禁止行为**：
- **⛔ 禁止 `List<T>` / `T[]` 用作查找集** — `.Contains()` 是 O(n)，高频路径必须用 `HashSet<T>` / `FrozenSet<T>`
- **⛔ 禁止 `static readonly T[]` 用于查找** — 改用 `static readonly FrozenSet<T>`
- **⛔ 禁止内联 `new[] { ... }.Contains()`** — 提取为 `static readonly FrozenSet<T>`

**正确模式**：
```csharp
// 静态查找集 — FrozenSet
private static readonly FrozenSet<string> ValidModes = FrozenSet.Create(
    StringComparer.OrdinalIgnoreCase, "default", "plan", "auto-accept");

// 动态查找集 — HashSet
var scopeSet = new HashSet<string>(scopes, StringComparer.Ordinal);
if (scopeSet.Contains(scope)) ...

// 配置属性懒加载 FrozenSet 缓存
private FrozenSet<string>? _filterSet;
public FrozenSet<string>? FilterSet => _filterSet ??= Filters?.ToFrozenSet();
```

### Claude Code 复刻任务

- 源码参考：`D:\project\claude-code-rust\claude-code-rev-main\src\ `

### 修复计划

| 步骤 | 内容 | 状态 |
|------|------|------|
| a1 | 遗留实现补充：组件 → 链路 → 链路测试 | ✅ 核心完成（TUI渲染链路因迁移WPF跳过） |
| a2 | 每个功能与 ts 文件对比，深度细节 | ✅ 完成（见剩余任务清单.md） |
| a3 | 先构造修复的单个功能计划 md | ✅ 完成（历史/子目录下各分类文档） |
| a4 | 再修复具体代码 | ✅ P0-P2核心功能已对齐，剩余P3 |

**原则**：ts 和 cs 两边功能完美同步，链路断裂不是删除而是修复；过渡方案、临时组件、过时的都删掉，消除两套实现；逐步消除冗余类和方法。

### 枚举 + [EnumValue] 使用规范

1. **有限集合的字符串常量必须枚举化** — 凡是有限个可选值的字符串标识（模型名、角色名、状态名等），必须定义枚举 + `[EnumValue]`，利用源码生成器自动生成 `XxxConstants` + `XxxExtensions`
2. **禁止手动维护 KV 完全相同的映射字典** — 当 Key == Value 时（如 `"gpt-4o" → "gpt-4o"`），直接用 `EnumType[]` + `ToValue()` 遍历匹配，不要写 `(string Key, string Value)[]` 冗余元组
3. **枚举是唯一数据源** — 字符串值由 `[EnumValue]` 定义一次，所有消费方通过 `ToValue()`/`FromValue()`/`XxxConstants` 获取，禁止在消费方重复硬编码相同字符串
4. **Contains 匹配场景** — 对需要模糊匹配（如 `modelId.Contains("gpt-4o")`）的场景，用 `EnumType[]` 按优先级排列，遍历时 `model.ToValue()` 获取匹配串，无需额外字典
5. 一个枚举可以多个特性注释，手动实现字典很蠢啊

## 🔴 平台专属操作禁令

### PowerShell 相关

1. **❌ 禁止使用 PowerShell `Set-Content` 修改 C# 文件**
   - 错误编号: CS1022
   - 原因: 可能导致文件损坏
   - 正确: 使用 IDE 的 `SearchReplace` 工具修改文件内容

2. **❌ 禁止使用 PowerShell 交互式命令**
   - 禁止: `Out-Host -Paging`
   - 推荐: 使用 `| Select-Object -First N` 替代分页

***

## ⚠️ Windows 命令行环境

### 路径格式

- 使用反斜杠 `\` 作为路径分隔符
  - 正确: `C:\Users\Name\Documents`
  - 错误: `/home/user/project`

### 命令分隔

- **禁止使用 `&&`** 连接命令
- 首选: 分步说明，每个命令单独一行
- PowerShell: 使用分号 `;` 连接
- CMD: 可使用单个 `&`（但忽略前序失败）

### 原生工具优先

- 优先使用 Windows 原生命令（`dir`, `findstr`）
- 或 PowerShell cmdlet（`Get-ChildItem`, `Select-String`）
- 避免依赖 Unix 工具（`grep`, `sed`, `awk`），除非明确要求 WSL

### 脚本语言优先级

> ADR: [0022](docs/adr/0022-csharp-ast-cli-over-regex.md)（C# AST CLI 优先于正则）

1. **C# AST CLI 优先**：涉及 C# 源码的批量分析/重构/检测，优先使用 `tools/JccAuditAstCli`（基于 Roslyn 的 AST 分析工具），而非正则或文本替换
   - 构建命令：`dotnet build tools/JccAuditAstCli/JccAuditCli.csproj -c Release`
   - 输出路径：`artifacts/bin/JccAuditCli/Release/net10.0/jcc-audit.exe`
   - 适用场景：Nullable 抑制检测、using 组织分析、命名规范检查、DI 注册验证等需要语义理解的场景
   - **子命令按功能分三组**（`jcc-audit --help` 查看完整用法）：

     | 组 | 子命令 | 用途 | 是否改文件 |
     |----|--------|------|-----------|
     | **审计(Audit)** | `audit` / `ctor-audit` / `layer-audit` | 扫描诊断输出报告 | 否 |
     | **修复(Fix)** | `replace` / `strip-bom` | 应用 CodeFix / 移除 BOM | 是 |
     | **统计(Stats)** | `top-files` | 大文件行数排行 | 否 |

   - **审计组**：
     - `jcc-audit [audit] <csproj-or-slnx> [--filter JCC规则ID] [--skip-tests] [--format json\|text] [--output <file>]` — JCC 规则审计（`audit` 可省略）
     - `jcc-audit ctor-audit <csproj-or-slnx> [--threshold 8] [--skip-tests]` — 构造函数参数审计，超过阈值报告
     - `jcc-audit layer-audit <slnx> [--skip-tests]` — 七层架构层依赖违规检测
   - **修复组**：
     - `jcc-audit replace <csproj-or-slnx> --rule <JCC规则ID> [--fix-all] [--dry-run]` — AST 批量替换，应用 CodeFix 到磁盘文件
     - `jcc-audit strip-bom <directory> [--dry-run] [--skip-tests]` — 移除指定目录下所有 .cs 文件的 UTF-8 BOM（字节级操作，自动跳过 bin/obj/.xxx/.git/artifacts 和 .Designer.cs/.g.cs）
   - **统计组**：
     - `jcc-audit top-files <directory> [--top 10] [--threshold 200] [--skip-tests]` — 按行数降序返回 Top N 大文件
   - **通用选项**：`--output` 写 JSON 报告、`--format json|text`、`--skip-tests` 跳过测试项目、`--dry-run` 预览不写入
   - **退出码**：0=无诊断/成功，1=参数错误，2=超时，3=有 Warning，4=有 Error
2. **Python 脚本次之**：本机 Python 3.12.10，批量文本处理/脚本检测优先使用 `.py` 脚本，而非 PowerShell
   - 适用场景：文件搜索统计、简单文本替换、报告生成等不需要语义理解的场景
3. **PowerShell 最后**：PowerShell 5.1.19041.6456，仅用于系统操作和 dotnet/gh 命令编排
4. **gh CLI 优先**：操作 PR/Issue/Release 等 GitHub 资源时，优先使用 `gh` CLI，而非 PowerShell 脚本或手动操作

### gh CLI 排错避坑指南（强制遵守）

> **以下全是血泪踩坑记录。排错时必须按此指南操作，禁止重复踩坑。**

#### 坑1：`gh api` + jq 在 PowerShell 中引号被吃掉

```powershell
# ❌ 绝对禁止：jq 把 "failure" 解释为除法，报 function not defined: failure/0
gh api .../jobs --jq '.jobs[] | select(.conclusion=="failure") | .id'

# ✅ 正确写法：用反引号转义双引号
gh api .../jobs --jq ".jobs[] | select(.conclusion==`"failure`") | {name:.name, id:.id}"

# ✅ 最可靠：写入 JSON 文件再用 PowerShell ConvertFrom-Json 解析，彻底绕开 jq 引号地狱
gh api repos/{owner}/{repo}/actions/runs/{run-id}/jobs > .xxx/jobs.json
Get-Content .xxx/jobs.json | ConvertFrom-Json | Select-Object -ExpandProperty jobs | Where-Object { $_.conclusion -eq "failure" } | Select-Object name, id
```

**根因**：PowerShell 把双引号当字符串边界吃掉了，jq 收到的是裸单词 `failure`，被解释为除法。别试图用单引号包双引号——PowerShell 单引号不转义，jq 又不认。反引号转义或干脆走 JSON 文件。

#### 坑2：`gh run view --log-failed` 直接超时炸掉

- 失败日志动辄几万行，`--log-failed` 一把梭直接超 60s 超时
- **⛔ 禁止**：`gh run view <run-id> --log-failed` 不加过滤
- **✅ 正确做法**：先拿到失败 job ID，再 `--job <job-id> --log` 精准拉日志，配合 `Select-String` 过滤

```powershell
# 第一步：拿失败 job ID（走 JSON 文件，别用 jq）
gh api repos/{owner}/{repo}/actions/runs/{run-id}/jobs > .xxx/jobs.json
Get-Content .xxx/jobs.json | ConvertFrom-Json | Select-Object -ExpandProperty jobs | Where-Object { $_.conclusion -eq "failure" } | Select-Object name, id

# 第二步：精准拉单个 job 日志，过滤关键行
gh run view <run-id> --job <job-id> --log 2>&1 | Select-String "Failed|FAIL|Test Run Failed|error" | Select-Object -First 20
```

#### 坑3：`gh api` 取 job logs 被 Sandbox 网络拦截

```powershell
# ❌ 报错：Sandbox Network Error: hit restricted [20.205.243.168:443]
gh api repos/{owner}/{repo}/actions/jobs/<job-id>/logs
```

- `gh api` 走的 API 端点可能被 Sandbox 网络策略拦截
- **✅ 解法**：改用 `gh run view --job <job-id> --log`，走不同的 API 路径，不会被拦

#### 坑4：`gh pr checks` 输出格式

```
<check-name>\t<status>\t<duration>\t<url>
```

| status 值 | 含义 |
|-----------|------|
| `pass` | CI 通过 |
| `fail` | CI 失败 |
| `pending` | 正在运行 |
| `skipping` | 前置 job 失败导致跳过 |

**注意**：`skipping` 不是失败！别看到一堆 `skipping` 就以为全挂了，那是依赖链跳过。

#### CI 排错完整流程（按此顺序，不许跳步）

```powershell
# 1. 查看哪些 check 失败
gh pr checks <pr-number>

# 2. 拿到 run-id（从 check URL 里提取，或 gh pr view）
gh pr view <pr-number> --json statusCheckRollup

# 3. 获取失败 job ID（走 JSON 文件，别用 jq）
gh api repos/{owner}/{repo}/actions/runs/<run-id>/jobs > .xxx/jobs.json
Get-Content .xxx/jobs.json | ConvertFrom-Json | Select-Object -ExpandProperty jobs | Where-Object { $_.conclusion -eq "failure" } | Select-Object name, id

# 4. 拉失败 job 日志，过滤关键信息
gh run view <run-id> --job <job-id> --log 2>&1 | Select-String "Failed|FAIL|Test Run Failed" | Select-Object -First 20

# 5. 本地复现失败测试
dotnet test <csproj> -c Release --filter "<test-name>" --nologo /p:SkipLocalPack=true
```

### UTF-8 编码配置

```powershell
[Console]::OutputEncoding = [System.Text.Encoding]UTF8
chcp 65001
```

### .NET 测试和构建输出禁令

1. **❌ 禁止使用 `Out-File` 重定向 dotnet 命令输出**
   - `dotnet test ... | Out-File "$env:TEMP\test.txt"` 是**错误**的
   - 原因: PowerShell 管道逐行传递，`Out-File` 每次写入覆盖前一行，最终文件只有最后一行
   - `Out-File -Append` 虽然不覆盖，但会丢失实时性，无法及时看到结果
    - **✅ 正确做法**: 使用 PowerShell 重定向运算符 `>` 写入日志文件
      - 编译: `dotnet build ... > .xxx/build_log.txt 2>&1`
      - 测试: `dotnet test ... > .xxx/test_log.txt 2>&1`
      - 查看结果: `Get-Content .xxx/test_log.txt -Tail 50` 或 `Select-String -Path .xxx/test_log.txt -Pattern "失败!|已通过!"`
    - **⚠️ 日志文件必须放在 `.xxx/` 目录内**: 如 `.xxx/build_log.txt`，避免被 git 追踪

2. **❌ 禁止使用 `Out-File` 保存编译错误**
   - `dotnet build ... 2>&1 | Out-File "build_error.txt"` 是**错误**的
   - 原因: `Out-File` 通过 PowerShell 管道逐行传递，存在数据丢失风险
   - **✅ 正确做法**: 使用 PowerShell 重定向 `dotnet build ... > .xxx/build_log.txt 2>&1`
   - 查看错误: `Select-String -Path .xxx/build_log.txt -Pattern "error"`

3. **❌ 禁止使用 `Select-String` 过滤 dotnet 输出**
   - `dotnet test ... 2>&1 | Select-String "失败|通过"` 会丢失上下文
   - 原因: 过滤后只剩匹配行，无法看到完整错误信息
   - **✅ 正确做法**: 直接运行，不使用管道过滤

4. **❌ 禁止使用 `Select-Object -Last` 管道连接 dotnet 命令**
   - `dotnet test ... 2>&1 | Select-Object -Last 30` 会导致**进程卡死**
   - 原因: PowerShell 管道是消费端驱动的，`Select-Object -Last N` 必须等所有行输出完才返回最后 N 行。当 dotnet test 输出量大时，PowerShell 管道缓冲区满，dotnet 进程的 stdout 写入阻塞，双方互相等待形成死锁
   - **✅ 正确做法**: 直接运行 `dotnet test/build` 命令，不使用任何管道。终端本身会显示完整输出
   - 如果输出过长，使用 RunCommand 工具的 `CheckCommandStatus` 分段读取，而非 PowerShell 管道


### CLI 运行时测试

1. **✅ 非交互模式测试** — `jcc --trust -p "提示词"` 或 `echo "提示词" | jcc --trust --non-interactive`
2. **✅ 交互式 REPL 测试** — 用 `Register-ObjectEvent` + `BeginOutputReadLine` 异步捕获 stdout，通过 `StandardInput.WriteLine` 发送命令
3. **✅ TUI 模式测试** — `jcctui --trust` 启动独立 TUI 工程（Terminal.Gui v2 全屏界面，多行输入 `Ctrl+Enter` 发送，斜杠命令转发到底层 CmdMap）
4. **⚠️ Mock 测试** — 使用 MockServer 进程提供模拟 AI 响应，通过 `JCC_ENDPOINT` 环境变量指向 MockServer

**常用 CLI 参数**：

| 参数 | 说明 |
|------|------|
| `--trust` | 信任当前目录（跳过目录信任确认） |
| `--bypass` | 跳过所有权限检查（替代旧 `--dangerously-skip-permissions`，等价 `--permission-mode bypass`） |
| `jcctui` | 启动独立 TUI 全屏界面（jcctui.exe，Terminal.Gui v2） |
| `--debuglog` / `-d` | 启用调试日志（等效 `JCC_DEBUGLOG=1`） |
| `--await <seconds>` | 非交互模式超时自动关闭（超时返回 1234） |

### .NET FileMode.Append 陷阱

1. **❌ `FileMode.Append` 在 .NET 5+ 中文件不存在时抛 `FileNotFoundException`**
   - 与 .NET Framework 行为不同！旧版会自动创建文件
   - **✅ 正确做法**: 先检查文件是否存在，不存在则用 `FileMode.CreateNew` 创建空文件
   - 涉及文件: `TranscriptFileWriter`、`BridgeSubprocessManager`
2. **⚠️ `InMemoryFileSystem` 的 `ByteContent`/`TextContent` 不一致**
   - `WriteAllBytes` 设置 `ByteContent`，`AppendAllText` 修改 `TextContent`
   - `ReadAllBytes` 优先返回 `ByteContent`，如果 `ByteContent` 存在但过时，会返回旧数据
   - **✅ 正确做法**: `AppendAllText` 中如果 `ByteContent` 存在，先解码为 `TextContent` 再追加，然后清除 `ByteContent`




# 项目架构

> **详细架构索引见 [README.md](README.md#项目架构索引)**，包含：组件依赖图、组件详情表、内部结构、源码生成器、中间件管道清单、测试结构、构建命令速查、组件名→路径映射

## 关键约束

nuget包: 拒绝全部微软的AI包，因为大部分不支持NativeAOT。
复杂任务: 网络上面查询有没有nuget包,并且支持AOT编译,需要单独项目做测试,避免工程冗余,可以制作卫星项目.

| 约束 | 说明 |
|------|------|
| **目标框架** | `net10.0` |
| **NativeAOT** | 强制，Release 模式自动启用 `PublishAot` + `TrimMode=full` |
| **AOT 兼容** | 禁止 `dynamic`、反射 emit、直接解析 JSON；必须用 `JsonContext` + 源码生成器；写文件 JSON 统一用 `RelaxedJsonSerializer`（> ADR: [0042](docs/adr/0042-json-relaxed-serializer-unification.md)） |
| **GlobalUsings** | `.cs` 文件内禁止写 `using`，统一放 `GlobalUsings.cs` |
| **TreatWarningsAsErrors** | 已启用，零警告容忍 |
| **InvariantGlobalization** | `true`，Release 模式 Exe 项目强制 |
| **全球化策略** | 渐进式双语（中英文），遇到全球化问题时逐步实现，不必一次性处理完 |
| **IsAotCompatible** | 所有源码项目已标记 |
| **MCP 协议版本** | `2025-11-25`（Streamable HTTP）— 旧 `2024-11-05` + SseClientTransport/SseTransport 已归档到 `services/Mcp/.xxx/`；客户端 `HttpTransport` + 服务端 `McpHttpServer`（HttpListener，无状态/有状态双模式）；`MCP-Protocol-Version` 头握手协商，`MCP-Session-Id` 不分配=无状态 |

### 核心技术选型

| 技术 | 用途 | 说明 |
|------|------|------|
| **System.Linq** | LINQ | 标准库，通过 `Directory.Build.props` 全局 `using System.Linq`，所有源码项目自动引用 |
| **MiddlewarePipeline\<TContext\>** | Task 管道 | `Infrastructure.Pipeline` — DI 注入中间件集合，支持 PreHook/PostHook、异常捕获/传播两种模式 |
| **StreamMiddlewarePipeline\<TContext, TEvent\>** | 流式管道 | 同上，返回 `IAsyncEnumerable<TEvent>`，流式场景异常默认传播 |
| **McpHttpServer** | MCP Streamable HTTP 服务端 | `services/Mcp/src/McpProtocol/McpHttpServer.cs` — HttpListener 实现，无状态（不分配 Session-Id）/有状态（分配+DELETE 终止）双模式，GET 开 SSE 推送 NotificationReceived |
| **上下文压缩** | 长对话 token 回收 | Compact（对话级管道）+ Compression（内容级策略）+ Collapse（折叠级）三子系统，Microcompact 纯规则优先、LLM 摘要兜底，CompactOutputGuard 守卫降级 > ADR: [0053](docs/adr/0053-context-compaction-layered-mechanism.md) |

***

# 特殊要求

## 文件整理

要求分类，通常一个文件夹内直接暴露的文件少于十个，可以多层文件夹。
强迫症就是每个文件夹内文件和文件夹不应该同时存在，而是纯文件夹或者内纯文件，不得混淆。

## 编译

### 七层解决方案架构（强制编译顺序）

项目采用七层 slnx 隔离架构，**必须按顺序编译**，上层依赖下层的构建产物：

| 编译顺序 | 解决方案 | 职责 | 目录 | 关键内容 |
|----------|----------|------|------|----------|
| ① | `Generators.slnx` | 源码生成器 | `generators/` | 9 个 Generator + 测试 |
| ② | `Foundation.slnx` | 基础抽象 | `foundation/` | Abstractions + Structura + Transport.Contracts |
| ③ | `Infrastructure.slnx` | 基础设施 | `infrastructure/` | Infrastructure + Transport.Impl |
| ④ | `Core.slnx` | 核心组件 | `core/` | ai/(Llm,Agents,Reasoning) + execution/(Brain,Hands,Scheduling,McpToolDispatch) + safety/(Guard,Vault) + search/(CodeIndex,Browser) |
| ⑤ | `Services.slnx` | 服务组件 | `services/` | Mcp + Dream + Eyes + Bridge |
| ⑥ | `Composition.slnx` | 组合层 | `composition/` | Composition + Clock |
| ⑦ | `App.slnx` | 主工程 | `app/` | JoinCode.exe + Sdk + 集成测试 + MockServers |

**依赖链**：`Generators` → `Foundation` → `Infrastructure` → `Core` → `Services` → `Composition` → `App`

**为什么必须按顺序？**
- `Generators.slnx` 包含源码生成器（EnumMetadata.Generator、McpToolDispatch.Generator 等），它们生成 `XxxConstants` 静态类
- `Foundation.slnx` 中的 Abstractions 需要生成器才能编译出枚举常量
- 如果跳层编译，依赖的 DLL 不存在，编译会失败

**CI 编译命令（Release + 全量）**：
```powershell
dotnet build Generators.slnx -c Release --no-incremental
dotnet build Foundation.slnx -c Release --no-incremental
dotnet build Infrastructure.slnx -c Release --no-incremental
dotnet build Core.slnx -c Release --no-incremental
dotnet build Services.slnx -c Release --no-incremental
dotnet build Composition.slnx -c Release --no-incremental
dotnet build App.slnx -c Release --no-incremental
```

**修改不同层时的编译策略**：
| 修改内容 | 需要重新编译的层 |
|----------|------------------|
| 枚举/Abstractions/generators | ①②③④⑤⑥⑦ 全部 |
| Infrastructure/Transport | ③④⑤⑥⑦ |
| 核心组件（core/） | ④⑤⑥⑦ |
| 服务组件（services/） | ⑤⑥⑦ |
| 组合层（composition/） | ⑥⑦ |
| 主工程源码（app/） | ⑦ |
| 仅测试代码 | 对应的 slnx |

### 开发编译策略（Debug + 增量 + 单 csproj）

**核心原则**：编码期间用 Debug 模式增量编译单个 csproj，Release 全量编译交给 CI。

**开发阶段（改代码时）**：
1. **只编译改动的那个 `.csproj`** — 例如改了 `Llm.csproj` 就只编译 `dotnet build core/ai/Llm/src/Llm.csproj -c Debug`，不编译整个 slnx
2. **使用 Debug 模式** — Debug 编译更快，无需 AOT/Trim 等优化开销
3. **连续修改多个文件时，改完所有文件后再编译一次** — 禁止改一个文件就编译
4. **只有影响面很大时才编译 slnx** — 例如改了 Abstractions 接口导致大量项目受影响，才用 `dotnet build Foundation.slnx -c Debug`

**提交前（git commit 前）**：
1. 不需要本地 Release 全量编译 — CI 会做
2. 只需确保改动的 csproj 在 Debug 模式下编译通过即可提交

**开发编译命令示例**：
```powershell
# 改了 Llm 组件 → 只编译那个 csproj
dotnet build core/ai/Llm/src/Llm.csproj -c Debug
# 改了主工程 CliSession → 只编译主工程
dotnet build app/JoinCode/JoinCode.csproj -c Debug
# 改了 Abstractions → 影响面大，编译基础层 slnx
dotnet build Foundation.slnx -c Debug
```

**CI 全量编译命令（Release + --no-incremental）**：
```powershell
dotnet build Generators.slnx -c Release --no-incremental; dotnet build Foundation.slnx -c Release --no-incremental; dotnet build Infrastructure.slnx -c Release --no-incremental; dotnet build Core.slnx -c Release --no-incremental; dotnet build Services.slnx -c Release --no-incremental; dotnet build Composition.slnx -c Release --no-incremental; dotnet build App.slnx -c Release --no-incremental
```

### 编译注意事项

1. 当遇到编译锁定,编译时候打不开,编译不了,表示有`其他CLI项目`编译中,当前电脑内存紧迫,你只能用 wait 30s 之后再尝试执行编译.
2. 你有 wait 工具吗? 没有的话尝试 powershell 里面的.
3. 一直尝试就好,不要放弃,你肯定可以某个时机交错编译得出来的.

## 测试

```powershell
dotnet test App.slnx -c Release /p:SkipLocalPack=true --filter "Category!=Integration"
```

1. 每个测试都加入一个限时10s，再去找到高耗时。
2. 一旦无法全局测试，出现卡死，就停下来修复全局测试，确保永远都是快速的全局测试。
3. **⚠️ 测试"卡死"排查优先级**（按成本从低到高）：
   - **先查残留 testhost**：`Get-Process -Name testhost | Stop-Process -Force` — 之前 `dotnet test` 被强杀时 testhost 子进程存活，锁住编译产物 DLL 导致后续构建报 `MSB3027 超出重试计数`，表象也是"卡死"。
   - **再查 stdout 管道锁**：`RedirectStandardOutput + ReadToEnd()` 一次性读取会因输出量大缓冲填满而死锁。正确做法：用重定向 `>` 写日志文件再查，不用管道。
   - **最后才是测试逻辑死锁**：见下方"GUI 异步测试"经验。
4. **🔍 定位未标记 Integration 的副作用测试（throw 探针法）**：
   - **场景**：某测试操作鼠标/键盘/文件系统等副作用，但没标 `[Trait("Category", "Integration")]`，导致 `--filter "Category!=Integration"` 仍会触发副作用。
   - **方法**：把可疑的生产方法函数体改成 `throw new NotImplementedException("XXX disabled for testing")`（保留方法签名，编译不报错），然后 `dotnet test` **不带 filter** 运行全部测试，哪些测试抛 `NotImplementedException` 就是哪些测试在调用该方法。
   - **示例**：把 `Win32DesktopInputService.ClickAsync` 和 `TypeTextAsync` 方法体改成 throw，跑 `dotnet test tests/Unit/Hands.Tests/Hands.Tests.csproj`，5个测试抛异常 → 这5个就是操作鼠标的测试。
   - **恢复**：定位完成后 `git checkout -- <文件>` 恢复原始代码。
   - **优势**：比 grep 搜索更准确——能找到间接调用链（如测试调用 `CompoundOperationToolHandlers.MultiClickAsync`，内部再调用 `ClickAsync`），grep 只能找到直接调用。

### GUI / 异步 UI 测试（Avalonia + CommunityToolkit.Mvvm 适用）

- `[RelayCommand]` 生成的 `AsyncRelayCommand` **默认 `AllowConcurrentExecutions=false`**（命令运行中 `CanExecute` 返回 false、UI 自动禁用）。**不要再为"是否被 IsBusy 拦截"写并发测试**——那是框架内置能力，测它=减法思维的冗余测试，且在单线程上下文极难写好。
- **异步命令在 xUnit 单线程 `AsyncTestSyncContext` 下直接 `await` 会死锁**（命令续体被 post 回单线程队列，而测试方法正等命令完成，互等）。解法：命令调用包 `Task.Run(...)` + 对返回任务 `.WaitAsync(TimeSpan.FromSeconds(5))` 硬超时兜底，任何情况下 5 秒内必结束测试。
- 测试标配模板：
  ```csharp
  var vm = new MainViewModel();
  vm.InputText = "hello";
  await Task.Run(() => vm.SendCommand.ExecuteAsync(null)).WaitAsync(Timeout);
  ```
- 分析器铁律：`JCC5002` — 循环内禁止 `+=` 拼字符串，流式追加用 `StringBuilder`。
- 分析器铁律：`JCC9006` — `FileStream` 构造必须用 `FileShare.ReadWrite`（避免跨进程读写冲突），`PhysicalFileSystem`/`SafeFileIO` 已豁免。
- 命令本身不检查 `CanExecute` 就执行命令体（`execute(parameter)` 无条件调用）——So 若需在命令内拦截"运行中"，应在命令体开头显式 `if (IsBusy) return;`。

#### Avalonia XAML 专属坑（2026-08-06 实战）

| 坑 | 错误写法 | 正确写法 |
|----|----------|----------|
| **引用资源** | `<StaticResource x:Key="Foo" />`（会导致运行时 `StaticResourceExtension.ResourceKey must be set` 崩溃） | 绑定处直接用 `{StaticResource Foo}`（App.axaml 注册后全树可见） |
| **ToolTip** | `ToolTip="..."`（AVLN2000） | `ToolTip.Tip="..."`（附加属性） |
| **StackPanel Padding** | `Padding="12,14"`（AVLN2000，StackPanel 无 Padding） | 用 `Margin` 或外包 `Border Padding` |
| **DataTemplate 绑定** | 无 `x:DataType`（AVLN2000 无法解析属性） | `<DataTemplate x:DataType="vm:ChatUiMessage">` |
| **ThemeVariant** | `Avalonia.Themes.Fluent.ThemeVariant`（不存在） | `Avalonia.Styling.ThemeVariant.Dark/Light`，赋给 `RequestedThemeVariant` |
| **转义字符** | XAML 属性里直接用 `<` `>` | 用 `&lt;` `&gt;` 或 `StringFormat` 单引号包裹 |
| **ScrollChanged 首帧 NRE** | `ScrollChanged` 在窗口首次布局时先于 code-behind 命名字段赋值触发（`BackToBottomButton` 为 null，报 0xC0000005） | handler 内判空 `if (BackToBottomButton is not null)` |

**GUI 崩溃诊断**（Avalonia 桌面无控制台，CLI 的 `--await`/stderr 方案不适用）：
- 进程退出码 `-532462766` = `0xE0434352` = .NET CLR 未处理异常
- 在 `App.OnFrameworkInitializationCompleted` 挂 `AppDomain.CurrentDomain.UnhandledException` + `TaskScheduler.UnobservedTaskException`，异常写 `dumps/crash_*.log`（BaseDirectory 下），冒烟崩溃后读该文件定位
- 注意：冒烟启动后可能弹出"是否调试"对话框卡死进程，用 `Start-Process -PassThru` + 定时 `Stop-Process` 兜底

### 启动 exe 测试

当用户要求启动 exe 进行测试时，使用 `Start-Process -Wait` 等待程序结束：

```powershell
Start-Process -FilePath "{当前项目}\artifacts\bin\JoinCode\Release\net10.0\jcc.exe" -ArgumentList "<args>" -Wait
```

示例：
- 启动主程序：`Start-Process -FilePath "{当前项目}\artifacts\bin\JoinCode\Release\net10.0\jcc.exe" -Wait`
- 带参数启动：`Start-Process -FilePath "{当前项目}\artifacts\bin\JoinCode\Release\net10.0\jcc.exe" -ArgumentList "/reset-config" -Wait`

### MockServer + jcc 联合测试

⚠️ **阻塞进程禁止直接运行**，必须后台启动，否则会卡住 sandbox。

**MockServer 参数表**

| 参数 | 格式 | 默认值 | 说明 |
|------|------|--------|------|
| `--port` | `--port <数字>` | 配置文件中的 `port` 字段（0=自动分配） | 监听端口 |
| `--config` | `--config <路径>` | `mockserver.json` | 预设脚本配置文件 |

**MockServer 端点**

| 方法 | 路径 | 说明 |
|------|------|------|
| GET | `/` | 健康检查，返回 `{"status":"ok"}` |
| GET | `/shutdown` | 优雅关闭，返回 `{"status":"shutting_down"}` |
| POST | `/v1/chat/completions` | OpenAI 兼容的 chat 接口（stream=true/false） |
| POST | `{**path}` | 通配 POST，匹配任意路径 |

**1. 启动 MockServer**

```powershell
# ✅ 方式A：Start-Process（推荐，最简单）
Start-Process -FilePath "D:\project\{当前分支名}\artifacts\bin\OpenAI.MockServer\Release\net10.0\JoinCode.OpenAI.MockServer.exe" -ArgumentList "--port","9901"

# ✅ 方式B：ProcessStartInfo（需要捕获输出时用）
$psi = [System.Diagnostics.ProcessStartInfo]::new()
$psi.FileName = "D:\project\{当前分支名}\artifacts\bin\OpenAI.MockServer\Release\net10.0\JoinCode.OpenAI.MockServer.exe"
$psi.Arguments = "--port 9901"
$psi.UseShellExecute = $false
[System.Diagnostics.Process]::Start($psi)

# 验证启动成功：
Invoke-RestMethod -Uri "http://localhost:9901/" -Method Get
# 期望返回：@{status=ok}

# 手动 POST 测试（模拟 jcc 发送请求）：
$body = '{"model":"gpt-4o","messages":[{"role":"user","content":"hello"}],"stream":true}'
Invoke-RestMethod -Uri "http://localhost:9901/v1/chat/completions" -Method Post -Body $body -ContentType "application/json"

# 关闭：
Invoke-RestMethod -Uri "http://localhost:9901/shutdown" -Method Get
```

**踩坑记录**

| 问题 | 原因 | 解决 |
|------|------|------|
| 端口始终绑定到配置文件默认值 | `Start-Process -ArgumentList "--port=9901"` 把等号格式当单参数传入 | 用逗号分隔：`-ArgumentList "--port","9901"` |
| MockServer 启动后立即崩溃 | `File.AppendAllText` 在 Kestrel 多线程中并发写同一文件导致 IOException | 禁止在 KestrelMockServer 中用 File.AppendAllText，只用 Console.WriteLine |
| `RedirectStandardOutput` + `ReadToEnd()` 死锁 | PowerShell 管道消费端阻塞，dotnet 进程 stdout 写入阻塞 | 用 `BeginOutputReadLine()` 异步读取，或不用重定向 |
| Console.WriteLine 在后台进程中不可见 | `UseShellExecute=false` 时输出到父进程控制台，不写文件 | 前台调试用 `& $exe --port 9901`；后台运行靠 dump 文件诊断 |
| jcc 环境变量不生效（JCC_ENDPOINT等） | `ApplyEnvOverrides` 只在 `dotEnv != null` 时调用，无 `.env/api.json` 时环境变量被跳过 | 已修复：`ApplyEnvOverrides` 移出 `if (dotEnv is not null)` 块，无论 dotEnv 是否存在都调用 |
| MockServer 流式最终 chunk 未发送 | `WriteAsync(lastChunk)` 后缺少 `FlushAsync`，`data: [DONE]` 缓冲在服务端 | 在 `BuildStreamFinalChunk` 写入后加 `await ctx.Response.Body.FlushAsync()` |

**2. 启动 jcc 连接 MockServer**

```powershell
$psi = [System.Diagnostics.ProcessStartInfo]::new()
$psi.FileName = "D:\project\{当前分支名}\artifacts\bin\JoinCode\Release\net10.0\jcc.exe"
$psi.Arguments = "--trust --await 20 -p `"echo hello`""
$psi.EnvironmentVariables["JCC_ENDPOINT"] = "http://localhost:9901"
$psi.EnvironmentVariables["OPENAI_API_KEY"] = "sk-test-1234567890"
$psi.EnvironmentVariables["JCC_VENDOR"] = "openai"
$psi.EnvironmentVariables["JCC_MODEL_ID"] = "gpt-4o"
$psi.UseShellExecute = $false
$psi.WorkingDirectory = "D:\project\{当前分支名}"
[System.Diagnostics.Process]::Start($psi)
# --await 20: 20秒超时自动关闭（超时返回1234，正常完成不受影响）
# --debuglog: 启用诊断输出（[WIRE] [STEP] [READY] 等）
```

**jcc 环境变量参数表**

| 环境变量 | 示例值 | 说明 |
|----------|--------|------|
| `JCC_ENDPOINT` | `http://localhost:9901` | API 端点（⚠️ 不要带 `/v1`，jcc 内部会自动拼接 `chat/completions`） |
| `OPENAI_API_KEY` | `sk-test-1234567890` | API 密钥（MockServer 不校验，任意值即可） |
| `JCC_VENDOR` | `openai` | LLM 供应商（openai/anthropic/deepseek/sensenova/agnes） |
| `JCC_MODEL_ID` | `gpt-4o` | 模型 ID（MockServer 不校验，任意值即可） |
| `JCC_PROTOCOL` | `responses` | LLM 协议覆盖（`openai-compatible`/`anthropic`/`responses`），不设置则用供应商配置的默认协议 |
| `JCC_SUBAGENT_MODEL` | `gpt-4o-mini` | 子代理模型全局覆盖（优先级最高，高于 SpawnOptions.Model 和 Agent 定义文件） |
| `JCC_PERMISSION_MODE` | `bypass` | 权限模式（plan/auto/ask/bypass），等价于 `--permission-mode` 参数 |
| `JCC_DEBUGLOG` | `1` | 启用调试日志（等效 `--debuglog` 参数） |

**3. 诊断：查看 MockServer 请求记录**

```powershell
# dump 目录包含每个请求的完整记录
Get-ChildItem "D:\project\{当前分支名}\tests\MockServers\MockServer.Core\dumps\OpenAI" -File | Sort-Object LastWriteTime -Descending | Select-Object -First 5 Name,LastWriteTime
```

***

## 批量替换 C# 源码禁令与导向

| ❌ 禁止 | ✅ 导向 |
|---------|---------|
| `Out-File`/`Set-Content` 写 C# 文件 | `ReadAllBytes` → `.Replace()` → `WriteAllBytes` |
| `[regex]::Replace($text, $pat, '$1')` | `.Replace()` 简单替换；必须正则则写 C# 脚本 |
| `[IO.File]::WriteAllText($path, $text)` | `[IO.File]::WriteAllBytes($path, [Encoding]::UTF8.GetBytes($text))` |
| `git show REV:path \| Out-File` | `git show REV:path > local_path`（重定向） |

**原因**: Out-File 写 UTF-8 带 BOM → CS0234；WriteAllText 可能清空文件；`$1` 被 PowerShell 展开为空

## E2E 测试脚本模式规范

> ADR: [0021](docs/adr/0021-e2e-script-mode-inferred.md)（Mode 计算属性）

### 问题背景

Interactive 模式下 `Console.In.ReadLineAsync` 从重定向 stdin 管道读取存在竞争条件，偶发卡死60s超时。单轮命令尤其容易触发。

### 架构防护：Mode 为计算属性（不可手动设置）

`ConversationScript.Mode` 是**只读计算属性**，根据 `Turns.Count` 自动推断：

```csharp
public ConversationMode Mode => Turns.Count == 1
    ? ConversationMode.NonInteractive   // 单轮 → NonInteractive
    : ConversationMode.Interactive;     // 多轮 → Interactive
```

**开发者无需（也无法）手动设置 Mode**。删除所有 `Mode = ConversationMode.xxx` 赋值，约束编码进类型系统，从架构层面消除模式误用。

### 推断规则

| 脚本类型 | 自动推断为 | 原因 |
|----------|-----------|------|
| **单轮(Turns.Count==1)** | `NonInteractive` | `-p` 参数直接传命令，不经过 stdin 管道，无竞争条件 |
| **多轮(Turns.Count>1)** | `Interactive` | 需要根据上一轮输出发送下一轮输入，无法用 `-p` |

### 运行时不变量断言

`DualRoleConversationRunner.ValidateScriptMode` 和 `CoverageTestBase.ValidateScriptMode` 在运行时断言计算属性推断正确：
- 单轮 + 非 NonInteractive → `[GEN036]` 报错
- 多轮 + 非 Interactive → `[GEN037]` 报错

### 新增 E2E 脚本时的检查清单

1. **不要设置 Mode** — 它是计算属性，赋值会编译失败
2. 单轮命令 → 只写1个 Turn，Mode 自动推断为 NonInteractive
3. 多轮交互 → 写多个 Turn，Mode 自动推断为 Interactive
4. 运行测试确认无 `[GEN036]`/`[GEN037]` 报错

### 定位 E2E 卡死问题的快速方法

1. **先用 `jcc.exe -p "/命令"` 非交互模式验证命令本身** — 不需要 MockServer，1秒出结果
2. **用 `dotnet test --filter "单个测试"` 本地跑** — E2E 框架自己管 MockServer
3. **检查 jcc.exe 时间戳** — E2E 用 `Host.Tests\Debug` 路径，改代码后必须重编译 Host.Tests
4. **同步 `ReadToEnd` 读 stderr** — `BeginErrorReadLine` 在进程被Kill时不flush

# 同义词

## 用户说的"合并"

- **合并 = rebase，禁止 merge**

- 当用户说"合并 main"、"同步 main"、"把 main 合过来"等，一律执行 `git rebase main`，**禁止使用 `git merge`**

- 原因: merge 会产生大量 "Merge branch 'xxx' into yyy" 合并提交，污染历史；rebase 保持线性历史，干净可读

- 唯一例外: 首次将功能分支合入 main 时，由用户手动执行 `git merge --ff-only` 或 `git rebase`

- **rebase 前必须确保工作区干净**：rebase 要求无未提交修改，否则会拒绝执行。处理方式：
  - 先提交：`git add -A; git commit -m "wip: 临时保存"` → `git rebase main`
  - 或暂存：`git stash` → `git rebase main` → `git stash pop`
  
- **⚠️ `reset --hard` vs `rebase` 的生死线**：

  | 场景 | 命令 | 原因 |
  |------|------|------|
  | 分支有**未合入 main** 的新 commit | `git rebase main` | rebase 会把独有 commit 变基到 main 之上，**不丢失** |
  | PR 已合入 main，分支同步 | `git reset --hard main` | 分支 commit 已在 main 中，reset 只是快进指针，**不丢失** |
  | main 与开发分支哈希冲突 | `git reset --hard {分支名}`（在 main 上执行） | squash 合并后哈希不同，reset 直接指向，**不丢失** |

  - **⛔ 绝对禁止**：分支有未合入 main 的独有 commit 时执行 `git reset --hard main` — 这会**永久丢失**这些 commit
  - **判断方法**：`git log --oneline w3 --not main` — 有输出说明有独有 commit，只能 rebase；无输出说明已全部合入，可以 reset
  
- **分支工作流**：任务分支（w1/w2/w3...）→ main 两阶段流水线

  - 任务分支同步 main：`git rebase main`
  - 禁止在 main 上直接提交或 rebase 任务分支

- **两阶段流水线（强制）**：

  1. **任务分支 → main**：PR 触发 CI（编译+单元测试+集成测试+E2E+AOT），CI 通过后 auto-merge（squash）合并到 main
  2. **main 合并后**：开发分支 `git reset --hard main` 同步

- **PR 创建前必须先合并最新 main（强制）**：

  1. `git fetch origin main` — 拉取最新 main
  2. `git merge origin/main` — 合并到当前任务分支（用 merge，不是 rebase，因为要保留完整历史供 CI 验证）
  3. 如有冲突，解决冲突后编译验证
  4. 编译通过后再创建 PR

- **PR 合并后同步流程（强制）**：

  1. main 分支：`git pull --rebase origin main`（拉取 squash 合并后的新提交）
  2. 任务分支：`git reset --hard main`（覆盖为 main 最新状态，避免哈希分叉）
  - 原因: squash 合并后 main 的提交哈希与任务分支不同，不 reset 会导致分支分叉

- **PR 创建规则**：

  - 任务分支 → main：`gh pr create --base main --head w3 --title "feat: xxx"`
  - 创建后启用 auto-merge：`gh pr merge <number> --auto --squash`
  - 禁止手动合并 PR（除非 auto-merge 不可用）

- **CI 触发**：
  - PR 到 main 时触发全量 CI
  - CI 必须通过才允许合并
  - **⛔ dirty PR 不触发 CI**：`mergeable_state=dirty`（分支与 main 有冲突）时 GitHub 不会运行 CI。必须先在分支上 `git merge origin/main` 解决冲突并推送，CI 才会触发
  - **CI 重试**：`gh run rerun <run-id> --failed` 只重试失败的 job（不加 `--failed` 也是默认只重试失败项）
  - **⚠️ auto-merge BLOCKED 排查清单**（2026-07-30 踩坑记录，2026-08-18 修正区分正常/异常）：
    1. **先区分正常 vs 异常 BLOCKED**（最关键一步，跳过会误判）：
       - 诊断命令：`gh pr view {number} --json mergeable,mergeStateStatus,statusCheckRollup --jq '{mergeable:.mergeable, state:.mergeStateStatus, checks:[.statusCheckRollup[] | {name:.name, status:.status, conclusion:.conclusion}]}'`
       - **正常 BLOCKED**（无需处理）：`mergeable=MERGEABLE` 且存在 `status=IN_PROGRESS` 或 `status=PENDING` 的 check → CI 正在运行，等全部通过后 auto-merge 自动触发。其他 `needs: build` 的 job 在 build 完成前不会出现在 checks 列表中，**不要因为"实际 check 数 < required check 数"就判定为名称不匹配**
       - **异常 BLOCKED**（需修复）：所有 check 的 `conclusion` 均非 null（全部完成）且无 `IN_PROGRESS`/`PENDING`，但 `mergeStateStatus` 仍为 `BLOCKED` → 这才是 check 名称不匹配
    2. **异常 BLOCKED 根因**：Branch protection 的 required status checks 名称与 CI workflow 实际 job 名称不匹配 → GitHub 认为该 required check 永远未完成 → PR 永远 `mergeStateStatus: BLOCKED` → auto-merge 永远不触发
    3. **典型案例**：protection 配了 `McpToolHandlers`（旧名），CI 实际是 `McpToolDispatch`（新名），差一个词就导致所有 PR 永远无法 auto-merge
    4. **触发场景**：重命名 CI job、删除/重建保护分支、修改 workflow 文件名后未同步更新 branch protection
    5. **异常 BLOCKED 诊断命令**（仅在确认异常后执行）：
       - `gh api repos/{owner}/{repo}/branches/main/protection --jq '.required_status_checks.contexts'` — 查看保护规则要求的 check 名称
       - `gh pr checks {number}` — 查看 PR 实际的 check 名称
       - 对比两者，找出不匹配的名称
    6. **修复命令**：构造 JSON body 调用 `gh api -X PUT repos/{owner}/{repo}/branches/main/protection -H "Accept: application/vnd.github+json" --input protection_update.json`（需要 `restrictions: null` 字段，否则 422）
    7. **预防**：每次重命名 CI job 后，必须同步更新 branch protection 的 required status checks

## 用户说的E2E

1. MockServers是真实的服务exe,每个用配置文件绑定不同的端口.预设一些对话返回,包括调用read工具.
2. jcc.exe真实启动,通过-p发送对话,到本机服务,加端口参数.不要模拟对话,遇到直接删除.
启动之后,需要观察发生什么错误,并且修复.
3. 当前可能有直接启动jcc.exe的卡死问题,你修改它内部,提供一个启动参数-await 5,
表示停留5s自动关闭.这个agnet肯定可以5s内完成任务.
触发计时器死亡的话,提供一个返回值1234,这个时候你就去修复它内部的东西.
4. 遇到bug,卡死,等等,应该尽可能去加日志点位,不要自己猜测,去行动证明.
过程中遇到任务问题,都必须要修复.并记录到doc.
5. 修复全部的链路和服务.

# 八荣八耻
以瞎猜接口为耻,以认真查询为荣;
以模糊执行为耻,以寻求确认为荣;
以臆想业务为耻,以人类确认为荣;
以创造接口为耻,以复用现有为荣;
以跳过验证为耻,以主动测试为荣;
以破坏架构为耻,以遵循规范为荣;
以假装理解为耻,以诚实无知为荣;
以盲目修改为耻,以谨慎重构为荣;

## 六项架构规则（2026-08-05 新增）

> 📖 各规则已收编为 ADR，详见 [docs/adr/README.md](docs/adr/README.md) 索引。下方每条规则标注对应 ADR 编号，可二次打开查看完整决策上下文与替代方案。

### 规则1：超图与DAG不统一，但 ChainOrder 可升级

> ADR: [0013](docs/adr/0013-hypergraph-vs-dag-separation.md)

- **结论**：DAG 管**执行顺序+硬依赖**（拓扑排序、环检测、增量重算），超图管**评分共享+链路推荐**（语义关联、权重传播）
- **当前**：`ToolHyperedge.ChainOrder` 是 `string[]?`（简单线性链），是 DAG 的特例
- **升级条件**：当 ChainOrder 需要支持分支/汇合（如"分析后可走代码生成或测试生成两条路"）时，改用 `Dag<string>` 替代 `string[]`
- **禁止**：在无实际需求时强行统一两者，造成过度抽象

### 规则2：MCP工具覆盖原则 — 296个工具已覆盖53个Category

> ADR: [0014](docs/adr/0014-mcp-tool-coverage-principle.md)

- **现状**：63个Handler类，296个McpTool方法，覆盖53个ToolCategory
- **新增工具原则**：
  1. 新工具必须归属已有 ToolCategory 枚举值，除非有充分理由新增枚举
  2. 新增 ToolCategory 枚举值需同步更新 `ToolHypergraphPresets`（如有关联工具链）
  3. 优先用 `[McpTool]` + 源码生成器模式，禁止手动实现 `IToolHandler`
  4. 工具描述用中文（对齐 ErrorRecoveryToolHandlers 风格）
  5. 新增工具后必须更新 `ToolCategory` 枚举的 `[EnumValue]` 并全量重建

### 规则3：配置热重载 — 双变量切换模式

> ADR: [0015](docs/adr/0015-config-hotreload-dual-variable.md)

- **现状**：`IConfigChangeNotifier` + `SettingsChangeApplier` 管道已监控 settings.json 变更，但只更新部分字段（EffortLevel、Hook缓存、Permission缓存），**不重建 WorkflowConfig**
- **双变量切换模式**：
  1. 每个可热重载的配置项维护两个变量：`_active`（当前生效）和 `_staging`（新值待切换）
  2. 文件变更时：加载新值到 `_staging` → 验证合法性 → 原子交换 `_active = _staging`
  3. 交换用 `Interlocked.Exchange` 或 `lock`，确保读取端无锁
  4. WorkflowConfig 中的可热重载字段改为 `volatile` 或用 `FrozenDictionary` 不可变快照
- **新增热重载字段**：ToolScoreSettings、BlacklistedTools、ToolPenalties、HyperedgeSettings（评分配置变更最频繁）
- **禁止**：直接修改 `_active` 而不经过 `_staging` 验证

### 规则4：工具函数统一 — 三项合并

> ADR: [0025](docs/adr/0025-archive-dead-imcpprotocolhandler.md)（归档死接口，取代 0012）

- **合并1：双 IToolHandler 接口**
  - `McpProtocol.IToolHandler`（InputSchema=JsonElement, 返回object）保留为 MCP 协议内部类型
  - `Abstractions.IToolHandler`（InputSchema=ToolSchema, 返回ToolResult, 有Kind/GroupName/onProgress）是主接口
  - 两者不合并（语义不同），但 `McpProtocol.IToolHandler` 重命名为 `IMcpProtocolHandler` 避免混淆
- **合并2：三个 ResultBuilder → 一个**
  - `ToolResultBuilder`（Abstractions）= 基础版
  - `ResultBuilder`（Hands）= +WithPdf +WithEntityMetadata
  - `McpResultBuilder`（Abstractions）= +WithBinary +WithEntityMetadata
  - **统一方案**：将 WithPdf/WithBinary/WithEntityMetadata 全部合并到 `ToolResultBuilder`，删除 `ResultBuilder` 和 `McpResultBuilder`
- **合并3：ToolHandler 委托的 toolName 参数**
  - 保留当前设计（DelegateToolHandler 内部补传 Name），不做修改
  - 原因：委托需要工具名做路由，接口通过 this.Name 获取，两者语义不同

### 规则5：参数传递传父类/接口，不传属性

> ADR: [0016](docs/adr/0016-pass-interface-not-property.md)

- **核心原则**：函数参数尽可能传父类/接口/完整对象，到了末尾才拆开使用
- **反面案例**：`bool isBash = shell.Type == ShellType.Bash`，然后传 `isBash` 给下游
- **正面案例**：直接传 `ShellProvider shell`，下游在需要时才 `shell.Type == ShellType.Bash`
- **适用范围**：
  1. 构造函数参数：传接口/完整对象
  2. 方法参数：传接口/完整对象，除非方法只需要一个原始值（如 `int timeoutMs`）
  3. 中间件管道：传 `TContext` 上下文对象，不传上下文的某个属性
- **例外**：当拆开的属性是原始类型且语义独立（如 `string filePath`），不需要传整个 `IFileSystem`
- **重构策略**：渐进式，每次发现一个就修一个，禁止一次性大规模重构

### 规则6：归纳性重构不放弃

> ADR: [0017](docs/adr/0017-inductive-refactor-no-abandon.md)

- **原则**：无论扫描的地方如何复杂，只要存在归纳可能性，都不要放弃重构
- **操作**：
  1. 发现重复模式 → 提取公共方法/基类/接口
  2. 发现相似逻辑 → 用策略模式或模板方法统一
  3. 发现散落的常量 → 枚举化 + `[EnumValue]` + 源码生成器
  4. 发现冗余的 Builder/Helper → 合并到统一入口
- **放弃条件**：必须用户明确同意，AI不得自行放弃
- **验证**：每次重构后编译+测试，确保不破坏现有功能

### 规则7：文件驱动界面 — 配置文件是界面数据的唯一数据源

> ADR: [0005](docs/adr/0005-file-driven-ui.md)

- **核心原则**：任何界面下拉/列表/表格的数据源必须绑定配置文件（如 `models.json`、`settings.json`），禁止硬编码枚举遍历或固定列表。改配置文件 → 自动驱动界面更新，无需改代码重新编译。
- **适用范围**：
  1. 供应商下拉 → 绑定 `ModelConfigLoader.Config.Providers`（`models.json` 的 `providers` 节点）
  2. 模型下拉 → 绑定 `IJccChatSession.AvailableModels`（从 `ModelConfigLoader` 按当前供应商读取）
  3. 工具补全 → 绑定 `IJccChatSession.GetAvailableToolsAsync()`（从引擎 `IToolRegistry` 读取）
  4. 斜杠命令 → 绑定 `IJccChatSession.GetAvailableSlashCommands()`（从源码生成器 `[ChatCommand]` 提取）
  5. 任何未来新增的界面列表数据 → 必须有对应配置文件或引擎数据源，禁止硬编码
- **禁止行为**：
  - **⛔ 禁止硬编码枚举遍历构建下拉列表** — 如 `Enum.GetValues<ProviderKind>()` 填充 ComboBox，改枚举要重新编译
  - **⛔ 禁止在 ViewModel 中写固定列表** — 如 `new[] { "openai", "deepseek" }`，改列表要改代码
  - **✅ 正确做法**：通过 `IJccChatSession` 接口从配置读取，配置文件是唯一数据源
- **热重载**：配置文件变更时通过 `IConfigChangeNotifier` 触发 `OnPropertyChanged(nameof(XxxOptions))` 驱动界面刷新（见规则3双变量切换模式）
- **测试桩**：测试 mock session 实现 `AvailableProviders` 返回固定列表（如 `["fake"]`），不依赖真实配置文件

### 规则8：循环检测器状态机设计风格（推荐）

> ADR: [0018](docs/adr/0018-loop-detector-state-machine.md) | [0054](docs/adr/0054-llm-output-loop-detection-intervention.md)（完整机制）

- **状态机模式**：检测器内部用显式状态枚举 + switch 表达式实现状态转换，不用隐式 `if-else` + 标志变量
  - 状态定义：`enum XxxDetectionState { Monitoring, Suspected, Confirmed }`
  - 转换驱动：`Record(input)` 方法内 `_state switch { ... }` 链式流转
  - 每次返回的结果携带 `State` 字段，调用方可观察当前状态
- **时间窗口二次确认（去抖）**：检测器触发后不立即干预，进入 `Suspected` 状态等待二次确认
  - 确认窗口内（如5s）再次触发 → `Confirmed`（确认为真死循环）
  - 窗口超时 → 复位到 `Monitoring`（误报消除）
  - 时钟通过 `Func<DateTimeOffset>? clock = null` 注入，测试可控、生产用 `DateTimeOffset.UtcNow`
- **配置统一到 Options 子配置类**：检测器所有参数集中到 `LoopInterventionOptions` 的子配置类（如 `ShannonEntropyConfig`），不散落在构造函数默认值
  - 配置类属性有默认值（系统默认配置）
  - `InformationEntropyGuardian` 从 `LoopInterventionOptions` 统一创建所有检测器（生产路径）
  - 测试可直接传入检测器实例（测试路径，保留构造函数默认值）
- **干预层显式状态枚举**：干预级别用 `enum InterventionLevel { None, Soft, Hard, Compact }` + 决策方法 `ClassifyIntervention(count)`，不用 `if-else` 链
- **适用范围**：所有循环/异常检测器（OutputLoop、LogicFingerprint、ToolCallSequence、ShannonEntropy）及干预中间件

## ⚠️ 反例清单（踩过的坑，禁止再犯）

> 📖 部分反例已收编为 ADR，详见 [docs/adr/README.md](docs/adr/README.md) 索引。

### 反例1：不优先查阅 AGENTS.md 已有文档

| ❌ 禁止 | ✅ 正确 |
|---------|---------|
| 自己摸索命令行参数格式 | 先查 AGENTS.md 的"CLI 运行时测试"和"踩坑记录"章节 |
| 用 ProcessStartInfo 手动拼接参数 | 用 AGENTS.md 文档化的 `Start-Process -ArgumentList "--port","9901"` 方式 |
| 修改共享配置文件（mockserver.json）来适配测试 | 用 `--port` 覆盖端口，`--config` 指定配置，不改文件 |
| 遇到问题自己猜方案 | 先查 AGENTS.md 踩坑记录，再查项目代码，最后才自己试 |

**根因**：AGENTS.md 是团队积累的操作手册，包含大量踩坑记录和验证过的命令。跳过它直接试错，浪费时间且容易引入新问题（如改了共享配置文件忘记恢复）。

### 反例2：修改共享配置文件来跑测试

| ❌ 禁止 | ✅ 正确 |
|---------|---------|
| 改 `mockserver.json` 的端口/内容来适配 E2E | 用 `--port 9901` 覆盖端口 |
| 改 `mockserver_cluster.json` 的端口来匹配启动参数 | 启动时用 `--port` 覆盖，配置文件保持原始值 |
| 改完配置文件忘记恢复 | 不改配置文件，用命令行参数覆盖 |

**根因**：配置文件是项目共享的，改了会影响其他人。命令行参数覆盖是零副作用的。

### 反例3：治标不治本的修复链

> ADR: [0024](docs/adr/0024-no-symptomatic-fix-chain.md)

| ❌ 禁止 | ✅ 正确 |
|---------|---------|
| FileShare.None 失败 → 加 FileShare 降级策略 | 先分析根因：是读-写冲突还是写-写冲突？ |
| 降级策略失败 → 换 AppendAllTextAsync | 识别跨进程 vs 同进程，选择正确的同步原语 |
| AppendAllTextAsync 失败 → 加重试 | 跨进程并发 = Named Mutex；读-写冲突 = FileShare.ReadWrite |
| 重试仍失败 → 继续换方案 | 停下来做方案，让用户确认方向 |

### 反例4：加法思维而非减法思维

> ADR: [0023](docs/adr/0023-subtraction-over-addition.md)

| ❌ 禁止 | ✅ 正确 |
|---------|---------|
| 加 `[DoNotAutoRegister]` 新特性来阻止 DI 注册 | 减少不必要的 DI 暴露（如 ShellProviderBase 不需要 IShellProvider） |
| 加 ShellCapabilityProvider DI 单例只为首次检测缓存 | 用静态 ShellCapabilityCache，启动时检测一次 |
| 加 FileShare 降级策略层 | 用 FileShare.ReadWrite + Named Mutex 一步到位 |

### 反例5：依赖模型 ID 字符串推断模态而非显式注册（配置大于代码）

> ADR: [0004](docs/adr/0004-config-over-code-modalities.md)

| ❌ 禁止 | ✅ 正确 |
|---------|---------|
| 代码里硬编码模型 ID 字符串模式推断模态（如含 `vision`→识图） | `settings.json` 的 `vendor.{provider}.models` 显式注册模型描述（含 `Capabilities.Modalities`） |
| `JCC_MODEL_ID` 指定未注册模型时静默推断补注册 | 无条件抛 `ConfigurationException[GRD016]`，要求用户先在 settings.json 注册 |
| `AutoFetchModels` 远程拉取新模型时从 ID 推断模态 | 远程新模型模态留默认（`Text`），用户在 settings.json 手动配置需要的模态 |

**定位文件**：`ConfigLoader.cs:582 EnsureEnvModelInConfig`、`ModelListMerger.cs:39 Merge`