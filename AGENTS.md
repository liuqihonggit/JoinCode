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

### 项目风格与复用

1. **无后向兼容** — 项目不需要任何后向兼容，遇到相关字样直接删除，大修大改
2. **JSON 宽容** — 已实现 JSON 宽容解析（RelaxedJsonSerializer），无需重复实现
3. **Rust 风格报错** — 已实现 Rust 编译器风格报错（带行列指示+代码片段+箭头），面向参数错误必须用此风格
4. **BitMask 位掩码工具类** — 已实现 `BitMask` 静态工具类（`Abstractions/00-core/Core/Utils/BitMask.cs`），类似 BitArray/Bitmap，减少 hash 查找、降低内存使用、提高性能。枚举集合优先用 `BitMask.Of()` + `BitMask.Contains()`，替代 `FrozenSet<Enum>`
5. **字符串处理优先级** — 首选用 `Span<char>`（0-GC）、SIMD、mmap、`AsParallel()` 链式编程风格

***

## 🎯 AI 助手总纲（设计哲学与编码准则）

### 工具调用准则

- **执行 bash 工具前，先思考先后果而非结果**，避免一切不可撤回的动作
- **用移动代替删除**：有非要删除的情况必须调用 `ask_user` 工具让用户接管（详见下文"🔴 绝对禁止"第1条）
- **Agent 层已帮你把工具分类权限**：用户明确睡觉等无人值守环节下，不调用 `ask_user` 工具

### 架构优先

- **先思考最坏代码方式**，要有反例才能知道如何写好（详见下文"⚠️ 反例清单"）
- **不要用简单方式为导向**，而是满足架构最优，方便日后不断扩展
- **使用 ADR 保证全局决策**，每个任务用 spec 来保存执行（详见下文"ADR 工作流"）

### 开心路径

先充分理解当前项目和用户需求，防止架构疲劳，推荐使用开心路径处理项目：

1. **主路径清晰暴露实现路径**：例如 main 仅做配置工作，每个命令是一个主路径
2. **主路径上面先实现开心路径**，对于边缘场景一律采用断言屏蔽。任务结束后推荐用户用新窗口做之后的任务，需要根据路径进行创立新的 spec 或者 plan

**开心路径设计理由**：
- a. 边缘场景没有覆盖可以在上层正确就终止，而不是在底层过程游离
- b. 防止非核心路径过分补充，先做第一版代码给用户验证方向
- c. 即使有工作计划、TODO 表，也会因为上下文压缩保留 commit 信息条目过多，造成 AI 信心疲劳，后续 AI 会注意力涣散和偷懒
- d. 断言要具备栈帧信息，AI 调试 exe 时候可以立即整改
- e. 外部环境传入参数是异常，内部屏蔽就是断言。发布期间会自动清理
- f. 如果当前已经进入了处理断言阶段，就逐个断言展开决策和修复方案，不要同时处理多个

### 替换便利

为了未来使用脚本替换函数方便,
你应该把函数消费写得相同和类似,这样替换才能更便利.

- 多态,在这个要求场景非常优美.
- 函数重载要慎用,因为这个目的消除消费耦合,不要难以替换.
- 实在不行就封装多一个函数.

> 替换时必须为未来做工作：当当前生产代码的写法/格式与替换目标不一致时，必须先转为**统一写法和格式**，再执行替换（幂等可重复）。提示词来源：`ReplacementMethodologySection`（系统提示词 Section，关键词"替换/批量替换"触发注入）

### 字典配置

大量采用和改造为：**AOT 元编程 + 关注点分离 + 约定大于配置**。

- 通过源码生成器获取特性（非 AOT 就反射）
- 通过非耦合方式进行扩展，特性写在函数上面，构造各种字典，例如 cmdMap
- 减少硬编码，大量采用 `nameof(xxType)` 防止改了类型忘记改字符串
- 详见下文"枚举 + [EnumValue] 使用规范"和"封装要求"

