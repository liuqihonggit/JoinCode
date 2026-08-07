using System.Collections.Frozen;
using System.Reflection;
using JoinCode.Abstractions.Attributes;

namespace Infra.Tests;

/// <summary>
/// DI 注册正确性测试 — 检测 [Register] 特性是否正确指定接口类型
/// 新规则: [Register] 无参数时自动发现接口(排除能力接口)，多服务接口时需显式指定
/// </summary>
public sealed class DiRegistrationCorrectnessTests
{
    /// <summary>
    /// 能力接口集合 — 这些接口是 .NET 基础接口或框架内部能力标记，不用于 DI 服务注册
    /// 新增能力接口时只需在此集合添加一行全名
    /// </summary>
    private static readonly FrozenSet<string> s_capabilityInterfaceNames = new[]
    {
        "System.IDisposable",
        "System.IAsyncDisposable",
        "System.ICloneable",
        "System.IEquatable`1",
        "JoinCode.Abstractions.Entity.ICloneableEntity",
    }.ToFrozenSet();

    /// <summary>
    /// 管道接口前缀 — 这些接口的泛型定义以此前缀开头，属于框架管道能力，不用于 DI 服务注册
    /// </summary>
    private static readonly FrozenSet<string> s_pipelineInterfacePrefixes = new[]
    {
        "JoinCode.Abstractions.Pipeline.IMiddleware",
        "JoinCode.Abstractions.Pipeline.IStreamMiddleware",
    }.ToFrozenSet();

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

                    // [Register] 无参数: 自动发现接口是允许的，但多服务接口时需要显式指定
                    var businessInterfaces = type.GetInterfaces()
                        .Where(IsServiceInterface)
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

    /// <summary>
    /// 判断接口是否为服务接口（用于 DI 注册），排除能力接口和管道接口
    /// </summary>
    private static bool IsServiceInterface(Type interfaceType)
    {
        // 能力接口按全名匹配
        var name = interfaceType.IsGenericType
            ? interfaceType.GetGenericTypeDefinition().FullName
            : interfaceType.FullName;
        if (name is not null && s_capabilityInterfaceNames.Contains(name))
            return false;

        // 管道接口按前缀匹配
        if (name is not null && s_pipelineInterfacePrefixes.Any(p => name.StartsWith(p)))
            return false;

        return true;
    }
}
