namespace Infra.Services.Tests;

/// <summary>
/// NativePluginHost E2E 测试 — 加载真实 native DLL → invoke → unload 全链路验证 (ADR 0099)
/// <para>前置条件: 需先执行 dotnet publish tools/SampleNativePlugin -c Release -o tools/SampleNativePlugin/publish</para>
/// </summary>
[Trait("Category", "Integration")]
public class NativePluginHostE2ETests
{
    private static readonly IFileSystem Fs = new PhysicalFileSystem();
    private static string? s_nativeDllPath;

    private static string? FindNativeDll()
    {
        if (s_nativeDllPath is not null) return s_nativeDllPath;

        var dir = AppContext.BaseDirectory;
        while (dir is not null && !Fs.FileExists(Path.Combine(dir, "Directory.Build.props")))
            dir = Path.GetDirectoryName(dir);

        if (dir is null) return null;

        var path = Path.Combine(dir, "tools", "SampleNativePlugin", "publish", "SampleNativePlugin.dll");
        return Fs.FileExists(path) ? (s_nativeDllPath = path) : null;
    }

    [Fact]
    public void Load_Echo_Unload_FullLifecycle()
    {
        var dllPath = FindNativeDll();
        Skip.If(dllPath is null, "Native DLL 未发布,请先执行: dotnet publish tools/SampleNativePlugin -c Release -o tools/SampleNativePlugin/publish");

        using var host = new NativePluginHost(dllPath!, "sample-echo", Fs);

        host.Load().IsSuccess.Should().BeTrue();
        host.IsLoaded.Should().BeTrue();

        var echoResult = host.Invoke("""{"method":"echo","args":{"text":"hello world"}}""");
        echoResult.IsSuccess.Should().BeTrue();
        echoResult.ResponseJson.Should().Contain("hello world");
        echoResult.ResponseJson.Should().Contain("\"ok\":true");

        host.Unload();
        host.IsLoaded.Should().BeFalse();
    }

    [Fact]
    public void Ping_ReturnsPong()
    {
        var dllPath = FindNativeDll();
        Skip.If(dllPath is null, "Native DLL 未发布");

        using var host = new NativePluginHost(dllPath!, "sample-ping", Fs);

        host.Load().IsSuccess.Should().BeTrue();

        var pingResult = host.Invoke("""{"method":"ping"}""");
        pingResult.IsSuccess.Should().BeTrue();
        pingResult.ResponseJson.Should().Contain("pong");

        host.Unload();
    }

    [Fact]
    public void UnknownMethod_ReturnsMethodNotFound()
    {
        var dllPath = FindNativeDll();
        Skip.If(dllPath is null, "Native DLL 未发布");

        using var host = new NativePluginHost(dllPath!, "sample-unknown", Fs);

        host.Load().IsSuccess.Should().BeTrue();

        var result = host.Invoke("""{"method":"nonexistent"}""");
        result.IsSuccess.Should().BeTrue();
        result.ResponseJson.Should().Contain("method not found");

        host.Unload();
    }

    [Fact]
    public void Load_Idempotent_ReturnsSuccessOnSecondLoad()
    {
        var dllPath = FindNativeDll();
        Skip.If(dllPath is null, "Native DLL 未发布");

        using var host = new NativePluginHost(dllPath!, "sample-idempotent", Fs);

        var first = host.Load();
        var second = host.Load();

        first.IsSuccess.Should().BeTrue();
        second.IsSuccess.Should().BeTrue();

        host.Unload();
    }

    [Fact]
    public void Invoke_BeforeLoad_ReturnsNotLoaded()
    {
        var dllPath = FindNativeDll();
        Skip.If(dllPath is null, "Native DLL 未发布");

        using var host = new NativePluginHost(dllPath!, "sample-notloaded", Fs);

        var result = host.Invoke("""{"method":"ping"}""");
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Be(NativePluginError.NotLoaded);
    }

    [Fact]
    public void NonExistentDll_ReturnsFail()
    {
        using var host = new NativePluginHost("C:/nonexistent/plugin.dll", "nonexistent", Fs);

        var result = host.Load();
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Be(NativePluginError.NotLoaded);
    }
}
