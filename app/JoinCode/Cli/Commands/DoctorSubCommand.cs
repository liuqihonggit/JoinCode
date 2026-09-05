namespace JoinCode.CliCommands;

/// <summary>
/// 医生模式子命令 — jcc doctor [--server] [--endpoint &lt;url&gt;] [--port &lt;n&gt;]
/// <para>ADR: 0069 — 从全局参数 --doctor/--doctor-server/--doctor-endpoint/--doctor-port 迁移为独立子命令。</para>
/// <para>医生模式: spawn jcc.exe 子进程作为病人，监控运行状态并自动修复问题。</para>
/// <para>病人模式: --endpoint 指定医生 SSE 服务器 URL，连接并发送遥测事件。</para>
/// </summary>
internal static class DoctorSubCommand
{
    public static async Task<int?> ExecuteAsync(string[] args, CancellationToken ct)
    {
        var isServer = FlatSubCommandRouter.HasFlag(args, "--server");
        var endpoint = FlatSubCommandRouter.GetOptionValue(args, "--endpoint");
        var port = int.TryParse(FlatSubCommandRouter.GetOptionValue(args, "--port"), out var p) ? p : 9902;

        var options = new CommandLineOptions
        {
            NonInteractive = true,
            TrustWorkspace = true,
            DoctorMode = true,
            DoctorServerMode = isServer,
            DoctorEndpoint = endpoint,
            DoctorPort = port,
        };

        var doctorFs = IO.FileSystem.FileSystemFactory.Create();
        Core.Utils.TestEnvironmentDetector.ForceNonInteractive = true;
        var doctorResult = await App.Builder.EngineSessionFactory.CreateCliSessionAsync(options, doctorFs, ct).ConfigureAwait(false);

        try
        {
            return await Entry.DoctorModeRunner.RunAsync(options, doctorResult.Host.Services);
        }
        finally
        {
            if (doctorResult.Host is IAsyncDisposable asyncDoc)
                await asyncDoc.DisposeAsync().ConfigureAwait(false);
            else
                doctorResult.Host.Dispose();
        }
    }
}
