namespace JoinCode.Abstractions.Security.Shell;

/// <summary>
/// 只读命令检测器 — Git 子命令安全标志构建器
/// 包含所有 Git 相关的 BuildGitXxxSafeFlags、CheckGitXxxDangerous 方法
/// </summary>
public sealed partial class ReadOnlyCommandDetector
{
    #region Git 子命令安全标志构建器

    /// <summary>
    /// git diff 安全标志 — 对齐 TS GIT_READ_ONLY_COMMANDS["git diff"]
    /// </summary>
    private static FrozenDictionary<string, FlagArgType> BuildGitDiffSafeFlags() =>
        MergeGitFlags(
            GitStatFlags(),
            GitColorFlags(),
            new Dictionary<string, FlagArgType>(StringComparer.OrdinalIgnoreCase)
            {
                ["--dirstat"] = FlagArgType.None,
                ["--summary"] = FlagArgType.None,
                ["--patch-with-stat"] = FlagArgType.None,
                ["--word-diff"] = FlagArgType.None,
                ["--word-diff-regex"] = FlagArgType.Required,
                ["--color-words"] = FlagArgType.None,
                ["--no-renames"] = FlagArgType.None,
                ["--no-ext-diff"] = FlagArgType.None,
                ["--check"] = FlagArgType.None,
                ["--ws-error-highlight"] = FlagArgType.None,
                ["--full-index"] = FlagArgType.None,
                ["--binary"] = FlagArgType.None,
                ["--abbrev"] = FlagArgType.Optional,
                ["--break-rewrites"] = FlagArgType.None,
                ["--find-renames"] = FlagArgType.None,
                ["--find-copies"] = FlagArgType.None,
                ["--find-copies-harder"] = FlagArgType.None,
                ["--irreversible-delete"] = FlagArgType.None,
                ["--diff-algorithm"] = FlagArgType.Required,
                ["--histogram"] = FlagArgType.None,
                ["--patience"] = FlagArgType.None,
                ["--minimal"] = FlagArgType.None,
                ["--ignore-space-at-eol"] = FlagArgType.None,
                ["--ignore-space-change"] = FlagArgType.None,
                ["--ignore-all-space"] = FlagArgType.None,
                ["--ignore-blank-lines"] = FlagArgType.None,
                ["--inter-hunk-context"] = FlagArgType.Required,
                ["--function-context"] = FlagArgType.None,
                ["--exit-code"] = FlagArgType.None,
                ["--quiet"] = FlagArgType.None,
                ["--cached"] = FlagArgType.None,
                ["--staged"] = FlagArgType.None,
                ["--pickaxe-regex"] = FlagArgType.None,
                ["--pickaxe-all"] = FlagArgType.None,
                ["--no-index"] = FlagArgType.None,
                ["--relative"] = FlagArgType.Required,
                ["--diff-filter"] = FlagArgType.Required,
                ["-p"] = FlagArgType.None,
                ["-u"] = FlagArgType.None,
                ["-s"] = FlagArgType.None,
                ["-M"] = FlagArgType.None,
                ["-C"] = FlagArgType.None,
                ["-B"] = FlagArgType.None,
                ["-D"] = FlagArgType.None,
                ["-l"] = FlagArgType.Required,
                ["-S"] = FlagArgType.Required,
                ["-G"] = FlagArgType.Required,
                ["-O"] = FlagArgType.Required,
                ["-R"] = FlagArgType.None,
            }
        ).ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// git log 安全标志 — 对齐 TS GIT_READ_ONLY_COMMANDS["git log"]
    /// </summary>
    private static FrozenDictionary<string, FlagArgType> BuildGitLogSafeFlags() =>
        MergeGitFlags(
            GitLogDisplayFlags(),
            GitRefSelectionFlags(),
            GitDateFilterFlags(),
            GitCountFlags(),
            GitStatFlags(),
            GitColorFlags(),
            GitPatchFlags(),
            GitAuthorFilterFlags(),
            new Dictionary<string, FlagArgType>(StringComparer.OrdinalIgnoreCase)
            {
                ["--abbrev-commit"] = FlagArgType.None,
                ["--full-history"] = FlagArgType.None,
                ["--dense"] = FlagArgType.None,
                ["--sparse"] = FlagArgType.None,
                ["--simplify-merges"] = FlagArgType.None,
                ["--ancestry-path"] = FlagArgType.None,
                ["--source"] = FlagArgType.None,
                ["--first-parent"] = FlagArgType.None,
                ["--merges"] = FlagArgType.None,
                ["--no-merges"] = FlagArgType.None,
                ["--reverse"] = FlagArgType.None,
                ["--walk-reflogs"] = FlagArgType.None,
                ["--skip"] = FlagArgType.Required,
                ["--max-age"] = FlagArgType.Required,
                ["--min-age"] = FlagArgType.Required,
                ["--no-min-parents"] = FlagArgType.None,
                ["--no-max-parents"] = FlagArgType.None,
                ["--follow"] = FlagArgType.None,
                ["--no-walk"] = FlagArgType.None,
                ["--left-right"] = FlagArgType.None,
                ["--cherry-mark"] = FlagArgType.None,
                ["--cherry-pick"] = FlagArgType.None,
                ["--boundary"] = FlagArgType.None,
                ["--topo-order"] = FlagArgType.None,
                ["--date-order"] = FlagArgType.None,
                ["--author-date-order"] = FlagArgType.None,
                ["--pretty"] = FlagArgType.Required,
                ["--format"] = FlagArgType.Required,
                ["--diff-filter"] = FlagArgType.Required,
                ["-S"] = FlagArgType.Required,
                ["-G"] = FlagArgType.Required,
                ["--pickaxe-regex"] = FlagArgType.None,
                ["--pickaxe-all"] = FlagArgType.None,
            }
        ).ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// git show 安全标志 — 对齐 TS GIT_READ_ONLY_COMMANDS["git show"]
    /// </summary>
    private static FrozenDictionary<string, FlagArgType> BuildGitShowSafeFlags() =>
        MergeGitFlags(
            GitLogDisplayFlags(),
            GitStatFlags(),
            GitColorFlags(),
            GitPatchFlags(),
            new Dictionary<string, FlagArgType>(StringComparer.OrdinalIgnoreCase)
            {
                ["--abbrev-commit"] = FlagArgType.None,
                ["--word-diff"] = FlagArgType.None,
                ["--word-diff-regex"] = FlagArgType.Required,
                ["--color-words"] = FlagArgType.None,
                ["--pretty"] = FlagArgType.Required,
                ["--format"] = FlagArgType.Required,
                ["--first-parent"] = FlagArgType.None,
                ["--raw"] = FlagArgType.None,
                ["--diff-filter"] = FlagArgType.Required,
                ["-m"] = FlagArgType.None,
                ["--quiet"] = FlagArgType.None,
            }
        ).ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// git shortlog 安全标志 — 对齐 TS GIT_READ_ONLY_COMMANDS["git shortlog"]
    /// </summary>
    private static FrozenDictionary<string, FlagArgType> BuildGitShortlogSafeFlags() =>
        MergeGitFlags(
            GitRefSelectionFlags(),
            GitDateFilterFlags(),
            new Dictionary<string, FlagArgType>(StringComparer.OrdinalIgnoreCase)
            {
                ["-s"] = FlagArgType.None,
                ["--summary"] = FlagArgType.None,
                ["-n"] = FlagArgType.None,
                ["--numbered"] = FlagArgType.None,
                ["-e"] = FlagArgType.None,
                ["--email"] = FlagArgType.None,
                ["-c"] = FlagArgType.None,
                ["--committer"] = FlagArgType.None,
                ["--group"] = FlagArgType.Required,
                ["--format"] = FlagArgType.Required,
                ["--no-merges"] = FlagArgType.None,
                ["--author"] = FlagArgType.Required,
            }
        ).ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// git reflog 安全标志 — 对齐 TS GIT_READ_ONLY_COMMANDS["git reflog"]
    /// AdditionalDangerousCallback: 阻止 expire/delete/exists 子命令
    /// </summary>
    private static FrozenDictionary<string, FlagArgType> BuildGitReflogSafeFlags() =>
        MergeGitFlags(
            GitLogDisplayFlags(),
            GitRefSelectionFlags(),
            GitDateFilterFlags(),
            GitCountFlags(),
            GitAuthorFilterFlags()
        ).ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// git stash list 安全标志 — 对齐 TS GIT_READ_ONLY_COMMANDS["git stash list"]
    /// </summary>
    private static FrozenDictionary<string, FlagArgType> BuildGitStashListSafeFlags() =>
        MergeGitFlags(
            GitLogDisplayFlags(),
            GitRefSelectionFlags(),
            GitCountFlags()
        ).ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// git ls-remote 安全标志 — 对齐 TS GIT_READ_ONLY_COMMANDS["git ls-remote"]
    /// 故意排除 --server-option/-o
    /// </summary>
    private static FrozenDictionary<string, FlagArgType> BuildGitLsRemoteSafeFlags() =>
        new Dictionary<string, FlagArgType>(StringComparer.OrdinalIgnoreCase)
        {
            ["--branches"] = FlagArgType.None,
            ["-b"] = FlagArgType.None,
            ["--tags"] = FlagArgType.None,
            ["-t"] = FlagArgType.None,
            ["--heads"] = FlagArgType.None,
            ["-h"] = FlagArgType.None,
            ["--refs"] = FlagArgType.None,
            ["--quiet"] = FlagArgType.None,
            ["-q"] = FlagArgType.None,
            ["--exit-code"] = FlagArgType.None,
            ["--get-url"] = FlagArgType.None,
            ["--symref"] = FlagArgType.None,
            ["--sort"] = FlagArgType.Required,
        }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// git status 安全标志 — 对齐 TS GIT_READ_ONLY_COMMANDS["git status"]
    /// </summary>
    private static FrozenDictionary<string, FlagArgType> BuildGitStatusSafeFlags() =>
        new Dictionary<string, FlagArgType>(StringComparer.OrdinalIgnoreCase)
        {
            ["--short"] = FlagArgType.None,
            ["-s"] = FlagArgType.None,
            ["--branch"] = FlagArgType.None,
            ["-b"] = FlagArgType.None,
            ["--porcelain"] = FlagArgType.None,
            ["--long"] = FlagArgType.None,
            ["--verbose"] = FlagArgType.None,
            ["-v"] = FlagArgType.None,
            ["--untracked-files"] = FlagArgType.Required,
            ["-u"] = FlagArgType.Required,
            ["--ignored"] = FlagArgType.None,
            ["--ignore-submodules"] = FlagArgType.Required,
            ["--column"] = FlagArgType.None,
            ["--no-column"] = FlagArgType.None,
            ["--ahead-behind"] = FlagArgType.None,
            ["--no-ahead-behind"] = FlagArgType.None,
            ["--renames"] = FlagArgType.None,
            ["--no-renames"] = FlagArgType.None,
            ["--find-renames"] = FlagArgType.Required,
            ["-M"] = FlagArgType.Required,
        }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// git blame 安全标志 — 对齐 TS GIT_READ_ONLY_COMMANDS["git blame"]
    /// </summary>
    private static FrozenDictionary<string, FlagArgType> BuildGitBlameSafeFlags() =>
        MergeGitFlags(
            GitColorFlags(),
            new Dictionary<string, FlagArgType>(StringComparer.OrdinalIgnoreCase)
            {
                ["-L"] = FlagArgType.Required,
                ["--porcelain"] = FlagArgType.None,
                ["-p"] = FlagArgType.None,
                ["--line-porcelain"] = FlagArgType.None,
                ["--incremental"] = FlagArgType.None,
                ["--root"] = FlagArgType.None,
                ["--show-stats"] = FlagArgType.None,
                ["--show-name"] = FlagArgType.None,
                ["--show-number"] = FlagArgType.None,
                ["-n"] = FlagArgType.None,
                ["--show-email"] = FlagArgType.None,
                ["-e"] = FlagArgType.None,
                ["-f"] = FlagArgType.None,
                ["--date"] = FlagArgType.Required,
                ["-w"] = FlagArgType.None,
                ["--ignore-rev"] = FlagArgType.Required,
                ["--ignore-revs-file"] = FlagArgType.Required,
                ["-M"] = FlagArgType.None,
                ["-C"] = FlagArgType.None,
                ["--score-debug"] = FlagArgType.None,
                ["--abbrev"] = FlagArgType.Optional,
                ["-s"] = FlagArgType.None,
                ["-l"] = FlagArgType.None,
                ["-t"] = FlagArgType.None,
            }
        ).ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// git ls-files 安全标志 — 对齐 TS GIT_READ_ONLY_COMMANDS["git ls-files"]
    /// </summary>
    private static FrozenDictionary<string, FlagArgType> BuildGitLsFilesSafeFlags() =>
        new Dictionary<string, FlagArgType>(StringComparer.OrdinalIgnoreCase)
        {
            ["--cached"] = FlagArgType.None,
            ["-c"] = FlagArgType.None,
            ["--deleted"] = FlagArgType.None,
            ["-d"] = FlagArgType.None,
            ["--modified"] = FlagArgType.None,
            ["-m"] = FlagArgType.None,
            ["--others"] = FlagArgType.None,
            ["-o"] = FlagArgType.None,
            ["--ignored"] = FlagArgType.None,
            ["-i"] = FlagArgType.None,
            ["--stage"] = FlagArgType.None,
            ["-s"] = FlagArgType.None,
            ["--killed"] = FlagArgType.None,
            ["-k"] = FlagArgType.None,
            ["--unmerged"] = FlagArgType.None,
            ["-u"] = FlagArgType.None,
            ["--directory"] = FlagArgType.None,
            ["--no-empty-directory"] = FlagArgType.None,
            ["--eol"] = FlagArgType.None,
            ["--full-name"] = FlagArgType.None,
            ["--abbrev"] = FlagArgType.Optional,
            ["--debug"] = FlagArgType.None,
            ["-z"] = FlagArgType.None,
            ["-t"] = FlagArgType.None,
            ["-v"] = FlagArgType.None,
            ["-f"] = FlagArgType.None,
            ["--exclude"] = FlagArgType.Required,
            ["-x"] = FlagArgType.Required,
            ["--exclude-from"] = FlagArgType.Required,
            ["-X"] = FlagArgType.Required,
            ["--exclude-per-directory"] = FlagArgType.Required,
            ["--exclude-standard"] = FlagArgType.None,
            ["--error-unmatch"] = FlagArgType.None,
            ["--recurse-submodules"] = FlagArgType.None,
        }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// git config --get 安全标志 — 对齐 TS GIT_READ_ONLY_COMMANDS["git config --get"]
    /// 三词键注册，仅允许读取模式
    /// </summary>
    private static FrozenDictionary<string, FlagArgType> BuildGitConfigGetSafeFlags() =>
        new Dictionary<string, FlagArgType>(StringComparer.OrdinalIgnoreCase)
        {
            ["--local"] = FlagArgType.None,
            ["--global"] = FlagArgType.None,
            ["--system"] = FlagArgType.None,
            ["--worktree"] = FlagArgType.None,
            ["--default"] = FlagArgType.Required,
            ["--type"] = FlagArgType.Required,
            ["--bool"] = FlagArgType.None,
            ["--int"] = FlagArgType.None,
            ["--bool-or-int"] = FlagArgType.None,
            ["--path"] = FlagArgType.None,
            ["--expiry-date"] = FlagArgType.None,
            ["-z"] = FlagArgType.None,
            ["--null"] = FlagArgType.None,
            ["--name-only"] = FlagArgType.None,
            ["--show-origin"] = FlagArgType.None,
            ["--show-scope"] = FlagArgType.None,
        }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// git remote show 安全标志 — 对齐 TS GIT_READ_ONLY_COMMANDS["git remote show"]
    /// AdditionalDangerousCallback: 位置参数必须是字母数字远程名
    /// </summary>
    private static FrozenDictionary<string, FlagArgType> BuildGitRemoteShowSafeFlags() =>
        new Dictionary<string, FlagArgType>(StringComparer.OrdinalIgnoreCase)
        {
            ["-n"] = FlagArgType.None,
        }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// git remote 安全标志 — 对齐 TS GIT_READ_ONLY_COMMANDS["git remote"]
    /// AdditionalDangerousCallback: 仅允许裸命令或 -v/--verbose
    /// </summary>
    private static FrozenDictionary<string, FlagArgType> BuildGitRemoteSafeFlags() =>
        new Dictionary<string, FlagArgType>(StringComparer.OrdinalIgnoreCase)
        {
            ["-v"] = FlagArgType.None,
            ["--verbose"] = FlagArgType.None,
        }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// git merge-base 安全标志 — 对齐 TS GIT_READ_ONLY_COMMANDS["git merge-base"]
    /// </summary>
    private static FrozenDictionary<string, FlagArgType> BuildGitMergeBaseSafeFlags() =>
        new Dictionary<string, FlagArgType>(StringComparer.OrdinalIgnoreCase)
        {
            ["--is-ancestor"] = FlagArgType.None,
            ["--fork-point"] = FlagArgType.None,
            ["--octopus"] = FlagArgType.None,
            ["--independent"] = FlagArgType.None,
            ["--all"] = FlagArgType.None,
        }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// git rev-parse 安全标志 — 对齐 TS GIT_READ_ONLY_COMMANDS["git rev-parse"]
    /// </summary>
    private static FrozenDictionary<string, FlagArgType> BuildGitRevParseSafeFlags() =>
        new Dictionary<string, FlagArgType>(StringComparer.OrdinalIgnoreCase)
        {
            ["--verify"] = FlagArgType.None,
            ["--short"] = FlagArgType.Required,
            ["--abbrev-ref"] = FlagArgType.None,
            ["--symbolic"] = FlagArgType.None,
            ["--symbolic-full-name"] = FlagArgType.None,
            ["--show-toplevel"] = FlagArgType.None,
            ["--show-cdup"] = FlagArgType.None,
            ["--show-prefix"] = FlagArgType.None,
            ["--git-dir"] = FlagArgType.None,
            ["--git-common-dir"] = FlagArgType.None,
            ["--absolute-git-dir"] = FlagArgType.None,
            ["--show-superproject-working-tree"] = FlagArgType.None,
            ["--is-inside-work-tree"] = FlagArgType.None,
            ["--is-inside-git-dir"] = FlagArgType.None,
            ["--is-bare-repository"] = FlagArgType.None,
            ["--is-shallow-repository"] = FlagArgType.None,
            ["--is-shallow-update"] = FlagArgType.None,
            ["--path-prefix"] = FlagArgType.None,
        }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// git rev-list 安全标志 — 对齐 TS GIT_READ_ONLY_COMMANDS["git rev-list"]
    /// </summary>
    private static FrozenDictionary<string, FlagArgType> BuildGitRevListSafeFlags() =>
        MergeGitFlags(
            GitRefSelectionFlags(),
            GitDateFilterFlags(),
            GitCountFlags(),
            GitAuthorFilterFlags(),
            new Dictionary<string, FlagArgType>(StringComparer.OrdinalIgnoreCase)
            {
                ["--count"] = FlagArgType.None,
                ["--reverse"] = FlagArgType.None,
                ["--first-parent"] = FlagArgType.None,
                ["--ancestry-path"] = FlagArgType.None,
                ["--merges"] = FlagArgType.None,
                ["--no-merges"] = FlagArgType.None,
                ["--min-parents"] = FlagArgType.Required,
                ["--max-parents"] = FlagArgType.Required,
                ["--no-min-parents"] = FlagArgType.None,
                ["--no-max-parents"] = FlagArgType.None,
                ["--skip"] = FlagArgType.Required,
                ["--max-age"] = FlagArgType.Required,
                ["--min-age"] = FlagArgType.Required,
                ["--walk-reflogs"] = FlagArgType.None,
                ["--oneline"] = FlagArgType.None,
                ["--abbrev-commit"] = FlagArgType.None,
                ["--pretty"] = FlagArgType.Required,
                ["--format"] = FlagArgType.Required,
                ["--abbrev"] = FlagArgType.Optional,
                ["--full-history"] = FlagArgType.None,
                ["--dense"] = FlagArgType.None,
                ["--sparse"] = FlagArgType.None,
                ["--source"] = FlagArgType.None,
                ["--graph"] = FlagArgType.None,
            }
        ).ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// git describe 安全标志 — 对齐 TS GIT_READ_ONLY_COMMANDS["git describe"]
    /// </summary>
    private static FrozenDictionary<string, FlagArgType> BuildGitDescribeSafeFlags() =>
        new Dictionary<string, FlagArgType>(StringComparer.OrdinalIgnoreCase)
        {
            ["--tags"] = FlagArgType.None,
            ["--match"] = FlagArgType.Required,
            ["--exclude"] = FlagArgType.Required,
            ["--long"] = FlagArgType.None,
            ["--abbrev"] = FlagArgType.Optional,
            ["--always"] = FlagArgType.None,
            ["--contains"] = FlagArgType.None,
            ["--first-match"] = FlagArgType.None,
            ["--exact-match"] = FlagArgType.None,
            ["--candidates"] = FlagArgType.Required,
            ["--dirty"] = FlagArgType.None,
            ["--broken"] = FlagArgType.None,
        }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// git cat-file 安全标志 — 对齐 TS GIT_READ_ONLY_COMMANDS["git cat-file"]
    /// 故意排除 --batch（从 stdin 读取，可管道转储敏感对象）
    /// </summary>
    private static FrozenDictionary<string, FlagArgType> BuildGitCatFileSafeFlags() =>
        new Dictionary<string, FlagArgType>(StringComparer.OrdinalIgnoreCase)
        {
            ["-t"] = FlagArgType.None,
            ["-s"] = FlagArgType.None,
            ["-p"] = FlagArgType.None,
            ["-e"] = FlagArgType.None,
            ["--batch-check"] = FlagArgType.None,
            ["--allow-undetermined-type"] = FlagArgType.None,
        }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// git for-each-ref 安全标志 — 对齐 TS GIT_READ_ONLY_COMMANDS["git for-each-ref"]
    /// </summary>
    private static FrozenDictionary<string, FlagArgType> BuildGitForEachRefSafeFlags() =>
        new Dictionary<string, FlagArgType>(StringComparer.OrdinalIgnoreCase)
        {
            ["--format"] = FlagArgType.Required,
            ["--sort"] = FlagArgType.Required,
            ["--count"] = FlagArgType.Required,
            ["--contains"] = FlagArgType.Required,
            ["--no-contains"] = FlagArgType.Required,
            ["--merged"] = FlagArgType.Required,
            ["--no-merged"] = FlagArgType.Required,
            ["--points-at"] = FlagArgType.Required,
        }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// git grep 安全标志 — 对齐 TS GIT_READ_ONLY_COMMANDS["git grep"]
    /// </summary>
    private static FrozenDictionary<string, FlagArgType> BuildGitGrepSafeFlags() =>
        new Dictionary<string, FlagArgType>(StringComparer.OrdinalIgnoreCase)
        {
            ["-e"] = FlagArgType.Required,
            ["-E"] = FlagArgType.None,
            ["--extended-regexp"] = FlagArgType.None,
            ["-G"] = FlagArgType.None,
            ["--basic-regexp"] = FlagArgType.None,
            ["-F"] = FlagArgType.None,
            ["--fixed-strings"] = FlagArgType.None,
            ["-P"] = FlagArgType.None,
            ["--perl-regexp"] = FlagArgType.None,
            ["-i"] = FlagArgType.None,
            ["--ignore-case"] = FlagArgType.None,
            ["-v"] = FlagArgType.None,
            ["--invert-match"] = FlagArgType.None,
            ["-w"] = FlagArgType.None,
            ["--word-regexp"] = FlagArgType.None,
            ["-n"] = FlagArgType.None,
            ["--line-number"] = FlagArgType.None,
            ["-c"] = FlagArgType.None,
            ["--count"] = FlagArgType.None,
            ["-l"] = FlagArgType.None,
            ["--files-with-matches"] = FlagArgType.None,
            ["-L"] = FlagArgType.None,
            ["--files-without-match"] = FlagArgType.None,
            ["-h"] = FlagArgType.None,
            ["-H"] = FlagArgType.None,
            ["--heading"] = FlagArgType.None,
            ["--break"] = FlagArgType.None,
            ["--full-name"] = FlagArgType.None,
            ["--color"] = FlagArgType.None,
            ["--no-color"] = FlagArgType.None,
            ["-o"] = FlagArgType.None,
            ["--only-matching"] = FlagArgType.None,
            ["-A"] = FlagArgType.Required,
            ["--after-context"] = FlagArgType.Required,
            ["-B"] = FlagArgType.Required,
            ["--before-context"] = FlagArgType.Required,
            ["-C"] = FlagArgType.Required,
            ["--context"] = FlagArgType.Required,
            ["--and"] = FlagArgType.None,
            ["--or"] = FlagArgType.None,
            ["--not"] = FlagArgType.None,
            ["--max-depth"] = FlagArgType.Required,
            ["--untracked"] = FlagArgType.None,
            ["--no-index"] = FlagArgType.None,
            ["--recurse-submodules"] = FlagArgType.None,
            ["--cached"] = FlagArgType.None,
            ["--threads"] = FlagArgType.Required,
            ["-q"] = FlagArgType.None,
            ["--quiet"] = FlagArgType.None,
        }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// git stash show 安全标志 — 对齐 TS GIT_READ_ONLY_COMMANDS["git stash show"]
    /// </summary>
    private static FrozenDictionary<string, FlagArgType> BuildGitStashShowSafeFlags() =>
        MergeGitFlags(
            GitStatFlags(),
            GitColorFlags(),
            GitPatchFlags(),
            new Dictionary<string, FlagArgType>(StringComparer.OrdinalIgnoreCase)
            {
                ["--word-diff"] = FlagArgType.None,
                ["--word-diff-regex"] = FlagArgType.Required,
                ["--diff-filter"] = FlagArgType.Required,
                ["--abbrev"] = FlagArgType.Optional,
            }
        ).ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// git worktree list 安全标志 — 对齐 TS GIT_READ_ONLY_COMMANDS["git worktree list"]
    /// </summary>
    private static FrozenDictionary<string, FlagArgType> BuildGitWorktreeListSafeFlags() =>
        new Dictionary<string, FlagArgType>(StringComparer.OrdinalIgnoreCase)
        {
            ["--porcelain"] = FlagArgType.None,
            ["-v"] = FlagArgType.None,
            ["--verbose"] = FlagArgType.None,
            ["--expire"] = FlagArgType.Required,
        }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// git tag 安全标志 — 对齐 TS GIT_READ_ONLY_COMMANDS["git tag"]
    /// AdditionalDangerousCallback: 阻止位置参数创建标签（仅允许 -l/--list 后的位置参数）
    /// </summary>
    private static FrozenDictionary<string, FlagArgType> BuildGitTagSafeFlags() =>
        new Dictionary<string, FlagArgType>(StringComparer.OrdinalIgnoreCase)
        {
            ["-l"] = FlagArgType.None,
            ["--list"] = FlagArgType.None,
            ["-n"] = FlagArgType.Required,
            ["--contains"] = FlagArgType.Required,
            ["--no-contains"] = FlagArgType.Required,
            ["--merged"] = FlagArgType.Required,
            ["--no-merged"] = FlagArgType.Required,
            ["--sort"] = FlagArgType.Required,
            ["--format"] = FlagArgType.Required,
            ["--points-at"] = FlagArgType.Required,
            ["--column"] = FlagArgType.None,
            ["--no-column"] = FlagArgType.None,
            ["-i"] = FlagArgType.None,
            ["--ignore-case"] = FlagArgType.None,
        }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// git branch 安全标志 — 对齐 TS GIT_READ_ONLY_COMMANDS["git branch"]
    /// AdditionalDangerousCallback: 阻止位置参数创建分支（仅允许列表模式）
    /// </summary>
    private static FrozenDictionary<string, FlagArgType> BuildGitBranchSafeFlags() =>
        new Dictionary<string, FlagArgType>(StringComparer.OrdinalIgnoreCase)
        {
            ["-l"] = FlagArgType.None,
            ["--list"] = FlagArgType.None,
            ["-a"] = FlagArgType.None,
            ["--all"] = FlagArgType.None,
            ["-r"] = FlagArgType.None,
            ["--remotes"] = FlagArgType.None,
            ["-v"] = FlagArgType.None,
            ["-vv"] = FlagArgType.None,
            ["--verbose"] = FlagArgType.None,
            ["--color"] = FlagArgType.None,
            ["--no-color"] = FlagArgType.None,
            ["--column"] = FlagArgType.None,
            ["--no-column"] = FlagArgType.None,
            ["--abbrev"] = FlagArgType.Optional,
            ["--no-abbrev"] = FlagArgType.None,
            ["--contains"] = FlagArgType.Required,
            ["--no-contains"] = FlagArgType.Required,
            ["--merged"] = FlagArgType.Required,
            ["--no-merged"] = FlagArgType.Required,
            ["--points-at"] = FlagArgType.Required,
            ["--sort"] = FlagArgType.Required,
            ["--show-current"] = FlagArgType.None,
            ["-i"] = FlagArgType.None,
            ["--ignore-case"] = FlagArgType.None,
        }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// git cherry-pick 安全标志 — 对齐 TS GIT_READ_ONLY_COMMANDS["git cherry-pick"]
    /// 仅允许 --no-commit 预览模式和安全操作标志
    /// </summary>
    private static FrozenDictionary<string, FlagArgType> BuildGitCherryPickSafeFlags() =>
        new Dictionary<string, FlagArgType>(StringComparer.OrdinalIgnoreCase)
        {
            ["--no-commit"] = FlagArgType.None,
            ["-n"] = FlagArgType.None,
            ["--continue"] = FlagArgType.None,
            ["--abort"] = FlagArgType.None,
            ["--quit"] = FlagArgType.None,
        }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// git whatchanged 安全标志 — 对齐 TS GIT_READ_ONLY_COMMANDS["git whatchanged"]
    /// 与 git log 相同的标志组
    /// </summary>
    private static FrozenDictionary<string, FlagArgType> BuildGitWhatchangedSafeFlags() =>
        MergeGitFlags(
            GitLogDisplayFlags(),
            GitRefSelectionFlags(),
            GitDateFilterFlags(),
            GitCountFlags(),
            GitStatFlags()
        ).ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// git show-branch 安全标志 — 对齐 TS GIT_READ_ONLY_COMMANDS["git show-branch"]
    /// </summary>
    private static FrozenDictionary<string, FlagArgType> BuildGitShowBranchSafeFlags() =>
        new Dictionary<string, FlagArgType>(StringComparer.OrdinalIgnoreCase)
        {
            ["--all"] = FlagArgType.None,
            ["--remotes"] = FlagArgType.None,
            ["--more"] = FlagArgType.Required,
            ["--less"] = FlagArgType.Required,
            ["--sha1-name"] = FlagArgType.None,
            ["--no-name"] = FlagArgType.None,
            ["--list"] = FlagArgType.None,
            ["--topo-order"] = FlagArgType.None,
            ["--date-order"] = FlagArgType.None,
            ["--sparse"] = FlagArgType.None,
        }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// git verify-pack 安全标志 — 对齐 TS GIT_READ_ONLY_COMMANDS["git verify-pack"]
    /// </summary>
    private static FrozenDictionary<string, FlagArgType> BuildGitVerifyPackSafeFlags() =>
        new Dictionary<string, FlagArgType>(StringComparer.OrdinalIgnoreCase)
        {
            ["-v"] = FlagArgType.None,
            ["--verbose"] = FlagArgType.None,
            ["-s"] = FlagArgType.None,
            ["--stat-only"] = FlagArgType.None,
            ["-o"] = FlagArgType.Required,
            ["--object-format"] = FlagArgType.Required,
        }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// git annotate 安全标志 — 与 git blame 相同
    /// </summary>
    private static FrozenDictionary<string, FlagArgType> BuildGitAnnotateSafeFlags() =>
        BuildGitBlameSafeFlags();

