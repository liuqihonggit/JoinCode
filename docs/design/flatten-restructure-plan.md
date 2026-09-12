# 文件夹扁平化重组方案 — 语义分组 + 组内扁平

> 状态: 设计草案(待用户确认)
> 创建: 2026-09-13
> 更新: 2026-09-13(rebase 后路径同步 + 小写命名规范 + libs/Terminal.Gui 改 NuGet)
> 取代: ADR 0102(数字前缀方案,proposed → 将标记 superseded by 0103)
> 范围: 112 个 csproj 全量迁移 + 10 个 slnx 归集 + 残留清理

---

## 1. 背景与动机

### 1.1 当前状态(ADR 0102 数字前缀方案)

ADR 0102 将项目从"脑/眼/手"领域驱动改为数字前缀功能驱动(`00_generators/` ~ `09_app_*`),已部分实施。用户已将大部分目录从驼峰转为小写+下划线(commit `a358b4dc3`),但仍存在以下问题:

| 问题 | 具体表现 |
|------|---------|
| 根目录爆炸 | 24 个数字文件夹 + 8 个 `.slnx` + 3 个 `.ps1` + 配置文件,根目录 50+ 项 |
| 数字撞车 | `04_mcp_dispatch`/`04_mcp_service` 共用 04,`08_brain`/`08_composition`/`08_pipelines` 共用 08 |
| 语义弱 | `05_server_*` 占 8 个位置,光看 `05` 不知道是 server |
| 内部不够扁 | 每个数字目录里还有 `src/`+`tests/` 子层 |
| 两套并存 | `core/`、`foundation/`、`services/` 老结构残留(经查全为 TestResults 垃圾) |
| 命名未统一 | 部分目录已转小写+下划线(`aot_safety.generator/`),部分仍 PascalCase(`Fsm.Generator/`、`02_llm/Agents/`) |

### 1.2 新方案选择

用户选择**方向 A:语义分组 + 组内扁平**。核心思路:根目录只留 10 个语义文件夹,每个文件夹内直接平铺所有项目,测试用 `.Tests` 后缀和源码同层,彻底消除 `src/`+`tests/` 双层。

---

## 2. 目标结构

```
D:/project/w2/
│
├── build/                    # 构建相关(slnx + 脚本 + smoke-test)
│   ├── sln/                  #   10 个 .slnx
│   ├── build.ps1
│   ├── publish.ps1
│   ├── rebuild.ps1
│   └── smoke-test.json
│
├── docs/                     # 文档(不动)
├── gen/                      # 源码生成器(13 csproj + shared/)
├── lib/                      # 基础库(23 csproj)
├── llm/                      # LLM 抽象与实现(6 csproj)
├── kit/                      # jcc 核心能力(18 csproj)
├── server/                   # MCP server(15 csproj)
├── app/                      # 应用入口(4 csproj)
├── test/                     # 跨层测试(26 csproj)
├── tool/                     # 辅助/非交付工具(7 csproj + scripts/ + stryker/)
├── libs/                     # 第三方库(见 2.2)
│
├── AGENTS.md                 # ── 以下留根目录(DOTNET/MSBuild 硬性要求或入口文件)
├── README.md
├── LICENSE
├── Directory.Build.props     # MSBuild 向上查找,必须留根
├── Directory.Build.targets   # 同上
├── global.json               # dotnet SDK 版本,必须留根
├── nuget.config              # NuGet 源,必须留根
├── .editorconfig
├── .gitignore / .gitattributes / .gitmodules
└── (dotfiles/构建产物: .codeartsdoer/ .github/ .git/ .jcc/ .xxx/ .nuget/ artifacts/ publish/ dumps/)
```

### 2.1 各文件夹职责

| 文件夹 | 职责 | csproj 数 | 来源 |
|--------|------|-----------|------|
| `build/` | .slnx、构建脚本、smoke-test | 0 | 根目录散落文件归集 |
| `gen/` | 源码生成器 | 13 | `00_generators/` |
| `lib/` | 全局共享基础 | 23 | `01_shared/` |
| `llm/` | LLM 抽象与实现 | 6 | `02_llm/` |
| `kit/` | jcc 核心能力 | 18 | `03_builtin`~`08_pipelines` 合并 |
| `server/` | MCP server | 15 | `05_server_*` |
| `app/` | 应用入口 | 4 | `09_app_*` |
| `test/` | 跨层测试 | 26 | `tests/` |
| `tool/` | 辅助/非交付工具 | 7 | `non_deliverables_tools/` |
| `libs/` | 第三方库(见 2.2) | 8(不动) | `libs/` |

