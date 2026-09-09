# 0080. 手动测试 exe 功能与推荐配置

- 状态：accepted
- 日期：2026-09-08
- 决策者：项目架构组

## 背景

单元测试不能验证整个启动链路（如 DI 卡死、JSON 宽容出错），需要通过启动参数调用工具进行实际运行（非 mock 操作）。本文档定义手动测试的准备工作、验收标准与推荐配置。

## 详细内容

## 手动测试exe功能(非mock操作)

### 准备工作

必须要统一启动参数格式,不要有让人和AI都存在唔会情况.
其它AI在执行工具时候是无法查询这个工具的代码的,
因此你进行手动验证,通过启动参数调用工具进行实际运行,
若出现任何错误和不明确,都会是另一个AI会重复犯的错,也就是隐患.
你需要分析出原因之后,修改代码,实现更宽容应对,也就是面向笨蛋客户的调整.

通过源码生成器,捕捉命令生成map,暴露启动参数上面,
让AI手动调用bash启动并运行,例如`jcc.exe mcp_call xxArgs` 
为什么要真实手动运行?
因为单元测试不能验证整个启动链路,例如DI卡死,json宽容出错.

> bash运行在沙箱会抽风,偶尔获取的CI信息日志条数是0.

### 验收标准
在项目里面有300多个mcp函数,几十个斜杠命令.
面对这种高数量,AI会信心不足而罢工,因此要多轮次进行分解.
第一轮
随机抽样手动运行,把过程中有任何不满,疑惑,都需要修改,
返回值结构化,统一,整理整齐.
你除了是项目的设计者,你还是项目的使用者,请你站在客户角度思考每个交互.
第二轮
脚本测试,但是它只能验证参数是否返回,好处是快速得到坏点.
第三轮
让AI写n个交接文档,
每个文档只处理少量命令,每个文档让用户开一个AI窗口处理.
每次AI只手动执行一个命令测试,和第一轮类似,遇到任何不适都需要改.
手动执行exe的启动参数进行真实运行,你可能会遇到下面几种情况:
1,直接错误,崩溃,换一个边缘参数就蹦了.
2,输入输出格式错误.
3,格式对了,但是 Error:true.
4,格式对了,是 Error:false, 但是没有逾期输出结果,空输出(必须要返回信息,即使这很冗余)
5,超时,死锁.改用Actor管道模式(而不是处理了这边死锁,另一边死锁又出现了,查询已有代码实现)
5,输出结果,但是没有完成对应mcp名称功能,词不达意.
6,设定是jcc内部使用,测试bash沙箱没有能力使用,也就是跨进程无法使用,是持久化没有实现.
7,不要用`设计限制`这种话安慰自己,用不了就是用不了!!现在已经是交付验收阶段,你用不了表示客户也用不了!!
8,成功执行并输出有意义的结果,正面真正的成功,可以交付给客户的.
备注:
必须要统一启动参数格式,不要有唔会情况,发生过一次唔会都需要进行修复.
其它AI在执行工具时候是无法查询这个工具的代码的,
因此你进行手动验证,通过启动参数调用工具进行实际运行,
若出现任何错误,不明确,不适,都会是另一个AI会重复犯的错,也就是隐患.
你需要分析出原因之后,修改代码,实现更宽容应对,也就是面向笨蛋客户的调整.

超过30s的功能都必须要备注,毕竟这个对于用户体感非常不好.

推荐配置（可选项）

> 默认行为是快速路径，能写三行就不引入架构；
> 效率工具是知道热路径才进行优化，按照热点优化而不是全局，除非用户明确要求。

#### [架构选型]

1. **可复用和归纳的函数、类、枚举，仅写一套**：它们非常类似也要尽可能写成一个，避免用户难以理解
2. **泛型模块化**
3. **状态机**（状态查表事件，动作转移）
4. **中间件洋葱模型**（详见下文"核心技术选型"MiddlewarePipeline）
5. **任何资源类都必须树状生长**：这不是反模式设计，这是 is-a，不是鸵鸟问题（重写 fly）。否则难以收集到容器 `map[typeName,object]`，设计资源生命周期，提供插件卸载，可以参考 deepseekHarness。后台会不断扫描这些资源健康，如果被卸载或者宿主死亡了会自动破坏
6. 统一写文件的输入管道,用Actor模型,可以随意只读(可接受脏读风险).
   通过语法分析器禁止随意写,并且推荐管道进行写入.
   需要效率时候,不受这项管控.

#### [效率]

1. **计算字符串需要 0-GC**，效率拉满，学习 1BRC 操作：多线程 + SIMD + 非托管字符数组指针直写
2. **纯异步函数来处理 IO**，高性能内存数据结构用同步函数。异步锁要自己封装 `AsyncLock`，利用 using 释放，锁内超时报错 key 名，避免同步锁定异步的情况（详见下文"AsyncLock 重入检测"）
3. **下载用多线程和断点续传**
4. **直接硬件支持的函数**，例如 CRC 等等
5. **热路径优化**
6. **无锁队列、环形队列、可排序结构、LRU 缓存结构、Everything 扫盘 + 索引**

#### [编译]

- **NativeAOT 编译，内联函数特性支持**（详见下文"关键约束"）
- 如果当前项目不是这样的，就不要改变用户已配置的，除非用户明确要求

## 替代方案

无。手动测试是验证完整启动链路的必要手段，单元测试无法替代。

## 测试进展记录

### 已完成修复（w3 分支，2026-09-09）