    /// <summary>
    /// git name-rev 安全标志 — 对齐 TS GIT_READ_ONLY_COMMANDS["git name-rev"]
    /// </summary>
    private static FrozenDictionary<string, FlagArgType> BuildGitNameRevSafeFlags() =>
        new Dictionary<string, FlagArgType>(StringComparer.OrdinalIgnoreCase)
        {
            ["--name-only"] = FlagArgType.None,
            ["--tags"] = FlagArgType.None,
            ["--refs"] = FlagArgType.Required,
            ["--no-undefined"] = FlagArgType.None,
            ["--undefined"] = FlagArgType.None,
            ["--always"] = FlagArgType.None,
            ["--exclude"] = FlagArgType.Required,
        }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

    #endregion

    #region Git 危险回调

    /// <summary>
    /// git reflog 危险回调 — 阻止 expire/delete/exists 子命令（写入 .git/logs/**）
    /// </summary>
    private static bool CheckGitReflogDangerous(string command, IReadOnlyList<string> args)
    {
        return args.Count > 0 &&
            (args[0].Equals("expire", StringComparison.OrdinalIgnoreCase) ||
             args[0].Equals("delete", StringComparison.OrdinalIgnoreCase) ||
             args[0].Equals("exists", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// git remote show 危险回调 — 位置参数必须是字母数字远程名
    /// </summary>
    private static bool CheckGitRemoteShowDangerous(string command, IReadOnlyList<string> args)
    {
        var positionArgs = args.Where(a => !a.StartsWith('-')).ToList();
        if (positionArgs.Count != 1) return true;
        return !Regex.IsMatch(positionArgs[0], @"^[a-zA-Z0-9_-]+$");
    }

    /// <summary>
    /// git remote 危险回调 — 仅允许裸命令或 -v/--verbose，阻止任何位置参数
    /// </summary>
    private static bool CheckGitRemoteDangerous(string command, IReadOnlyList<string> args)
    {
        var nonFlagArgs = args.Where(a => !a.StartsWith('-')).ToList();
        if (nonFlagArgs.Count > 0) return true;
        return false;
    }

    /// <summary>
    /// git tag 危险回调 — 阻止位置参数创建标签（仅允许 -l/--list 后的位置参数）
    /// </summary>
    private static bool CheckGitTagDangerous(string command, IReadOnlyList<string> args)
    {
        var hasList = args.Any(a => a.Equals("-l", StringComparison.OrdinalIgnoreCase) ||
                                     a.Equals("--list", StringComparison.OrdinalIgnoreCase));
        if (!hasList)
        {
            var positionArgs = args.Where(a => !a.StartsWith('-')).ToList();
            if (positionArgs.Count > 0) return true;
        }
        return false;
    }

    /// <summary>
    /// git branch 危险回调 — 阻止位置参数创建分支（仅允许列表模式）
    /// </summary>
    private static bool CheckGitBranchDangerous(string command, IReadOnlyList<string> args)
    {
        var hasList = args.Any(a => a.Equals("-l", StringComparison.OrdinalIgnoreCase) ||
                                     a.Equals("--list", StringComparison.OrdinalIgnoreCase) ||
                                     a.Equals("-a", StringComparison.OrdinalIgnoreCase) ||
                                     a.Equals("--all", StringComparison.OrdinalIgnoreCase) ||
                                     a.Equals("-r", StringComparison.OrdinalIgnoreCase) ||
                                     a.Equals("--remotes", StringComparison.OrdinalIgnoreCase) ||
                                     a.Equals("--show-current", StringComparison.OrdinalIgnoreCase) ||
                                     a.Equals("--contains", StringComparison.OrdinalIgnoreCase) ||
                                     a.Equals("--no-contains", StringComparison.OrdinalIgnoreCase) ||
                                     a.Equals("--merged", StringComparison.OrdinalIgnoreCase) ||
                                     a.Equals("--no-merged", StringComparison.OrdinalIgnoreCase));
        if (!hasList)
        {
            var positionArgs = args.Where(a => !a.StartsWith('-')).ToList();
            if (positionArgs.Count > 0) return true;
        }
        return false;
    }

    #endregion
}
