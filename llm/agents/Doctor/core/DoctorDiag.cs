namespace Core.Agents.Doctor;

/// <summary>
/// Doctor 诊断日志 — 所有 Doctor 类共用，输出到 Console.Error（无条件）
/// 替代 ILogger? 参数，避免日志被吞掉
/// </summary>
internal static class DoctorDiag
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static void Write(string message)
    {
        Console.Error.WriteLine(message);
        Console.Error.Flush();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static void WriteError(string message)
    {
        Console.Error.WriteLine($"[ERROR] {message}");
        Console.Error.Flush();
    }
}