### 2.2 libs/ 第三方库状态

| 子目录 | 状态 | 处理 |
|--------|------|------|
| `libs/Editor/` | git submodule(`git@github.com:liuqihonggit/Editor.git`),有内容 | 保留不动 |
| `libs/Terminal.Gui/` | .gitmodules 已声明但空目录;commit `a358b4dc3` 起 JoinCodeTui 改用 NuGet 包引用 | 保留 submodule 声明(不影响扁平化);后续可单独清理 submodule 声明 |

> libs/ 不纳入扁平化范围,保持原样。

---

## 3. 设计原则

1. **每个 csproj = 一个文件夹** — 文件夹内直接放源码文件,不再有 `src/` 层
2. **测试与源码同层** — 测试项目和源码项目在同一语义文件夹内平铺
3. **消除 `src/`+`tests/` 双层** — 当前 `01_shared/shared_guard/src/Guard.csproj` + `tests/Config/...` 的双层结构全部拍平
4. **.slnx 归 `build/sln/`** — 10 个散在根目录的 .slnx 统一归集
5. **MSBuild/DOTNET 硬性要求文件留根** — `Directory.Build.props`、`Directory.Build.targets`、`global.json`、`nuget.config` 不能移
6. **禁止删除,残留移 `.xxx/`** — 老结构垃圾移到 `.xxx/{name}.{timestamp}.del/`

### 3.1 命名规范(对齐用户既有实践)

| 元素 | 规范 | 示例 |
|------|------|------|
| 语义文件夹 | 全小写 | `gen/ lib/ llm/ kit/ server/ app/ test/ tool/` |
| 项目文件夹 | csproj 名转小写+下划线,点号保留 | `AotSafety.Generator.csproj` → `aot_safety.generator/` |
| csproj 文件名 | 保持 PascalCase(不改) | `AotSafety.Generator.csproj` |
| 项目内部子目录 | 小写+下划线(用户已在做) | `slash_commands/ view_models/` |

**PascalCase → snake_case 转换规则**: 每个大写字母前插入下划线(首字母除外),全转小写,点号分隔的各段独立转换。

| csproj 名 | 文件夹名 |
|-----------|---------|
| `AotSafety.Generator` | `aot_safety.generator` |
| `Guard` | `guard` |
| `Guard.Config.Tests` | `guard.config.tests` |
| `Hands.ToolHandlers.Tests` | `hands.tool_handlers.tests` |
| `MockServer.Core.Tests` | `mock_server.core.tests` |

---

## 4. 逐项目迁移映射表

> 格式: `当前路径(rebase 后实际)` → `目标路径`
> 目标文件夹名 = csproj 名转小写+下划线(见 3.1)

### 4.1 gen/(源码生成器)— 13 csproj + shared/

```
00_generators/aot_safety.generator/src/AotSafety.Generator.csproj        → gen/aot_safety.generator/
00_generators/aot_safety.generator/tests/AotSafety.Tests.csproj          → gen/aot_safety.tests/
00_generators/app_module.generator/src/AppModule.Generator.csproj        → gen/app_module.generator/
00_generators/cli_option.generator/src/CliOption.Generator.csproj        → gen/cli_option.generator/
00_generators/code_fixes/src/CodeFixes.csproj                            → gen/code_fixes/
00_generators/danger_command.generator/src/DangerCommand.Generator.csproj→ gen/danger_command.generator/
00_generators/enum_metadata.generator/src/EnumMetadata.Generator.csproj  → gen/enum_metadata.generator/
00_generators/Fsm.Generator/src/Fsm.Generator.csproj                     → gen/fsm.generator/
00_generators/Fsm.Generator/tests/Fsm.Generator.Tests.csproj             → gen/fsm.generator.tests/
00_generators/mcp_tool_dispatch.generator/src/McpToolDispatch.Generator.csproj → gen/mcp_tool_dispatch.generator/
00_generators/prompt_section.generator/src/PromptSection.Generator.csproj→ gen/prompt_section.generator/
00_generators/prompt_template.generator/src/PromptTemplate.Generator.csproj → gen/prompt_template.generator/
00_generators/tool_prompt.generator/src/ToolPrompt.Generator.csproj      → gen/tool_prompt.generator/
00_generators/shared/  (2个.cs,非csproj共享源码)                        → gen/shared/
```

