namespace IO.ProcessService;

/// <summary>
/// ProcessStartInfo 统一构建器 — 强制三道防线 + 统一编码，消除散弹式 new ProcessStartInfo
/// <para>
/// 三道防线：
/// 1. <b>参数白名单校验</b> — <see cref="CommandArgumentValidator"/> 黑名单拦截 shell 元字符
/// 2. <b>参数化启动</b> — <see cref="ProcessStartInfo.ArgumentList"/> 优先于 <see cref="ProcessStartInfo.Arguments"/>
/// 3. <b>执行环境限制</b> — UseShellExecute=false + CreateNoWindow=true
/// </para>
/// <para>
/// 统一编码：options 显式指定编码优先，null 时回退到 <see cref="IProcessEncodingProvider"/> 全局编码。
/// </para>
/// <para>
/// 使用方式：DI 注入此构建器，调用 <see cref="Build(ProcessOptions)"/> 或 <see cref="BuildInteractive(InteractiveProcessOptions)"/>，
/// 禁止在生产代码中直接 <c>new ProcessStartInfo</c>。
/// </para>
/// </summary>
public sealed class ProcessStartInfoBuilder
{
    private readonly IProcessEncodingProvider _encodingProvider;

    /// <summary>
    /// 创建 ProcessStartInfo 统一构建器
    /// </summary>
    /// <param name="encodingProvider">进程编码统一管理器（DI 单例）</param>
    public ProcessStartInfoBuilder(IProcessEncodingProvider encodingProvider)
    {
        _encodingProvider = encodingProvider ?? throw new ArgumentNullException(nameof(encodingProvider));
    }

    /// <summary>
    /// 从 <see cref="ProcessOptions"/> 构建 <see cref="ProcessStartInfo"/> — 一次性执行模式
    /// </summary>
    public ProcessStartInfo Build(ProcessOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (!options.SkipArgumentValidation)
        {
            CommandArgumentValidator.ValidateList(options.ArgumentList);
            if (options.ArgumentList is null)
                CommandArgumentValidator.ValidateString(options.Arguments);
        }

        var psi = new ProcessStartInfo
        {
            FileName = options.FileName,
            WorkingDirectory = options.WorkingDirectory ?? string.Empty,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = options.RedirectStandardOutput,
            RedirectStandardError = options.RedirectStandardError,
            StandardOutputEncoding = options.StandardOutputEncoding ?? _encodingProvider.Output,
            StandardErrorEncoding = options.StandardErrorEncoding ?? _encodingProvider.Error,
        };

        if (options.ArgumentList is { Count: > 0 })
        {
            foreach (var arg in options.ArgumentList)
                psi.ArgumentList.Add(arg);
        }
        else
        {
            psi.Arguments = options.Arguments;
        }

        if (options.EnvironmentVariables is not null)
        {
            foreach (var (key, value) in options.EnvironmentVariables)
                psi.EnvironmentVariables[key] = value;
        }

        return psi;
    }

    /// <summary>
    /// 从 <see cref="InteractiveProcessOptions"/> 构建 <see cref="ProcessStartInfo"/> — 交互式进程模式
    /// </summary>
    public ProcessStartInfo BuildInteractive(InteractiveProcessOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (!options.SkipArgumentValidation)
        {
            CommandArgumentValidator.ValidateList(options.ArgumentList);
            if (options.ArgumentList is null)
                CommandArgumentValidator.ValidateString(options.Arguments);
        }

        var psi = new ProcessStartInfo
        {
            FileName = options.FileName,
            WorkingDirectory = options.WorkingDirectory ?? string.Empty,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardInput = true,
            RedirectStandardError = options.RedirectStandardError,
            StandardOutputEncoding = options.StandardOutputEncoding ?? _encodingProvider.Output,
            StandardErrorEncoding = options.StandardErrorEncoding ?? _encodingProvider.Error,
            StandardInputEncoding = options.StandardInputEncoding ?? _encodingProvider.Input,
        };

        if (options.ArgumentList is { Count: > 0 })
        {
            foreach (var arg in options.ArgumentList)
                psi.ArgumentList.Add(arg);
        }
        else
        {
            psi.Arguments = options.Arguments;
        }

        if (options.EnvironmentVariables is not null)
        {
            foreach (var (key, value) in options.EnvironmentVariables)
                psi.EnvironmentVariables[key] = value;
        }

        return psi;
    }

    /// <summary>
    /// 构建 UseShellExecute=true 的 ProcessStartInfo — 仅用于打开 URL/文件/目录（启动即忘模式）
    /// <para>此模式不经过参数校验和 ArgumentList，因为 UseShellExecute=true 时 ArgumentList 不可用</para>
    /// </summary>
    public ProcessStartInfo BuildShellOpen(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return new ProcessStartInfo
        {
            FileName = path,
            UseShellExecute = true,
        };
    }
}
