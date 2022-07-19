using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace DevTree;

public static class Extensions
{
    public static IEnumerable<PropertyInfo> GetAllProperties(this Type type)
    {
        return type
            .GetTypeChain()
            .SelectMany(x => x.GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance))
            .DistinctBy(x => (x.Name, x.DeclaringType));
    }

    public static IEnumerable<Type> GetTypeChain(this Type type)
    {
        while (type != null)
        {
            yield return type;
            type = type.BaseType;
        }
    }

    public static string ToHexString(this int value)
    {
        return $"0x{value:X}";
    }
}