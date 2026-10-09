
// JCC11003 抑制: 底层泛型集合实现, ! 用于 default(T)/数组槽位的可空抑制, 是 C# 语言限制下的必要用法
#pragma warning disable JCC11003
namespace Structura.Collections
{

    /// <summary>
    /// SwissTable 的异常抛出辅助类，集中管理各类异常的创建与抛出，便于 JIT 内联优化并隐藏堆栈帧。
    /// </summary>
    [StackTraceHidden]
    internal static class ThrowHelper
    {

        /// <summary>
        /// 抛出 <see cref="SerializationException"/>，异常消息由指定的资源枚举决定。
        /// </summary>
        [DoesNotReturn]
        internal static void ThrowSerializationException(ExceptionResource resource)
        {
            throw new SerializationException(GetResourceString(resource));
        }

        /// <summary>
        /// 抛出 <see cref="KeyNotFoundException"/>，指示指定的键不存在于字典中。
        /// </summary>
        [DoesNotReturn]
        internal static void ThrowKeyNotFoundException<T>(T key)
        {
            // Generic key to move the boxing to the right hand side of throw
            throw GetKeyNotFoundException((object?)key);
        }

        /// <summary>
        /// 抛出 <see cref="ArgumentException"/>，异常消息由指定的资源枚举决定。
        /// </summary>
        [DoesNotReturn]
        internal static void ThrowArgumentException(ExceptionResource resource)
        {
            throw GetArgumentException(resource);
        }

        /// <summary>
        /// 抛出不带参数名的 <see cref="ArgumentOutOfRangeException"/>。
        /// </summary>
        [DoesNotReturn]
        internal static void ThrowArgumentOutOfRangeException()
        {
            throw new ArgumentOutOfRangeException();
        }

        /// <summary>
        /// 抛出 <see cref="ArgumentException"/>，指示添加了重复键。
        /// </summary>
        [DoesNotReturn]
        internal static void ThrowAddingDuplicateWithKeyArgumentException<T>(T key)
        {
            // Generic key to move the boxing to the right hand side of throw
            throw GetAddingDuplicateWithKeyArgumentException((object?)key);
        }

        /// <summary>
        /// 抛出 <see cref="ArgumentNullException"/>，参数名由指定的参数枚举决定。
        /// </summary>
        [DoesNotReturn]
        internal static void ThrowArgumentNullException(ExceptionArgument argument)
        {
            throw new ArgumentNullException(GetArgumentName(argument));
        }

        /// <summary>
        /// 抛出 <see cref="NotSupportedException"/>，异常消息由指定的资源枚举决定。
        /// </summary>
        [DoesNotReturn]
        internal static void ThrowNotSupportedException(ExceptionResource resource)
        {
            throw new NotSupportedException(GetResourceString(resource));
        }

        /// <summary>
        /// 抛出 <see cref="ArgumentOutOfRangeException"/>，指示索引为负数。
        /// </summary>
        [DoesNotReturn]
        internal static void ThrowIndexArgumentOutOfRange_NeedNonNegNumException()
        {
            throw GetArgumentOutOfRangeException(ExceptionArgument.index,
                                                    ExceptionResource.ArgumentOutOfRange_NeedNonNegNum);
        }


        /// <summary>
        /// 抛出 <see cref="ArgumentException"/>，指示数组类型不兼容。
        /// </summary>
        [DoesNotReturn]
        internal static void ThrowArgumentException_Argument_InvalidArrayType()
        {
            throw new ArgumentException(SR.Argument_InvalidArrayType);
        }

        /// <summary>
        /// 抛出 <see cref="InvalidOperationException"/>，指示枚举器尚未开始或已结束。
        /// </summary>
        [DoesNotReturn]
        internal static void ThrowInvalidOperationException_InvalidOperation_EnumOpCantHappen()
        {
            throw new InvalidOperationException(SR.InvalidOperation_EnumOpCantHappen);
        }

