namespace NativeTui.Core.Repl.Dialogs;

/// <summary>搜索命中 — 文件（相对搜索根归一）+ 行号 + 行文本（基准 GlobalSearchDialog.tsx 的 Match 三元组）。</summary>
/// <param name="File">命中文件路径（相对搜索根；越出根目录时保留 rg 原样输出）。</param>
/// <param name="Line">命中行号（1 起 — rg --line-number 产出）。</param>
/// <param name="Text">命中行文本（换行已剥离的原文）。</param>
public sealed record SearchMatch(string File, int Line, string Text);

/// <summary>
/// 搜索源接缝 — 全局搜索对话框与 rg 执行器解耦的窄接口（对齐 IReplEngine 注入先例）：
/// 宿主/测试注入假结果流驱动对话框状态机；生产默认 <see cref="RipgrepSearcher"/>。
/// </summary>
public interface ISearchSource
{
    /// <summary>搜索后端是否可用（不可用 → 对话框降级空结果，不报错不打扰）。</summary>
    bool IsAvailable { get; }

    /// <summary>流式执行搜索 — 逐命中产出；消费者提前 break 或取消令牌触发即终止后端进程。</summary>
    /// <param name="rootPath">搜索根目录（建议绝对路径 — 命中文件相对该根归一显示）。</param>
    /// <param name="query">查询文本（固定字符串字面量搜索，非正则 — 基准 -F 语义）。</param>
    /// <param name="cancellationToken">取消令牌（防抖重触发场景杀旧搜索进程树）。</param>
    /// <returns>命中流（后端不可用/启动失败 → 空流静默降级）。</returns>
    IAsyncEnumerable<SearchMatch> ExecuteAsync(string rootPath, string query, CancellationToken cancellationToken = default);
}

/// <summary>
/// rg 流式搜索执行器 — 对齐基准 ripgrep.ts ripGrepStream（流式逐行产出，首结果先于进程退出
/// 到达）+ GlobalSearchDialog.tsx 的参数面（--line-number --no-heading --smart-case
/// --max-count --fixed-strings --regexp + --glob 排除构建产物目录）。进程封装对齐
/// BashDirectExecutor 先例（ArgumentList 免拼接转义 + UTF-8 双流解码 + CreateNoWindow +
/// 取消杀进程树无超时等待），差异在整体收集 → ReadLineAsync 循环流式产出。rg 退出码
/// 0=命中 1=无命中均静默（退出码非 0 不视为错误 — 基准 "code === 0 || code === 1 都是成功"
/// 语义）；rg 缺失降级 IsAvailable=false（--version 探测一次缓存），调用方拿空流而非异常。
/// 命中路径经 <see cref="RelativizePath"/> 相对搜索根归一（越出根保留原样 — 基准
/// relativePath + startsWith("..") 语义）。
/// </summary>
public sealed partial class RipgrepSearcher : ISearchSource
{
    /// <summary>默认可执行名（PATH 解析 rg — 基准 getRipgrepConfig 系统模式 command='rg' 防路径劫持语义）。</summary>
    public const string ExecutableName = "rg";

    /// <summary>单文件命中行上限（任务书 --max-count 50 — rg 输出量界；总量上限由对话框收集侧钳制）。</summary>
    public const int MaxCountPerFile = 50;

    /// <summary>排除目录（.git/依赖缓存/构建产物 — 否定 glob 形态附加，rg 遍历剪枝）。</summary>
    public static readonly string[] ExcludedDirectories = [".git", "node_modules", "bin", "obj", "artifacts"];

    private readonly string _executableFileName;

    /// <summary>探测缓存（null = 未探测；--version 启动成功即 true，启动异常即 false）。</summary>
    private bool? _isAvailable;

