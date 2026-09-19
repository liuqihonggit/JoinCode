namespace JoinCode.SandboxSatellite;

/// <summary>
/// 沙箱卫星进程入口 — 独立进程承载沙箱执行环境
/// </summary>
public static class Program {
    /// <summary>
    /// 主入口 — 创建物理文件系统并启动沙箱卫星宿主
    /// </summary>
    /// <param name="args">命令行参数</param>
    /// <returns>进程退出码 — 0 表示成功</returns>
    public static async Task<int> Main(string[] args) {
        var fs = new IO.FileSystem.PhysicalFileSystem();
        var host = new SandboxSatelliteHost(fs);
        await host.RunAsync().ConfigureAwait(false);
        return 0;
    }
}