        /// <summary>
        /// 抛出 <see cref="InvalidOperationException"/>，指示枚举期间集合已发生变更。
        /// </summary>
        [DoesNotReturn]
        internal static void ThrowInvalidOperationException_InvalidOperation_EnumFailedVersion()
        {
            throw new InvalidOperationException(SR.InvalidOperation_EnumFailedVersion);
        }

        // Allow nulls for reference types and Nullable<U>, but not for value types.
        // Aggressively inline so the jit evaluates the if in place and either drops the call altogether
        // Or just leaves null test and call to the Non-returning ThrowHelper.ThrowArgumentNullException
        /// <summary>
        /// 当类型 T 不允许 null（值类型且非 Nullable）而传入值为 null 时，抛出 <see cref="ArgumentNullException"/>。
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static void IfNullAndNullsAreIllegalThenThrow<T>(object? value, ExceptionArgument argName)
        {
            // Note that default(T) is not equal to null for value types except when T is Nullable<U>.
            if (!(default(T) == null) && value == null)
                ThrowHelper.ThrowArgumentNullException(argName);
        }


        /// <summary>
        /// 抛出 <see cref="ArgumentException"/>，指示键的类型与目标类型不匹配。
        /// </summary>
        [DoesNotReturn]
        internal static void ThrowWrongKeyTypeArgumentException<T>(T key, Type targetType)
        {
            // Generic key to move the boxing to the right hand side of throw
            throw GetWrongKeyTypeArgumentException((object?)key, targetType);
        }

        /// <summary>
        /// 抛出 <see cref="ArgumentException"/>，指示值的类型与目标类型不匹配。
        /// </summary>
        [DoesNotReturn]
        internal static void ThrowWrongValueTypeArgumentException<T>(T value, Type targetType)
        {
            // Generic key to move the boxing to the right hand side of throw
            throw GetWrongValueTypeArgumentException((object?)value, targetType);
        }

        /// <summary>
        /// 创建指示添加重复键的 <see cref="ArgumentException"/> 实例。
        /// </summary>
        private static ArgumentException GetAddingDuplicateWithKeyArgumentException(object? key)
        {
            return new ArgumentException(SR.Format(SR.Argument_AddingDuplicateWithKey, key));
        }

        /// <summary>
        /// 抛出 <see cref="ArgumentOutOfRangeException"/>，参数名由指定的参数枚举决定。
        /// </summary>
        [DoesNotReturn]
        internal static void ThrowArgumentOutOfRangeException(ExceptionArgument argument)
        {
            throw new ArgumentOutOfRangeException(GetArgumentName(argument));
        }

        /// <summary>
        /// 创建由资源枚举决定消息的 <see cref="ArgumentException"/> 实例。
        /// </summary>
        private static ArgumentException GetArgumentException(ExceptionResource resource)
        {
            return new ArgumentException(GetResourceString(resource));
        }


        /// <summary>
        /// 创建指示键类型不匹配的 <see cref="ArgumentException"/> 实例。
        /// </summary>
        private static ArgumentException GetWrongKeyTypeArgumentException(object? key, Type targetType)
        {
            return new ArgumentException(SR.Format(SR.Arg_WrongType, key, targetType), nameof(key));
        }

        /// <summary>
        /// 创建指示值类型不匹配的 <see cref="ArgumentException"/> 实例。
        /// </summary>
        private static ArgumentException GetWrongValueTypeArgumentException(object? value, Type targetType)
        {
            return new ArgumentException(SR.Format(SR.Arg_WrongType, value, targetType), nameof(value));
        }

        /// <summary>
        /// 创建指示指定键不存在的 <see cref="KeyNotFoundException"/> 实例。
        /// </summary>
        private static KeyNotFoundException GetKeyNotFoundException(object? key)
        {
            return new KeyNotFoundException(SR.Format(SR.Arg_KeyNotFoundWithKey, key));
        }

        /// <summary>
        /// 创建由参数枚举和资源枚举决定消息的 <see cref="ArgumentOutOfRangeException"/> 实例。
        /// </summary>
        private static ArgumentOutOfRangeException GetArgumentOutOfRangeException(ExceptionArgument argument, ExceptionResource resource)
        {
            return new ArgumentOutOfRangeException(GetArgumentName(argument), GetResourceString(resource));
        }

