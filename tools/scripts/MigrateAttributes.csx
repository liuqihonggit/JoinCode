using System;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

var dirs = new[] { @"D:\w2\src", @"D:\w2\components" };
int updated = 0;
foreach (var dir in dirs)
{
    foreach (var file in Directory.GetFiles(dir, "*.cs", SearchOption.AllDirectories))
    {
        if (Path.GetFileName(file) == "ServiceRegistrationAttributes.cs") continue;
        var bytes = File.ReadAllBytes(file);
        var text = Encoding.UTF8.GetString(bytes);
        var original = text;

        text = Regex.Replace(text, @"\[SingletonService\(typeof\(([^)]+)\)\)\]", "[Register(typeof($1))]");
        text = text.Replace("[SingletonService]", "[Register]");
        text = Regex.Replace(text, @"\[TransientService\(typeof\(([^)]+)\)\)\]", "[Register(typeof($1), Lifetime = ServiceLifetime.Transient)]");
        text = text.Replace("[TransientService]", "[Register(Lifetime = ServiceLifetime.Transient)]");
        text = Regex.Replace(text, @"\[ScopedService\(typeof\(([^)]+)\)\)\]", "[Register(typeof($1), Lifetime = ServiceLifetime.Scoped)]");
        text = text.Replace("[ScopedService]", "[Register(Lifetime = ServiceLifetime.Scoped)]");

        if (text != original)
        {
            File.WriteAllBytes(file, Encoding.UTF8.GetBytes(text));
            updated++;
            Console.WriteLine($"Updated: {Path.GetRelativePath(@"D:\w2", file)}");
        }
    }
}
Console.WriteLine($"Total updated: {updated}");