| # | Commit | 修复内容 | 根因 |
|---|--------|----------|------|
| 1 | `54ae98d01` | JSON repair 支持 PowerShell 剥引号后值含大括号 | PowerShell 调用 jcc.exe 时剥掉双引号 |
| 2 | `7c5d90133` | voice 工具 CLI 模式返回明确错误 | `voice_start_recording` 假装成功，`voice_transcribe` API Key 未配置时超时 |
| 3 | `6c952394d` | BuildPrCreateJson 多引号导致 GitHub API 解析失败 | `JsonEscapeString` 已自带引号，`BuildPrCreateJson` 又多加引号 |
| 4 | `bad25313c` | brief_mode 跨进程持久化 | 状态存储在内存中，CLI 单次调用模式进程退出后丢失。改为文件持久化 |
| 5 | `fb02b0e73` | csharp-ls LSP 服务器支持 | Encoding.UTF8 BOM 破坏管道通信 + LocationLink 反序列化 + WorkingDirectory 传递 |
| 6 | `8cbd79fb7` | lsp_workspace_symbol 显式 serverName + GitWorkspaceResolver 统一工具类 | workspace_symbol 不接受文件路径无法启动 LSP 服务器；11 处 git root 寻址重复实现 |
| 7 | `0b283419d` | 统一 10 处 git root 寻址到 GitWorkspaceResolver | 消除 11 处重复实现，B1/B2 修复 worktree 支持缺陷 |

### LSP 工具测试结果（10/10 通过）

C# LSP 服务器从 OmniSharp 切换为 csharp-ls（`dotnet tool install -g csharp-ls`，版本 0.27.0，支持 .sln + .slnx）。

| 工具名 | 结果 | 备注 |
|--------|------|------|
| `lsp_hover` | ✅ | 返回方法签名 |
| `lsp_goto_definition` | ✅ | 跳转到定义位置 |
| `lsp_find_references` | ✅ | 找到所有引用 |
| `lsp_goto_implementation` | ✅ | 跳转到实现位置 |
| `lsp_completion` | ✅ | 返回补全建议 |
| `lsp_document_symbols` | ✅ | 返回文档符号列表 |
| `lsp_prepare_call_hierarchy` | ✅ | 返回调用层次项 |
| `lsp_incoming_calls` | ✅ | 返回传入调用 |
| `lsp_outgoing_calls` | ✅ | 返回传出调用 |
| `lsp_workspace_symbol` | ✅ | 显式 serverName + 动态寻址兜底，6 种场景全通过 |

### lsp_workspace_symbol 测试详情（6 种场景）

| # | 场景 | 结果 | 备注 |
|---|------|------|------|
| 1 | .sln + 显式 serverName | ✅ | 返回 Program 符号 |
| 2 | .sln + 动态寻址（无 serverName） | ✅ | 按 .cs 扩展名自动匹配 csharp-ls |
| 3 | .slnx + 显式 serverName | ✅ | csharp-ls 0.18.0+ 支持 .slnx |
| 4 | git worktree + .sln | ✅ | GitWorkspaceResolver 正确解析 worktree .git 文件 |
| 5 | .sln + .slnx 共存 | ✅ | csharp-ls 自动选择解决方案 |
| 6 | 不存在的 serverName | ✅ | 友好错误提示，列出所有可用服务器 |

### GitWorkspaceResolver 统一工具类

提取到 `Abstractions/05-memory/FileIO/GitWorkspaceResolver.cs`，统一项目中 11 处重复的 workspace/git root 寻址实现：
- `FindGitRootAsync` — 向上搜索 git root，支持 worktree .git 文件解析到主仓库
- `FindGitWorkspaceDir` — 向上搜索 git 工作区目录（不解析 worktree 到主仓库，对应原 DiscoverWorkspaceRoot 行为）
- `FindSolutionRoot` — 向上搜索 .sln/.slnx
- `FindWorkspaceRootAsync` — 先找 .sln/.slnx，找不到 fallback 到 git root

已替换的 11 处重复实现：
- A1 `WorktreeGitRootMiddleware.FindGitRootAsync` → 委托 `GitWorkspaceResolver.FindGitRootAsync`
- A2 `AgentWorktreeService.FindGitRootAsync` → 委托 `GitWorkspaceResolver.FindGitRootAsync`
- B1 `AgentMemoryService.FindGitRoot` → `GitWorkspaceResolver.FindGitRootAsync`（修复 worktree 支持缺陷）
- B2 `SourceCodeEngine.SearchUpForGitRoot` → `GitWorkspaceResolver.FindGitRootAsync`（修复 worktree 支持缺陷）
- C1-C6 `DiscoverWorkspaceRoot` × 6 → `GitWorkspaceResolver.FindGitWorkspaceDir`
- D1 `LspService.ResolveWorkspaceRoot` → 已删除，用 `GitWorkspaceResolver.FindWorkspaceRootAsync`
- D2 `LspManager.FindWorkspaceRoot` → 已删除，用 `GitWorkspaceResolver.FindWorkspaceRootAsync`

### 关键修复：Encoding.UTF8 BOM 破坏管道通信

**根因**：`Encoding.UTF8` 默认带 BOM 前缀（EF BB BF）。通过管道写入外部进程 stdin 时，对方收到 BOM 字节后无法识别为合法输入，静默退出。
**修复**：`ProcessEncodingProvider` 改用 `new UTF8Encoding(false)`（无 BOM）。`ProcessStartInfoBuilder` 添加防御性异常 `ValidateNoBomEncoding`，禁止带 BOM 编码用于进程管道 I/O。

### 无法交付项

| 类别 | 数量 | 原因 | 解决方案 |
|------|------|------|----------|
| AI 限流 | 10 | sensenova 无 API 额度 | 切换到有额度的 AI 提供商 |
| GitHub 清理 | 4 | token 缺 `delete_repo` scope | 添加 scope 或手动删除 |
| Anthropic 依赖 | 1 | web_search 需 Anthropic API | 配置 Anthropic 提供商 |
