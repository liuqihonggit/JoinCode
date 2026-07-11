namespace Testing.Common;

public static class TestDbConfiguration
{
    public static bool UseInMemoryDatabase { get; set; } = true;

    public static string GetDbPath(string prefix)
    {
        return UseInMemoryDatabase
            ? ":memory:"
            : Path.Combine(Path.GetTempPath(), $"{prefix}_{Guid.NewGuid():N}.db");
    }

    public static bool IsInMemory(string dbPath)
    {
        return dbPath == ":memory:";
    }
}