        /// <summary>
        /// 将 <see cref="ExceptionResource"/> 枚举值映射为对应的本地化资源字符串。
        /// </summary>
        private static string GetResourceString(ExceptionResource resource)
        {
            switch (resource)
            {
                case ExceptionResource.NotSupported_KeyCollectionSet:
                    return SR.NotSupported_KeyCollectionSet;
                case ExceptionResource.NotSupported_ValueCollectionSet:
                    return SR.NotSupported_ValueCollectionSet;
                case ExceptionResource.Arg_RankMultiDimNotSupported:
                    return SR.Arg_RankMultiDimNotSupported;
                case ExceptionResource.Arg_NonZeroLowerBound:
                    return SR.Arg_NonZeroLowerBound;
                case ExceptionResource.Arg_ArrayPlusOffTooSmall:
                    return SR.Arg_ArrayPlusOffTooSmall;
                case ExceptionResource.ArgumentOutOfRange_NeedNonNegNum:
                    return SR.ArgumentOutOfRange_NeedNonNegNum;
                default:
                    Debug.Fail("The enum value is not defined, please check the ExceptionResource Enum.");
                    return "";
            }
        }

        /// <summary>
        /// 将 <see cref="ExceptionArgument"/> 枚举值映射为对应的参数名字符串。
        /// </summary>
        private static string GetArgumentName(ExceptionArgument argument)
        {
            switch (argument)
            {
                case ExceptionArgument.dictionary:
                    return "dictionary";
                case ExceptionArgument.array:
                    return "array";
                case ExceptionArgument.key:
                    return "key";
                case ExceptionArgument.value:
                    return "value";
                case ExceptionArgument.index:
                    return "index";
                case ExceptionArgument.capacity:
                    return "capacity";
                case ExceptionArgument.collection:
                    return "collection";
                default:
                    Debug.Fail("The enum value is not defined, please check the ExceptionArgument Enum.");
                    return "";
            }
        }

#if false // Reflection-based implementation does not work for CoreRT/ProjectN
        // This function will convert an ExceptionResource enum value to the resource string.
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static string GetResourceString(ExceptionResource resource)
        {
            Debug.Assert(Enum.IsDefined(typeof(ExceptionResource), resource),
                "The enum value is not defined, please check the ExceptionResource Enum.");

            return SR.GetResourceString(resource.ToString());
        }
#endif
    }

    //
    // The convention for this enum is using the argument name as the enum name
    //
    /// <summary>
    /// 异常参数名枚举，约定以参数名作为枚举名，用于在抛出异常时按枚举值查找参数名字符串。
    /// </summary>
    internal enum ExceptionArgument
    {
        dictionary,
        array,
        key,
        value,
        index,
        capacity,
        collection,
    }

    //
    // The convention for this enum is using the resource name as the enum name
    //
    /// <summary>
    /// 异常资源名枚举，约定以资源名作为枚举名，用于在抛出异常时按枚举值查找本地化资源字符串。
    /// </summary>
    internal enum ExceptionResource
    {
        Arg_ArrayPlusOffTooSmall,
        Arg_RankMultiDimNotSupported,
        Arg_NonZeroLowerBound,
        ArgumentOutOfRange_NeedNonNegNum,
        NotSupported_KeyCollectionSet,
        NotSupported_ValueCollectionSet,
    }

    /// <summary>
    /// 字符串资源访问类，提供资源键查找、格式化等辅助方法（partial 定义，实现分布在多个文件）。
    /// </summary>
    internal static partial class SR
    {
        private static readonly bool s_usingResourceKeys = AppContext.TryGetSwitch("System.Resources.UseSystemResourceKeys", out bool usingResourceKeys) ? usingResourceKeys : false;

        // This method is used to decide if we need to append the exception message parameters to the message when calling SR.Format.
        // by default it returns the value of System.Resources.UseSystemResourceKeys AppContext switch or false if not specified.
        // Native code generators can replace the value this returns based on user input at the time of native code generation.
        // The Linker is also capable of replacing the value of this method when the application is being trimmed.
        /// <summary>
        /// 返回是否使用资源键代替资源字符串，受 System.Resources.UseSystemResourceKeys 开关控制。
        /// </summary>
        private static bool UsingResourceKeys() => s_usingResourceKeys;

