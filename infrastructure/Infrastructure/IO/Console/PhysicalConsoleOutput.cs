namespace Infrastructure.IO;

/// <summary>
/// 物理控制台输出 — 直接使用 System.Console
/// </summary>
[Register(typeof(IConsoleOutput))]
public sealed partial class PhysicalConsoleOutput : IConsoleOutput
{
    public void WriteLine(string message) => System.Console.WriteLine(message);

    public void WriteError(string message) => System.Console.Error.WriteLine(message);

    public void WriteSuccess(string message)
    {
        System.Console.ForegroundColor = ConsoleColor.Green;
        System.Console.WriteLine(message);
        System.Console.ResetColor();
    }

    public void WriteWarning(string message)
    {
        System.Console.ForegroundColor = ConsoleColor.Yellow;
        System.Console.WriteLine(message);
        System.Console.ResetColor();
    }

    public string? Prompt(string message)
    {
        System.Console.Write(message);
        if (System.Console.IsInputRedirected) return null;
        return System.Console.ReadLine();
    }

    public bool Confirm(string message)
    {
        System.Console.Write($"{message} (y/N) ");
        if (System.Console.IsInputRedirected) return false;
        var input = System.Console.ReadLine()?.Trim().ToLowerInvariant();
        return input == "y" || input == "yes";
    }

    public void WriteLine(string message, ConsoleColor color)
    {
        System.Console.ForegroundColor = color;
        System.Console.WriteLine(message);
        System.Console.ResetColor();
    }

#pragma warning disable JCC2002 // ReadPassword 是同步交互方法，已检查 IsInputRedirected
#pragma warning disable JCC5001 // ReadPassword 是同步方法，无法使用 async Task.Delay
    public string ReadPassword(string prompt)
    {
        System.Console.Write(prompt);
        if (System.Console.IsInputRedirected) return string.Empty;
        var password = new System.Text.StringBuilder();
        while (true)
        {
            var key = System.Console.ReadKey(true);
            if (key.Key == ConsoleKey.Enter) break;
            if (key.Key == ConsoleKey.Backspace && password.Length > 0)
            {
                password.Remove(password.Length - 1, 1);
                System.Console.Write("\b \b");
            }
            else if (key.Key != ConsoleKey.Backspace)
            {
                password.Append(key.KeyChar);
                System.Console.Write("*");
            }
        }
        System.Console.WriteLine();
        return password.ToString();
    }
#pragma warning restore JCC2002
#pragma warning restore JCC5001
}