### 不要预估时间

你是 AI 助手写代码非常迅速，可以把人类的工作计划压缩到数分钟，因此你的 spec、adr、plan 等文档不要预估任何时间，除非用户明确要求。

### 和用户交互

- 方案选择的时候需要面向未来，选择插件式，好扩展，好替换的方案，架构最美设计，明确向用户说明好坏。
- **每次只问用户一个问题，**不要一次多个问题，多轮对话总能更好让用户选择方案，使用 `ask_user` 工具。
- **让用户做选择题而不是问答题**，让你自主决策的时候，则按照第一条架构最美设计。
- 详见上文"核心规则：每次回复结束前必须调用 ask_user 工具"
### 用户期望:
用户明确睡觉等无人值守环境,说明他期望你可以持续推进一个长任务.
用户希望醒来之后看见所有任务都已经完成,并且没有遗漏任何边缘场景.
你此时需要实现

1 主路径任务(开心路径)
a,文档
b,代码
c,单元测试
d,手动验证,通过设置启动参数,通过bash调用来实际运行,真实暴露运行,而不是mock.

2 边缘场景

实现流程跟主路径任务一样,
但是必须要主路径任务完成之后才能实现,禁止过早处理.

3 当发生上下文压缩

回来看看AGENTS.md,它能帮助你回忆这些必要要求.



## 手动测试exe功能(非mock操作)

> ADR: [0080](docs/adr/0080-manual-exe-testing-guide.md) — 详见 ADR 文档（含验收标准、推荐配置[架构选型/效率/编译]）

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

0. **⛔ 禁止把 bug 修复报告写成 ADR** — ADR 只用于统筹"为什么选 A 放弃 B"的架构决策。bug 修复/功能开发/排错过程**不是架构决策**，用 commit message（根因+修复+验证）+ 测试复现。判断：没有真正的"替代方案（考虑过但放弃）"就不是 ADR
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
| ⛔ gh 工具强制（禁系统 gh） | 操作 PR/Issue/Release/CI 等 GitHub 资源时，**必须**用 `jcc mcp_call gh_*`（直调 GitHub REST API，无需系统 gh CLI）；**禁止**裸调系统 `gh` CLI 或改用 PowerShell 脚本手动操作 > ADR: [0073](docs/adr/0073-gh-rest-api-direct-call.md)、[0089](docs/adr/0089-jcc-builtin-tools-only-no-system-gh-rg.md) |
| ⛔ rg 工具强制（禁系统 rg） | 日常代码/文本搜索**必须**用 `jcc rg`（或 `jcc mcp_call grep`）；**禁止**裸调系统 `rg` 或用宿主 IDE 内置 Grep 工具 > ADR: [0070](docs/adr/0070-rg-engine-mmap-plinq.md)、[0089](docs/adr/0089-jcc-builtin-tools-only-no-system-gh-rg.md) |

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
2. **📂 查项目代码** → .ps1 脚本、SearchCodebase、`jcc rg` 搜索现有实现模式（⛔ 禁止系统 rg / 宿主内置 Grep）
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

5. 根据用户对话，自行决定是否采用脚本检测法，否则 `jcc rg` 逐个检查太慢，尤其是大型重构
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

> ADR: [0085](docs/adr/0085-data-container-selection-spec.md) — 详见 ADR 文档（含容器性能对比、禁止行为、正确模式）

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

## 代码风格规范（资源管理与异常控制）

> ADR: [0093](docs/adr/0093-resource-management-exception-style.md) — 详见 ADR 文档（含替代方案、DisposeSafe 扩展方法实现、验证清单）

### 规则1：资源释放强制 `using var` / `await using var`

任何 `IDisposable`/`IAsyncDisposable` 对象，在当前作用域内创建且不逃逸，**必须**用 `using var` / `await using var` 声明。禁止裸 `new` 后手动 `Dispose` 或 `try-finally` 释放。

