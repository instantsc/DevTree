using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Windows.Forms;
using ExileCore;
using ExileCore.PoEMemory;
using ExileCore.PoEMemory.Components;
using ExileCore.PoEMemory.Elements;
using ExileCore.PoEMemory.MemoryObjects;
using ExileCore.Shared;
using ExileCore.Shared.Enums;
using ExileCore.Shared.Helpers;
using ImGuiNET;
using SharpDX;
using ImGuiVector4 = System.Numerics.Vector4;

namespace DevTree
{
    public partial class DevPlugin : BaseSettingsPlugin<DevSetting>
    {
        private const BindingFlags Flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static |
                                           BindingFlags.FlattenHierarchy;

        private static readonly HashSet<string> IgnoredRMOProperties = new HashSet<string> { "M", "TheGame", "Address" };
        private static readonly MethodInfo GetComponentMethod = typeof(Entity).GetMethod("GetComponent");
        private readonly Dictionary<string, MethodInfo> _genericMethodCache = new Dictionary<string, MethodInfo>();
        private readonly Dictionary<string, object> _debugObjects = new Dictionary<string, object>();
        private readonly Dictionary<string, object> _dynamicTabCache = new Dictionary<string, object>();
        private readonly Dictionary<string, int> _collectionSkipValues = new Dictionary<string, int>();
        private readonly Dictionary<string, string> _collectionSearchValues = new Dictionary<string, string>();
        private readonly ConditionalWeakTable<object, string> _objectSearchValues = new ConditionalWeakTable<object, string>();
        private List<Entity> _debugEntities = new List<Entity>();
        private string _inputFilter = "";
        private string _guiObjAddr = "";
        private MonsterRarity? _selectedRarity;
        private bool _windowState;
        private object _lastHoveredMenuItem = null;
        public Func<List<PluginWrapper>> Plugins;
        private Element UIHoverWithFallback => GameController.IngameState.UIHover switch { null or { Address: 0 } => GameController.IngameState.UIHoverElement, var s => s };

        public override void OnLoad()
        {
            Plugins = () => new List<PluginWrapper>();
        }

        public override bool Initialise()
        {
            Force = true;

            try
            {
                InitObjects();
            }
            catch (Exception e)
            {
                LogError($"{e}");
            }

            Input.RegisterKey(Settings.ToggleWindowKey);
            Input.RegisterKey(Settings.DebugUIHoverItemKey);
            Settings.DebugUIHoverItemKey.OnValueChanged += () => { Input.RegisterKey(Settings.DebugUIHoverItemKey); };
            Settings.ToggleWindowKey.OnValueChanged += () => { Input.RegisterKey(Settings.ToggleWindowKey); };
            Name = "DevTree";
            return true;
        }

        public override void AreaChange(AreaInstance area)
        {
            InitObjects();
            _collectionSkipValues.Clear();
            _collectionSearchValues.Clear();
            _dynamicTabCache.Clear();
        }

        private void InitObjects()
        {
            _debugObjects.Clear();
            AddObjects(GameController.Cache);
            AddObjects(GameController);
            AddObjects(GameController.Game);
            AddObjects(GameController.Player, "Player");
            AddObjects(GameController.IngameState, "IngameState");
            AddObjects(GameController.IngameState.IngameUi, "IngameState.IngameUi");
            AddObjects(GameController.IngameState.Data, "IngameState.Data");
            AddObjects(GameController.IngameState.Data.ServerData, "IngameState.Data.ServerData");
            AddObjects(GameController.IngameState.Data.ServerData.PlayerInventories[0].Inventory, "IngameState.Data.ServerData.PlayerInventories[0].Inventory");
            AddObjects(GameController.IngameState.Data.ServerData.PlayerInventories[0].Inventory.Items, "-> Items");
            AddObjects(GameController.IngameState.IngameUi.InventoryPanel[InventoryIndex.PlayerInventory], "IngameState.IngameUi.InventoryPanel[InventoryIndex.PlayerInventory]");
            AddObjects(GameController.IngameState.IngameUi.ItemsOnGroundLabels, "IngameState.IngameUi.ItemsOnGroundLabels");
            var pluginWrappers = Plugins();
            AddObjects(pluginWrappers, "PluginWrappers");
        }

