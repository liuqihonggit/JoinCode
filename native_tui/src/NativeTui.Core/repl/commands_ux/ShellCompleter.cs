namespace NativeTui.Core.Repl;

/// <summary>shell 命令补全器（TASK074 D3 — 对齐基准 useTypeahead.tsx:915 getShellCompletions）：
/// bash mode 下从 PATH 扫描可执行文件，按输入前缀过滤返回 CommandSuggestion 列表。
/// 构造时扫描 PATH + 缓存（避免每次补全扫描）；PATH 变更不热更新（会话级快照）。
/// 纯数据组件：不做键位/渲染决策，ReplShell 持有并在 UpdateSuggestions 按 mode 切换。</summary>
public sealed class ShellCompleter
{
    private readonly List<string> _commands;

    /// <summary>构造 — 从 PATH 环境变量扫描可执行文件（会话级快照）。</summary>
    /// <param name="pathDirs">PATH 目录列表（null = 从 Environment.GetEnvironmentVariable("PATH") 解析）。</param>
    public ShellCompleter(IEnumerable<string>? pathDirs = null)
    {
        var dirs = pathDirs ?? ResolvePathDirs();
        _commands = ScanCommands(dirs);
    }

    /// <summary>已缓存命令数（测试断言面）。</summary>
    public int Count => _commands.Count;

    /// <summary>按输入前缀补全 shell 命令 — 返回 CommandSuggestion 列表（Name=命令名，Description=空）。
    /// 取首个 token（命令名 — 空格前部分）按前缀过滤；空输入返回空列表。</summary>
    public IReadOnlyList<CommandSuggestion> Complete(string input)
    {
        if (input.Length == 0)
        {
            return [];
        }

        var cmd = input.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries)[0];
        if (cmd.Length == 0)
        {
            return [];
        }

        return _commands
            .Where(c => c.StartsWith(cmd, StringComparison.OrdinalIgnoreCase))
            .Take(20)
            .Select(c => new CommandSuggestion(c, string.Empty, [], false))
            .ToList();
    }

    /// <summary>从 PATH 环境变量解析目录列表。</summary>
    private static List<string> ResolvePathDirs()
    {
        var path = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        return path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries).ToList();
    }

    /// <summary>扫描 PATH 目录下可执行文件名（去重 + 排序；IO 异常静默跳过）。</summary>
    private static List<string> ScanCommands(IEnumerable<string> dirs)
    {
        var commands = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var dir in dirs)
        {
            try
            {
                if (!Directory.Exists(dir))
                {
                    continue;
                }

                foreach (var file in Directory.EnumerateFiles(dir))
                {
                    var name = Path.GetFileName(file);
                    if (name.Length > 0)
                    {
                        commands.Add(name);
                    }
                }
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        return commands.OrderBy(static c => c).ToList();
    }
}
