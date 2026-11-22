namespace Core.Tests.Permission;

/// <summary>
/// JCC_PERMISSION_MODE 环境变量互斥集合
/// <para>
/// 根因（CI #853 flaky）：环境变量是进程级全局状态，设值测试（PermissionCheckerTest/
/// PermissionManagerTest 的 TryGetPermissionModeFromEnv 系列）与其他构造 PermissionChecker
/// 的测试并行时，构造函数 fire-and-forget 的 InitializeModeAsync 可能在窗口内读到污染值
/// （如 "bypass"），把 _currentMode 翻转为 Bypass 导致管道首层放行。
/// </para>
/// <para>契约：所有设 JCC_PERMISSION_MODE 的测试类 + 所有构造 PermissionChecker 的测试类
/// 必须挂本集合，彼此串行消除窗口交集；新增同类测试时同样必须挂入。</para>
/// </summary>
[CollectionDefinition("permission-mode-env")]
public sealed class PermissionModeEnvCollection;