    /// <summary>创建搜索器 — executableFileName 为 rg 可执行名/路径（测试注入缺失名验证降级路径）。</summary>
    /// <param name="executableFileName">rg 可执行名/路径（默认 PATH 解析的 "rg"）。</param>
    public RipgrepSearcher(string executableFileName = ExecutableName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executableFileName);
        _executableFileName = executableFileName;
    }

    /// <summary>rg 是否可用 — 首次访问探测（--version 一次）后缓存；可执行缺失/权限拒绝/平台
    /// 不支持等启动失败形态均 false 不抛（--version 输出量远小于管道缓冲，不读不堵）。</summary>
    public bool IsAvailable => _isAvailable ??= ProbeAvailability();

    /// <summary>流式执行搜索 — 参数即时校验（迭代器壳防延迟校验陷阱）；核心见 <see cref="ExecuteCoreAsync"/>。</summary>
    /// <param name="rootPath">搜索根目录。</param>
    /// <param name="query">查询文本。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>命中流（后端不可用/启动失败 → 空流）。</returns>
    public IAsyncEnumerable<SearchMatch> ExecuteAsync(string rootPath, string query, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(query);
        return ExecuteCoreAsync(rootPath, query, cancellationToken);
    }

    /// <summary>构建 rg 参数序列（纯函数 — 单测直接断言；搜索根路径由进程启动方附加在末位）。</summary>
    /// <param name="query">查询文本（--fixed-strings 字面量 — 免正则转义防注入）。</param>
    /// <returns>ArgumentList 元素序列（不含搜索根路径）。</returns>
    public static IReadOnlyList<string> BuildArguments(string query)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(query);
        List<string> arguments =
        [
            "--line-number",
            "--no-heading",
            "--smart-case",
            "--max-count",
            MaxCountPerFile.ToString(),
            "--fixed-strings",
            "--regexp",
            query,
        ];
        foreach (var directory in ExcludedDirectories)
        {
            arguments.Add("--glob");
            arguments.Add($"!{directory}");
        }

        return arguments;
    }

    /// <summary>解析 rg 输出行（纯函数 — 基准 parseRipgrepLine 原样迁移）："path:line:text"
    /// 懒惰匹配到首个 :数字: — Windows 盘符路径（C:\…）安全（首个冒号后是反斜杠非数字，
    /// 回溯扩展路径段）；非命中形态返回 null（静默跳过）；text 可为空（空行命中）。</summary>
    /// <param name="line">rg 单行输出（换行已剥离）。</param>
    /// <returns>命中（File 为 rg 原样输出路径）；行号非数字/文件段空/不匹配 → null。</returns>
    public static SearchMatch? ParseRipgrepLine(string line)
    {
        if (line.Length == 0)
        {
            return null;
        }

        var match = MatchLinePattern().Match(line);
        if (!match.Success || match.Groups[1].Value.Length == 0)
        {
            return null;
        }

        return int.TryParse(match.Groups[2].ValueSpan, out var lineNumber)
            ? new SearchMatch(match.Groups[1].Value, lineNumber, match.Groups[3].Value)
            : null;
    }

    /// <summary>命中路径相对搜索根归一（纯函数 — 基准 relativePath + startsWith('..') 语义）：
    /// 根内命中转相对路径（对话框紧凑显示）；越出根/跨盘符（GetRelativePath 的 .. 回退形态）
    /// 保留原样；路径归一异常宽容原样（rg 输出已是非空路径，防御性兜底）。</summary>
    /// <param name="filePath">rg 输出的文件路径。</param>
    /// <param name="rootPath">搜索根目录。</param>
    /// <returns>相对路径（根内）或原样路径（越出根/归一失败）。</returns>
    public static string RelativizePath(string filePath, string rootPath)
    {
        try
        {
            var relative = Path.GetRelativePath(rootPath, filePath);
            return relative.StartsWith("..", StringComparison.Ordinal) ? filePath : relative;
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException)
        {
            return filePath;
        }
    }

    /// <summary>核心流式执行（迭代器） — 后端不可用/启动失败空流降级（启动失败同步回写探测缓存）；
    /// 逐行解析产出（含路径相对化）；finally 杀进程树（取消/消费者提前 break 防孤儿 rg）+
    /// 等收尾 + 排空 stderr（双流防管道死锁 — Kill 后管道关闭读取自然完成，无超时等待）。</summary>
    private async IAsyncEnumerable<SearchMatch> ExecuteCoreAsync(
        string rootPath,
        string query,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        if (!IsAvailable)
        {
            yield break;
        }

        var started = TryStartProcess(rootPath, query);
        if (started is null)
        {
            _isAvailable = false;
            yield break;
        }

        using var process = started;
        var stderrTask = process.StandardError.ReadToEndAsync(CancellationToken.None);
        try
        {
            while (true)
            {
                var line = await process.StandardOutput.ReadLineAsync(cancellationToken).ConfigureAwait(false);
                if (line is null)
                {
                    break;
                }

                var match = ParseRipgrepLine(line);
                if (match is not null)
                {
                    yield return match with { File = RelativizePath(match.File, rootPath) };
                }
            }
        }
        finally
        {
            if (!process.HasExited)
            {
                KillProcessTree(process);
            }

            await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
            await stderrTask.ConfigureAwait(false);
        }
    }

    /// <summary>启动 rg 进程 — 搜索根附加在参数末位（rg 非交互模式需显式路径目标）；失败返回 null。</summary>
    private Process? TryStartProcess(string rootPath, string query)
    {
        try
        {
            return StartProcess(rootPath, query);
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception
            or InvalidOperationException
            or PlatformNotSupportedException
            or FileNotFoundException)
        {
            return null;
        }
    }

    /// <summary>启动 rg 进程（抛出形态 — 启动失败异常向上传播由调用方降级）。</summary>
    private Process StartProcess(string rootPath, string query)
    {
        var psi = CreateBaseStartInfo();
        foreach (var argument in BuildArguments(query))
        {
            psi.ArgumentList.Add(argument);
        }

        psi.ArgumentList.Add(rootPath);
        return Process.Start(psi) ?? throw new InvalidOperationException($"rg 进程启动失败：{_executableFileName}");
    }

    /// <summary>探测 rg 可用性 — --version 启动成功且退出码 0 即可用；启动失败形态一律 false 不抛。</summary>
    private bool ProbeAvailability()
    {
        try
        {
            var psi = CreateBaseStartInfo();
            psi.ArgumentList.Add("--version");
            using var process = Process.Start(psi)
                ?? throw new InvalidOperationException($"rg 探测进程启动失败：{_executableFileName}");
            process.WaitForExit();
            return process.ExitCode == 0;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception
            or InvalidOperationException
            or PlatformNotSupportedException
            or FileNotFoundException)
        {
            return false;
        }
    }

    /// <summary>rg 进程启动信息基座 — ArgumentList 免拼接转义 + 双流重定向 + UTF-8 解码 + 无窗口（BashDirectExecutor 先例）。</summary>
    private ProcessStartInfo CreateBaseStartInfo() => new(_executableFileName)
    {
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        UseShellExecute = false,
        CreateNoWindow = true,
        StandardOutputEncoding = Encoding.UTF8,
        StandardErrorEncoding = Encoding.UTF8,
    };

    /// <summary>杀进程树 — 进程已退出/关联失败时静默（取消路径尽力而为，对齐 BashDirectExecutor）。</summary>
    private static void KillProcessTree(Process process)
    {
        try
        {
            process.Kill(entireProcessTree: true);
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
        }
    }

    /// <summary>rg 输出行解析正则（懒惰路径段到首个 :数字: — Windows 盘符安全；GeneratedRegex AOT 源生成）。</summary>
    [System.Text.RegularExpressions.GeneratedRegex("^(.*?):(\\d+):(.*)$", System.Text.RegularExpressions.RegexOptions.CultureInvariant)]
    private static partial System.Text.RegularExpressions.Regex MatchLinePattern();
}
