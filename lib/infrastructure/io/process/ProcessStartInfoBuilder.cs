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
public sealed class ProcessStartInfoBuilder : IProcessStartInfoBuilder
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
            if (options.ArgumentList.Count == 0)
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

        if (options.ArgumentList.Count > 0)
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
            if (options.ArgumentList.Count == 0)
                CommandArgumentValidator.ValidateString(options.Arguments);
        }

        var inputEncoding = options.StandardInputEncoding ?? _encodingProvider.Input;
        var outputEncoding = options.StandardOutputEncoding ?? _encodingProvider.Output;
        var errorEncoding = options.StandardErrorEncoding ?? _encodingProvider.Error;

        ValidateNoBomEncoding(inputEncoding, nameof(options.StandardInputEncoding));
        ValidateNoBomEncoding(outputEncoding, nameof(options.StandardOutputEncoding));
        ValidateNoBomEncoding(errorEncoding, nameof(options.StandardErrorEncoding));

        var psi = new ProcessStartInfo
        {
            FileName = options.FileName,
            WorkingDirectory = options.WorkingDirectory ?? string.Empty,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardInput = true,
            RedirectStandardError = options.RedirectStandardError,
            StandardOutputEncoding = outputEncoding,
            StandardErrorEncoding = errorEncoding,
            StandardInputEncoding = inputEncoding,
        };

        if (options.ArgumentList.Count > 0)
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

    /// <summary>
    /// 防御性检查：禁止带 BOM 的编码用于进程管道 I/O。
    /// </summary>
    /// <remarks>
    /// <b>根因</b>：<see cref="System.Text.Encoding.UTF8"/> 默认带 BOM 前缀（EF BB BF）。
    /// 当通过管道写入外部进程 stdin 时，对方收到 BOM 字节后无法识别为合法输入，静默退出。
    /// 典型受害者：csharp-ls、typescript-language-server 等 stdin 驱动的 CLI 工具。
    /// <para>
    /// <b>修复</b>：用 <c>new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)</c> 替代 <see cref="System.Text.Encoding.UTF8"/>。
    /// </para>
    /// </remarks>
    /// <param name="encoding">待检查的编码</param>
    /// <param name="paramName">参数名（用于异常消息）</param>
    /// <exception cref="ArgumentException">编码带 BOM 前缀时抛出</exception>
    private static void ValidateNoBomEncoding(Encoding? encoding, string paramName)
    {
        if (encoding is null) return;
        if (encoding.Preamble.Length == 0) return;

        throw new ArgumentException(
            $"编码 '{encoding.EncodingName}' 带 BOM 前缀（{encoding.Preamble.Length} 字节），" +
            "禁止用于进程管道 I/O。BOM 字节会导致外部进程（如 csharp-ls）收到非法输入后静默退出。" +
            "请改用无 BOM 的 UTF-8：new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)。" +
            "详见 ADR: Encoding.UTF8 带 BOM 破坏管道通信。",
            paramName);
    }
}
