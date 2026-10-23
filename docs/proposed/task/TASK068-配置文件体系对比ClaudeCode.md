# TASK068 — 配置文件体系对比 ClaudeCode（差异文档 + 一问一答）

> 状态：proposed
> 对比范围：C# 侧全仓配置体系（guard/abstractions/composition/apps/mcp/native_tui/native_gui）vs 基准侧 `D:\project\claude-code-rust\claude-code-rev-main\src\`
> 审计日期：2026-10-23
> 配套产出：Bug1-Bug3 直接修复（C# 内部不一致，无需决策）；本文档只列**需要用户决策**的差异项

## 总览

| 维度 | TS ClaudeCode | C# jcc | 定性 |
|------|---------------|--------|------|
| 主配置 | `~/.claude/settings.json` 扁平单文件 | `~/.jcc/settings.json` 极简主入口 + `current/` 10 子文件 + `providers_llm/` + `providers_search/`（TASK054 有意架构） | 有意差异，保留 |
| 全局状态 | `~/.claude.json`（GlobalConfig） | `~/.jcc/global.json` 扁平 KV | 已对齐 |
| 5 源合并 | userSettings→projectSettings→localSettings→flagSettings→policySettings | 同序（SettingSource 枚举，含 GlobalConfig=-1 共 6 源） | 已对齐 |
| 目录重定向 | `CLAUDE_CONFIG_DIR` 单一变量 145+ 消费 | **三套并存**（见 Q1） | ❌ 需决策 |
| 配置键 | SettingsSchema 80+ 键全消费 | CurrentSettings 49 键：16 活跃 / 10 仅命令读写 / 12 死链 / 11 死键 | ❌ 需决策（Q4） |
| hooks | 顶层 `hooks` 单一定义 + 5 源合并 | **双定义**（顶层活链 + `current.hooks` 死 DTO） | ❌ 需决策（Q2） |
| managed-settings | `C:\Program Files\ClaudeCode\` + drop-ins + MDM 注册表/plist + 远程 first-source-wins | `C:\ProgramData\jcc\` 单文件 + RemoteManagedSettingsService | ❌ 需决策（Q3） |
| env 安全模型 | 两阶段应用 + SAFE_ENV_VARS 白名单 + 危险变量确认对话框 | SettingsMapper 直接注入进程环境 | ❌ 需决策（Q5） |

---

## Q1. 配置目录重定向机制收敛（三套并存 → 一套）

**TS 侧**：`CLAUDE_CONFIG_DIR`（envUtils.ts:7-14）重定向全部 user 级配置（settings/CLAUDE.md/plans/credentials/sessions/plugins），145+ 处消费，单一机制。

**C# 侧现状（三套竞争机制）**：
1. `JCC_APP_DATA_FOLDER`（AppDataPaths.cs:43，JccEnvVar.cs:32）— **实际生效**，SettingsLoader/ConfigLoader/全部 AppDataConstants 消费方走这套
2. `JCC_CONFIG_PATH` → `XDG_CONFIG_HOME/jcc` → `~/.jcc/`（XdgPathResolver.cs:25-40）— **死代码**：GetConfigDirectory/GetSettingsPath/GetAuthPath/GetTrustedFoldersPath/GetDataDirectory/GetCacheDirectory 六个方法全仓库零消费方（已 grep 验证）。声称支持 XDG 但从未生效
3. `JCC_CONFIG_PATH`（EngineApplicationBuilder.cs:171）— 仅 .env/api.json 回退链第 1 级使用，语义与 2 不同（此处当文件路径用，2 处当目录用——同名变量两种语义）

**影响面**：用户设 `JCC_CONFIG_PATH` 或 `XDG_CONFIG_HOME` 期望重定向配置目录 → 实际无效（只有 JCC_APP_DATA_FOLDER 生效）；TS 用户迁移到 jcc 时习惯性找 CLAUDE_CONFIG_DIR 对应物会踩坑。

**选项**：
- [ ] A. 删除 XdgPathResolver 六个死方法（保留已被消费的 runtime 目录方法），目录重定向统一收敛为 `JCC_APP_DATA_FOLDER` 单一机制（最小改动，消除两套实现）
- [ ] B. 接线 XDG：AppDataPaths.JccDirectory 增加 `JCC_CONFIG_PATH` → `XDG_CONFIG_HOME/jcc` → 现状 的回退链（对齐 TS CLAUDE_CONFIG_DIR 语义，但引入第二变量）
- [ ] C. 保持现状（不推荐：死代码 + 同名变量双语义）

---

## Q2. hooks 键双定义收敛

**TS 侧**：settings.json **顶层** `hooks`（types.ts:594-608），写入（updateSettingsForSource）与读取同层，5 源合并生效（policy 的 hooks 可强制覆盖 user）。

**C# 侧现状**：
- **活链**：`HookConfigurationManager`/`JsonFileHookConfigurationProvider`（HookConfigurationManager.cs:271-334, 423-427）读写 settings.json **顶层** `hooks`，注册 User/Project/Local 三源（ServiceRegistration.cs:114-124），`/hooks` 命令写入顶层 → **写入读取自洽，功能可用**
- **死链**：`CurrentSettings.Hooks`（SettingsJson.cs:267-269，`current.hooks`）— DTO 定义 + 合并策略（MergeHookDictionaries）齐全，但全仓库无行为消费方；用户按 DTO 形状把 hooks 写进 `current.hooks` → 永远不生效
- **缺源**：policySettings（managed-settings.json）的 hooks 不被 Hook 系统消费（只有 3 源聚合）；TS 的 disableAllHooks/allowManagedHooksOnly/allowedHttpHookUrls/httpHookAllowedEnvVars 四键 C# 无

**影响面**：① 用户从 TS 迁移按文档把 hooks 写 `current.hooks` → 静默失效（无任何报错）；② 企业管理员在 managed-settings.json 配 hooks → 无效；③ 两个 DTO（HookSettings vs HookSettingsFile.HookMatcher）形状不同维护两套。

**选项**：
- [ ] A. 删除 `CurrentSettings.Hooks` 死 DTO（收敛为顶层唯一），Hook 系统补 policySettings 源（对齐 TS 5 源）+ 补 managed-settings 路径注册
- [ ] B. Hook 系统改读 `current.hooks`（收敛到 DTO 形状，破坏现有顶层用户数据需迁移）
- [ ] C. 仅删死 DTO，policy 源缺失单独立项

---

## Q3. managed-settings.json 路径与强度

**TS 侧**：Windows `C:\Program Files\ClaudeCode\managed-settings.json`（managedPath.ts:8-25）+ 同目录 `managed-settings.d/*.json` drop-ins 按字母序叠加 + HKLM/HKCU 注册表 + macOS plist + 远程托管，内部 first-source-wins；`CLAUDE_CODE_MANAGED_SETTINGS_PATH` 内部测试覆盖。

**C# 侧**：`C:\ProgramData\jcc\managed-settings.json`（SettingsLoader.cs:666-671）单文件；另有**死常量** `ClaudeCompatConstants.ManagedSettingsPath = C:\Program Files\ClaudeCode\managed-settings.json`（ClaudeCompatConstants.cs:109，零消费方，且产品名错位 ClaudeCode≠jcc）；RemoteManagedSettingsService 提供远程源。

**影响面**：`C:\ProgramData` 普通用户可写 → 任何用户进程可伪造"管理员策略"提升自己权限（disableBypassPermissionsMode 等强制项可被覆盖），安全语义弱于 TS 的 Program Files（写需管理员）。drop-ins/注册表/plist 机制均缺。

**选项**：
- [ ] A. 路径改为 `C:\Program Files\jcc\managed-settings.json` 对齐 TS 安全语义 + 删除死常量（推荐）
- [ ] B. 保持 ProgramData（安装器不落 Program Files 的场景），删除死常量，文档标注安全边界
- [ ] C. 保持现状（死常量已由本次 Bug 批次清理，见下方"已直接修复"）

---

## Q4. 23 个死配置键处置（用户第 2 问的核心答案）

CurrentSettings 49 键审计结论：**A 类活跃 16 / B 类仅 /config 读写 10 / C 类死链 12 / D 类完全死键 11**。B 类 10 键（editorMode/debuglog/autoCompactEnabled/fileCheckpointingEnabled/showTurnDuration/language/autoMemoryEnabled/autoDreamEnabled/outputStyle/modelHistory）是 /config 命令的合法持久化键，属"用户可配但暂无行为分支"，TS 对应键在 TS 侧有真实行为——差距项。

**C 类死链 12 键**（有 DTO/映射但无下游消费）：
`defaultShell`、`hooks`(见Q2)、`mcpServers`（实际走 mcp_servers.json/.mcp.json 双轨）、`sandbox`（映射到 CodeExecution 后无人读）、`enabledPlugins`、`apiKeyHelper`、`cleanupPeriodDays`、`availableModels`（实际用 IProviderDefinition）、`worktree`（管道用独立 WorktreeOptions）、`activeWorktreeSession`（**只写不读**：AgentWorktreeService.cs:451 写 settings.local.json，会话恢复读取方不存在）、`statusLine`、`actor`（ADR 0074 配置链断裂，Actor 系统用硬编码背压）

**D 类死键 11 个**（零引用）：`showThinkingSummaries`、`isUnattendedMode`、`chatRoomEnabled`、`respectGitignore`、`includeCoAuthoredBy`、`includeGitInstructions`、`modelOverrides`、`enableAllProjectMcpServers`、`enabledMcpjsonServers`、`disabledMcpjsonServers`、`disableAllHooks`

**影响面**：settings.json 默认骨架（BuildDefaultSettingsJson）会把这些键写给用户看 → 用户以为可配 → 实际静默无效（违反 AGENTS.md 规矩 9"错误提示必须有诱导方式"精神：配置了没反馈）。

**选项**：
- [ ] A. 分批接线：按 TS 对应行为逐键补消费方（工作量大，建议按需排优先级：cleanupPeriodDays[会话清理] > defaultShell[!命令] > statusLine > respectGitignore > …）
- [ ] B. 删死键：D 类 11 键 + C 类确认不做的键从 DTO/骨架/validator 删除，需要时再加回（DTO 是 init-only + Relaxed 宽容，删键不破坏既有文件读取）
- [ ] C. 混合：D 类全删，C 类逐键一问一答（activeWorktreeSession 只写不读是明确的断链 bug，可先修读取方或删写入方）

**另**：`AppDataPaths.ThemeFileName`/`CredentialsFileName`（+ JCC_THEME_FILE_NAME/JCC_CREDENTIALS_FILE_NAME 环境变量）为死路径定义（theme 实际走 settings.json theme 键 + global.json；credentials 走 tokens/ 目录）——建议随本项一并处置。

---

## Q5. settings.json `env` 键安全模型

**TS 侧**：两阶段应用（managedEnv.ts:124-199）——信任建立前只应用 SAFE_ENV_VARS 白名单（80+ 变量，managedEnvConstants.ts:108-191）；项目级来源设置白名单外变量（ANTHROPIC_BASE_URL/HTTP_PROXY/NODE_TLS_REJECT_UNAUTHORIZED 等）触发安全对话框；policy 源 env 不可覆盖。

**C# 侧**：`SettingsMapper.cs:122-130`（InjectEnvFromSettings）把合并后的 env 直接注入进程环境变量，无白名单、无来源区分、无确认。EnvInjectMiddleware.cs:11 热重载同。

**影响面**：恶意项目 `.jcc/settings.json` 的 `current.env` 可注入 `PATH`/`LD_PRELOAD` 等危险变量到用户进程（TS 明确防此攻击面）。

**选项**：
- [ ] A. 对齐 TS：引入 SAFE 白名单 + 危险变量来源分级（user/policy 直通，project/local 白名单外确认）
- [ ] B. 仅加黑名单（拒绝已知危险键 PATH/LD_PRELOAD/NODE_* 等），项目级来源全黑名单外确认
- [ ] C. 保持现状（个人工具威胁模型可接受时）

---

## Q6. 合并/写回语义细节差异（低风险登记）

- TS 数组**合并时拼接去重**（settings.ts:538-547）而**写回时整体替换**（L416-524）；C# CurrentSettings.Merge 用 SettingsProperty 声明式策略（Override/DictionaryMerge/RecursiveMerge/Custom），数组语义=Override 整体替换，与 TS 写回语义一致但与 TS 读取合并语义不同（TS allowedMcpServers 跨源拼接，C# 无此键所以暂无实际影响）
- TS `settings.local.json` 自动追加 .gitignore（settings.ts:508-514）；C# 未见对应（TrustFolderManager/settings.local 均无）→ clone 项目后 local 设置可能误提交
- TS 同文件去重（seenFiles settings.ts:746）防 flag 与 user 指向同一文件重复合并；C# LoadAllSourcesAsync 未处理（`--settings ~/.jcc/settings.json` 时会合并两次，后果=幂等无害但浪费）
- TS `~/.claude.json` 有 `.config.json` 遗留路径兼容（env.ts:14-26）；C# global.json 无遗留兼容（项目无后向兼容要求，符合规范，仅登记）
- TS flagSettings 支持内联 JSON（写临时文件内容哈希命名防缓存破坏，main.tsx:444-457）；C# LoadFlagSettingsAsync 仅文件路径

---

## 已直接修复（无需决策，C# 内部确认 bug）

| # | Bug | 位置 | 修复 |
|---|-----|------|------|
| Bug1 | 项目级/用户级 settings 文件名解析不一致：用户级走 `AppDataConstants.SettingsFileName`（支持 JCC_SETTINGS_FILE_NAME 覆盖），项目级/本地级硬编码 `"settings.json"`/`"settings.local.json"` 字面量；hook 注册路径另有第三套拼法（rooted 回退语义与主链路不一致） | SettingsLoader.cs:648-658、ServiceRegistration.cs:113-124 | 统一委托常量与主链路方法（含 AppDataPaths.LocalSettingsFileName 派生属性） |
| Bug2 | ConfigurationService（L97-98,129）与 TrustFolderManager（L17-20）手拼 `Path.Combine(UserProfile, AppDataFolder, ...)` 绕过 `AppDataConstants.Paths` 唯一数据源 | 两文件 | 改委托 `Paths` 派生属性（新增 TrustedFoldersFilePath） |
| Bug3 | native_tui 三处硬编码 `~/.jcc/`（KeybindingLoader.DefaultPath / CommandUsageMemory.DefaultFilePath / StartupErrorPrinter 三属性）绕过 `JCC_APP_DATA_FOLDER` → 测试隔离场景 TUI 读写真实用户目录 | native_tui 三文件 | Core 内建 JccHome 工具（读 JCC_APP_DATA_FOLDER，逻辑对齐 AppDataPaths.JccDirectory，保持 native_tui 零依赖）；Host 诊断直接委托主链路 |

**死代码处置**：用户已决策删除（2026-10-23）— XdgPathResolver 六死方法、ClaudeCompatConstants.ManagedSettingsPath 死常量、ThemeFileName/CredentialsFileName 死路径链、CurrentSettings D 类 11 死键均已执行（commit e6b00e89b、fe08a3fa7）。

**断链修复（用户决策"先修断链 bug"，commit e42f99e40）**：
- hooks 层级错位：Hook 系统补注册 policySettings 源（managed-settings.json 顶层 hooks，对齐 TS policySettings 参与钩子合并）+ 删 current.hooks 死 DTO/HookSettings 类/MergeHookDictionaries（收敛顶层唯一，消除"用户按 current.hooks 配置永不生效"误导面）
- activeWorktreeSession：删 ActiveWorktreeSessionJson 死 DTO（TS 侧同为写入无常规读取 — bridge KAIROS 场景外部构建剔除；写入方 AgentWorktreeService 保留对齐 TS saveCurrentProjectConfig 写入行为）

**衍生需求（用户决策）**：发送消息时以 `<system-reminder>` 形态把"洞悉用户诉求"系统提示词追加到消息末尾，10 分钟冷却 — SendReminderInjector 组件 + SessionController.StreamResponseAsync / ChatService.StreamWithEventsAsync 双接线（commit 1622020d0）。

**C 类剩余 10 个死链键**（mcpServers、sandbox、enabledPlugins、apiKeyHelper、cleanupPeriodDays、availableModels、worktree、statusLine、actor、defaultShell）：用户决策暂不处理，保持现状。

**Q1/Q3/Q5 + B 类键接线（用户决策"全部做"，2026-10-24）**：
- Q1 收尾：删 .env 回退链 JCC_CONFIG_PATH 死分支 — 目录重定向收敛为 JCC_APP_DATA_FOLDER 单一机制（bb8a8f5fa）
- Q3：GetManagedSettingsPath 对齐 TS managedPath.ts 三平台安全目录（Windows Program Files/jcc 写需管理员防伪造策略；rooted 透传测试隔离）+ ManagedSettingsPathTests（bb8a8f5fa）
- Q5：SettingsEnvGuard 危险键黑名单（33 键四类攻击面：进程基础设施/动态库注入/TLS 信任/流量劫持；拒绝时四要素日志）— InjectEnvFromSettings 门控（801a73c2d）
- B类 language：settings.current.language → WorkflowConfig.Language → SyncSystemPromptProviderOptions.LanguagePreference → LanguageSection（70df5a36e）
- B类 showTurnDuration：默认 true 对齐 TS，SessionController 门控 timing 显示（22c67752e）
- B类 debuglog：ConfigLoader Step2.1 三入口同效（--debuglog/JCC_DEBUGLOG/settings.debuglog）（22c67752e）
- B类 autoCompactEnabled：默认 true，AutoCompactService.ShouldAutoCompact 首行门控（4c0a59543）
- B类 fileCheckpointingEnabled：默认 true，FileHistoryService.BackupBeforeWriteAsync 首行门控（6e47c5fbf）
- 结构归位（第一惯性理解）：infra.tests 18 个跨目录 Compile Include 全部物理归位 io/services/utils + 根 GlobalUsings 合并三份 + 目录命名归一小写（168538987，三项目 377+416+589 全绿）

**B 类剩余 5 键列差异待决策**（需新建子系统/跨模块改造，不自行实现）：outputStyle（TS 为 ~/.claude/output-styles/ 目录 + settings 指针完整子系统，C# 仅有 Keyword 触发的两个 style section 为自创替代）、editorMode（TS vim/emacs 键位模式，C# TUI 键位系统无此概念）、autoMemoryEnabled/autoDreamEnabled（dream 模块独立 AutoDreamConfig，接通需跨模块配置桥）、modelHistory（TS 模态不匹配自动回退首选模型，C# ModalityValidationMiddleware 语义不同）。

## 验收表（四列）

| 基建实现 | 消费点 | 已实现 | 已验收 |
|---------|--------|--------|--------|
| 差异文档本体（本文档） | 用户一问一答决策 | ✅ | ✅（本文档即交付物） |
| Bug1-3 修复 | SettingsLoader/ConfigurationService/TrustFolderManager/native_tui | ✅（见 commit） | ✅（单元测试 + 编译） |
| 死代码批次1-4 | XdgPathResolver/ClaudeCompatConstants/AppDataPaths/SettingsJson | ✅（e6b00e89b/fe08a3fa7） | ✅（abs 697 + guard.config 1285 + host 1453 + composition 1003 全绿） |
| SendReminderInjector | SessionController（TUI/CLI）+ ChatService（GUI）双接线 | ✅（1622020d0） | ✅（组件 9 测试 + brain.context 949 + host 1453 + NativeTui.Host 125 全绿） |
| hooks 断链修复 | ServiceRegistration policy 源注册 + 死 DTO 删除 | ✅（e42f99e40） | ✅（HookPolicySourceRegistrationTests 2 红→绿 + guard.config 1285 + hooks 574 + agents 641 + host 1453 全绿） |
| Q1/Q3/Q5 差异项 | .env 死分支/managed-settings 三平台/SettingsEnvGuard 黑名单 | ✅（bb8a8f5fa/801a73c2d） | ✅（ManagedSettingsPathTests + 危险键 12 Theory + guard.config 1300 全绿） |
| B类 5 键接线 | language/showTurnDuration/debuglog/autoCompactEnabled/fileCheckpointingEnabled | ✅（70df5a36e/22c67752e/4c0a59543/6e47c5fbf） | ✅（各键红→绿测试 + brain.context 951 + guard.config 1304 + host 1455 + Infra.IO 377 全绿） |
| 结构归位（第一惯性） | infra.tests 18 跨目录 Include → io/services/utils | ✅（168538987） | ✅（io 377 + services 416 + utils 589 全绿） |
