Console.WriteLine("=== PithosDB NativeAOT Probe ===");

using var db = PithosDb.OpenInMemory();

Console.WriteLine("1. Put/Get test...");
var key = Encoding.UTF8.GetBytes("hello");
var value = Encoding.UTF8.GetBytes("world");
db.Put(key, value);
if (db.TryGet(key, out var got) && got is not null)
{
    var s = Encoding.UTF8.GetString(got);
    Console.WriteLine($"   Get(hello) = {s} {(s == "world" ? "PASS" : "FAIL")}");
}
else
{
    Console.WriteLine("   Get(hello) = NOT FOUND FAIL");
}

Console.WriteLine("2. Multiple Put + Scan test...");
for (var i = 0; i < 100; i++)
{
    var k = Encoding.UTF8.GetBytes($"key:{i:D4}");
    var v = Encoding.UTF8.GetBytes($"value:{i}");
    db.Put(k, v);
}
var scanCount = 0;
foreach (var (k, v) in db.Scan())
{
    scanCount++;
}
Console.WriteLine($"   Scan count = {scanCount} {(scanCount == 101 ? "PASS" : "FAIL")}");

Console.WriteLine("3. Delete test...");
var delKey = Encoding.UTF8.GetBytes("key:0000");
db.Delete(delKey);
if (!db.TryGet(delKey, out _))
{
    Console.WriteLine("   Delete(key:0000) = DELETED PASS");
}
else
{
    Console.WriteLine("   Delete(key:0000) = STILL EXISTS FAIL");
}

Console.WriteLine("4. WriteBatch test...");
var batch = new WriteBatch()
    .Put(Encoding.UTF8.GetBytes("batch:1"), Encoding.UTF8.GetBytes("a"))
    .Put(Encoding.UTF8.GetBytes("batch:2"), Encoding.UTF8.GetBytes("b"))
    .Delete(Encoding.UTF8.GetBytes("batch:1"));
db.Write(batch);
if (!db.TryGet(Encoding.UTF8.GetBytes("batch:1"), out _))
{
    Console.WriteLine("   Batch delete PASS");
}
else
{
    Console.WriteLine("   Batch delete FAIL");
}

Console.WriteLine("5. Persistence test (disk)...");
var tmpDir = Path.Combine(Path.GetTempPath(), $"pithos_probe_{Guid.NewGuid():N}");
using (var diskDb = new PithosDb(tmpDir))
{
    diskDb.Put(Encoding.UTF8.GetBytes("persist"), Encoding.UTF8.GetBytes("yes"));
}
using (var diskDb2 = new PithosDb(tmpDir))
{
    if (diskDb2.TryGet(Encoding.UTF8.GetBytes("persist"), out var pv) && pv is not null)
    {
        Console.WriteLine($"   Reopen: persist = {Encoding.UTF8.GetString(pv)} PASS");
    }
    else
    {
        Console.WriteLine("   Reopen: persist = NOT FOUND FAIL");
    }
}
if (Directory.Exists(tmpDir))
{
    foreach (var f in Directory.GetFiles(tmpDir, "*", SearchOption.AllDirectories))
    {
        File.Delete(f);
    }
    Directory.Delete(tmpDir, true);
}

Console.WriteLine("=== All tests done ===");
