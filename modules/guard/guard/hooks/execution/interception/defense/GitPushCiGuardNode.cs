namespace Core.Hooks.Execution.Interception.Defense;

/// <summary>
/// git push CI 守卫 node — 检测 git push 命令，查对应分支 GitHub CI 是否 in_progress。
/// <para>
/// 动机：仓库 ci.yml 配置 cancel-in-progress: true，push 新 commit 会取消正在跑的 CI run。
/// 此 node 在命令执行前拦截，避免 AI 在 CI 运行期间 push 导致 run 被取消。
/// 拦截后提示 AI 请求用户确认，而非自动放行。
/// </para>
/// </summary>
[Register(typeof(GitPushCiGuardNode), ServiceLifetime.Singleton)]
public sealed class GitPushCiGuardNode {
    private readonly ICiRunStatusChecker _ciStatusChecker;

    /// <summary>
    /// 构造 git push CI 守卫 node。
    /// </summary>
    /// <param name="ciStatusChecker">CI 运行状态检查器（查 GitHub Actions in_progress run）</param>
    public GitPushCiGuardNode(ICiRunStatusChecker ciStatusChecker) {
        _ciStatusChecker = ciStatusChecker ?? throw new ArgumentNullException(nameof(ciStatusChecker));
    }

    /// <summary>
    /// 检测 git push 命令，查对应分支 CI 是否在跑。
    /// </summary>
    /// <param name="command">原始命令字符串</param>
    /// <param name="workingDirectory">git 工作目录</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>CI 在跑时返回结果（含分支+run 数量）；非 git push 或 CI 没在跑返回 null</returns>
    public async ValueTask<GitPushCiGuardResult?> CheckGitPushAsync(string command, string workingDirectory, CancellationToken ct) {
        var branch = ExtractPushBranch(command);
        if (branch is null) return null;

        var status = await _ciStatusChecker.CheckBranchCiStatusAsync(workingDirectory, branch, ct).ConfigureAwait(false);
        if (!status.IsRunning) return null;

        return new GitPushCiGuardResult(status.Branch, status.RunCount, status.RunUrl);
    }

    /// <summary>
    /// 提取 git push 的目标分支。
    /// </summary>
    /// <param name="command">命令字符串</param>
    /// <returns>非 git push 返回 null；git push 无显式分支返回空字符串（表示用当前分支）；否则返回分支名</returns>
    internal static string? ExtractPushBranch(string command) {
        var span = command.AsSpan();
        var pushIdx = FindGitPush(span);
        if (pushIdx < 0) return null;

        var afterPush = span[pushIdx..];
        var cmdEnd = FindCommandEnd(afterPush);
        var argsSpan = afterPush[..cmdEnd].Trim();
        return ParseBranchFromArgs(argsSpan);
    }

    /// <summary>找 "git push" 或 "git.exe push" 的起始索引，返回 push 后参数的起始位置；未找到返回 -1。</summary>
    private static int FindGitPush(ReadOnlySpan<char> span) {
        const string gitPush = "git push";
        const string gitExePush = "git.exe push";
        var idx = span.IndexOf(gitPush.AsSpan(), StringComparison.OrdinalIgnoreCase);
        if (idx >= 0) return idx + gitPush.Length;
        idx = span.IndexOf(gitExePush.AsSpan(), StringComparison.OrdinalIgnoreCase);
        if (idx >= 0) return idx + gitExePush.Length;
        return -1;
    }

    /// <summary>找命令结束位置（&amp;&amp; / | / ; / 行尾），返回相对索引。</summary>
    private static int FindCommandEnd(ReadOnlySpan<char> span) {
        var idx = span.IndexOf("&&".AsSpan(), StringComparison.Ordinal);
        if (idx >= 0) return idx;
        idx = span.IndexOf('|');
        if (idx >= 0) return idx;
        idx = span.IndexOf(';');
        return idx >= 0 ? idx : span.Length;
    }

    /// <summary>
    /// 从 git push 参数解析目标分支。
    /// <para>
    /// git push 语法：git push [选项] [remote] [branch] / git push [选项] [remote] [local]:[remote]
    /// 0 个非选项参数 → 当前分支(""); 1 个 → remote，当前分支(""); 2+ → 第二个是分支引用
    /// </para>
    /// </summary>
    private static string ParseBranchFromArgs(ReadOnlySpan<char> args) {
        if (args.IsEmpty) return "";
        var nonOptionArgs = new List<string>();
        var reader = new SpanTokenReader(args);
        while (reader.TryReadNext(out var token)) {
            if (token.StartsWith('-')) continue;
            nonOptionArgs.Add(token.ToString());
        }
        if (nonOptionArgs.Count < 2) return "";
        var branchRef = nonOptionArgs[1];
        var colonIdx = branchRef.IndexOf(':');
        return colonIdx >= 0 ? branchRef[(colonIdx + 1)..] : branchRef;
    }

    /// <summary>Span token 读取器 — 按空格分割，支持引号包裹的 token。</summary>
    private ref struct SpanTokenReader(ReadOnlySpan<char> source) {
        private readonly ReadOnlySpan<char> _source = source;
        private int _pos = 0;

        /// <summary>读取下一个 token，跳过前导空格，支持引号包裹。</summary>
        /// <param name="token">输出的 token 字符串</param>
        /// <returns>成功读取返回 true；无更多 token 返回 false</returns>
        public bool TryReadNext(out string token) {
            while (_pos < _source.Length && char.IsWhiteSpace(_source[_pos])) _pos++;
            if (_pos >= _source.Length) { token = ""; return false; }
            var start = _pos;
            if (_source[_pos] is '"' or '\'') {
                var quote = _source[_pos];
                _pos++;
                start = _pos;
                while (_pos < _source.Length && _source[_pos] != quote) _pos++;
                token = _source[start.._pos].ToString();
                if (_pos < _source.Length) _pos++;
                return true;
            }
            while (_pos < _source.Length && !char.IsWhiteSpace(_source[_pos])) _pos++;
            token = _source[start.._pos].ToString();
            return true;
        }
    }
}

/// <summary>
/// git push CI 守卫结果 — CI 在跑时返回。
/// </summary>
/// <param name="Branch">CI 在跑的分支名</param>
/// <param name="RunCount">in_progress 的 run 数量</param>
/// <param name="RunUrl">最新 run 的 URL（可选）</param>
public record GitPushCiGuardResult(string Branch, int RunCount, string? RunUrl);