### 4.2 lib/(基础库)— 23 csproj

```
01_shared/shared_abstractions/Abstractions.csproj                       → lib/abstractions/
01_shared/shared_async_lock/AsyncLock.csproj                            → lib/async_lock/
01_shared/shared_async_lock/tests/Unit/AsyncLock.Tests.csproj           → lib/async_lock.tests/
01_shared/shared_clock/src/Clock.csproj                                 → lib/clock/
01_shared/shared_clock/tests/Unit/Clock.Tests.csproj                    → lib/clock.tests/
01_shared/shared_guard/src/Guard.csproj                                 → lib/guard/
01_shared/shared_guard/tests/Config/Guard.Config.Tests.csproj           → lib/guard.config.tests/
01_shared/shared_guard/tests/Hooks/Guard.Hooks.Tests.csproj             → lib/guard.hooks.tests/
01_shared/shared_guard/tests/Security/Guard.Security.Tests.csproj       → lib/guard.security.tests/
01_shared/shared_infrastructure/Infrastructure.csproj                   → lib/infrastructure/
01_shared/shared_plugins/Plugins.Contracts/Plugins.Contracts.csproj     → lib/plugins.contracts/
01_shared/shared_plugins/Plugins.Infrastructure/Plugins.Infrastructure.csproj → lib/plugins.infrastructure/
01_shared/shared_plugins/Plugins.Tests/Plugins.Tests.csproj             → lib/plugins.tests/
01_shared/shared_scheduling/src/Scheduling.csproj                       → lib/scheduling/
01_shared/shared_scheduling/tests/Unit/Scheduling.Tests.csproj          → lib/scheduling.tests/
01_shared/shared_structura/Structura.csproj                             → lib/structura/
01_shared/shared_structura/tests/Unit/Structura.Tests.csproj            → lib/structura.tests/
01_shared/shared_transport_contracts/Transport.Contracts.csproj         → lib/transport.contracts/
01_shared/shared_transport_impl/Transport.Impl.csproj                   → lib/transport.impl/
01_shared/shared_vault/src/Vault.csproj                                 → lib/vault/
01_shared/shared_vault/tests/Memdir/Vault.Memdir.Tests.csproj           → lib/vault.memdir.tests/
01_shared/shared_vault/tests/Other/Vault.Other.Tests.csproj             → lib/vault.other.tests/
01_shared/shared_vault/tests/Shared/Vault.Testing.Common.csproj         → lib/vault.testing.common/
```

### 4.3 llm/(LLM)— 6 csproj

```
02_llm/Agents/src/Agents.csproj                                        → llm/agents/
02_llm/Agents/tests/Unit/Agents.Tests.csproj                            → llm/agents.tests/
02_llm/Llm/src/Llm.csproj                                              → llm/llm/
02_llm/Llm/tests/Unit/Llm.Tests.csproj                                 → llm/llm.tests/
02_llm/Reasoning/src/Reasoning.csproj                                  → llm/reasoning/
02_llm/Reasoning/tests/Unit/Reasoning.Tests.csproj                     → llm/reasoning.tests/
```

### 4.4 kit/(jcc 核心能力)— 18 csproj

> 合并原 03_builtin + 04_mcp_dispatch + 04_mcp_service + 06_prompts + 07_slash + 08_brain + 08_composition + 08_pipelines

