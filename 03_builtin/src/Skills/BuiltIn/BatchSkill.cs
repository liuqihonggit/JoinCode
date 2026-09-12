
namespace Core.Skills.BuiltIn;

public sealed class BatchSkill
{
    private const int MinAgents = 5;
    private const int MaxAgents = 30;

    private const string WorkerInstructions = @"After you finish implementing the change:
1. **Simplify** — Review and clean up your changes.
2. **Run unit tests** — Run the project's test suite. If tests fail, fix them.
3. **Test end-to-end** — Follow the e2e test recipe from the coordinator's prompt (below). If the recipe says to skip e2e for this unit, skip it.
4. **Commit and push** — Commit all changes with a clear message, push the branch, and create a PR. Use a descriptive title. If push fails, note it in your final message.
5. **Report** — End with a single line: `PR: <url>` so the coordinator can track it. If no PR was created, end with `PR: none — <reason>`.";

    public static SkillDefinition CreateDefinition()
    {
        return new SkillDefinition
        {
            Name = "batch",
            Description = "Research and plan a large-scale change, then execute it in parallel across isolated worktree agents that each open a PR",
            Version = "3.0",
            Parameters = new Dictionary<string, SkillParameter>
            {
                ["instruction"] = new() { Type = "string", Description = "Description of the batch change to make", Required = true }
            },
            Steps = [],
            RequiresConfirmation = true,
            TimeoutSeconds = 600,
            Tags = new List<string> { "batch", "bulk", "automation", "parallel" }.AsReadOnly(),
            Permissions = new List<string> { "file.read", "file.write", "file.search", "shell.execute" }.AsReadOnly(),
            Context = SkillExecutionMode.Fork,
            Isolation = AgentIsolationMode.Worktree,
            DisableModelInvocation = true,
            ContentTemplate = BuildPromptTemplate()
        };
    }

    private static string BuildPromptTemplate()
    {
        return $""""
# Batch: Parallel Work Orchestration

You are orchestrating a large, parallelizable change across this codebase.

## User Instruction

$ARGUMENTS

## Phase 1: Research and Plan (Plan Mode)

Enter plan mode, then:

1. **Understand the scope.** Launch one or more subagents (in the foreground — you need their results) to deeply research what this instruction touches. Find all the files, patterns, and call sites that need to change. Understand the existing conventions so the migration is consistent.

2. **Decompose into independent units.** Break the work into {MinAgents}–{MaxAgents} self-contained units. Each unit must:
   - Be independently implementable in an isolated git worktree (no shared state with sibling units)
   - Be mergeable on its own without depending on another unit's PR landing first
   - Be roughly uniform in size (split large units, merge trivial ones)

   Scale the count to the actual work: few files → closer to {MinAgents}; hundreds of files → closer to {MaxAgents}. Prefer per-directory or per-module slicing over arbitrary file lists.

3. **Determine the e2e test recipe.** Figure out how a worker can verify its change actually works end-to-end — not just that unit tests pass. Look for:
   - A browser-automation tool (for UI changes: click through the affected flow)
   - A CLI-verifier pattern (for CLI changes: launch the app, exercise the changed behavior)
   - A dev-server + curl pattern (for API changes: start the server, hit the affected endpoints)
   - An existing e2e/integration test suite the worker can run

   If you cannot find a concrete e2e path, ask the user how to verify this change end-to-end. Offer 2–3 specific options based on what you found. Do not skip this — the workers cannot ask the user themselves.

   Write the recipe as a short, concrete set of steps that a worker can execute autonomously.

4. **Write the plan.** In your plan file, include:
   - A summary of what you found during research
   - A numbered list of work units — for each: a short title, the list of files/directories it covers, and a one-line description of the change
   - The e2e test recipe (or "skip e2e because …" if the user chose that)
   - The exact worker instructions you will give each agent (the shared template)

5. Present the plan for approval.

## Phase 2: Spawn Workers (After Plan Approval)

Once the plan is approved, spawn one background agent per work unit using the `Agent` tool. **All agents must use `isolation: "worktree"` and `run_in_background: true`.** Launch them all in a single message block so they run in parallel.

For each agent, the prompt must be fully self-contained. Include:
- The overall goal (the user's instruction)
- This unit's specific task (title, file list, change description — copied verbatim from your plan)
- Any codebase conventions you discovered that the worker needs to follow
- The e2e test recipe from your plan (or "skip e2e because …")
- The worker instructions below, copied verbatim:

```
{WorkerInstructions}
```

## Phase 3: Track Progress

After launching all workers, render an initial status table:

| # | Unit | Status | PR |
|---|------|--------|----|
| 1 | <title> | running | — |
| 2 | <title> | running | — |

As background-agent completion notifications arrive, parse the `PR: <url>` line from each agent's result and re-render the table with updated status (`done` / `failed`) and PR links. Keep a brief failure note for any agent that did not produce a PR.

When all agents have reported, render the final table and a one-line summary (e.g., "22/24 units landed as PRs").
"""";
    }
}
