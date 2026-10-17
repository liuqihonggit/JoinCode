using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;

namespace WebViewHostSpike;

internal static class Program
{
    private static Timer? s_watchdog;

    internal sealed record Options(
        bool Smoke,
        bool InputProbe,
        string PagePath,
        string ResultPath,
        int TimeoutSeconds);

    [STAThread]
    public static int Main(string[] args)
    {
        var opts = ParseArgs(args);
        if (Array.IndexOf(args, "--runtime-check") >= 0)
        {
            return RuntimeCheck(opts);
        }
        if (!File.Exists(opts.PagePath))
        {
            Console.Error.WriteLine($"PAGE-NOT-FOUND: {opts.PagePath}");
            return 1;
        }

        var app = new SpikeApp(opts);
        var builder = AppBuilder.Configure(() => app)
            .UsePlatformDetect()
            .LogToTrace();

        if (opts.Smoke || opts.InputProbe)
        {
            s_watchdog = new Timer(
                _ => SpikeApp.FailAndExit("watchdog-timeout", opts.ResultPath),
                null, opts.TimeoutSeconds * 1000, Timeout.Infinite);
        }

        return builder.StartWithClassicDesktopLifetime(args);
    }

    /// <summary>WebView2 Runtime 探测（离线/缺失策略）：返回可用版本号或退出码 3。</summary>
    private static int RuntimeCheck(Options opts)
    {
        try
        {
            var version = Microsoft.Web.WebView2.Core.CoreWebView2Environment.GetAvailableBrowserVersionString(null);
            var ok = !string.IsNullOrEmpty(version);
            Console.WriteLine(ok ? $"RUNTIME-OK: {version}" : "RUNTIME-MISSING");
            SpikeApp.WriteResult(opts.ResultPath, new SpikeResult(
                ok, ok ? null : "runtime-missing", "runtime-check", null, version, null, EngineMode()));
            return ok ? 0 : 3;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"RUNTIME-MISSING: {ex.Message}");
            SpikeApp.WriteResult(opts.ResultPath, new SpikeResult(
                false, $"runtime-missing:{ex.Message}", "runtime-check", null, null, null, EngineMode()));
            return 3;
        }
    }

    internal static string EngineMode() =>
        System.Runtime.CompilerServices.RuntimeFeature.IsDynamicCodeSupported ? "jit" : "aot";

    private static Options ParseArgs(string[] args)
    {
        var smoke = false;
        var inputProbe = false;
        var page = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "..",
            "modules", "apps", "gui", "webui", "index.html"));
        var result = Path.Combine(Environment.CurrentDirectory, "spike-result.json");
        var timeout = 30;
        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--smoke": smoke = true; break;
                case "--input-probe": inputProbe = true; break;
                case "--page" when i + 1 < args.Length: page = Path.GetFullPath(args[++i]); break;
                case "--result" when i + 1 < args.Length: result = Path.GetFullPath(args[++i]); break;
                case "--timeout" when i + 1 < args.Length: timeout = int.Parse(args[++i]); break;
            }
        }
        return new Options(smoke, inputProbe, page, result, timeout);
    }
}

/// <summary>spike 结果 DTO（JSON 源生成，AOT 安全）。</summary>
internal sealed record SpikeResult(
    bool Ok,
    string? Reason,
    string? Mode,
    string? Page,
    string? Runtime,
    long? RoundtripMs,
    string? EngineMode);

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(SpikeResult))]
internal sealed partial class SpikeJsonContext : JsonSerializerContext;

internal sealed class SpikeApp : Application
{
    private readonly Program.Options _opts;

    public SpikeApp(Program.Options opts) => _opts = opts;

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var window = new SpikeWindow(_opts);
            if (_opts.Smoke || _opts.InputProbe)
            {
                window.BridgeSucceeded += () => Environment.Exit(0);
                window.BridgeFailed += reason => FailAndExit(reason, _opts.ResultPath);
            }
            desktop.MainWindow = window;
            desktop.ShutdownMode = ShutdownMode.OnMainWindowClose;
        }
        base.OnFrameworkInitializationCompleted();
    }

    internal static void FailAndExit(string reason, string resultPath)
    {
        Console.Error.WriteLine($"SPIKE-FAIL: {reason}");
        WriteResult(resultPath, new SpikeResult(false, reason, null, null, null, null, null));
        Environment.Exit(2);
    }

    internal static void WriteResult(string path, SpikeResult payload)
    {
        try
        {
            File.WriteAllText(path, JsonSerializer.Serialize(payload, SpikeJsonContext.Default.SpikeResult));
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"RESULT-WRITE-FAIL: {ex.Message}");
        }
    }
}