```
03_builtin/src/Hands.csproj                                             → kit/hands/
03_builtin/tests/Api/Hands.Api.Tests.csproj                             → kit/hands.api.tests/
03_builtin/tests/Shell/Hands.Shell.Tests.csproj                         → kit/hands.shell.tests/
03_builtin/tests/Skills/Hands.Skills.Tests.csproj                       → kit/hands.skills.tests/
03_builtin/tests/tool_handlers/Hands.ToolHandlers.Tests.csproj          → kit/hands.tool_handlers.tests/
04_mcp_dispatch/src/McpToolDispatch.csproj                              → kit/mcp_tool_dispatch/
04_mcp_dispatch/tests/Unit/McpToolDispatch.Tests.csproj                 → kit/mcp_tool_dispatch.tests/
04_mcp_service/src/Mcp.csproj                                           → kit/mcp/
04_mcp_service/tests/Unit/Mcp.Tests.csproj                              → kit/mcp.tests/
06_prompts/src/Prompts/Prompts.csproj                                   → kit/prompts/
06_prompts/tests/Prompts/Brain.Prompts.Tests.csproj                     → kit/brain.prompts.tests/
07_slash/src/Slash/Slash.csproj                                         → kit/slash/
08_brain/src/Brain.csproj                                               → kit/brain/
08_brain/tests/Context/Brain.Context.Tests.csproj                       → kit/brain.context.tests/
08_brain/tests/Other/Brain.Other.Tests.csproj                           → kit/brain.other.tests/
08_composition/src/Composition.csproj                                   → kit/composition/
08_composition/tests/Unit/Composition.Tests.csproj                      → kit/composition.tests/
08_pipelines/src/Pipelines.csproj                                       → kit/pipelines/
```

### 4.5 server/(MCP server)— 15 csproj

```
05_server_bridge/src/Bridge.csproj                                      → server/bridge/
05_server_bridge/tests/Unit/Bridge.Tests.csproj                         → server/bridge.tests/
05_server_browser/src/Browser.csproj                                    → server/browser/
05_server_browser/tests/Unit/Browser.Tests.csproj                       → server/browser.tests/
05_server_codeindex/src/CodeIndex.csproj                                → server/code_index/
05_server_codeindex/tests/Unit/CodeIndex.Tests.csproj                   → server/code_index.tests/
05_server_codeindex/tests/E2E/CodeIndex.E2E.Tests.csproj                → server/code_index.e2e.tests/
05_server_dream/src/Dream.csproj                                        → server/dream/
05_server_dream/tests/Unit/Dream.Tests.csproj                           → server/dream.tests/
05_server_eyes/src/Eyes.csproj                                          → server/eyes/
05_server_eyes/tests/Unit/Eyes.Tests.csproj                             → server/eyes.tests/
05_server_sandbox/src/SandboxSatellite.csproj                           → server/sandbox_satellite/
05_server_update/src/Update.csproj                                      → server/update/
05_server_vision/src/Vision.csproj                                      → server/vision/
05_server_vision/tests/Unit/Vision.Tests.csproj                         → server/vision.tests/
```

### 4.6 app/(应用入口)— 4 csproj

> 项目内部子文件夹(adapters/app/cli/entry/pipe/queue/services 等)保留在各自项目文件夹内

```
09_app_cli/JoinCode.csproj                                              → app/cli/
09_app_gui/JoinCodeGui.csproj                                           → app/gui/
09_app_sdk/Sdk.csproj                                                   → app/sdk/
09_app_tui/JoinCodeTui.csproj                                           → app/tui/
```

### 4.7 test/(跨层测试)— 26 csproj

