using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using ExileCore2.PoEMemory;

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

    public static IEnumerable<MethodInfo> GetAllMethods(this Type type)
    {
        return type
            .GetTypeChain()
            .SelectMany(x => x.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance))
            .DistinctBy(x => (x.Name, string.Join(";", x.GetParameters().Select(t => t.ParameterType.AssemblyQualifiedName)), x.DeclaringType));
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

    public static void Resize<T>(this List<T> list, int newSize, T defaultElement = default)
    {
        int oldSize = list.Count;
        if (newSize < oldSize)
        {
            list.RemoveRange(newSize, oldSize - newSize);
        }
        else if (newSize > oldSize)
        {
            if (newSize > list.Capacity)
            {
                list.Capacity = newSize;
            }

            list.AddRange(Enumerable.Repeat(defaultElement, newSize - oldSize));
        }
    }

    public static long GetAddress(this RemoteMemoryObject rmo, bool hide)
    {
        if (hide)
        {
            return 0xDEADBEEF;
        }

        return rmo.Address;
    }
}