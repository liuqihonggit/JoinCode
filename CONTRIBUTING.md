# Contributing to JoinCode

Thank you for your interest in contributing to JoinCode! This document
describes how to set up your development environment and the conventions we
follow.

## Code of Conduct

By participating, you agree to uphold our [Code of Conduct](CODE_OF_CONDUCT.md).

## Prerequisites

| Requirement | Version |
|-------------|---------|
| .NET SDK | 10.0.301+ |
| OS | Windows / Linux / macOS |
| Git | 2.30+ |

## Development Setup

```powershell
# Clone the repository
git clone https://github.com/JoinCode/JoinCode.git
cd JoinCode

# Restore and build (Debug, incremental)
dotnet build Generators.slnx -c Debug
dotnet build Foundation.slnx -c Debug
dotnet build Infrastructure.slnx -c Debug
dotnet build Core.slnx -c Debug
dotnet build Services.slnx -c Debug
dotnet build Composition.slnx -c Debug
dotnet build App.slnx -c Debug

# Or use the build script
.\build.ps1 -Mode Fast -SkipTests -Configuration Debug
```

The build output is placed in `artifacts/bin/`. The main executable is
`artifacts/bin/JoinCode/Debug/net10.0/jcc.exe`.

## Architecture Overview

JoinCode uses a **seven-layer solution isolation** architecture. Each layer is
an independent `.slnx` that depends only on lower layers:

```
① Generators      →  Source generators (netstandard2.0)
② Foundation      →  Abstractions, Structura, Transport.Contracts
③ Infrastructure  →  Infrastructure, Transport.Impl
④ Core            →  ai/ + execution/ + safety/ + search/
⑤ Services        →  Mcp, Dream, Eyes, Bridge
⑥ Composition     →  Composition, Clock
⑦ App             →  JoinCode.exe, Sdk, tests
```

**Build order matters.** Always build lower layers before upper layers. See
[ADR 0001](docs/adr/0001-seven-layer-slnx-isolation.md) for the rationale.

## Development Workflow

### 1. Create a Branch

```powershell
git checkout main
git pull --rebase origin main
git checkout -b w1   # or w2, w3, ... (task branches)
```

### 2. Make Changes

Follow the **progressive development** approach (ADR 0007):

- Complete one feature at a time
- Compile after each change
- Run relevant unit tests
- Commit after each green test

### 3. Build & Test

```powershell
# Build only the affected .csproj (fastest)
dotnet build core/ai/Llm/src/Llm.csproj -c Debug

# Run unit tests for a specific component
dotnet test tests/Unit/Hands.Tests/Hands.Tests.csproj -c Debug --filter "Category!=Integration"

# Full build + test (before creating a PR)
.\build.ps1 -Mode Fast
```

### 4. Commit