```
tests/aot_compatibility/aot-browser-test/aot-browser-test.csproj         → test/aot/aot_browser_test/
tests/aot_compatibility/aot-httpclientfactory-test/...                   → test/aot/aot_httpclientfactory_test/
tests/aot_compatibility/pptr-api-check/PptrApiCheck.csproj               → test/aot/pptr_api_check/
tests/Benchmarks/async_lock.benchmarks/AsyncLock.Benchmarks.csproj       → test/benchmarks/async_lock.benchmarks/
tests/Benchmarks/Eyes.Benchmarks/Eyes.Benchmarks.csproj                 → test/benchmarks/eyes.benchmarks/
tests/Integration/Integration.Tests/Integration.Tests.csproj            → test/integration/integration.tests/
tests/mock_servers/anthropic.mock_server/Anthropic.MockServer.csproj    → test/mock/anthropic.mock_server/
tests/mock_servers/deep_seek.mock_server/DeepSeek.MockServer.csproj     → test/mock/deep_seek.mock_server/
tests/mock_servers/mcp.mock_server/Mcp.MockServer.csproj                → test/mock/mcp.mock_server/
tests/mock_servers/mock_server.core/MockServer.Core.csproj              → test/mock/mock_server.core/
tests/mock_servers/mock_server.core.tests/MockServer.Core.Tests.csproj  → test/mock/mock_server.core.tests/
tests/mock_servers/mock_server.e2e.tests/MockServer.E2E.Tests.csproj    → test/mock/mock_server.e2e.tests/
tests/mock_servers/open_ai.mock_server/OpenAI.MockServer.csproj         → test/mock/open_ai.mock_server/
tests/mock_servers/responses.mock_server/Responses.MockServer.csproj    → test/mock/responses.mock_server/
tests/mock_servers/sync.integration.tests/Sync.Integration.Tests.csproj → test/mock/sync.integration.tests/
tests/Unit/Abs.Tests/Abs.Tests.csproj                                   → test/unit/abs.tests/
tests/Unit/Hands.Tests/Hands.Tests.csproj                               → test/unit/hands.tests/
tests/Unit/Host.Tests/Host.Tests.csproj                                 → test/unit/host.tests/
tests/Unit/Infra.Tests/IO/Infra.IO.Tests.csproj                         → test/unit/infra.io.tests/
tests/Unit/Infra.Tests/Services/Infra.Services.Tests.csproj             → test/unit/infra.services.tests/
tests/Unit/Infra.Tests/Utils/Infra.Utils.Tests.csproj                   → test/unit/infra.utils.tests/
tests/Unit/join_code_gui.tests/JoinCodeGui.Tests.csproj                 → test/unit/join_code_gui.tests/
tests/Unit/Mcp.Tests/Mcp.UniqueTests.csproj                             → test/unit/mcp.tests/
tests/Unit/mcp_tool_dispatch.tests/McpToolDispatch.UniqueTests.csproj   → test/unit/mcp_tool_dispatch.tests/
tests/Unit/Testing.Common/Testing.Common.csproj                         → test/unit/testing.common/
tests/Unit/Tui.Tests/Tui.Tests.csproj                                   → test/unit/tui.tests/
```

### 4.8 tool/(辅助/非交付工具)— 7 csproj + scripts/ + stryker/

```
non_deliverables_tools/inject_migration/InjectMigration.csproj           → tool/inject_migration/
non_deliverables_tools/jcc_audit_ast_cli/JccAuditCli.csproj              → tool/jcc_audit_cli/
non_deliverables_tools/jcc_audit_ast_cli/tests/JccAuditCli.Tests.csproj  → tool/jcc_audit_cli.tests/
non_deliverables_tools/jcc_interactive_tester/JccInteractiveTester.csproj → tool/jcc_interactive_tester/
non_deliverables_tools/sample_native_plugin/SampleNativePlugin.csproj    → tool/sample_native_plugin/
non_deliverables_tools/skia_sharp_aot_test/SkiaSharpAotTest.csproj       → tool/skia_sharp_aot_test/
non_deliverables_tools/terminal_gui_aot_probe/TerminalGuiAotProbe.csproj → tool/terminal_gui_aot_probe/
non_deliverables_tools/scripts/                                          → tool/scripts/
non_deliverables_tools/stryker/                                          → tool/stryker/
non_deliverables_tools/tools.slnx                                        → tool/tools.slnx
```

### 4.9 build/(构建相关)

```
App.slnx          → build/sln/App.slnx
JoinCode.slnx     → build/sln/JoinCode.slnx
Core.slnx         → build/sln/Core.slnx
Foundation.slnx   → build/sln/Foundation.slnx
Generators.slnx   → build/sln/Generators.slnx
Infrastructure.slnx → build/sln/Infrastructure.slnx
Services.slnx     → build/sln/Services.slnx
Composition.slnx  → build/sln/Composition.slnx
Gui.slnx          → build/sln/Gui.slnx
Stryker.slnx      → build/sln/Stryker.slnx
build.ps1         → build/build.ps1
publish.ps1       → build/publish.ps1
rebuild.ps1       → build/rebuild.ps1
smoke-test.json   → build/smoke-test.json
```

### 4.10 libs/(第三方库,不动)

```
libs/Editor/          → libs/Editor/  (git submodule,保持不动)
libs/Terminal.Gui/    → libs/Terminal.Gui/  (空目录,submodule 声明保留,JoinCodeTui 已改 NuGet 包引用)
```

---

## 5. 拼留清理(移到 .xxx/)

> 经查,以下老结构目录内仅含 TestResults 垃圾/空目录,实际项目早已迁入数字前缀目录。

