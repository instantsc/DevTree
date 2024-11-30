using System;
using System.Collections;
using System.Collections.Generic;
using System.Drawing;
using ExileCore2.Shared.Helpers;
using GameOffsets2.Native;
using ImGuiNET;

namespace DevTree;

public partial class DevPlugin
{
    private static readonly HashSet<Type> PrimitiveTypes =
    [
        typeof(Enum),
        typeof(string),
        typeof(decimal),
        typeof(DateTime),
        typeof(TimeSpan),
        typeof(Guid),
        typeof(System.Numerics.Vector2),
        typeof(System.Numerics.Vector3),
        typeof(System.Numerics.Vector4),
        typeof(Vector2i),
    ];

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

    private bool ColoredTreeNode(string text, Color color, object entity, out bool isHovered)
    {
        ImGui.PushStyleColor(ImGuiCol.Text, color.ToImgui());
        var result = TreeNode(text, entity, out isHovered);
        ImGui.PopStyleColor();
        return result;
    }

    private bool TreeNode(string text, object entity) => TreeNode(text, entity, out _);

    private bool TreeNode(string text, object entity, out bool isHovered)
    {
        var result = ImGui.TreeNode(text);
        isHovered = ImGui.IsItemHovered();
        if (isHovered)
        {
            _lastHoveredMenuItem = entity;
        }

        return result;
    }
}