**✅ 正确**：
```csharp
using var stream = new FileStream(path, FileMode.Open, FileAccess.Read);
using var reader = new StreamReader(stream, encoding);
return await reader.ReadToEndAsync(ct).ConfigureAwait(false);
```

**❌ 错误**（裸 new + try-finally）：
```csharp
var reader = new StreamReader(new FileStream(path, FileMode.Open, FileAccess.Read));
try { return await reader.ReadToEndAsync(); }
finally { reader.Dispose(); } // FileStream 未释放，且样板冗余
```

**例外**（允许手动释放，需注释说明）：
- 字段持有的长生命周期资源 → 在 `Dispose(bool)` 中释放
- 工厂方法返回可释放对象（如 `OpenRead()` 返回 `Stream`）→ 调用方负责
- 故意不释放底层流（`leaveOpen: true` 或 `Console.OpenStandardInput()`）→ 注释说明

### 规则2：一个方法一个 `try-catch`（尽量）

一个方法内尽量只保留一个 `try-catch`。化解手段（按优先级）：
1. **`using var` 消除 `finally`** — 资源释放交给 using，去掉 try-finally
2. **提取辅助方法** — 每个资源操作独立成方法各自 using，主方法只编排
3. **合并相邻 try-catch** — 异常处理相同时合并，用 `when` 子句区分类型
4. **嵌套 try-catch 提取** — try 内套 try（补偿/回退逻辑）提取为独立方法，外层 catch 调用

**❌ 错误**（Dispose 里 3 个 try-catch）：
```csharp
public void Dispose() {
    try { _cts.Cancel(); } catch (ObjectDisposedException ex) { _logger?.LogWarning(ex, "x"); }
    try { _cts.Dispose(); } catch (ObjectDisposedException ex) { _logger?.LogWarning(ex, "y"); }
    try { _sem.Dispose(); } catch (ObjectDisposedException ex) { _logger?.LogWarning(ex, "z"); }
}
```

**✅ 正确**（用 DisposeSafe 扩展方法）：
```csharp
public void Dispose() {
    _cts.CancelAndDisposeSafe(_logger);
    _sem.DisposeSafe(_logger);
}
```

### 规则3：`DisposeSafe` 扩展方法（消除 Dispose 样板）

`Abstractions/00-core` 提供 `DisposeSafeExtensions`：
- `obj.DisposeSafe(logger)` — 吞 `ObjectDisposedException`（幂等），其他异常可选日志
- `cts.CancelAndDisposeSafe(logger)` — Cancel + Dispose 合并

所有 `Dispose()` 方法禁止再写 `try { x.Dispose(); } catch (ObjectDisposedException)` 样板，统一调 `x.DisposeSafe(_logger)`。

### 好代码一键清单（推荐模式速查）

| 场景 | ✅ 推荐 | ❌ 禁止 |
|------|---------|---------|
| 作用域内资源 | `using var` / `await using var` | 裸 `new` + 手动 `Dispose` |
| 异步资源 | `await using var` | async 方法里 `.Dispose()` |
| Dispose 多资源 | `x.DisposeSafe(_logger)` | 每资源一个 try-catch |
| 取消令牌链接 | `using var cts = CancellationTokenSource.CreateLinkedTokenSource(...)` | 手动链接 + try-finally |
| 只读集合 | `FrozenSet<T>` / `FrozenDictionary<K,V>` | `HashSet` + `AsReadOnly()` |
| 枚举集合 | `BitMask.Of()` + `BitMask.Contains()` | `FrozenSet<Enum>` |
| 字符串切片 | `Span<char>` / `ReadOnlySpan<char>` | `Substring` 链式分配 |
| JSON 解析 | `using var doc = JsonDocument.Parse(...)` | 不 using 的 JsonDocument |
| 路径拼接 | `Path.Combine` | 字符串 `+` 拼接路径 |
| 空检查 | `ArgumentNullException.ThrowIfNull` | `if (x == null) throw new...` |
| 配置字典 | 枚举 + `[EnumValue]` 源码生成 | 手动 `(string,string)[]` 元组 |
| 临时目录 | `await using var tmp = TempDirScope.Create(fs)` | 手写 `CreateDirectory` + try-finally `DeleteDirectory` |
| 环境变量临时设置 | `using var env = EnvVarScope.Set("K","v").Add("K2","v2")` | 手写 `var prev=Get;Set;try{}finally{Set(prev)}` |
| 工作目录切换 | `using var cwd = CwdScope.Enter(fs, newPath)` | 手写 `var prev=GetCwd;SetCwd;try{}finally{SetCwd(prev)}` |
| 事件订阅 | `await using var sub = bus.SubscribeAsync(handler)` | `Subscribe` + 手动 `Unsubscribe` try-finally |

