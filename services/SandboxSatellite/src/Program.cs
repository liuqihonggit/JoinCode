namespace JoinCode.SandboxSatellite;

public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        var fs = new IO.FileSystem.PhysicalFileSystem();
        var host = new SandboxSatelliteHost(fs);
        await host.RunAsync().ConfigureAwait(false);
        return 0;
    }
}
