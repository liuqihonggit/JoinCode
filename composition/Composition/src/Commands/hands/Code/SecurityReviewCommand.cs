namespace JoinCode.ChatCommands;

[ChatCommand(Name = ChatCommandNameConstants.SecurityReview, Description = "对当前分支变更进行安全审查", Usage = "/security-review", Category = ChatCommandCategory.Code)]
public sealed class SecurityReviewCommand : ChatCommandBase
{
    public async override Task<ChatCommandResult> ExecuteAsync(ChatCommandContext context)
    {
        Diag.WriteLifecycle("[DIAG-SEC-REVIEW] ExecuteAsync entry");
        var fs = context.GetCommandServices().FileSystem;
        var gitRunner = ChatCommandBase.GetService<IGitCommandRunner>(context);
        if (gitRunner is null)
        {
            TerminalHelper.WriteLine("Git 命令执行器未注册，无法执行 /security-review");
            Diag.WriteLifecycle("[DIAG-SEC-REVIEW] gitRunner is null, returning early");
            return ChatCommandResult.Continue();
        }
        Diag.WriteLifecycle("[DIAG-SEC-REVIEW] gitRunner resolved");

        Diag.WriteLifecycle("[DIAG-SEC-REVIEW] git status --porcelain start");
        var gitStatus = await RunGitCommandAsync($"{GitSubCommand.Status.ToValue()} --porcelain", context.CancellationToken, fs, gitRunner).ConfigureAwait(false);
        Diag.WriteLifecycle($"[DIAG-SEC-REVIEW] git status --porcelain end, len={gitStatus.Length}");

        Diag.WriteLifecycle("[DIAG-SEC-REVIEW] git diff --name-only start");
        var gitDiffNames = await RunGitCommandAsync($"{GitSubCommand.Diff.ToValue()} --name-only", context.CancellationToken, fs, gitRunner).ConfigureAwait(false);
        Diag.WriteLifecycle($"[DIAG-SEC-REVIEW] git diff --name-only end, len={gitDiffNames.Length}");

        Diag.WriteLifecycle("[DIAG-SEC-REVIEW] git log --oneline -10 start");
        var gitLog = await RunGitCommandAsync($"{GitSubCommand.Log.ToValue()} --oneline -10", context.CancellationToken, fs, gitRunner).ConfigureAwait(false);
        Diag.WriteLifecycle($"[DIAG-SEC-REVIEW] git log --oneline -10 end, len={gitLog.Length}");

        Diag.WriteLifecycle("[DIAG-SEC-REVIEW] git diff start");
        var gitDiff = await RunGitCommandAsync(GitSubCommand.Diff.ToValue(), context.CancellationToken, fs, gitRunner).ConfigureAwait(false);
        Diag.WriteLifecycle($"[DIAG-SEC-REVIEW] git diff end, len={gitDiff.Length}");

        if (string.IsNullOrWhiteSpace(gitDiff))
        {
            Diag.WriteLifecycle("[DIAG-SEC-REVIEW] git diff --cached start");
            gitDiff = await RunGitCommandAsync($"{GitSubCommand.Diff.ToValue()} --cached", context.CancellationToken, fs, gitRunner).ConfigureAwait(false);
            Diag.WriteLifecycle($"[DIAG-SEC-REVIEW] git diff --cached end, len={gitDiff.Length}");
        }

        if (string.IsNullOrWhiteSpace(gitStatus) && string.IsNullOrWhiteSpace(gitDiff))
        {
            TerminalHelper.WriteLine("没有要审查的变更");
            Diag.WriteLifecycle("[DIAG-SEC-REVIEW] no changes, returning early");
            return ChatCommandResult.Continue();
        }

        var prompt = BuildSecurityReviewPrompt(gitStatus, gitDiffNames, gitLog, gitDiff);

        try
        {
            TerminalHelper.WriteLine($"{TerminalColors.Primary}正在执行安全审查...{AnsiStyleConstants.Reset}");
            Diag.WriteLifecycle("[DIAG-SEC-REVIEW] SendMessageAsync start (LLM call)");
            var result = await context.GetCommandServices().ChatService.SendMessageAsync(prompt, context.CancellationToken).ConfigureAwait(false);
            Diag.WriteLifecycle($"[DIAG-SEC-REVIEW] SendMessageAsync end, resultLen={result.Length}");
            TerminalHelper.WriteLine(result);
        }
        catch (Exception ex)
        {
            Diag.WriteLifecycle($"[DIAG-SEC-REVIEW] SendMessageAsync EXCEPTION: {ex.GetType().Name}: {ex.Message}");
            ChatCommandBase.HandleError("安全审查", ex);
        }

        Diag.WriteLifecycle("[DIAG-SEC-REVIEW] ExecuteAsync returning Continue");
        return ChatCommandResult.Continue();
    }

