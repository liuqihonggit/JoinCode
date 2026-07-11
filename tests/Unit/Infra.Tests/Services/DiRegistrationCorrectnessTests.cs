using System.Reflection;
using JoinCode.Abstractions.Attributes;

namespace Infra.Tests;

/// <summary>
/// DI 注册正确性测试 — 检测 [Register] 特性是否正确指定接口类型
/// 新规则: [Register] 无参数时自动发现接口(排除IDisposable)，多接口时需显式指定
/// </summary>
public sealed class DiRegistrationCorrectnessTests
{
    /// <summary>
    /// 检测 Contracts 程序集中所有标记了 [Register] 但未指定 typeof(IInterface) 且实现多个业务接口的类
    /// 这些类应该使用 [Register(typeof(IFoo), typeof(IBar))] 显式指定
    /// </summary>
    [Fact]
    public void Register_WithoutTypeOf_ShouldNotImplementMultipleInterfaces()
    {
        var assembly = typeof(RegisterAttribute).Assembly;
        var buggyTypes = FindMultiInterfaceBareRegisterAttributes(assembly);

        Assert.Empty(buggyTypes);
    }

    /// <summary>
    /// 检测所有引用的程序集中 [Register] 注册正确性
    /// 通过扫描 AppDomain 中已加载的程序集
    /// </summary>
    [Fact]
    public void AllLoadedAssemblies_Register_ShouldBeRegisteredAsInterfaces()
    {
        var allBuggyTypes = new List<string>();

        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            if (assembly.IsDynamic) continue;
            if (assembly.FullName?.StartsWith("System.") == true) continue;
            if (assembly.FullName?.StartsWith("Microsoft.") == true) continue;
            if (assembly.FullName?.StartsWith("mscorlib") == true) continue;
            if (assembly.FullName?.StartsWith("netstandard") == true) continue;

            var buggyTypes = FindMultiInterfaceBareRegisterAttributes(assembly);
            allBuggyTypes.AddRange(buggyTypes);
        }

        Assert.Empty(allBuggyTypes);
    }

    private static List<string> FindMultiInterfaceBareRegisterAttributes(Assembly assembly)
    {
        var buggyTypes = new List<string>();

        try
        {
            foreach (var type in assembly.GetTypes())
            {
                var attrs = type.GetCustomAttributes<RegisterAttribute>();
                foreach (var attr in attrs)
                {
                    if (attr.InterfaceType is not null) continue;

                    // [Register] 无参数: 自动发现接口是允许的，但多接口时需要显式指定
                    var businessInterfaces = type.GetInterfaces()
                        .Where(i => i != typeof(IDisposable) && i != typeof(IAsyncDisposable))
                        .Where(i => !i.IsGenericType || i.GetGenericTypeDefinition() != typeof(IEquatable<>))
                        // 排除管道接口
                        .Where(i => !i.IsGenericType || !i.GetGenericTypeDefinition().FullName?.StartsWith("JoinCode.Abstractions.Pipeline.IMiddleware") == true)
                        .Where(i => !i.IsGenericType || !i.GetGenericTypeDefinition().FullName?.StartsWith("JoinCode.Abstractions.Pipeline.IStreamMiddleware") == true)
                        .ToList();

                    if (businessInterfaces.Count > 1)
                    {
                        var interfaceNames = string.Join(", ", businessInterfaces.Select(i => i.Name));
                        buggyTypes.Add($"{type.FullName} [Register] without typeof implements {businessInterfaces.Count} interfaces: {interfaceNames}. Use [Register(typeof(A), typeof(B))]");
                    }
                }
            }
        }
        catch (ReflectionTypeLoadException ex)
        {
            System.Diagnostics.Trace.WriteLine($"[DiRegistrationCorrectness] 跳过无法加载的程序集: {assembly.FullName} - {ex.Message}");
        }

        return buggyTypes;
    }
}