        /// <summary>
        /// 按资源键获取本地化资源字符串；当启用资源键模式时直接返回键本身。
        /// </summary>
        internal static string GetResourceString(string resourceKey)
        {
            if (UsingResourceKeys())
            {
                return resourceKey;
            }

            string? resourceString = "";  // We do not have any real resource, so we set it to "" rather than `null` in dotnet/runtime.
            try
            {
                resourceString = ResourceManager.GetString(resourceKey);
#if SYSTEM_PRIVATE_CORELIB || CORERT
                // InternalGetResourceString(resourceKey);
#else
                // ResourceManager.GetString(resourceKey);
#endif
            }
            catch (MissingManifestResourceException) { }

            return resourceString!; // only null if missing resources
        }

        /// <summary>
        /// 按资源键获取本地化资源字符串，获取失败时返回指定的默认字符串。
        /// </summary>
        internal static string GetResourceString(string resourceKey, string defaultString)
        {
            string resourceString = GetResourceString(resourceKey);

            return resourceKey == resourceString || resourceString == null ? defaultString : resourceString;
        }

        /// <summary>
        /// 用指定参数格式化资源字符串；资源键模式下以逗号拼接参数。
        /// </summary>
        internal static string Format(string resourceFormat, object? p1)
        {
            if (UsingResourceKeys())
            {
                return string.Join(", ", resourceFormat, p1);
            }

            return string.Format(resourceFormat, p1);
        }

        /// <summary>
        /// 用指定两个参数格式化资源字符串；资源键模式下以逗号拼接参数。
        /// </summary>
        internal static string Format(string resourceFormat, object? p1, object? p2)
        {
            if (UsingResourceKeys())
            {
                return string.Join(", ", resourceFormat, p1, p2);
            }

            return string.Format(resourceFormat, p1, p2);
        }

        /// <summary>
        /// 用指定三个参数格式化资源字符串；资源键模式下以逗号拼接参数。
        /// </summary>
        internal static string Format(string resourceFormat, object? p1, object? p2, object? p3)
        {
            if (UsingResourceKeys())
            {
                return string.Join(", ", resourceFormat, p1, p2, p3);
            }

            return string.Format(resourceFormat, p1, p2, p3);
        }

        /// <summary>
        /// 用参数数组格式化资源字符串；资源键模式下以逗号拼接参数。
        /// </summary>
        internal static string Format(string resourceFormat, params object?[]? args)
        {
            if (args != null)
            {
                if (UsingResourceKeys())
                {
                    return resourceFormat + ", " + string.Join(", ", args);
                }

                return string.Format(resourceFormat, args);
            }

            return resourceFormat;
        }

        /// <summary>
        /// 用指定格式提供者和参数格式化资源字符串；资源键模式下以逗号拼接参数。
        /// </summary>
        internal static string Format(IFormatProvider? provider, string resourceFormat, object? p1)
        {
            if (UsingResourceKeys())
            {
                return string.Join(", ", resourceFormat, p1);
            }

            return string.Format(provider, resourceFormat, p1);
        }

        /// <summary>
        /// 用指定格式提供者和两个参数格式化资源字符串；资源键模式下以逗号拼接参数。
        /// </summary>
        internal static string Format(IFormatProvider? provider, string resourceFormat, object? p1, object? p2)
        {
            if (UsingResourceKeys())
            {
                return string.Join(", ", resourceFormat, p1, p2);
            }

            return string.Format(provider, resourceFormat, p1, p2);
        }

        /// <summary>
        /// 用指定格式提供者和三个参数格式化资源字符串；资源键模式下以逗号拼接参数。
        /// </summary>
        internal static string Format(IFormatProvider? provider, string resourceFormat, object? p1, object? p2, object? p3)
        {
            if (UsingResourceKeys())
            {
                return string.Join(", ", resourceFormat, p1, p2, p3);
            }

            return string.Format(provider, resourceFormat, p1, p2, p3);
        }