        private void AddObjects(object o, string name = null)
        {
            if (o == null)
            {
                DebugWindow.LogError($"{Name} cant add object to debug.");
                return;
            }

            var type = o.GetType();
            name ??= $"{type.Name}";

            if (o is RemoteMemoryObject or FileInMemory)
            {
                var propertyInfo = type.GetProperty("Address");
                if (propertyInfo != null) name += $" ({(long)propertyInfo.GetValue(o, null):X})##InitObject";
            }

            _debugObjects[name] = o;
        }

        public override void Render()
        {
            if (Settings.ToggleWindowUsingHotkey)
            {
                if (Settings.ToggleWindowKey.PressedOnce())
                {
                    Settings.ToggleWindowState = !Settings.ToggleWindowState;
                }

                if (!Settings.ToggleWindowState)
                    return;
            }

            if (Settings.DebugUIHoverItemKey.PressedOnce())
            {
                var ingameStateUiHover = UIHoverWithFallback;
                var hoverItemIcon = ingameStateUiHover.AsObject<HoverItemIcon>();
                if (ingameStateUiHover.Address != 0)
                {
                    AddObjects(new { Hover = ingameStateUiHover, HoverLikeItem = hoverItemIcon }, "Stored UIHover");
                }
            }

            if (Settings.SaveHoveredDevTreeNodeKey.PressedOnce())
            {
                if (_lastHoveredMenuItem != null)
                {
                    AddObjects(_lastHoveredMenuItem, "Stored tree node");
                }
            }

            var isKeyDown = Input.IsKeyDown(Keys.V);
            var keyDown = Input.IsKeyDown(Keys.ControlKey);
            if (isKeyDown && keyDown)
            {
                var clipboardText = GetClipboardText();
                if (!string.IsNullOrWhiteSpace(clipboardText))
                {
                    if (clipboardText.StartsWith("0x"))
                        clipboardText = clipboardText.Substring(2);
                    if (long.TryParse(clipboardText, NumberStyles.HexNumber, CultureInfo.InvariantCulture,
                            out var parsedAddress) && parsedAddress != 0)
                    {
                        var ingameStateUiHover = GameController.Game.GetObject<Element>(parsedAddress);
                        var hoverItemIcon = ingameStateUiHover.AsObject<HoverItemIcon>();
                        AddObjects(new { Hover = ingameStateUiHover, HoverLikeItem = hoverItemIcon }, "Stored UIHover");
                    }
                }
            }

            _windowState = Settings.Enable;
            ImGui.Begin($"{Name}", ref _windowState);

            if (Settings.Enable != _windowState)
            {
                if (!Settings.ToggleWindowUsingHotkey)
                    Settings.Enable.Value = _windowState;
                else
                    Settings.ToggleWindowState = _windowState;
            }

            if (ImGui.Button("Reload")) InitObjects();

            ImGui.SameLine();
            ImGui.PushItemWidth(200);
			var mem = GameController.Memory;
			var fileRootAddr = mem.AddressOfProcess + mem.BaseOffsets[OffsetsName.FileRoot];
			ImGui.Text($"FileRoot: {fileRootAddr:X}");

            ImGui.InputText("Filter", ref _inputFilter, 128);

            ImGui.PopItemWidth();
            ImGui.SameLine();
            ImGui.PushItemWidth(128);

            if (ImGui.BeginCombo("Rarity", _selectedRarity?.ToString()??"All"))
            {
                foreach (var rarity in Enum.GetValues<MonsterRarity>().Cast<MonsterRarity?>().Append(null))
                {
                    var isSelected = _selectedRarity == rarity;

                    if (ImGui.Selectable(rarity?.ToString()??"All", isSelected))
                    {
                        _selectedRarity = rarity;
                    }

                    if (isSelected) ImGui.SetItemDefaultFocus();
                }

                ImGui.EndCombo();
            }

            ImGui.PopItemWidth();
            ImGui.SameLine();

            if (ImGui.Button("Debug around entities"))
            {
                var playerGridPos = GameController.Player.GridPos;
                _debugEntities = GameController.Entities
                    .Where(x => string.IsNullOrEmpty(_inputFilter) ||
                                x.Path.Contains(_inputFilter) ||
                                x.Address.ToString("x").Contains(_inputFilter, StringComparison.OrdinalIgnoreCase))
                    .Where(x => _selectedRarity == null || x.GetComponent<ObjectMagicProperties>()?.Rarity == _selectedRarity)
                    .Where(x => x.GridPos.Distance(playerGridPos) < Settings.NearestEntitiesRange)
                    .OrderBy(x => x.GridPos.Distance(playerGridPos))
                    .ToList();
            }

            ImGui.SameLine();
            ImGui.PushItemWidth(128);
            if (ImGui.InputText("CheckAddressIsGuiObject", ref _guiObjAddr, 128))
            {
                if (long.TryParse(_guiObjAddr, NumberStyles.HexNumber, CultureInfo.CurrentCulture, out var objAddr))
                {
                    var queue = new Queue<Element>();
                    queue.Enqueue(GameController.Game.IngameState.UIRoot);
                    var found = false;
                    while (queue.Count > 0)
                    {
                        var element = queue.Dequeue();

                        if (element.Address == objAddr)
                        {
                            var indexPath = new List<int>();
                            var iterator = element;

                            while (iterator != null && iterator.Address != 0)
                            {
                                if (iterator.Parent != null && iterator.Parent.Address != 0)
                                    indexPath.Add(iterator.Parent.Children.ToList().FindIndex(x => x.Address == iterator.Address));

                                iterator = iterator.Parent;
                            }

                            indexPath.Reverse();

                            LogMessage("IS gui element!" + $"Path from root: [{string.Join(", ", indexPath)}]", 3);
                            found = true;
                            break;
                        }

                        foreach (var elementChild in element.Children)
                        {
                            queue.Enqueue(elementChild);
                        }
                    }

                    if (!found)
                        LogMessage("NOT a gui element!", 3);
                }
            }

            foreach (var o in _debugObjects)
            {
                if (TreeNode($"{o.Key}##0", o.Value))
                {
                    ImGui.Indent();

                    try
                    {
                        Debug(o.Value);
                    }
                    catch (Exception e)
                    {
                        DebugWindow.LogError($"{Name} -> {e}");
                    }

                    finally
                    {
                        ImGui.Unindent();
                        ImGui.TreePop();
                    }
                }
            }

            if (ImGui.TreeNode("UIHover"))
            {
                ImGui.Indent();

                try
                {
                    Debug(UIHoverWithFallback);
                }
                catch (Exception e)
                {
                    DebugWindow.LogError($"UIHover -> {e}");
                }
                finally
                {
                    ImGui.Unindent();
                    ImGui.TreePop();
                }
            }

            if (ImGui.TreeNode("UIHover as Item"))
            {
                ImGui.Indent();

                try
                {
                    Debug((UIHoverWithFallback).AsObject<HoverItemIcon>());
                }
                catch (Exception e)
                {
                    DebugWindow.LogError($"UIHover -> {e}");
                }
                finally
                {
                    ImGui.Unindent();
                    ImGui.TreePop();
                }
            }

            if (ImGui.TreeNode("Only visible InGameUi"))
            {
                ImGui.Indent();
                var os = GameController.IngameState.IngameUi.Children.Where(x => x.IsVisibleLocal);

                foreach (var el in os)
                {
                    try
                    {
                        if (TreeNode($"{el.Address:X} - {el.X}:{el.Y},{el.Width}:{el.Height}##{el.GetHashCode()}", el))
                        {
                            var keyForOffset = $"{el.Address}{el.GetHashCode()}";

                            if (_dynamicTabCache.TryGetValue(keyForOffset, out var offset))
                                ImGui.Text($"Offset: {offset:X}");
                            else
                            {
                                var IngameUi = GameController.IngameState.IngameUi;
                                var pointers = IngameUi.M.ReadPointersArray(IngameUi.Address, IngameUi.Address + 10000);

                                for (var i = 0; i < pointers.Count; i++)
                                {
                                    var p = pointers[i];
                                    if (p == el.Address) _dynamicTabCache[keyForOffset] = i * 0x8;
                                }
                            }

                            Debug(el);
                            ImGui.TreePop();
                        }

                        if (ImGui.IsItemHovered())
                        {
                            var clientRectCache = el.GetClientRectCache;
                            Graphics.DrawFrame(clientRectCache, Settings.FrameColor, 1);

                            foreach (var element in el.Children)
                            {
                                clientRectCache = element.GetClientRectCache;
                                Graphics.DrawFrame(clientRectCache, Settings.FrameColor, 1);
                            }
                        }

                    }
                    catch (Exception e)
                    {
                        DebugWindow.LogError($"UIHover -> {e}");
                    }
                }

                ImGui.Unindent();
                ImGui.TreePop();
            }

            if (_debugEntities.Count > 0 && ImGui.TreeNode($"Entities {_debugEntities.Count}"))
            {
                var camera = GameController.IngameState.Camera;

                for (var index = 0; index < _debugEntities.Count; index++)
                {
                    var debugEntity = _debugEntities[index];
                    var worldtoscreen = camera.WorldToScreen(debugEntity.Pos);

                    Graphics.DrawText($"{index}", worldtoscreen);

                    if (TreeNode($"[{index}] {debugEntity}", debugEntity))
                    {
                        Debug(debugEntity);
                        ImGui.TreePop();
                    }

                    var borderColor = Color.Black;
                    if (ImGui.IsItemHovered())
                    {
                        borderColor = Color.DarkSlateGray;
                    }

                    Graphics.DrawBox(worldtoscreen.TranslateToNum(-9, -9), worldtoscreen.TranslateToNum(18, 18), borderColor);
                }
            }

            ImGui.End();
        }

