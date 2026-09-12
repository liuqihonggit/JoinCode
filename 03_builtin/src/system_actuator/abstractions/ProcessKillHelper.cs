namespace Services.SystemActuator;

internal static class ProcessKillHelper
{
    internal static void KillProcessTree(Process process, ILogger? logger)
    {
        if (OperatingSystem.IsWindows())
        {
            try
            {
                var killerPsi = SystemActuatorBase.SharedBuilder.Build(new ProcessOptions
                {
                    FileName = "taskkill.exe",
                    ArgumentList = ["/T", "/F", "/PID", process.Id.ToString()],
                });
                using var killer = new Process { StartInfo = killerPsi };
                killer.Start();
                killer.WaitForExit(5000);
            }
            catch (Exception ex)
            {
                logger?.LogDebug(ex, "taskkill.exe 终止进程树失败，尝试直接 Kill PID {Pid}", process.Id);
                TryKillSafely(process, logger);
            }
        }
        else
        {
            TryKillSafely(process, logger);
        }
    }

    private static void TryKillSafely(Process process, ILogger? logger)
    {
        try { process.Kill(); }
        catch (Exception killEx)
        {
            logger?.LogDebug(killEx, "直接 Kill PID {Pid} 失败（可能已退出或无权限）", process.Id);
        }
    }
}
