namespace JoinCode.Entry.Startup;

/// <summary>
/// 原生 DLL 引导器 — 从嵌入资源释放原生 DLL 到临时目录，并通过 SetDllDirectory 添加搜索路径。
/// 必须在 Main 最开始、任何 P-Invoke 调用之前调用 <see cref="Initialize"/>。
/// </summary>
/// <remarks>
/// NativeAOT 编译时，原生 C/C++ DLL（SkiaSharp/pdfium/tree-sitter）无法静态链接进 exe，
/// 需作为 EmbeddedResource 嵌入，运行时释放到 %TEMP%\jcc-native\ 并通过 SetDllDirectoryW
/// 添加到 DLL 搜索路径，使第三方 P-Invoke 的 LoadLibrary 自动找到。
/// </remarks>
internal static class NativeDllBootstrapper
{
    private const string ResourcePrefix = "JoinCode.NativeDlls.";
    private const string RuntimeSubDir = "runtime";

    private static int _initialized; // 0=未初始化, 1=已初始化

    /// <summary>
    /// 从嵌入资源释放原生 DLL 到临时目录并注册 DLL 搜索路径。幂等，多次调用安全。
    /// </summary>
    public static void Initialize()
    {
        if (Interlocked.Exchange(ref _initialized, 1) != 0)
            return;

        if (!OperatingSystem.IsWindows())
            return;

        try
        {
            var asm = System.Reflection.Assembly.GetExecutingAssembly();
            var resourceNames = asm.GetManifestResourceNames();

            var nativeDlls = Array.FindAll(resourceNames, n => n.StartsWith(ResourcePrefix, StringComparison.Ordinal));
            if (nativeDlls.Length == 0)
                return;

            var targetDir = AppDataConstants.Paths.RuntimeDirectory;
            Directory.CreateDirectory(targetDir);

            foreach (var resourceName in nativeDlls)
            {
                var fileName = resourceName[ResourcePrefix.Length..];
                var targetPath = Path.Combine(targetDir, fileName);

                if (ShouldExtract(asm, resourceName, targetPath))
                    ExtractResource(asm, resourceName, targetPath);
            }

            SetDllDirectory(targetDir);
        }
        catch (Exception ex)
        {
            // 释放失败不阻塞启动 — 原生库可能已在 exe 同目录（开发模式）
            Diag.WriteLine($"[NativeDll] 释放失败: {ex.Message}");
        }
    }

    /// <summary>
    /// 判断是否需要释放：目标文件不存在或大小不匹配时释放。
    /// </summary>
    private static bool ShouldExtract(System.Reflection.Assembly asm, string resourceName, string targetPath)
    {
        if (!File.Exists(targetPath))
            return true;

        try
        {
            using var stream = asm.GetManifestResourceStream(resourceName);
            if (stream is null)
                return false;

            var existingLength = new FileInfo(targetPath).Length;
            return stream.Length != existingLength;
        }
        catch
        {
            return true;
        }
    }

    /// <summary>
    /// 从嵌入资源释放单个 DLL 到目标路径。
    /// </summary>
    private static void ExtractResource(System.Reflection.Assembly asm, string resourceName, string targetPath)
    {
        using var stream = asm.GetManifestResourceStream(resourceName);
        if (stream is null)
            return;

        var buffer = new byte[stream.Length];
        stream.ReadExactly(buffer);
        File.WriteAllBytes(targetPath, buffer);
    }

    [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true, CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static extern bool SetDllDirectory(string lpPathName);
}
