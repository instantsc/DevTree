using System;
using System.Collections;
using System.Collections.Generic;
using ExileCore.Shared.Helpers;
using GameOffsets.Native;
using ImGuiNET;

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
            typeof(SharpDX.Vector2),
            typeof(SharpDX.Vector3),
            typeof(SharpDX.Vector4),
            typeof(System.Numerics.Vector2),
            typeof(System.Numerics.Vector3),
            typeof(System.Numerics.Vector4),
            typeof(Vector2i),
            typeof(SharpDX.ColorBGRA)
        };

        public static bool IsEnumerable(Type type)
        {
            return type != typeof(string) && typeof(IEnumerable).IsAssignableFrom(type);
        }

        public static bool IsSimpleType(Type type)
        {
            return type.IsPrimitive ||
                   PrimitiveTypes.Contains(type) ||
                   Convert.GetTypeCode(type) != TypeCode.Object ||
                   type.BaseType == typeof(Enum) ||
                   type.IsGenericType &&
                   type.GetGenericTypeDefinition() == typeof(Nullable<>) &&
                   IsSimpleType(type.GetGenericArguments()[0]);
        }

        private bool ColoredTreeNode(string text, SharpDX.Color color, object entity)
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