## 🔴 平台专属操作禁令

> ADR: [0084](docs/adr/0084-platform-windows-env-rules.md) — 详见 ADR 文档（含 PowerShell 禁令、路径格式、命令分隔、脚本语言优先级[AST CLI/Python/PowerShell/jcc gh/jcc rg]）

### 🔧 jcc 自带工具统一入口（⛔ 禁止系统/宿主 gh/rg）

> ADR: [0089](docs/adr/0089-jcc-builtin-tools-only-no-system-gh-rg.md) — 详见 ADR 文档（含实测证据、边缘错误提示清单、`jcc rg` 宽容策略 7 条）

**`jcc.exe` 启动后已自带大量工具**（实测：`jcc mcp_list` = **390 个工具 / 43 个分类**，另有 `jcc rg` 等 CLI 子命令）。
因此 **⛔ 禁止使用系统/宿主环境自带的 `gh` / `rg`**：

- 禁止在 Bash/PowerShell 里裸调 `gh`、`rg` 可执行文件
- 禁止用宿主 IDE 内置的 Grep 工具代替 `jcc rg`
- 报错时按 jcc 提示自愈修正参数，**不得**因为 jcc 工具报错就回退到系统 `gh`/`rg` 绕过

**1. `jcc.exe` gh 工具 → 处理 GitHub 的 PR 和 CI 问题**（`github` 分类 31 个 `gh_*`，HttpClient 直调 REST API，无需系统 gh CLI）

CLI 形态（**推荐**，ADR: [0090](docs/adr/0090-jcc-gh-cli-subcommand.md)）：

```bash
jcc gh pr list --limit 3            # 列 PR
jcc gh pr checks 123                # CI 检查状态
jcc gh pr view 123                  # PR 详情
jcc gh run view 123 --log --filter error   # CI 日志（只留 error）
jcc gh run rerun 123                # 重跑失败的 job
jcc gh issue comment 12 "正文"       # 评论 Issue
jcc gh api repos/o/r/issues         # 通用 REST 调用
jcc gh --help                       # 完整用法
```

MCP 工具直调形态（脚本/结构化场景，ADR: [0073](docs/adr/0073-gh-rest-api-direct-call.md)）：

```bash
jcc mcp_list --category github                      # 列出全部 gh_* 工具
jcc mcp_schema <tool>                               # 查参数 schema，不必记忆
jcc mcp_call gh_pr_checks '{"pr_number":"123"}'     # pr_number 必填
jcc mcp_call gh_run_view  '{"run_id":"123"}'        # run_id 必填
```

两种入口等价：`jcc gh <group> <action>` 按约定拼成 `gh_{group}_{action}`，位置参数按工具 schema 的 `required` 顺序绑定（`pr_number` / `run_id` / `tag` / `issue_number`…），选项支持 `--key value`、`--key=value`，连字符自动归一化为下划线（`--max-lines` → `max_lines`）。

**2. `jcc.exe` rg 工具 → 处理日常搜索**（内置 `RgEngine`，已对边缘错误实现友好提示）：