        private static string GetClipboardText()
        {
            var text = "";
            var staThread = new Thread(() =>
            {
                try
                {
                    text = Clipboard.GetText();
                }
                catch
                {
                }
            });
            staThread.SetApartmentState(ApartmentState.STA);
            staThread.Start();
            staThread.Join();
            return text;
        }

        public void Debug(object obj, Type type = null)
        {
            try
            {
                if (obj == null)
                {
                    ImGui.TextColored(Settings.ErrorColor.Value.ToImguiVec4(), "Null");
                    return;
                }

                type ??= obj.GetType();

                if (Convert.GetTypeCode(obj) == TypeCode.Object)
                {
                    var methodInfo = type.GetMethod("ToString", Type.EmptyTypes);

                    if (methodInfo != null && (methodInfo.Attributes & MethodAttributes.VtableLayoutMask) == 0)
                    {
                        try
                        {
                            var toString = methodInfo.Invoke(obj, null);
                            if (toString != null) ImGui.TextColored(Color.Orange.ToImguiVec4(), toString.ToString());
                        }
                        catch (Exception ex)
                        {
                            LogError($"ToString() -> {ex}");
                            ImGui.TextColored(Settings.ErrorColor.Value.ToImguiVec4(), "ToString(): <exception thrown>");
                        }
                    }
                }

                if (type.BaseType == typeof(MulticastDelegate) && type.GenericTypeArguments.Length == 1)
                {
                    ImGui.TextColored(Color.Lime.ToImguiVec4(), type.GetMethod("Invoke")?.Invoke(obj, null).ToString());

                    return;
                }

                //IEnumerable from start
                if (obj is IEnumerable enumerable)
                {
                    var index = 0;

                    foreach (var col in enumerable)
                    {
                        var colType = col.GetType();

                        string colName = col switch
                        {
                            Entity e => e.Path,
                            _ => colType.Name
                        };

                        var methodInfo = colType.GetMethod("ToString", Type.EmptyTypes);

                        if (methodInfo != null && (methodInfo.Attributes & MethodAttributes.VtableLayoutMask) == 0)
                        {
                            var toString = methodInfo.Invoke(col, null);
                            if (toString != null) colName = $"{toString}";
                        }

                        if (TreeNode($"[{index}] {colName}", col))
                        {
                            Debug(col, colType);

                            ImGui.TreePop();
                        }

                        index++;
                    }

                    return;
                }

                if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(KeyValuePair<,>))
                {
                    var key = type.GetProperty("Key").GetValue(obj, null);
                    var value = type.GetProperty("Value").GetValue(obj, null);
                    var valueType = value?.GetType();

                    if (valueType != null && IsEnumerable(valueType))
                    {
                        var count = valueType.GetProperty("Count")?.GetValue(value, null);

                        if (TreeNode($"{key} {count}", value))
                        {
                            Debug(value);
                            ImGui.TreePop();
                        }
                    }
                }

                var isMemoryObject = obj as RemoteMemoryObject;

                if (isMemoryObject is { Address: 0 })
                {
                    ImGui.TextColored(Settings.ErrorColor.Value.ToImguiVec4(), "Address 0. Cant read this object.");
                    return;
                }

                ImGui.Indent();
                _objectSearchValues.TryGetValue(obj, out var objectFilter);
                objectFilter ??= "";
                ImGui.BeginTabBar("Tabs");

                if (ImGui.BeginTabItem("Properties"))
                {
                    DebugObjectProperties(obj, type, objectFilter);

                    ImGui.EndTabItem();
                }

                if (ImGui.BeginTabItem("Fields"))
                {
                    DebugObjectFields(obj, type, objectFilter);
                    ImGui.EndTabItem();
                }

                if (isMemoryObject != null && ImGui.BeginTabItem("Dynamic"))
                {
                    var remoteMemoryObject = (RemoteMemoryObject)obj;
                    ImGui.TextColored(Color.GreenYellow.ToImguiVec4(), "Address: ");
                    if (ImGui.IsItemClicked()) ImGui.SetClipboardText(remoteMemoryObject.Address.ToString());

                    ImGui.SameLine();

                    ImGui.PushStyleColor(ImGuiCol.Text, new ImGuiVector4(1, 0.647f, 0, 1));
                    ImGui.PushStyleColor(ImGuiCol.Button, new ImGuiVector4(0, 0, 0, 0));
                    ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new ImGuiVector4(0.25f, 0.25f, 0.25f, 1));
                    ImGui.PushStyleColor(ImGuiCol.ButtonActive, new ImGuiVector4(1, 1, 1, 1));

                    if (ImGui.SmallButton($"{remoteMemoryObject.Address:X}")) ImGui.SetClipboardText(remoteMemoryObject.Address.ToString());

                    ImGui.PopStyleColor(4);
                    ImGui.EndTabItem();

                    var key = remoteMemoryObject switch
                    {
                        Entity e => $"{e.Address}{e.Id}{e.Path}",
                        Element e => $"{e.Address}{e.GetHashCode()}",
                        _ => null
                    };
                    if (key != null)
                    {
                        if (!_dynamicTabCache.TryGetValue(key, out var cachedValue))
                        {
                            if (GetDynamicTabObject(remoteMemoryObject, out cachedValue) && cachedValue != null)
                            {
                                _dynamicTabCache[key] = cachedValue;
                            }
                        }

                        if (cachedValue != null)
                        {
                            DebugObjectProperties(cachedValue, cachedValue.GetType(), objectFilter);
                        }
                    }
                }

