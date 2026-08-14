using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace JccInteractiveTester;

/// <summary>
/// jcc.exe 交互式测试器 — 通过 stderr 生命周期标记 [DONE]/[READY] 驱动输入时序
/// </summary>
internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        var jccExe = args.Length > 0 ? args[0]
            : @"D:\project\w1\artifacts\bin\JoinCode\Debug\net10.0\jcc.exe";

        var psi = new ProcessStartInfo
        {
            FileName = jccExe,
            Arguments = "--trust --await 180 --permission-mode bypass --force-interactive",
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            WorkingDirectory = @"D:\project\w1"
        };

        psi.EnvironmentVariables["JCC_ENDPOINT"] = "https://apihub.agnes-ai.com/v1";
        psi.EnvironmentVariables["JCC_API_KEY"] = "sk-EiRSNKFnB9wJihmwJdVIVejHEW6UYUwEbKlgK4nC3WPw7tUL";
        psi.EnvironmentVariables["JCC_VENDOR"] = "agnes";
        psi.EnvironmentVariables["JCC_MODEL_ID"] = "agnes-2.0-flash";

        using var p = Process.Start(psi)!;

        var readyTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var doneTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cts = new CancellationTokenSource(TimeSpan.FromMinutes(5));

        // 异步读 stdout — 直接输出到控制台
        _ = Task.Run(async () =>
        {
            var buf = new char[4096];
            while (!cts.Token.IsCancellationRequested)
            {
                var read = await p.StandardOutput.ReadAsync(buf, cts.Token).ConfigureAwait(false);
                if (read == 0) break;
                Console.Write(buf, 0, read);
            }
        }, cts.Token);

        // 异步读 stderr — 检测生命周期标记 [READY]/[DONE]
        _ = Task.Run(async () =>
        {
            string? line;
            while ((line = await p.StandardError.ReadLineAsync(cts.Token).ConfigureAwait(false)) is not null)
            {
                Console.WriteLine(line);
                if (line.Contains("[AI助手] 就绪")) readyTcs.TrySetResult();
                if (line.Contains("[AI对话结束]")) doneTcs.TrySetResult();
            }
        }, cts.Token);

        // 等待 [READY]
        Console.WriteLine("========== 等待 REPL 就绪 ==========");
        await readyTcs.Task.ConfigureAwait(false);
        // [READY] 后还有一个 > 提示符，稍等
        await Task.Delay(500, cts.Token).ConfigureAwait(false);

        var turns = new List<(string Label, string Input)>
        {
            ("第1轮: bash ls", "AI你调用一下bash的ls工具列出当前目录"),
            ("第2轮: 上下文记忆测试", "我刚刚让你做什么了？"),
            ("第3轮: read README", "你看到我的README.md内容了吗？请用read工具读取README.md"),
        };

        for (var i = 0; i < turns.Count; i++)
        {
            var (label, input) = turns[i];
            Console.WriteLine($"\n========== {label} ==========");

            await p.StandardInput.WriteLineAsync(input).ConfigureAwait(false);

            // 等待 [DONE] 表示本轮处理完成
            await doneTcs.Task.ConfigureAwait(false);
            doneTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

            // [DONE] 后 REPL 会打印新的 > 提示符，稍等
            await Task.Delay(300, cts.Token).ConfigureAwait(false);
        }

        Console.WriteLine("\n========== 退出 ==========");
        await p.StandardInput.WriteLineAsync("/exit").ConfigureAwait(false);

        p.WaitForExit(15000);
        Console.WriteLine($"\nEXIT: {p.ExitCode}");
        return p.ExitCode;
    }
}
