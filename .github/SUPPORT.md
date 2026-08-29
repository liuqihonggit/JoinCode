# Support

## How to Get Help

Choose the most appropriate channel below based on your needs.

### :books: Documentation

| Resource | Description |
|----------|-------------|
| [README](../README.md) | Project overview, quick start, and CLI reference |
| [中文 README](../docs/zh-CN/README.md) | Chinese documentation |
| [Architecture Decision Records](../docs/adr/) | 40+ ADRs explaining *why* decisions were made |
| [Design Docs](../docs/design/) | *How* features are implemented |
| [AGENTS.md](../AGENTS.md) | Development conventions and engineering rules |

### :bug: Issues & Bugs

Use [GitHub Issues](https://github.com/JoinCode/JoinCode/issues) for:

- Bug reports (use the **Bug Report** template)
- Feature requests (use the **Feature Request** template)
- Questions about usage (use the **Question** template)

Before opening a new issue, please:

1. Search [existing issues](https://github.com/JoinCode/JoinCode/issues?q=is%3Aissue)
   to avoid duplicates.
2. Use the `--doctor` flag or `--debuglog` flag to collect diagnostic
   information.
3. Include the JoinCode version, OS, and .NET SDK version in your report.

### :speech_balloon: Discussions

For general discussion, ideas, and community Q&A, use
[GitHub Discussions](https://github.com/JoinCode/JoinCode/discussions)
(if enabled) or open an issue with the **Question** template.

### :lock: Security Issues

See [SECURITY.md](SECURITY.md). **Do not open public issues for security
vulnerabilities.**

## Common Issues

### Build Fails with "MSB3027: Exceeded retry count"

Another `dotnet build` or `testhost` process is holding a lock on build
artifacts. Kill stale processes and retry:

```powershell
Get-Process -Name testhost -ErrorAction SilentlyContinue | Stop-Process -Force
dotnet build App.slnx -c Debug
```

### NativeAOT Compilation Fails on New `[Register]` Types

Source generators cache output during incremental builds. After adding or
modifying `[Register]` classes, rebuild with `--no-incremental`:

```powershell
dotnet build Generators.slnx -c Release --no-incremental
```

### `jcc` Hangs in Interactive Mode

Use the non-interactive mode with a timeout for debugging:

```powershell
jcc --trust --await 20 -p "your prompt here"
```

If the process times out (exit code `1234`), run with `--debuglog` to capture
diagnostic traces.

## Filing a Good Bug Report

A great bug report includes:

1. **Summary:** One-sentence description of the problem
2. **Reproduction steps:** Minimal sequence to trigger the bug
3. **Expected vs. actual behavior**
4. **Environment:** OS, .NET SDK version, JoinCode version
5. **Logs:** Output from `--debuglog` (trim sensitive data first!)

## Contributing

See [CONTRIBUTING.md](CONTRIBUTING.md) for how to contribute code, documentation,
or fixes.
