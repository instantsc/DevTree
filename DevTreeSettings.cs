using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Windows.Forms;
using ExileCore.Shared.Attributes;
using ExileCore.Shared.Interfaces;
using ExileCore.Shared.Nodes;
using ImGuiNET;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using SharpDX;
using Vector4 = System.Numerics.Vector4;

namespace DevTree;

public class DevSetting : ISettings
{
    public ToggleNode ToggleWindowUsingHotkey { get; set; } = new ToggleNode(false);

    public HotkeyNode ToggleWindowKey { get; set; } = new HotkeyNode(Keys.NumPad9);

    public HotkeyNode DebugUIHoverItemKey { get; set; } = Keys.NumPad5;

    public HotkeyNode SaveHoveredDevTreeNodeKey { get; set; } = Keys.NumPad8;

    public RangeNode<int> NearestEntitiesRange { get; set; } = new(300, 1, 2000);

    [Menu(null, "Size of displayed collection slice")]
    public RangeNode<int> LimitForCollections { get; set; } = new(500, 2, 5000);

    public ColorNode FrameColor { get; set; } = new ColorNode(Color.Yellow);
    public ColorNode ErrorColor { get; set; } = new ColorNode(Color.Red);

    public ToggleNode HideAddresses { get; set; } = new ToggleNode(false);
    public ToggleNode RegisterInspector { get; set; } = new ToggleNode(true);
    public ToggleNode Enable { get; set; } = new(false);

    public ExclusionSettings ExclusionSettings { get; set; } = new();

    public bool ToggleWindowState; //Just save the state
}

[Submenu(CollapsedByDefault = true, EnableSelfDrawCollapsing = true, RenderMethod = nameof(Render))]
public class ExclusionSettings
{
    private static List<ExcludedMember> DefaultExclusions =>
    [
        new ExcludedMember { ContainingType = "ExileCore.PoEMemory.RemoteMemoryObject", Name = "M", Type = ExcludedMemberType.Property },
        new ExcludedMember { ContainingType = "ExileCore.PoEMemory.RemoteMemoryObject", Name = "TheGame", Type = ExcludedMemberType.Property },
        new ExcludedMember { ContainingType = "ExileCore.PoEMemory.RemoteMemoryObject", Name = "Address", Type = ExcludedMemberType.Property },
        new ExcludedMember { ContainingType = "ExileCore.PoEMemory.RemoteMemoryObject", Name = "CoreSettings", Type = ExcludedMemberType.Property },
        new ExcludedMember { ContainingType = "ExileCore.PoEMemory.RemoteMemoryObject", Name = "Cache", Type = ExcludedMemberType.Property },
        new ExcludedMember { ContainingType = "ExileCore.PoEMemory.RemoteMemoryObject", Name = "pCache", Type = ExcludedMemberType.Property },
        new ExcludedMember { ContainingType = "ExileCore.PoEMemory.RemoteMemoryObject", Name = "pM", Type = ExcludedMemberType.Property },
        new ExcludedMember { ContainingType = "ExileCore.PoEMemory.RemoteMemoryObject", Name = "pTheGame", Type = ExcludedMemberType.Property },
    ];

    public List<ExcludedMember> Exclusions { get; set; }

    private readonly ConditionalWeakTable<ExcludedMember, MemberInfo> _resolvedInfo = new();

    public void Render(DevPlugin plugin)
    {
        var exclusionList = Exclusions ?? DefaultExclusions;
        int i = 0;

        void TryUpdateExclusionList()
        {
            Exclusions ??= exclusionList;
            RebuildExcludedMemberList();
        }

        void RebuildExcludedMemberList()
        {
            _resolvedInfo.Clear();
            plugin.IgnoredProperties = GetExcludedMemberInfos();
        }

        if (Exclusions != null && ImGui.Button("Reset"))
        {
            Exclusions = null;
            RebuildExcludedMemberList();
        }

        if (ImGui.Button("Rebuild excluded member list"))
        {
            RebuildExcludedMemberList();
        }

        if (ImGui.BeginTable("Excluded Members", 4, ImGuiTableFlags.SizingFixedFit | ImGuiTableFlags.Borders))
        {
            ImGui.TableSetupColumn("Type", ImGuiTableColumnFlags.WidthStretch, 200);
            ImGui.TableSetupColumn("Name", ImGuiTableColumnFlags.WidthStretch, 200);
            ImGui.TableSetupColumn("Delete", ImGuiTableColumnFlags.WidthFixed);
            ImGui.TableSetupColumn("State", ImGuiTableColumnFlags.WidthFixed);
            ImGui.TableHeadersRow();
            foreach (var excludedMember in exclusionList.ToList())
            {
                ImGui.TableNextRow(ImGuiTableRowFlags.None, 24);
                ImGui.PushID(ImGui.TableGetRowIndex());
                ImGui.TableNextColumn();
                ImGui.SetNextItemWidth(ImGui.GetColumnWidth());
                if (ImGui.InputText("##type", ref excludedMember.ContainingType, 500))
                {
                    TryUpdateExclusionList();
                }

                ImGui.TableNextColumn();
                ImGui.SetNextItemWidth(ImGui.GetColumnWidth());
                if (ImGui.InputText("##name", ref excludedMember.Name, 200))
                {
                    TryUpdateExclusionList();
                }

                ImGui.TableNextColumn();
                if (ImGui.Button("Delete"))
                {
                    exclusionList.Remove(excludedMember);
                    TryUpdateExclusionList();
                }

                ImGui.TableNextColumn();
                var memberFound = LookupMember(excludedMember) != null;
                ImGui.TextColored(memberFound ? new Vector4(0, 1, 0, 1) : new Vector4(1, 0, 0, 1), memberFound ? "Found" : "Not found");
                ImGui.PopID();
            }

            ImGui.EndTable();
        }


        if (ImGui.Button("Add"))
        {
            exclusionList.Add(new ExcludedMember { ContainingType = "", Name = "" });
            TryUpdateExclusionList();
        }
    }

    internal HashSet<(Type DeclaringType, string Name)> GetExcludedMemberInfos()
    {
        var exclusionList = Exclusions ?? DefaultExclusions;
        var set = new HashSet<(Type DeclaringType, string Name)>();
        foreach (var excludedMember in exclusionList)
        {
            if (_resolvedInfo.GetValue(excludedMember, LookupMember) is { } member)
            {
                set.Add((member.DeclaringType, member.Name));
            }
        }

        return set;
    }

    private static MemberInfo LookupMember(ExcludedMember member)
    {
        if (string.IsNullOrWhiteSpace(member.ContainingType) || string.IsNullOrWhiteSpace(member.Name))
        {
            return null;
        }

        var type = AppDomain.CurrentDomain.GetAssemblies().Select(x => x.GetType(member.ContainingType)).FirstOrDefault(x => x != null);
        return type?.GetAllProperties().FirstOrDefault(x => x.Name == member.Name && x.DeclaringType?.FullName == member.ContainingType);
    }
}

public class ExcludedMember
{
    public ExcludedMemberType Type;
    public string ContainingType;
    public string Name;
}

[JsonConverter(typeof(StringEnumConverter))]
public enum ExcludedMemberType
{
    Property,
    Field,
}