internal sealed class SpikeWindow : Window
{
    private readonly Program.Options _opts;
    private readonly WebView2Host _host;
    private readonly Stopwatch _watch = Stopwatch.StartNew();
    private bool _pingSeen;
    private bool _done;

    public SpikeWindow(Program.Options opts)
    {
        _opts = opts;
        Title = "WebView2 Host Spike (Avalonia + NativeAOT)";
        Width = 1440;
        Height = 900;
        ClientSize = new Size(1440, 900);
        _host = new WebView2Host(new Uri(opts.PagePath).AbsoluteUri,
            Path.Combine(Path.GetTempPath(), "jcc-webview2-spike"));
        _host.WebViewReady += OnWebViewReady;
        if (opts.Smoke || opts.InputProbe)
        {
            _host.InitFailed += reason => BridgeFailed?.Invoke($"webview2-init-failed:{reason}");
        }
        Content = _host;
    }

    public event Action? BridgeSucceeded;

    public event Action<string>? BridgeFailed;

    private async void OnWebViewReady(Microsoft.Web.WebView2.Core.CoreWebView2 core)
    {
        Console.WriteLine($"WEBVIEW2-READY: runtime={core.Environment.BrowserVersionString}");
        core.WebMessageReceived += OnWebMessage;
        core.NavigationCompleted += async (_, e) =>
        {
            Console.WriteLine($"NAVIGATION-COMPLETED: success={e.IsSuccess} status={e.WebErrorStatus}");
            if (!e.IsSuccess)
            {
                BridgeFailed?.Invoke($"navigation-failed:{e.WebErrorStatus}");
                return;
            }
            if (_opts.Smoke)
            {
                await core.ExecuteScriptAsync(BridgeJs);
            }
            else if (_opts.InputProbe)
            {
                await core.ExecuteScriptAsync(InputProbeJs);
            }
        };
    }

    private void OnWebMessage(object? sender, Microsoft.Web.WebView2.Core.CoreWebView2WebMessageReceivedEventArgs e)
    {
        var msg = e.TryGetWebMessageAsString();
        Console.WriteLine($"WEB-MESSAGE: {msg}");
        if (sender is not Microsoft.Web.WebView2.Core.CoreWebView2 core)
        {
            return;
        }
        switch (msg)
        {
            case "ping-from-js":
                _pingSeen = true;
                core.PostWebMessageAsJson("\"pong-from-cs\"");
                break;
            case "bridge-ok:pong-received" when _pingSeen && !_done:
                Complete(core, "smoke-bridge");
                break;
            case "input-probe:ready":
                Console.WriteLine("INPUT-PROBE-READY");
                try
                {
                    System.IO.File.WriteAllText($"{_opts.ResultPath}.ready", "ready");
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"READY-MARKER-FAIL: {ex.Message}");
                }
                break;
            case { } m when m.StartsWith("input-probe:down:", StringComparison.Ordinal) && !_done:
                Complete(core, $"input-probe({m["input-probe:down:".Length..]})");
                break;
        }
    }

    private void Complete(Microsoft.Web.WebView2.Core.CoreWebView2 core, string mode)
    {
        _done = true;
        _watch.Stop();
        Console.WriteLine($"SPIKE-PASS: {mode} {_watch.ElapsedMilliseconds}ms");
        SpikeApp.WriteResult(_opts.ResultPath, new SpikeResult(
            Ok: true,
            Reason: null,
            Mode: mode,
            Page: _opts.PagePath,
            Runtime: core.Environment.BrowserVersionString,
            RoundtripMs: _watch.ElapsedMilliseconds,
            EngineMode: Program.EngineMode()));
        BridgeSucceeded?.Invoke();
    }

    private const string BridgeJs = """
        (() => {
          if (window.__spikeHooked) return 'already';
          window.__spikeHooked = true;
          window.chrome.webview.addEventListener('message', (e) => {
            if (e.data === 'pong-from-cs') {
              window.chrome.webview.postMessage('bridge-ok:pong-received');
            }
          });
          window.chrome.webview.postMessage('ping-from-js');
          return 'hooked';
        })()
        """;

    private const string InputProbeJs = """
        (() => {
          document.addEventListener('mousedown', (e) => {
            window.chrome.webview.postMessage('input-probe:down:' + e.clientX + ',' + e.clientY);
          }, { once: true });
          window.chrome.webview.postMessage('input-probe:ready');
          return 'probe-armed';
        })()
        """;
}