| 拼留路径 | 内容 | 处理 |
|---------|------|------|
| `core/execution/Brain/tests/TestResults/` | hangdump + xml | 移 `.xxx/core.{timestamp}.del/` |
| `core/execution/Hands/tests/TestResults/` | hangdump + xml | 同上 |
| `core/safety/Guard/tests/TestResults/` | hangdump + xml | 同上 |
| `foundation/Abstractions/00-core/Models/Task/` | 空目录 | 移 `.xxx/foundation.{timestamp}.del/` |
| `services/Bridge/tests/Unit/TestResults/` | hangdump + xml | 移 `.xxx/services.{timestamp}.del/` |
| `tools/SampleNativePlugin/publish/` | 空目录 | 移 `.xxx/tools.{timestamp}.del/` |

---

## 6. 各数字目录的 Directory.Build.props 处理

当前每个数字目录(`00_generators/` ~ `09_app_*`)下都有 `Directory.Build.props`。迁移后需评估:

| 策略 | 说明 |
|------|------|
| **合并到根 Directory.Build.props** | 如果各层 props 内容相同或可统一,合并到根目录一份 |
| **按语义文件夹保留** | 如果 `gen/`、`lib/` 等需要不同 MSBuild 配置,在各语义文件夹下放 `Directory.Build.props` |
| **移到 .xxx/ 并验证** | 如果根 Directory.Build.props 已覆盖所有需求,数字目录的 props 移到 `.xxx/` |

> ⚠️ 迁移前需逐个对比这些 props 的内容差异,确定合并还是保留。这是迁移的**前置调研项**。

---

## 7. 迁移步骤(渐进式)

> 遵循 AGENTS.md 渐进式开发:每次一个语义文件夹,迁移后立即编译 + 测试 + 提交。

### 阶段 0:前置调研
1. 对比各数字目录 `Directory.Build.props` 内容差异,确定合并/保留策略
2. 备份 git:确保干净状态
3. 创建目标语义文件夹:`gen/ lib/ llm/ kit/ server/ app/ test/ tool/ build/`

### 阶段 1:build/(最低风险,无代码依赖)
1. 移 10 个 .slnx → `build/sln/`
2. 移 3 个 .ps1 + smoke-test.json → `build/`
3. 更新 .slnx 内的项目路径引用
4. 编译验证 + 提交

### 阶段 2:gen/(源码生成器,编译最底层)
1. 逐个移 13 个 csproj 文件夹 → `gen/`(消除 src/ 层,源码直接放项目文件夹)
2. 移 `shared/` → `gen/shared/`
3. 更新所有引用生成器的 ProjectReference 路径
4. 编译验证 + 测试 + 提交

### 阶段 3:lib/(基础库)
1. 逐个移 23 个 csproj → `lib/`(消除 src/+tests/ 双层)
2. 更新 ProjectReference 路径
3. 编译验证 + 测试 + 提交

### 阶段 4:llm/
1. 移 6 个 csproj → `llm/`
2. 更新引用 + 编译 + 测试 + 提交

### 阶段 5:kit/(合并 8 个数字目录)
1. 移 18 个 csproj → `kit/`
2. 更新引用 + 编译 + 测试 + 提交

### 阶段 6:server/
1. 移 15 个 csproj → `server/`
2. 更新引用 + 编译 + 测试 + 提交

### 阶段 7:app/
1. 移 4 个 csproj → `app/`(保留项目内部子文件夹)
2. 更新引用 + 编译 + 测试 + 提交

### 阶段 8:test/
1. 移 26 个 csproj → `test/`(按 aot/benchmarks/integration/mock/unit 子分类)
2. 更新引用 + 编译 + 测试 + 提交

### 阶段 9:tool/
1. 移 7 个 csproj + scripts/ + stryker/ → `tool/`
2. 更新 tools.slnx 路径 + 提交

### 阶段 10:拼留清理
1. 移 core/、foundation/、services/、tools/ 垃圾 → `.xxx/`
2. 清理空的数字前缀目录(移到 `.xxx/` 而非删除)
3. 最终全量编译 + 全量测试 + 提交

### 阶段 11:文档同步
1. 更新 AGENTS.md 中的架构索引/测试地图/CI 流水线路径
2. 更新 README.md 的项目架构部分(用目录占位策略)
3. 更新 CI yml 中的路径引用
4. 标记 ADR 0102 为 `superseded by 0103`,新建 ADR 0103

---

## 8. 注意事项