    private static string BuildSecurityReviewPrompt(
        string gitStatus, string diffNames, string gitLog, string gitDiff)
    {
        return $"""
You are a senior security engineer conducting a focused security review of the changes on this branch.

## Context

GIT STATUS:
{gitStatus}

FILES MODIFIED:
{diffNames}

RECENT COMMITS:
{gitLog}

DIFF CONTENT:
{TruncateDiff(gitDiff, 30000)}

## Objective

Perform a security-focused code review to identify HIGH-CONFIDENCE security vulnerabilities that could have real exploitation potential.

Only flag vulnerabilities with >80% confidence that are exploitable. Skip theoretical issues, style issues, and low-impact findings.

## Security Categories

1. **Input Validation** - SQL injection, command injection, XXE, template injection, NoSQL injection, path traversal
2. **Authentication & Authorization** - Auth bypass, privilege escalation, session management flaws, JWT vulnerabilities
3. **Crypto & Secrets Management** - Hardcoded keys, weak crypto algorithms, improper key storage, random number issues
4. **Injection & Code Execution** - Deserialization RCE, eval injection, XSS
5. **Data Exposure** - Sensitive data logging, PII handling violations, API endpoint data leakage

## Analysis Methodology

Phase 1 - Repository Context Research:
- Identify existing security frameworks and libraries
- Find established security coding patterns
- Check existing sanitization and validation patterns

Phase 2 - Comparative Analysis:
- Compare new code changes against existing security patterns
- Identify deviations from established security practices
- Flag code introducing new attack surfaces

Phase 3 - Vulnerability Assessment:
- Check security implications of each modified file
- Trace data flow from user input to sensitive operations
- Identify injection points and unsafe deserialization

## Output Format

For each vulnerability found:

# Vuln N: [Category]: `[file:line]`
* Severity: HIGH/MEDIUM/LOW
* Description: ...
* Exploit Scenario: ...
* Recommendation: ...

## Exclusion Rules (Do NOT report)

- DOS/resource exhaustion attacks
- Keys/credentials already on disk and protected
- Rate limiting/service overload
- Theoretical race conditions/timing attacks
- Outdated third-party library vulnerabilities
- Memory safety issues in memory-safe languages
- Test-only files
- Log spoofing
- SSRF where only path is controllable (must control host/protocol)
- Regex injection or ReDoS
- Missing audit logs
- UUID considered unguessable
- Environment variables and CLI flags are trusted values

If no high-confidence vulnerabilities are found, state: "No high-confidence security vulnerabilities identified in the reviewed changes."
""";
    }

    private static string TruncateDiff(string diff, int maxLength) => StringTruncator.Truncate(diff, maxLength, "\n... (diff truncated)", suffixWithinLimit: false);

    private static async Task<string> RunGitCommandAsync(string arguments, CancellationToken cancellationToken, IFileSystem fs, IGitCommandRunner gitRunner)
    {
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromSeconds(15));
            Diag.WriteLifecycle($"[DIAG-SEC-REVIEW] RunGitCommandAsync start: git {arguments}");
            var result = await gitRunner.ExecuteAsync(arguments, fs.GetCurrentDirectory(), cts.Token).ConfigureAwait(false);
            Diag.WriteLifecycle($"[DIAG-SEC-REVIEW] RunGitCommandAsync end: git {arguments}, exitCode={result.ExitCode}, outputLen={result.Output.Length}");
            return result.Output;
        }
        catch (Exception ex)
        {
            Diag.WriteLifecycle($"[DIAG-SEC-REVIEW] RunGitCommandAsync EXCEPTION: git {arguments}, {ex.GetType().Name}: {ex.Message}");
            return string.Empty;
        }
    }
}
