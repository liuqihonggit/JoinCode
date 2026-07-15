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
    - **✅ 唯一正确做法：移动到 `.xxx/` 目录**
      - 格式: `.xxx/{原文件名}.{原后缀}.{时间戳}.del`
   
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

### 开发流程强制要求

0. 脚本替换规范
必须要先在一个文件或者一个项目上面验证成功,才可以用脚本推广到全部位置.
1. **✅ 必须采用渐进式开发方法**
   - 每次只完成一个功能，编译，单元测试，git提交
   - 主工程编译成功后，测试用例也需编译成功
   - 一旦有疑问或发现错误，**立即停止**，先修复再继续
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
| 提交前验证 | `.\build.ps1 -Mode Full` → `.\build.ps1` → 全部通过才允许提交 |
| 禁止跳过 | 即使只改了一个注释，也必须走完整个流水线 |
| 禁止单元测试不通过 | 单元测试不通过 = 不允许提交 |
| 允许 push（非 main/master） | LLM 可 `git commit` + `git push` 到功能分支；禁止 force push 到 main/master |
| HEREDOC 禁令 | PowerShell 不支持，用多个 `-m` 参数替代 |
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
- **⛔ 禁止包含无意义标记**：commit 消息禁止包含 `#数字`（会被 GitHub 自动关联为 PR/Issue 引用）、纯序号、临时标记等

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

| 规则 | 说明 |
|------|------|
| API 粒度 | 尽可能少暴露公开接口，测试用 `internal` 类 |
| 字符串性能 | 用 Span 消除性能差异，用只读类型消除多线程不安全 |
| 类拆分 | 字段太多时拆成多个类，封装层次更清晰 |
| 枚举扩展 | 用 `[EnumValue]` + 源码生成器遍历特性收集函数，实现扩展 |

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

1. **C# AST CLI 优先**：涉及 C# 源码的批量分析/重构/检测，优先使用 `tools/JccAuditAstCli`（基于 Roslyn 的 AST 分析工具），而非正则或文本替换
   - 构建命令：`dotnet build tools/JccAuditAstCli/JccAuditCli.csproj -c Release`
   - 输出路径：`artifacts/bin/JccAuditCli/Release/net10.0/jcc-audit.exe`
   - 适用场景：Nullable 抑制检测、using 组织分析、命名规范检查、DI 注册验证等需要语义理解的场景
2. **Python 脚本次之**：本机 Python 3.12.10，批量文本处理/脚本检测优先使用 `.py` 脚本，而非 PowerShell
   - 适用场景：文件搜索统计、简单文本替换、报告生成等不需要语义理解的场景
3. **PowerShell 最后**：PowerShell 5.1.19041.6456，仅用于系统操作和 dotnet/gh 命令编排
4. **gh CLI 优先**：操作 PR/Issue/Release 等 GitHub 资源时，优先使用 `gh` CLI，而非 PowerShell 脚本或手动操作

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
3. **⚠️ Mock 测试** — 使用 MockServer 进程提供模拟 AI 响应，通过 `JCC_ENDPOINT` 环境变量指向 MockServer
4. **⚠️ 进程锁定** — 运行 jcc.exe 后必须先杀进程再编译，否则 DLL 被锁定导致 MSB3027 错误

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
| **AOT 兼容** | 禁止 `dynamic`、反射 emit、直接解析 JSON；必须用 `JsonContext` + 源码生成器 |
| **GlobalUsings** | `.cs` 文件内禁止写 `using`，统一放 `GlobalUsings.cs` |
| **TreatWarningsAsErrors** | 已启用，零警告容忍 |
| **InvariantGlobalization** | `true`，Release 模式 Exe 项目强制 |
| **全球化策略** | 渐进式双语（中英文），遇到全球化问题时逐步实现，不必一次性处理完 |
| **IsAotCompatible** | 所有源码项目已标记 |

### 核心技术选型

