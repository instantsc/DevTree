using System;
using System.Collections;
using System.Collections.Generic;
using ExileCore.Shared.Helpers;
using GameOffsets.Native;
using ImGuiNET;
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

        public static bool IsSimpleType(Type type)
        {
            return type.IsPrimitive || PrimitiveTypes.Contains(type) || Convert.GetTypeCode(type) != TypeCode.Object ||
                   type.BaseType == typeof(Enum) || type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Nullable<>) &&
                   IsSimpleType(type.GetGenericArguments()[0]);
        }

        private bool ColoredTreeNode(string text, Color color, object entity)
        {
            ImGui.PushStyleColor(ImGuiCol.Text, color.ToImgui());
            var result = TreeNode(text, entity);
            ImGui.PopStyleColor();
            return result;
        }

        private bool TreeNode(string text, object entity)
        {
            var result = ImGui.TreeNode(text);
            if (ImGui.IsItemHovered())
            {
                _lastHoveredMenuItem = entity;
            }

            return result;
        }
    }
}