                ImGui.PushItemWidth(0);
                ImGui.PushStyleColor(ImGuiCol.Tab, ImGui.GetColorU32(ImGuiCol.WindowBg));
                ImGui.PushStyleColor(ImGuiCol.TabHovered, ImGui.GetColorU32(ImGuiCol.WindowBg));
                ImGui.TabItemButton("##emptybutton");
                ImGui.PopStyleColor(2);
                ImGui.PopItemWidth();
                ImGui.EndTabBar();

                var oldPos = ImGui.GetCursorPos();
                ImGui.SameLine(0, 0);
                ImGui.SetCursorPosY(ImGui.GetCursorPosY() - 1);
                if (ImGui.InputTextWithHint("##objFilterEdit", "Filter", ref objectFilter, 200))
                {
                    _objectSearchValues.AddOrUpdate(obj, objectFilter);
                }
                ImGui.SetCursorPos(oldPos);
                ImGui.Unindent();
            }
            catch (Exception e)
            {
                LogError($"{Name} -> {e}");
            }
        }

        private void DebugObjectFields(object obj, Type type, string filter)
        {
            var fields = type.GetFields(Flags)
                .Where(x => string.IsNullOrEmpty(filter) || x.Name.Contains(filter, StringComparison.OrdinalIgnoreCase));

            foreach (var field in fields)
            {
                var fieldValue = field.GetValue(obj);

                if (IsSimpleType(field.FieldType))
                {
                    ImGui.Text($"{field.Name}: ");
                    ImGui.SameLine();
                    ImGui.PushStyleColor(ImGuiCol.Text, new ImGuiVector4(1, 0.647f, 0, 1));
                    ImGui.PushStyleColor(ImGuiCol.Button, new ImGuiVector4(0, 0, 0, 0));
                    ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new ImGuiVector4(0.25f, 0.25f, 0.25f, 1));
                    ImGui.PushStyleColor(ImGuiCol.ButtonActive, new ImGuiVector4(1, 1, 1, 1));

                    if (ImGui.SmallButton($"{fieldValue}")) ImGui.SetClipboardText($"{fieldValue}");

                    ImGui.PopStyleColor(4);
                }
                else if (TreeNode($"{field.Name} {type.FullName}", fieldValue))
                {
                    Debug(fieldValue);
                    ImGui.TreePop();
                }
            }
        }

        private void DebugObjectProperties(object obj, Type type, string filter)
        {
            if (obj is RemoteMemoryObject asMemoryObject)
            {
                ImGui.Text("Address: ");
                ImGui.SameLine();
                ImGui.PushStyleColor(ImGuiCol.Text, new ImGuiVector4(1, 0.647f, 0, 1));
                ImGui.PushStyleColor(ImGuiCol.Button, new ImGuiVector4(0, 0, 0, 0));
                ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new ImGuiVector4(0.25f, 0.25f, 0.25f, 1));
                ImGui.PushStyleColor(ImGuiCol.ButtonActive, new ImGuiVector4(1, 1, 1, 1));

                if (ImGui.SmallButton($"{asMemoryObject.Address:X}"))
                    ImGui.SetClipboardText($"{asMemoryObject.Address:X}");

                ImGui.PopStyleColor(4);

                if (asMemoryObject is Component asComponent)
                {
                    ImGui.Text("OwnerAddress: ");
                    ImGui.SameLine();
                    ImGui.PushStyleColor(ImGuiCol.Text, new ImGuiVector4(1, 0.647f, 0, 1));
                    ImGui.PushStyleColor(ImGuiCol.Button, new ImGuiVector4(0, 0, 0, 0));
                    ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new ImGuiVector4(0.25f, 0.25f, 0.25f, 1));
                    ImGui.PushStyleColor(ImGuiCol.ButtonActive, new ImGuiVector4(1, 1, 1, 1));

                    if (ImGui.SmallButton($"{asComponent.OwnerAddress:X}")) ImGui.SetClipboardText($"{asComponent.OwnerAddress:X}");

                    ImGui.PopStyleColor(4);
                }

                switch (asMemoryObject)
                {
                    case Entity e:
                        if (e.CacheComp != null && ImGui.TreeNode($"Components: {e.CacheComp.Count}###__Components"))
                        {
                            foreach (var component in e.CacheComp)
                            {
                                var compFullName = typeof(Positioned).AssemblyQualifiedName.Replace(nameof(Positioned), component.Key);
                                var componentType = Type.GetType(compFullName);

                                if (componentType == null)
                                {
                                    ImGui.Text($"{component.Key}: Not implemented.");
                                    ImGui.SameLine();
                                    ImGui.PushStyleColor(ImGuiCol.Text, new ImGuiVector4(1, 0.647f, 0, 1));
                                    ImGui.PushStyleColor(ImGuiCol.Button, new ImGuiVector4(0, 0, 0, 0));
                                    ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new ImGuiVector4(0.25f, 0.25f, 0.25f, 1));
                                    ImGui.PushStyleColor(ImGuiCol.ButtonActive, new ImGuiVector4(1, 1, 1, 1));

                                    if (ImGui.SmallButton($"{component.Value:X}")) ImGui.SetClipboardText($"{component.Value:X}");

                                    ImGui.PopStyleColor(4);

                                    continue;
                                }

                                if (!_genericMethodCache.TryGetValue(component.Key, out var generic))
                                {
                                    generic = GetComponentMethod.MakeGenericMethod(componentType);
                                    _genericMethodCache[component.Key] = generic;
                                }

                                var g = generic.Invoke(e, null);

                                if (TreeNode(component.Key, g))
                                {
                                    Debug(g);
                                    ImGui.TreePop();
                                }
                            }

                            ImGui.TreePop();
                        }

                        if (e.HasComponent<Base>())
                        {
                            if (ImGui.TreeNode("Item info###__Base"))
                            {
                                var BIT = GameController.Files.BaseItemTypes.Translate(e.Path);
                                Debug(BIT);
                                ImGui.TreePop();
                            }
                        }

                        break;
                }
            }

            var properties = type.GetAllProperties()
                .Where(x => x.GetIndexParameters().Length == 0)
                .Where(x => string.IsNullOrEmpty(filter) || x.Name.Contains(filter, StringComparison.OrdinalIgnoreCase))
                .OrderBy(x => x.PropertyType.GetInterface("IEnumerable") != null)
                .ThenBy(x => x.Name);

            foreach (var property in properties)
            {
                try
                {
                    if (obj is RemoteMemoryObject && IgnoredRMOProperties.Contains(property.Name)) continue;

                    var propertyValue = property.GetValue(obj);

                    if (propertyValue == null)
                    {
                        ImGui.Text($"{property.Name}: ");
                        ImGui.SameLine();
                        ImGui.TextColored(Settings.ErrorColor.Value.ToImguiVec4(), "Null");
                        continue;
                    }

                    //Draw primitives
                    if (IsSimpleType(property.PropertyType))
                    {
                        ImGui.Text($"{property.Name}: ");
                        ImGui.SameLine();
                        ImGui.PushStyleColor(ImGuiCol.Text, new ImGuiVector4(1, 0.647f, 0, 1));
                        ImGui.PushStyleColor(ImGuiCol.Button, new ImGuiVector4(0, 0, 0, 0));
                        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new ImGuiVector4(0.25f, 0.25f, 0.25f, 1));
                        ImGui.PushStyleColor(ImGuiCol.ButtonActive, new ImGuiVector4(1, 1, 1, 1));

                        var propertyVal = property.Name == "Address" ? ((long)propertyValue).ToString("x") : propertyValue.ToString();
                        if (ImGui.SmallButton(propertyVal)) ImGui.SetClipboardText(propertyVal);

                        ImGui.PopStyleColor(4);
                    }
                    else
                    {
                        //Draw enumrable 
                        var isEnumerable = IsEnumerable(property.PropertyType);

                        if (isEnumerable)
                        {
                            if (propertyValue is not ICollection collection)
                                continue;

                            if (collection.Count > 0)
                            {
                                ImGui.TextColored(Color.OrangeRed.ToImguiVec4(), $"[{collection.Count}]");

                                var isElementEnumerable = property.PropertyType.GenericTypeArguments.Length == 1 &&
                                                          (property.PropertyType.GenericTypeArguments[0] == typeof(Element) ||
                                                           property.PropertyType.GenericTypeArguments[0].IsSubclassOf(typeof(Element)));

                                if (isElementEnumerable)
                                {
                                    if (ImGui.IsItemHovered())
                                    {
                                        var index = 0;

                                        foreach (var el in collection)
                                        {
                                            if (el is Element e)
                                            {
                                                var clientRectCache = e.GetClientRectCache;
                                                Graphics.DrawFrame(clientRectCache, Settings.FrameColor, 1);
                                                Graphics.DrawText(index.ToString(), clientRectCache.Center);
                                                index++;
                                            }
                                        }
                                    }
                                }

                                ImGui.SameLine();
                                var strId = $"{property.Name} ##{property.DeclaringType.FullName}";

                                if (ImGui.TreeNode(strId))
                                {
                                    var collectionKey = $"{strId} {obj.GetHashCode()}";

                                    var skip = _collectionSkipValues.GetValueOrDefault(collectionKey);
                                    if (ImGui.InputInt("Skip", ref skip, 1, 100))
                                    {
                                        _collectionSkipValues[collectionKey] = skip;
                                    }
                                    var search = _collectionSearchValues.GetValueOrDefault(collectionKey) ?? "";
                                    ImGui.SameLine();
                                    if (ImGui.InputTextWithHint("##filter", "Filter", ref search, 200))
                                    {
                                        _collectionSearchValues[collectionKey] = search;
                                    }

                                    foreach (var (col, index) in collection
                                                .Cast<object>()
                                                .Select((x, i) => (x, i))
                                                .Where(x => string.IsNullOrEmpty(search) || 
                                                            ToStringSafe(x)?.Contains(search, StringComparison.InvariantCultureIgnoreCase) == true)
                                                .Skip(skip)
                                                .Take(Settings.LimitForCollections))
                                    {
                                        if (col == null)
                                        {
                                            ImGui.TextColored(Settings.ErrorColor.Value.ToImguiVec4(), "Null");
                                            continue;
                                        }

                                        var colType = col.GetType();
                                        var colName = col switch
                                        {
                                            Entity e => e.Path,
                                            Inventory e => $"{e.InvType} Count: ({e.ItemCount}) Box:{e.TotalBoxesInInventoryRow}",
                                            Element { Text.Length: > 0 } e => $"{e.Text}##{index}",
                                            Element => $"{colType.Name}",
                                            _ => $"{colType.Name}"
                                        };

                                        if (IsSimpleType(colType))
                                            ImGui.TextUnformatted(col.ToString());
                                        else
                                        {
                                            Element element = null;

                                            if (isElementEnumerable)
                                            {
                                                element = col as Element;

                                                //  colName += $" ({element.ChildCount})";
                                                ImGui.Text($" ({element.ChildCount})");
                                                ImGui.SameLine();
                                            }
                                            else
                                            {
                                                var methodInfo = colType.GetMethod("ToString", Type.EmptyTypes);

                                                if (methodInfo != null &&
                                                    (methodInfo.Attributes & MethodAttributes.VtableLayoutMask) == 0)
                                                {
                                                    var toString = methodInfo?.Invoke(col, null);
                                                    if (toString != null) colName = $"{toString}";
                                                }
                                            }

                                            if (ColoredTreeNode($"[{index}] {colName} ###{index},{col.GetType().Name}", col switch
                                                {
                                                    Element { IsValid: false } => Color.DarkRed,
                                                    Element { IsVisible: true } => Color.Green,
                                                    _ => Color.White
                                                }, col))
                                            {
                                                Debug(col, colType);
                                                ImGui.TreePop();
                                            }

                                            if (element != null && ImGui.IsItemHovered() && element.Width > 0 && element.Height > 0)
                                            {
                                                Graphics.DrawFrame(element.GetClientRectCache, Settings.FrameColor, 2);
                                            }
                                        }
                                    }

                                    ImGui.TreePop();
                                }
                            }
                            else
                            {
                                ImGui.Indent();
                                ImGui.TextColored(Color.Red.ToImguiVec4(), $"{property.Name} [Empty]");
                                ImGui.Unindent();
                            }
                        }

                        //Debug others objects
                        else
                        {
                            if (property.Name.Equals("Value"))
                                Debug(propertyValue);
                            else
                            {
                                string name;
                                if (propertyValue is RemoteMemoryObject rmo)
                                    name = $"{property.Name} [{rmo.Address:X}]###{property.Name} {property.DeclaringType.FullName}";
                                else
                                    name = $"{property.Name} ###{property.Name} {type.FullName}";
                                if (ColoredTreeNode(name, propertyValue switch
                                    {
                                        Element { IsValid: false } => Color.DarkRed,
                                        Element { IsVisible: true } => Color.Green,
                                        _ => Color.White
                                    }, propertyValue))
                                {
                                    Debug(propertyValue);
                                    ImGui.TreePop();
                                }

                                if (propertyValue is Element { Width: > 0, Height: > 0 } e && ImGui.IsItemHovered())
                                {
                                    Graphics.DrawFrame(e.GetClientRectCache, Settings.FrameColor, 2);
                                }
                            }
                        }
                    }
                }
                catch (Exception e)
                {
                    ImGui.Text($"{property.Name}: ");
                    ImGui.SameLine();
                    ImGui.TextColored(Settings.ErrorColor.Value.ToImguiVec4(), "<exception thrown>");
                    LogError($"{property.Name} -> {e}");
                }
            }
        }

        private bool GetDynamicTabObject(RemoteMemoryObject remoteMemoryObject, out object result)
        {
            switch (remoteMemoryObject)
            {
                case Entity e:
                    if (e.GetComponent<Render>() != null)
                    {
                        var pos = e.Pos;
                        var renderComponentBounds = e.GetComponent<Render>().Bounds;
                        var toScreen = GameController.IngameState.Camera.WorldToScreen(pos);

                        result = new { Position = pos, ToScreen = toScreen };
                        Graphics.DrawFrame(toScreen.ToVector2Num(),
                            toScreen.TranslateToNum(renderComponentBounds.X, -renderComponentBounds.Y),
                            Color.Orange, 0, 1, 0);
                        return false;
                    }

                    result = null;
                    return false;
                case Element e:
                    var parentOffsets = ScanForElementOffset(e, e.Parent);
                    var ingameUiOffsets = ScanForElementOffset(e, GameController.IngameState.IngameUi);
                    result = new
                    {
                        ParentOffset = string.Join(", ", parentOffsets.Select(x => x.ToHexString())),
                        IngameUiOffset = string.Join(", ", ingameUiOffsets.Select(x => x.ToHexString()))
                    };
                    return true;
                default:
                    result = null;
                    return false;
            }
        }

        private static List<int> ScanForElementOffset(Element element, Element parentElement)
        {
            var offsets = new List<int>();
            if (parentElement != null)
            {
                var pointers = element.M.ReadMem<long>(parentElement.Address, 8000);
                var startIndex = 0;
                int index;
                while ((index = Array.IndexOf(pointers, element.Address, startIndex)) != -1)
                {
                    offsets.Add(index * 0x8);
                    startIndex = index + 1;
                }
            }

            return offsets;
        }

        private string ToStringSafe(object obj)
        {
            try
            {
                return obj?.ToString();
            }
            catch (Exception ex)
            {
                LogError($"ToString() -> {ex}");
                return null;
            }
        }
    }
}
