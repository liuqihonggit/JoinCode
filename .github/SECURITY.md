# Security Policy

## Supported Versions

JoinCode is actively developed. Security fixes are applied to the latest
release on the `main` branch.

| Version | Supported          |
|---------|--------------------|
| 1.0.x   | :white_check_mark: |
| < 1.0   | :x:                |

## Reporting a Vulnerability

We take security bugs seriously. We appreciate your efforts to responsibly
disclose your findings and will make every effort to acknowledge your
contributions.

### How to Report

**Please do NOT report security vulnerabilities through public GitHub issues.**

Instead, please report them privately using one of the following methods:

1. **Preferred:** Use [GitHub Security Advisories](https://github.com/JoinCode/JoinCode/security/advisories/new)
   to create a private vulnerability report.
2. **Email:** Send details to [superhong@foxmail.com](mailto:superhong@foxmail.com)
   with the subject line `[SECURITY] JoinCode Vulnerability Report`.

### What to Include

To help us triage and resolve the issue quickly, please include:

- A clear description of the vulnerability and its potential impact
- The version of JoinCode affected
- Step-by-step instructions to reproduce the issue
- A proof-of-concept or exploit code (if available)
- Any suggested fixes or mitigations (optional)

### Response Timeline

| Action                          | Target Timeframe |
|---------------------------------|------------------|
| Acknowledge receipt of report    | Within 48 hours  |
| Initial assessment & triage      | Within 5 days    |
| Status update to reporter        | Every 7 days     |
| Fix release (if accepted)        | Within 30 days   |

### Disclosure Policy

- We follow a **coordinated disclosure** process.
- We request that you do not disclose the vulnerability publicly until a fix
  has been released.
- Once a fix is available, we will publish a GitHub Security Advisory and
  credit the reporter (unless they prefer to remain anonymous).

## Security Features

JoinCode is designed with defense-in-depth security architecture:

- **Multi-layer permission pipeline:** Path permissions → dangerous operation
  interception → automatic safety classification → agent restrictions
- **NativeAOT compilation:** Single-file native binary, no JIT, reduced attack
  surface
- **SSRF protection:** Built-in Server-Side Request Forgery guards on all web
  requests
- **Sandbox isolation:** Tool execution sandboxed with configurable boundaries
- **No Microsoft AI SDK dependency:** Eliminates supply-chain risk from
  AOT-incompatible AI packages

See [ADR 0036](../docs/adr/0036-defense-in-depth-l1-l10.md) for the full
defense-in-depth architecture (L1–L10).
