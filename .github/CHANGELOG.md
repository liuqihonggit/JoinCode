# Changelog

All notable changes to JoinCode are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

Given a version number `MAJOR.MINOR.PATCH`, we increment:

- **MAJOR** for incompatible API changes
- **MINOR** for backward-compatible new functionality
- **PATCH** for backward-compatible bug fixes

## [Unreleased]

### Added

- Community health files: `LICENSE`, `CODE_OF_CONDUCT.md`, `SECURITY.md`,
  `SUPPORT.md`, `CONTRIBUTING.md`, `CHANGELOG.md`
- GitHub issue templates (bug report, feature request, question)
- GitHub pull request template
- English `README.md` (Chinese version moved to `docs/zh-CN/README.md`)

## [1.0.0] - 2025-08-29

### Added

- **Core AI agent:** Pure C# terminal-based AI coding agent, NativeAOT
  single-file binary, zero runtime dependencies
- **Multi-provider support:** DeepSeek, OpenAI, Anthropic, Azure, SenseNova,
  Agnes — 41 model entries across 5 providers
- **Three LLM protocols:** `openai-compatible` (Chat Completions),
  `anthropic` (Messages), `responses` (OpenAI Responses API)
- **MCP protocol:** Full Model Context Protocol client (version
  `2025-11-25` Streamable HTTP), two-phase tool loading
- **Seven-layer slnx isolation:** Generators → Foundation → Infrastructure →
  Core → Services → Composition → App, strict dependency ordering
- **Nine source generators:** EnumMetadata, McpToolDispatch, PromptSection,
  PromptTemplate, ToolPrompt, CliOption, AppModule, AotSafety, CodeFixes
- **14 middleware pipelines:** Chat, Permission, Shell, Web, Skill, Code,
  AgentSpawn, Fork, Settings, Preprocess, Compact, Query, ChatInit, ChatAdmin
- **Structured reasoning (`/falv`):** Three-branch adversarial engine
  (Prosecution → Defense → Judge) with DAG evidence chain and dual budget
- **Multi-agent collaboration (`/goal`):** Task graph engine with hot-spot
  detection replacing file locks, 22 anti-conflict components
- **TUI mode:** Terminal.Gui v2 full-screen interface with multi-line editor,
  slash command forwarding, `Ctrl+Enter` to send
- **Tool inertia error correction:** Unified `gh` command executor, shell
  pipeline auto-rewriting, threshold-triggered fix hooks
- **Defense-in-depth security:** L1–L10 layered security architecture
  (ADR 0036)
- **Loop intervention:** Three-tier output loop detection with Shannon
  entropy, logic fingerprint, and tool-call sequence detectors
- **40+ Architecture Decision Records** in `docs/adr/`
- **Custom analyzers:** `JCC5002` (no string concatenation in loops),
  `JCC9006` (enforce `FileShare.ReadWrite`)

### Performance

- 3,313-file AST parse in ~2.7 seconds
- TUI startup parallelization: -37%
- Reflection-free startup: -70%
- Full-pipeline UTF-8 + batch mode terminal writes: -83%
- `LayoutAndDraw`: -58%

### Technical Decisions

- NativeAOT + `TrimMode=full` enforced in Release (ADR 0002)
- `TreatWarningsAsErrors` enabled, zero-warning tolerance (ADR 0027)
- `InvariantGlobalization=true` (ADR 0028)
- Rebase over merge for linear history (ADR 0003)
- Archive to `.xxx/` instead of deleting files (ADR 0008)
- TDD double-layer: E2E red → unit red → unit green → refactor → E2E green
  (ADR 0006)

[Unreleased]: https://github.com/JoinCode/JoinCode/compare/v1.0.0...HEAD
[1.0.0]: https://github.com/JoinCode/JoinCode/releases/tag/v1.0.0