### 8.1 ProjectReference 路径更新
- 每个 `.csproj` 内的 `<ProjectReference Include="..\..\xx\xx.csproj" />` 路径需同步更新
- 建议用脚本批量替换,但需逐个验证(AGENTS.md:脚本替换须先在单文件验证)

### 8.2 .slnx 内路径更新
- 10 个 .slnx 文件内每个项目的路径引用需同步
- .slnx 是 XML 格式,路径在 `<Project Path="..." />` 节点

### 8.3 GlobalUsings.cs
- 各项目的 `GlobalUsings.cs` 内命名空间引用不受目录移动影响(命名空间不变)

### 8.4 CI matrix
- `.github/workflows/ci-*.yml` 中的项目路径矩阵需同步
- Stryker.slnx 路径需同步

### 8.5 Directory.Build.props 层级
- MSBuild 从项目目录向上逐级查找 `Directory.Build.props` 并合并
- 迁移后层级变浅(从 `root/01_shared/shared_guard/src/` 变为 `root/lib/guard/`),需确认 props 仍能被正确发现
- 根目录的 `Directory.Build.props` 会被所有项目继承,确认覆盖需求

### 8.6 源码生成器输出路径
- 生成器的输出路径可能在 `.csproj` 或 `Directory.Build.props` 中硬编码
- 迁移后需验证生成器输出仍到正确的 `obj/Generated/` 目录

### 8.7 app/ 项目内部子文件夹保留
- `app/cli/` 内保留 adapters/app/cli/entry/pipe/queue/services 等子文件夹(项目内部组织,非 csproj 层)
- `app/gui/` 内保留 assets/converters/design/hosting/markdown/slash_commands/view_models 等子文件夹
- 这些是项目内源码组织,不属于扁平化范围

### 8.8 文件夹名与 csproj 名不一致
- 文件夹用小写+下划线(如 `aot_safety.generator/`),csproj 保持 PascalCase(如 `AotSafety.Generator.csproj`)
- `dotnet build` 需指定完整路径或 csproj 名,IDE 内不影响导航
- 这是用户既有实践(01_shared/shared_guard/ 内含 Guard.csproj),本方案对齐

---

## 9. 与 ADR 0102 的关系

| 维度 | ADR 0102(数字前缀) | 本方案(语义分组) |
|------|---------------------|-------------------|
| 根目录文件夹数 | 24(数字前缀) | 10(语义) |
| 排序机制 | 数字前缀(00-09) | 无(靠语义名自然分组) |
| 数字撞车 | 有(04×2, 08×3) | 无(无数字) |
| src/+tests/ 双层 | 保留 | 消除(测试同层) |
| 语义可读性 | 弱(需看全名) | 强(目录名即语义) |
| 编译顺序保证 | 数字前缀隐式 | 靠 ProjectReference 显式 |
| .slnx 归集 | 散在根目录 | 统一 build/sln/ |
| 命名规范 | 混合(部分驼峰部分下划线) | 统一小写+下划线 |

> ADR 0102 的"功能驱动"理念正确(废弃脑/眼/手领域分类),本方案继承此理念,仅改进**组织形式**(数字前缀 → 语义分组 + 消除双层 + 统一命名)。

---

## 10. 验证清单

- [ ] 前置调研:各 Directory.Build.props 差异分析
- [ ] 阶段 1-9:每阶段编译 + 测试 + 提交
- [ ] 全量编译通过(Debug)
- [ ] 全量测试通过
- [ ] 所有 .slnx 内路径正确
- [ ] 所有 ProjectReference 路径正确
- [ ] CI yml 路径同步
- [ ] AGENTS.md 架构索引更新
- [ ] README.md 更新(目录占位策略)
- [ ] ADR 0102 标记 superseded,ADR 0103 新建
- [ ] 拼留垃圾已移 .xxx/
- [ ] 根目录仅剩 10 个语义文件夹 + 入口文件 + dotfiles

---

## 变更记录

- 2026-09-13 初版:基于 88 csproj(含 libs/Editor 8 个)设计 10 语义文件夹方案
- 2026-09-13 更新:rebase 后路径同步(部分目录已转小写+下划线);libs/Terminal.Gui 改 NuGet 包引用(commit a358b4dc3);命名规范明确(3.1 节);映射表当前路径全部更新为 rebase 后实际路径;目标路径统一小写+下划线
