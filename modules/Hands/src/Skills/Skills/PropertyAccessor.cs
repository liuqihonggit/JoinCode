namespace Core.Skills;

/// <summary>
/// 属性访问器 - 统一的对象属性获取逻辑
/// 支持 Dictionary、JsonElement、Array、ICollection、IEnumerable、string 等类型
/// </summary>
internal static class PropertyAccessor
{
    /// <summary>
    /// 获取对象属性值
    /// </summary>
    /// <typeparam name="T">对象类型</typeparam>
    /// <param name="obj">目标对象</param>
    /// <param name="propertyName">属性名</param>
    /// <returns>属性值，未找到返回 null</returns>
    internal static object? GetPropertyValue<T>(T obj, string propertyName)
    {
        if (obj is Array array)
        {
            if (propertyName.Equals("length", StringComparison.OrdinalIgnoreCase) ||
                propertyName.Equals("count", StringComparison.OrdinalIgnoreCase))
            {
                return array.Length;
            }
        }

        if (obj is System.Collections.ICollection collection)
        {
            if (propertyName.Equals("count", StringComparison.OrdinalIgnoreCase) ||
                propertyName.Equals("length", StringComparison.OrdinalIgnoreCase))
            {
                return collection.Count;
            }
        }

        if (obj is System.Collections.IEnumerable enumerable &&
            (propertyName.Equals("count", StringComparison.OrdinalIgnoreCase) ||
             propertyName.Equals("length", StringComparison.OrdinalIgnoreCase)))
        {
            var count = 0;
            foreach (var _ in enumerable)
            {
                count++;
            }
            return count;
        }

        if (obj is string str)
        {
            if (propertyName.Equals("length", StringComparison.OrdinalIgnoreCase))
            {
                return str.Length;
            }
        }

        if (obj is Dictionary<string, JsonElement> dict)
        {
            if (dict.TryGetValue(propertyName, out var dictValue))
            {
                return dictValue;
            }
        }

        if (obj is System.Text.Json.JsonElement jsonElement)
        {
            return GetJsonElementProperty(jsonElement, propertyName);
        }

        return null;
    }

    /// <summary>
    /// 获取 JsonElement 属性
    /// </summary>
    internal static object? GetJsonElementProperty(System.Text.Json.JsonElement element, string propertyName)
    {
        if (propertyName.Equals("length", StringComparison.OrdinalIgnoreCase) ||
            propertyName.Equals("count", StringComparison.OrdinalIgnoreCase))
        {
            if (element.ValueKind == System.Text.Json.JsonValueKind.Array)
            {
                return element.GetArrayLength();
            }

            if (element.ValueKind == System.Text.Json.JsonValueKind.String)
            {
                var str = element.GetString();
                return str?.Length ?? 0;
            }
        }

        if (element.ValueKind == System.Text.Json.JsonValueKind.Object &&
            element.TryGetProperty(propertyName, out var property))
        {
            return property.ValueKind switch
            {
                System.Text.Json.JsonValueKind.String => property.GetString(),
                System.Text.Json.JsonValueKind.Number => property.TryGetInt64(out var longValue) ? longValue : property.GetDouble(),
                System.Text.Json.JsonValueKind.True => true,
                System.Text.Json.JsonValueKind.False => false,
                _ => property
            };
        }

        return null;
    }
}
