namespace JoinCode.Services;

/// <summary>
/// 控制台输出 — 纯 CLI 模式，无 TUI 依赖
/// </summary>
[Register]
public sealed partial class ConsoleOutput : IConsoleOutput
{
    [Inject] private readonly ILogger<ConsoleOutput>? _logger;

    public ConsoleOutput(ILogger<ConsoleOutput>? logger = null)
    {
        _logger = logger;
    }

    public void WriteLine(string message)
    {
        System.Console.WriteLine(message);
        _logger?.LogInformation("{Message}", message);
    }

    public void WriteError(string message)
    {
        System.Console.ForegroundColor = ConsoleColor.Red;
        System.Console.WriteLine($"错误: {message}");
        System.Console.ResetColor();
        _logger?.LogError("{Message}", message);
    }

    public void WriteSuccess(string message)
    {
        System.Console.ForegroundColor = ConsoleColor.Green;
        System.Console.WriteLine(message);
        System.Console.ResetColor();
        _logger?.LogInformation("{Message}", message);
    }

    public void WriteWarning(string message)
    {
        System.Console.ForegroundColor = ConsoleColor.Yellow;
        System.Console.WriteLine($"警告: {message}");
        System.Console.ResetColor();
        _logger?.LogWarning("{Message}", message);
    }

    public string? Prompt(string message)
    {
        if (Core.Utils.TestEnvironmentDetector.IsNonInteractive || System.Console.IsInputRedirected)
        {
            System.Console.WriteLine(message);
            return null;
        }
        System.Console.Write(message);
        var response = System.Console.ReadLine();
        _logger?.LogDebug("Prompt: {Message}, Response: {Response}", message, response);
        return response;
    }

    public bool Confirm(string message)
    {
        if (Core.Utils.TestEnvironmentDetector.IsNonInteractive || System.Console.IsInputRedirected)
        {
            System.Console.WriteLine(message);
            return false;
        }
        System.Console.Write($"{message} (y/N) ");
        var response = System.Console.ReadLine();
        var confirmed = response?.ToLowerInvariant() == "y";
        _logger?.LogDebug("Confirm: {Message}, Result: {Result}", message, confirmed);
        return confirmed;
    }

    public void WriteLine(string message, ConsoleColor color)
    {
        System.Console.ForegroundColor = color;
        System.Console.WriteLine(message);
        System.Console.ResetColor();
        _logger?.LogInformation("{Message}", message);
    }

    public string ReadPassword(string prompt)
    {
        if (Core.Utils.TestEnvironmentDetector.IsNonInteractive || System.Console.IsInputRedirected)
        {
            System.Console.WriteLine(prompt);
            return string.Empty;
        }
        System.Console.Write(prompt);
        var secret = new System.Text.StringBuilder();
        ConsoleKeyInfo key;
        do
        {
#pragma warning disable JCC2002 // ReadKey is intentional for password masking
            key = System.Console.ReadKey(true);
#pragma warning restore JCC2002
            if (key.Key == ConsoleKey.Backspace && secret.Length > 0)
            {
                secret.Length--;
                System.Console.Write("\b \b");
            }
            else if (!char.IsControl(key.KeyChar))
            {
                secret.Append(key.KeyChar);
                System.Console.Write('*');
            }
        } while (key.Key != ConsoleKey.Enter);
        System.Console.WriteLine();
        return secret.ToString();
    }
}
