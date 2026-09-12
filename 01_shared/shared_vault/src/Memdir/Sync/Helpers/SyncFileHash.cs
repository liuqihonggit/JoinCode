namespace Memdir.Sync.Helpers;

internal static class SyncFileHash
{
    internal static string Compute(IFileSystem fs, string filePath)
    {
        try
        {
            if (!fs.FileExists(filePath)) return string.Empty;

            var content = fs.ReadAllText(filePath);
            var hash = 0;
            foreach (var c in content)
            {
                hash = ((hash << 5) - hash) + c;
                hash &= 0x7FFFFFFF;
            }

            return hash.ToString("x8");
        }
        catch
        {
            return string.Empty;
        }
    }
}