        /// <summary>
        /// 用指定格式提供者和参数数组格式化资源字符串；资源键模式下以逗号拼接参数。
        /// </summary>
        internal static string Format(IFormatProvider? provider, string resourceFormat, params object?[]? args)
        {
            if (args != null)
            {
                if (UsingResourceKeys())
                {
                    return resourceFormat + ", " + string.Join(", ", args);
                }

                return string.Format(provider, resourceFormat, args);
            }

            return resourceFormat;
        }
    }

    namespace System.Private.CoreLib
    {
        /// <summary>
        /// 资源管理器所引用的标记类型，用于定位资源程序集。
        /// </summary>
        internal static class Strings { }
    }

    /// <summary>
    /// 字符串资源访问类，提供资源管理器与各资源键对应的字符串属性（partial 定义的资源属性部分）。
    /// </summary>
    internal static partial class SR
    {
        private static global::System.Resources.ResourceManager? s_resourceManager;
        /// <summary>
        /// 获取 <see cref="ResourceManager"/> 实例，惰性初始化并绑定到 Strings 标记类型。
        /// </summary>
        internal static global::System.Resources.ResourceManager ResourceManager => s_resourceManager ?? (s_resourceManager = new global::System.Resources.ResourceManager(typeof(System.Private.CoreLib.Strings)));

        /// <summary>The given key '{0}' was not present in the dictionary.</summary>
        internal static string @Arg_KeyNotFoundWithKey => GetResourceString("Arg_KeyNotFoundWithKey");
        /// <summary>Collection was modified; enumeration operation may not execute.</summary>
        internal static string @InvalidOperation_EnumFailedVersion => GetResourceString("InvalidOperation_EnumFailedVersion");
        /// <summary>Enumeration has either not started or has already finished.</summary>
        internal static string @InvalidOperation_EnumOpCantHappen => GetResourceString("InvalidOperation_EnumOpCantHappen");
        /// <summary>The value "{0}" is not of type "{1}" and cannot be used in this generic collection.</summary>
        internal static string @Arg_WrongType => GetResourceString("Arg_WrongType");
        /// <summary>An item with the same key has already been added. Key: {0}</summary>
        internal static string @Argument_AddingDuplicateWithKey => GetResourceString("Argument_AddingDuplicateWithKey");
        /// <summary>Target array type is not compatible with the type of items in the collection.</summary>
        internal static string @Argument_InvalidArrayType => GetResourceString("Argument_InvalidArrayType");
        /// <summary>Mutating a key collection derived from a dictionary is not allowed.</summary>
        internal static string @NotSupported_KeyCollectionSet => GetResourceString("NotSupported_KeyCollectionSet");
        /// <summary>Mutating a value collection derived from a dictionary is not allowed.</summary>
        internal static string @NotSupported_ValueCollectionSet => GetResourceString("NotSupported_ValueCollectionSet");
        /// <summary>The lower bound of target array must be zero.</summary>
        internal static string @Arg_NonZeroLowerBound => GetResourceString("Arg_NonZeroLowerBound");
        /// <summary>Only single dimensional arrays are supported for the requested action.</summary>
        internal static string @Arg_RankMultiDimNotSupported => GetResourceString("Arg_RankMultiDimNotSupported");
        /// <summary>Destination array is not long enough to copy all the items in the collection. Check array index and length.</summary>
        internal static string @Arg_ArrayPlusOffTooSmall => GetResourceString("Arg_ArrayPlusOffTooSmall");
        /// <summary>The Keys for this Hashtable are missing.</summary>
        internal static string @Serialization_MissingKeys => GetResourceString("Serialization_MissingKeys");
        /// <summary>One of the serialized keys is null.</summary>
        internal static string @Serialization_NullKey => GetResourceString("Serialization_NullKey");
        /// <summary>Non-negative number required.</summary>
        internal static string @ArgumentOutOfRange_NeedNonNegNum => GetResourceString("ArgumentOutOfRange_NeedNonNegNum");
    }
}