We follow [Conventional Commits](https://www.conventionalcommits.org/):

```
<type>: <description> | 决策: [optional decision note]
```

**Types:** `feat`, `fix`, `refactor`, `docs`, `test`, `chore`

```powershell
git add -A
git commit -m "feat: add tool search capability" -m "决策: query MCP memory first, then internet"
```

**Commit rules:**

- Do not include branch names (w1/w2/feature-xxx) in the message
- Do not include PR/Issue numbers (GitHub auto-links them)
- Unit tests must pass before committing
- No `$`, backticks, or triple quotes in commit messages

### 5. Create a Pull Request

```powershell
# Sync with latest main first
git fetch origin main
git merge origin/main

# Push and create PR
git push -u origin w1
gh pr create --base main --head w1 --title "feat: add tool search capability" --body "..."
gh pr merge <number> --auto --squash
```

PRs trigger full CI (build + unit tests + integration tests + E2E + AOT). CI
must pass before auto-merge.

## Coding Conventions

### C# Style

- **`GlobalUsings.cs`:** All `using` directives go in `GlobalUsings.cs`, never
  in individual `.cs` files
- **Nullable reference types:** Enabled globally
- **`TreatWarningsAsErrors`:** Enabled — zero warnings allowed
- **XML doc comments:** Required on all public members; never delete them
- **No comments in code** unless explicitly requested

### Naming

- PascalCase for public members, types, and namespaces
- camelCase for local variables and parameters
- `_camelCase` for private fields

### Architecture Rules

| Rule | Reference |
|------|-----------|
| Seven-layer slnx isolation | [ADR 0001](docs/adr/0001-seven-layer-slnx-isolation.md) |
| NativeAOT, no Microsoft AI packages | [ADR 0002](docs/adr/0002-nativeaot-no-microsoft-ai-packages.md) |
| Rebase over merge | [ADR 0003](docs/adr/0003-rebase-over-merge.md) |
| TDD double-layer | [ADR 0006](docs/adr/0006-tdd-double-layer.md) |
| Archive to `.xxx/`, never delete | [ADR 0008](docs/adr/0008-archive-to-xxx-not-delete.md) |
| Pass interfaces, not properties | [ADR 0016](docs/adr/0016-pass-interface-not-property.md) |
| Treat warnings as errors | [ADR 0027](docs/adr/0027-treat-warnings-as-errors.md) |

### Data Container Selection

| Scenario | Use |
|----------|-----|
| Lookup (unordered) | `Dictionary<K,V>` / `HashSet<T>` |
| Hardcoded ordered (e.g., enum map) | `SortedList<K,V>` |
| High-frequency insert + ordered | `SortedDictionary<K,V>` |
| AOT immutable lookup | `FrozenSet<T>` / `FrozenDictionary<K,V>` |
| **Never** for lookup | `List<T>` / `T[]` (`.Contains()` is O(n)) |

### Enum + `[EnumValue]`

Finite string constants must be enums with `[EnumValue]` attributes. Source
generators produce `XxxConstants` and `XxxExtensions` automatically. See
[ADR 0019](docs/adr/0019-enum-enumvalue-source-generator.md).

## Testing

### TDD Cycle

```
🔴 E2E red → 🔴 unit red → 🟢 unit green → 🔵 refactor → 🟢 E2E green
```

- Write a failing E2E test first (when external interface changes)
- Then write failing unit tests to localize the root cause
- Implement until unit tests pass
- Refactor
- Verify E2E passes

### Running Tests

```powershell
# All tests except integration
dotnet test App.slnx -c Release /p:SkipLocalPack=true --filter "Category!=Integration"

# Single test
dotnet test tests/Unit/Hands.Tests/Hands.Tests.csproj -c Debug --filter "FullyQualifiedName~YourTestClass"
```

Each test should have a 10-second timeout. If the full test suite hangs, stop
and fix it — never leave the suite in a broken state.

## Adding a New MCP Tool

1. Add the tool to an existing `ToolCategory` enum value (or add a new one
   with justification)
2. Use the `[McpTool]` attribute + source generator pattern
3. Write tool description in Chinese (align with `ErrorRecoveryToolHandlers`)
4. Update `ToolCategory` `[EnumValue]` and rebuild with `--no-incremental`
5. See [ADR 0014](docs/adr/0014-mcp-tool-coverage-principle.md)

## Architecture Decision Records (ADRs)

When making a cross-module or architecturally significant decision:

1. Create `docs/adr/NNNN-kebab-case-title.md` (NNNN = next four-digit number)
2. Set status to `proposed`
3. Implement the decision
4. Change status to `accepted` after verification
5. ADRs are immutable — only the status field changes after acceptance

See [docs/adr/README.md](docs/adr/README.md) for the template and conventions.

## Getting Help

- See [SUPPORT.md](SUPPORT.md) for support channels
- Read [AGENTS.md](AGENTS.md) for detailed engineering rules (Chinese)
- Check existing [issues](https://github.com/JoinCode/JoinCode/issues) and
  [discussions](https://github.com/JoinCode/JoinCode/discussions)

## Recognition

Contributors are recognized in release notes and the GitHub Contributors page.
Significant contributions may be acknowledged in the README.
