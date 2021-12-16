using System;
using System.Collections;
using System.Collections.Generic;
using GameOffsets.Native;
using SharpDX;

namespace DevTree
{
    public partial class DevPlugin
    {
        private static readonly HashSet<Type> PrimitiveTypes = new HashSet<Type>
        {
            typeof(Enum),
            typeof(string),
            typeof(decimal),
            typeof(DateTime),
            typeof(TimeSpan),
            typeof(Guid),
            typeof(Vector2),
            typeof(Vector2i),
            typeof(Vector3),
            typeof(ColorBGRA)
        };

        public static bool IsEnumerable(Type type)
        {
            return type != typeof(string) && typeof(IEnumerable).IsAssignableFrom(type);
        }

        public static bool IsCollection(Type type)
        {
            return typeof(ICollection).IsAssignableFrom(type) || typeof(ICollection<>).IsAssignableFrom(type);
        }

        public static bool IsSimpleType(Type type)
        {
            return type.IsPrimitive || PrimitiveTypes.Contains(type) || Convert.GetTypeCode(type) != TypeCode.Object ||
                   type.BaseType == typeof(Enum) || type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Nullable<>) &&
                   IsSimpleType(type.GetGenericArguments()[0]);
        }
    }
}