```bash
jcc rg "pattern" <path> [path...]     # CLI 场景：path 必填，退出码 0=有匹配/1=无匹配/2=超时
jcc mcp_call grep '{"pattern":"x","path":"core/"}'   # MCP 场景：结构化输出
```

`jcc rg` 宽容策略（缺路径/根目录/超时/无匹配全部给出可执行提示，不会静默扫盘卡死）：

| 边缘情况 | 行为 |
|----------|------|
| PowerShell 传成 `\\s` | 自动修复为 `\s` |
| 缺 `path` | 立即报错退出（禁止无路径搜索） |
| 根目录 `C:\` / `/` | 拒绝扫盘 |
| 超时 | 硬终止，返回退出码 2（默认 30s，最大 300s） |
| 无匹配 | 退出码 1（对齐 rg） |
| 二进制 / `.gitignore` | 自动跳过 |

### gh CLI 排错避坑指南（强制遵守）

> ADR: [0075](docs/adr/0075-gh-cli-troubleshooting-guide.md) — 详见 ADR 文档（含坑1-5：jq引号/超时/Sandbox拦截/checks格式/管道死锁 + CI排错完整流程）

### UTF-8 编码配置

```powershell
[Console]::OutputEncoding = [System.Text.Encoding]UTF8
chcp 65001
```

### .NET 测试和构建输出禁令

> ADR: [0076](docs/adr/0076-dotnet-test-build-output-rules.md) — 详见 ADR 文档（含 Out-File/Select-String/Select-Object 禁令、CLI运行时测试[常用参数/扁平元动词/jcc rg]、FileMode.Append 陷阱）

# 项目架构

> **详细架构索引见 [README.md](README.md#项目架构索引)**，包含：组件依赖图、组件详情表、内部结构、源码生成器、中间件管道清单、测试结构、构建命令速查、组件名→路径映射

## 测试项目地图

**49 个测试项目**，按七层解决方案 + 跨层测试中心组织：

| 层 | 数量 | 位置 |
|----|------|------|
| ① Generators | 2 | `generators/*/tests/`（AotSafety, Fsm.Generator） |
| ② Foundation | 2 | `foundation/*/tests/Unit/`（AsyncLock, Structura） |
| ③ Infrastructure | 3 | `tests/Unit/Infra.Tests/{IO,Services,Utils}/` |
| ④ Core | 20 | `core/*/tests/`（ai, execution, safety, search） |
| ⑤ Services | 5 | `services/*/tests/Unit/`（Bridge, Dream, Eyes, Mcp, Vision） |
| ⑥ Composition | 2 | `composition/*/tests/Unit/`（Composition, Clock） |
| ⑦ App | 2 | `tests/Unit/Host.Tests/`, `tools/*/tests/` |
| 跨层 | 13 | `tests/Unit/{Abs,Hands,JoinCodeGui,Tui}.Tests/`, `tests/Integration/`, `tests/MockServers/` |

## CI 流水线

CI 拆分为可复用 workflow（PR → main 触发）：

| Workflow | Jobs | 说明 |
|----------|------|------|
| `ci.yml` | 4 | 主入口，调用子 workflow |
| `ci-build.yml` | 1 | 七层有序编译 + 组件测试项目 + 卫星项目 |
| `ci-unit-tests.yml` | 40 (matrix) | 每个 csproj 独立 job，`--filter "Category!=Integration&Category!=Benchmark"` |
| `ci-integration.yml` | 5 | `Integration.Tests`（64 .cs）+ App.slnx filter 分组 |
| `ci-e2e.yml` | 14 | MockServer.E2E + Sync.Integration + CodeIndex.E2E + smoke tests |
| `mutation-testing.yml` | matrix | Stryker.NET，每日定时 |

> 注：`tests/Unit/Mcp.Tests`（ToolInterventionManagerTest）和 `tests/Unit/McpToolDispatch.Tests`（ToolHealthMonitor/Scorer/Template/ScoreDebug）有独特测试类，已加入 JoinCode.slnx + CI unit-tests matrix

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
| **Workflow 断点续跑** | DAG 模式每层完成后原子保存快照 `workflow_{id}.state.json`，启动时加载跳过已完成步骤；`IWorkflowStateStore` 可选注入（> ADR: [0097](docs/adr/0097-workflow-checkpoint-resume.md)） |

### 核心技术选型

> ADR: [0086](docs/adr/0086-core-tech-selection-lock-design.md) — 详见 ADR 文档（含 MiddlewarePipeline/StreamMiddlewarePipeline/McpHttpServer/上下文压缩/AsyncLock + 锁设计与死锁防护[Actor模型/状态机]）

***

# 特殊要求

## 文件整理

要求分类，通常一个文件夹内直接暴露的文件少于十个，可以多层文件夹。
强迫症就是每个文件夹内文件和文件夹不应该同时存在，而是纯文件夹或者内纯文件，不得混淆。

## 编译

### 七层解决方案架构（强制编译顺序）

> ADR: [0081](docs/adr/0081-seven-layer-build-strategy.md) — 详见 ADR 文档（含七层依赖链、CI编译命令、修改不同层时的编译策略、开发编译策略[Debug+增量+单csproj]、编译注意事项）

## 测试

> ADR: [0088](docs/adr/0088-test-execution-rules.md) — 详见 ADR 文档（含全局测试命令、卡死排查优先级[testhost/管道锁/逻辑死锁]、throw 探针法定位副作用测试）

### GUI / 异步 UI 测试（Avalonia + CommunityToolkit.Mvvm 适用）

> ADR: [0082](docs/adr/0082-gui-async-test-avalonia.md) — 详见 ADR 文档（含 AsyncRelayCommand 死锁解法、测试标配模板、Avalonia XAML 专属坑、GUI崩溃诊断、启动 exe 测试）

### MockServer + jcc 联合测试

> ADR: [0077](docs/adr/0077-mockserver-jcc-joint-testing.md) — 详见 ADR 文档（含 MockServer 参数表/端点、启动方式、踩坑记录、jcc 环境变量参数表、诊断方法）

## 批量替换 C# 源码禁令与导向

> ADR: [0087](docs/adr/0087-batch-replace-csharp-source-rules.md) — 详见 ADR 文档（含 Out-File/Set-Content/regex/WriteAllText 禁令与正确导向）

## E2E 测试脚本模式规范

> ADR: [0083](docs/adr/0083-e2e-script-mode-spec.md) — 详见 ADR 文档（含 Mode 计算属性、推断规则、运行时不变量断言[GEN036/GEN037]、新增 E2E 脚本检查清单、定位卡死方法）

# 同义词

## 用户说的"合并"

> ADR: [0078](docs/adr/0078-merge-e2e-synonym-rules.md) — 详见 ADR 文档（含 rebase vs merge、reset --hard 生死线、两阶段流水线、PR创建/同步流程、auto-merge BLOCKED 排查、E2E定义）

## 用户说的E2E

> ADR: [0078](docs/adr/0078-merge-e2e-synonym-rules.md) — 同上，详见 ADR 文档

# 八荣八耻
以瞎猜接口为耻,以认真查询为荣;
以模糊执行为耻,以寻求确认为荣;
以臆想业务为耻,以人类确认为荣;
以创造接口为耻,以复用现有为荣;
以跳过验证为耻,以主动测试为荣;
以破坏架构为耻,以遵循规范为荣;
以假装理解为耻,以诚实无知为荣;
以盲目修改为耻,以谨慎重构为荣;

# 本项目规则

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

> ADR: [0079](docs/adr/0079-anti-pattern-examples.md) — 详见 ADR 文档（含反例1-6：不查AGENTS.md/改共享配置/治标修复链/加法思维/模型ID推断模态/多余ToList拷贝）

