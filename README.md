# JoinCode

[![.NET](https://img.shields.io/badge/.NET-10.0-512BD4?style=flat-square)](https://dotnet.microsoft.com/)
[![NativeAOT](https://img.shields.io/badge/NativeAOT-Enabled-00A4EF?style=flat-square)](https://learn.microsoft.com/dotnet/core/deploying/native-aot/)
[![C#](https://img.shields.io/badge/C%23-13-68217A?style=flat-square)](https://docs.microsoft.com/dotnet/csharp/)
[![License: MIT](https://img.shields.io/badge/License-MIT-green?style=flat-square)](LICENSE)
[![Code of Conduct](https://img.shields.io/badge/CoC-Microsoft-blue?style=flat-square)](.github/CODE_OF_CONDUCT.md)

> 🌐 **Languages:** **English** | [简体中文](docs/zh-CN/README.md)

**JoinCode** is a pure C# open-source AI coding agent that runs in your terminal.
It understands your codebase and helps you code faster through natural language —
executing everyday tasks, explaining complex code, and handling Git workflows,
all in a single command.

It compiles to a native single-file executable `jcc.exe` with zero runtime
dependencies and peak performance on launch.

> 📖 **Engineering rules:** [AGENTS.md](AGENTS.md) (Chinese) · [ADR](docs/adr/README.md)

---

## Table of Contents

- [Why JoinCode?](#why-joincode)
- [Quick Start](#quick-start)
- [CLI Reference](#cli-reference)
- [Core Features](#core-features)
- [Architecture](#architecture)
- [Configuration](#configuration)
- [Detailed Documentation](#detailed-documentation)
- [Contributing](#contributing)
- [License](#license)

---

## Why JoinCode?

- **🚀 Native performance** — NativeAOT compiles to a single-file native binary.
  No JIT, no GC pauses, no runtime dependencies, millisecond cold start.
- **🧠 Multi-provider** — DeepSeek / OpenAI / Anthropic / Azure / SenseNova /
  Agnes out of the box. Supports OpenAI Chat Completions, Anthropic Messages,
  and OpenAI Responses protocols.
- **🔧 Rich built-in tools** — Shell execution, file operations, web requests,
  code indexing (TreeSitter AST), browser automation, skill system.
- **🔌 MCP protocol** — Full Model Context Protocol client (version
  `2025-11-25` Streamable HTTP). Two-phase tool loading for infinite extensibility.
- **🛡️ Production-grade resilience** — LLM fault tolerance (JSON repair,
  parameter normalization, tool-name canonicalization), three-tier loop
  intervention, prefix-cache optimization, tool-inertia error correction.
- **⚖️ Structured reasoning** — `/falv` adversarial three-branch engine
  (Prosecution → Defense → Judge) with DAG evidence chains and dual budgets.
- **🎯 Multi-agent collaboration** — `/goal` task-graph engine with hot-spot
  detection replacing file locks.
- **🖥️ Multi-mode UI** — CLI interactive REPL + non-interactive script + TUI
  full-screen interface (Terminal.Gui v2).
- **📦 Zero Microsoft AI dependency** — Rejects all AOT-incompatible Microsoft
  AI SDKs; builds LLM adapters from the protocol layer.

---

## Quick Start

### Prerequisites

- **.NET 10 SDK** (10.0.301+)
- **Windows / Linux / macOS** (NativeAOT cross-platform)

### Build

```powershell
git clone <repo-url>
cd JoinCode

# Build all seven layers in order (Release enables NativeAOT)
dotnet build Generators.slnx -c Release --no-incremental
dotnet build Foundation.slnx -c Release --no-incremental
dotnet build Infrastructure.slnx -c Release --no-incremental
dotnet build Core.slnx -c Release --no-incremental
dotnet build Services.slnx -c Release --no-incremental
dotnet build Composition.slnx -c Release --no-incremental
dotnet build App.slnx -c Release --no-incremental

# Or use the build script
.\build.ps1 -Mode Fast -SkipTests -Configuration Release
```

Output: `artifacts/bin/JoinCode/Release/net10.0/jcc.exe`

### Configure Authentication

Set LLM provider via environment variables:

| Variable | Required | Description | Example |
|----------|----------|-------------|---------|
| `JCC_VENDOR` | No | Provider name (default: `deepseek`) | `deepseek` / `openai` / `anthropic` / `azure` / `sensenova` / `agnes` |
| `JCC_MODEL_ID` | No | Model ID (default: `deepseek-v4-flash`) | `gpt-4o` / `claude-opus-5-20250815` |
| `JCC_ENDPOINT` | No | API endpoint (default: provider built-in) | `http://localhost:9901` |

Provider API keys:

| Provider | Environment Variable | Default Endpoint |
|----------|---------------------|------------------|
| `deepseek` | `DEEPSEEK_API_KEY` | Built-in (OpenAI-compatible) |
| `openai` | `OPENAI_API_KEY` | `https://api.openai.com/v1` |
| `anthropic` | `ANTHROPIC_API_KEY` | `https://api.anthropic.com` (or SenseNova relay) |
| `azure` | `AZURE_OPENAI_API_KEY` | User-configured Azure OpenAI |
| `sensenova` | `SENSENOVA_API_KEY` | `https://token.sensenova.cn/v1` |
| `agnes` | `AGNES_API_KEY` | `https://apihub.agnes-ai.com/v1` |

### Run

```powershell
# Non-interactive (single prompt, for scripting)
jcc --trust -p "Explain this codebase architecture"

# Interactive REPL
jcc --trust

# TUI full-screen mode (Terminal.Gui v2)
jcc --trust --tui

# Specify model
jcc --trust -m gpt-4o

# Bypass all permission checks (equivalent to --permission-mode bypass)
jcc --bypass -p "batch refactor"

# Diagnostic mode
jcc --debuglog -p "hello"
```

---

## CLI Reference

| Flag | Description |
|------|-------------|
| `--trust` | Trust current directory (skip trust prompt) |
| `-p` / `--prompt <text>` | Non-interactive single prompt |
| `-m` / `--model <id>` | Model ID or alias |
| `--tui` | Launch TUI full-screen interface |
| `--bypass` | Skip all permission checks (equivalent to `--permission-mode bypass`) |
| `--permission-mode <mode>` | Permission mode: `plan` / `auto` / `ask` / `bypass` |
| `--debuglog` / `-d` | Enable debug logging (equivalent to `JCC_DEBUGLOG=1`) |
| `--await <seconds>` | Non-interactive timeout auto-close (returns 1234 on timeout) |
| `--doctor` | Doctor mode: monitor and auto-fix a patient process |
| `--non-interactive` | Read from stdin, write to stdout |

### Slash Commands

| Command | Description |
|---------|-------------|
| `/help` | List all commands |
| `/model <name>` | Switch model (e.g., `/model flash`, `/model pro`) |
| `/goal` | Goal setting (Outcome + Verification + Constraints) |
| `/falv` | Structured reasoning (three-branch + evidence chain + dual budget) |
| `/brief` | Brief mode |
| `/clear` | Clear context |
| `/rewind` | Rewind messages |
| `/exit` | Exit |

---

## Core Features

### Code Understanding & Generation

- Query and edit large codebases — 3,313 files AST-parsed in ~2.7s
- TreeSitter-driven code indexing with incremental AST (no persistence needed)
- Debug and troubleshoot with natural language

### Automation & Integration

- Automate ops tasks — query PRs, handle complex rebases, batch refactor
- Connect MCP servers for custom tools, skills, and workflows
- Integrate `jcc -p "..."` into CI/CD scripts

### Production-Grade Resilience

- **LLM fault tolerance:** JSON format repair, parameter name normalization,
  type auto-conversion, tool-name canonicalization
- **Three-tier loop intervention:** Soft prompt → hard truncation + cooldown
  retry → context compaction + unattended recovery
- **Prefix-cache optimization:** System prompt partitioning + message history
  prefix preservation + DeepSeek cache statistics
- **Smart progress discount:** Lower intervention level when real progress is
  detected

### Structured Reasoning (`/falv`)

- **Three-branch adversarial:** Prosecution (collect evidence) → Defense
  (rebut) → Judge (rule)
- **DAG evidence chain** + dual budget control (rounds + tokens, whichever
  runs out first stops)
- **Three proof standards:** Murder (beyond reasonable doubt) / Panda
  (circumstantial) / Divorce (preponderance of evidence)
- `/falv --continue` to extend reasoning

### Multi-Agent Collaboration (`/goal`)

- **Task-graph engine** based on PRD v2.1, reusing team MCP shared components
- **Hot-spot detection replaces file locks:** `HotFileDetector` +
  `IntentCollector` + `HotSpotTracker` + `HotSpotResolutionPolicy`
- **Contract change broadcast:** `ContractChangeBroadcaster` +
  `ContractChangeNotificationRouter`
- **Captain dispatch + merge queue:** `CaptainDispatchGuard` +
  `MergeQueueService` + `DeferredMailService`
- 22 new components + 4 integration tasks + 7 breakpoint fixes, 3300+ tests
  zero breakage

### Native Performance

- NativeAOT single-file native binary, zero runtime dependencies
- 9 source generators eliminate runtime reflection
- Seven-layer slnx isolation, strict dependency-ordered builds, zero circular
  dependencies
- 14 middleware pipelines (Chat/Permission/Shell/Web/Skill…), onion model with
  explicit manual registration

---

## Architecture

JoinCode uses a **seven-layer solution isolation** architecture with strict
dependency ordering:

```
① Generators      →  Source generators (netstandard2.0)
② Foundation      →  Abstractions, Structura, Transport.Contracts
③ Infrastructure  →  Infrastructure, Transport.Impl
④ Core            →  ai/ (Llm, Agents, Reasoning)
│                    execution/ (Brain, Hands, Scheduling, McpToolDispatch)
│                    safety/ (Guard, Vault)
│                    search/ (CodeIndex, Browser)
⑤ Services        →  Mcp, Dream, Eyes, Bridge
⑥ Composition     →  Composition, Clock
⑦ App             →  JoinCode.exe, Sdk, integration tests, MockServers
```

### Key Design Decisions

| Decision | ADR |
|----------|-----|
| Seven-layer slnx isolation | [0001](docs/adr/0001-seven-layer-slnx-isolation.md) |
| NativeAOT, no Microsoft AI packages | [0002](docs/adr/0002-nativeaot-no-microsoft-ai-packages.md) |
| Rebase over merge | [0003](docs/adr/0003-rebase-over-merge.md) |
| Config over code for modalities | [0004](docs/adr/0004-config-over-code-modalities.md) |
| File-driven UI | [0005](docs/adr/0005-file-driven-ui.md) |
| TDD double-layer | [0006](docs/adr/0006-tdd-double-layer.md) |
| Progressive development | [0007](docs/adr/0007-progressive-development.md) |
| Archive to `.xxx/`, never delete | [0008](docs/adr/0008-archive-to-xxx-not-delete.md) |
| Defense-in-depth L1–L10 | [0036](docs/adr/0036-defense-in-depth-l1-l10.md) |

See [docs/adr/README.md](docs/adr/README.md) for all 40+ ADRs.

### Source Generators

| Generator | Purpose |
|-----------|---------|
| `AotSafety.Generator` | AOT safety analysis + code org rules (JCC5002, JCC9006) |
| `EnumMetadata.Generator` | `[EnumValue]` → `XxxConstants` + `XxxExtensions` |
| `McpToolDispatch.Generator` | MCP tool handler registration + `[Register]` DI |
| `PromptSection.Generator` | Prompt section generation |
| `PromptTemplate.Generator` | Prompt template generation |
| `ToolPrompt.Generator` | Tool prompt generation |
| `CliOption.Generator` | CLI option binding |
| `AppModule.Generator` | App module registration |
| `CodeFixes` | JCC code fixes |

### Middleware Pipelines

| Pipeline | Subsystem | Middleware Chain |
|----------|-----------|------------------|
| Chat | Brain | Timing→ErrorHandling→AuditLog→TokenBudget→PreChat→QueryLoop→LoopIntervention→ProcessUsage→CleanupInjections→SaveContext |
| Permission | Guard | Bypass→AgentRestriction→AutoClassifier→ConfigGetOperation→WebFetchPermission→EarlyPathDeny→ToolListPermission→PathPermission→DangerousOperation→PlanMode→AutoSafety→DefaultResult |
| Shell | Hands | Validation→Classification→SedIntercept→Background→BuildIntercept→Execution→Output |
| Web | Hands | Metrics→Validation→SsrfGuard→CacheCheck→DomainCheck→Fetch→ContentProcessing→CacheWrite |
| Skill | Hands | Metrics→Validation→Telemetry→Execution |
| Code | Hands | Cache→Security→Llm→Sandbox→Metrics |
| AgentSpawn | Agents | DefinitionResolution→PromptBuilding→ContextSetup→AgentWorktreeSpawn→HookSetup→McpSetup→Metadata→Transcript |
| Fork | Agents | ForkValidation→ForkSpawn→ForkPermission→ForkExecution |
| Settings | Guard | SettingsReload→EffortLevel→HookRefresh→PermissionCache |
| Preprocess | Brain | KeywordInjection→SynonymInjection→SystemPrompt→ReminderInjection→ToolListingInjection→LspDiagnostic |
| Compact | Brain | CompactHook→ContextCollapse→Microcompact→SessionMemoryCompact→ReactiveCompact |
| Query | Brain | UsdBudget→QueryTokenBudget→CostTracking→DiminishingReturns→HistorySnip→IdleReminder→StopHook→StateTransition→ContentReplacement |
| ChatInit | Brain | ContextLoad→CostRestore→ConfigChangeStart→SessionStartHook |
| ChatAdmin | Brain | SessionAdmin→SessionSave |

---

## Configuration

### Provider Configuration

Provider settings are stored in `~/.jcc/settings.json`. The file ships with
41 model entries across 5 providers (40 unique models after cross-provider
deduplication).

Switch models interactively with `/model <alias>`:

```
/model flash      # DeepSeek deepseek-v4-flash
/model pro        # DeepSeek deepseek-v4-pro
/model 4o         # OpenAI gpt-4o
/model opus5      # Anthropic claude-opus-5-20250815
/model 5.6        # OpenAI gpt-5.6-sol
```

### Project-Level Configuration

Create `.env/api.json` in the project root for team-shared defaults:

```json
{
  "env": {
    "DEEPSEEK_API_KEY": "sk-your-key",
    "JCC_VENDOR": "deepseek",
    "JCC_MODEL_ID": "deepseek-v4-flash"
  }
}
```

### API Key Priority

From low to high:

1. `~/.jcc/auth.json` `"deepseek"` field
2. `DEEPSEEK_API_KEY` environment variable (highest priority)
3. Fallback: `OPENAI_API_KEY` environment variable

---

## Detailed Documentation

| Document | Description |
|----------|-------------|
| [Available Models](docs/reference/models.md) | 41 models across 5 providers (aliases, context length, notes) |
| [Technical Details](docs/design/technical-details.md) | Fault tolerance / prefix caching / loop intervention / parallel load / serial build |
| [Small Model Strategy](docs/design/small-model-strategy.md) | Engineering strategies for small model scenarios (synonyms / prohibitions / counterexamples / match) |
| [Architecture Index](docs/design/architecture-index.md) | Component dependency graph / detail table / internal structure / middleware pipelines / build commands |
| [Architecture Decision Records](docs/adr/README.md) | 40+ ADRs: *why* choice A over B |

---

## Contributing

We welcome contributions! Please read [CONTRIBUTING.md](.github/CONTRIBUTING.md) for
development setup, coding conventions, and the PR workflow.

Key points:

- Follow the [seven-layer build order](.github/CONTRIBUTING.md#architecture-overview)
- Use [Conventional Commits](https://www.conventionalcommits.org/) format
- `TreatWarningsAsErrors` is enabled — zero warnings allowed
- Write ADRs for architecturally significant decisions
- See [AGENTS.md](AGENTS.md) for detailed engineering rules (Chinese)

### Testing

```powershell
# All tests except integration
dotnet test App.slnx -c Release /p:SkipLocalPack=true --filter "Category!=Integration"

# Single component
dotnet test tests/Unit/Hands.Tests/Hands.Tests.csproj -c Debug --filter "Category!=Integration"
```

---

## License

This project is licensed under the [MIT License](LICENSE).

## Acknowledgements

- **ByteDance TraeCN** and **Huawei Cloud CodeArts** — for foundational
  tooling and inspiration
- The open-source community for TreeSitter, Terminal.Gui, and all
  dependencies listed in our NuGet references

## Contact

- **Email:** [superhong@foxmail.com](mailto:superhong@foxmail.com)
- **Issues:** [GitHub Issues](https://github.com/JoinCode/JoinCode/issues)
- **Security:** See [SECURITY.md](.github/SECURITY.md) — do not use public issues for
  security reports