| 技术 | 用途 | 说明 |
|------|------|------|
| **System.Linq** | LINQ | 标准库，通过 `Directory.Build.props` 全局 `using System.Linq`，所有源码项目自动引用 |
| **MiddlewarePipeline\<TContext\>** | Task 管道 | `Infrastructure.Pipeline` — DI 注入中间件集合，支持 PreHook/PostHook、异常捕获/传播两种模式 |
| **StreamMiddlewarePipeline\<TContext, TEvent\>** | 流式管道 | 同上，返回 `IAsyncEnumerable<TEvent>`，流式场景异常默认传播 |

***

# 特殊要求

## 文件整理

要求分类，通常一个文件夹内直接暴露的文件少于十个，可以多层文件夹。
强迫症就是每个文件夹内文件和文件夹不应该同时存在，而是纯文件夹或者内纯文件，不得混淆。

## 编译

1. 当遇到编译锁定,编译时候打不开,编译不了,表示有`其他CLI项目`编译中,当前电脑内存紧迫,你只能用 wait 30s 之后再尝试执行编译.
2. 你有 wait 工具吗? 没有的话尝试 powershell 里面的.
3. 一直尝试就好,不要放弃,你肯定可以某个时机交错编译得出来的.

## 测试

```powershell
dotnet test JoinCode.slnx -c Release /p:SkipLocalPack=true --filter "Category!=Integration"
```

1. 每个测试都加入一个限时10s，再去找到高耗时。
2. 一旦无法全局测试，出现卡死，就停下来修复全局测试，确保永远都是快速的全局测试。

### 启动 exe 测试

当用户要求启动 exe 进行测试时，使用 `Start-Process -Wait` 等待程序结束：

```powershell
Start-Process -FilePath "{当前项目}\artifacts\bin\JoinCode\Release\net10.0\jcc.exe" -ArgumentList "<args>" -Wait
```

示例：
- 启动主程序：`Start-Process -FilePath "{当前项目}\artifacts\bin\JoinCode\Release\net10.0\jcc.exe" -Wait`
- 带参数启动：`Start-Process -FilePath "{当前项目}\artifacts\bin\JoinCode\Release\net10.0\jcc.exe" -ArgumentList "/reset-config" -Wait`

### MockServer + jcc 联合测试

⚠️ **阻塞进程禁止直接运行**，必须用 `Start-Process` 后台启动，否则会卡住 sandbox。

**1. 启动 MockServer（固定端口）**

```powershell
Start-Process -FilePath "{项目根目录}\artifacts\bin\OpenAI.MockServer\Release\net10.0\JoinCode.OpenAI.MockServer.exe" -ArgumentList "--port 9901"
# 验证：Invoke-RestMethod -Uri "http://localhost:9901/" -Method Get
# 关闭：Invoke-RestMethod -Uri "http://localhost:9901/shutdown" -Method Get
```

**2. 启动 jcc 连接 MockServer（需 ProcessStartInfo 传环境变量）**

```powershell
$psi = [System.Diagnostics.ProcessStartInfo]::new()
$psi.FileName = "{项目根目录}\artifacts\bin\JoinCode\Release\net10.0\jcc.exe"
$psi.Arguments = "--trust --force-interactive"
$psi.EnvironmentVariables["JCC_ENDPOINT"] = "http://localhost:9901"
$psi.EnvironmentVariables["JCC_API_KEY"] = "sk-test-1234567890"
$psi.EnvironmentVariables["JCC_PROVIDER"] = "openai"
$psi.EnvironmentVariables["JCC_MODEL_ID"] = "gpt-4o"
$psi.UseShellExecute = $false
$psi.WorkingDirectory = "{项目根目录}"
[System.Diagnostics.Process]::Start($psi)
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
  | main 与开发分支哈希冲突 | `git reset --hard w2`（在 main 上执行） | squash 合并后哈希不同，reset 直接指向，**不丢失** |

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