using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Windows.Forms;
using ExileCore2;
using ExileCore2.PoEMemory;
using ExileCore2.PoEMemory.Components;
using ExileCore2.PoEMemory.Elements;
using ExileCore2.PoEMemory.MemoryObjects;
using ExileCore2.Shared;
using ExileCore2.Shared.Enums;
using ExileCore2.Shared.Helpers;
using ImGuiNET;
using System.Drawing;
using ImGuiVector4 = System.Numerics.Vector4;
using Vector2 = System.Numerics.Vector2;

namespace DevTree;

public partial class DevPlugin : BaseSettingsPlugin<DevSetting>
{
    private const BindingFlags Flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static |
                                       BindingFlags.FlattenHierarchy;

    private record ParamsAndResult(List<string> Params, object Result, bool WasCalled);

    internal HashSet<(Type DeclaringType, string Name)> IgnoredProperties = [];

    private static readonly IReadOnlySet<MethodInfo> ExcludedMethods = new HashSet<MethodInfo>
    {
        typeof(object).GetMethod("Finalize"),
        typeof(object).GetMethod("Equals", BindingFlags.Public | BindingFlags.Instance, [typeof(object),]),
        typeof(object).GetMethod("Equals", BindingFlags.Public | BindingFlags.Static, [typeof(object), typeof(object),]),
        typeof(object).GetMethod("ReferenceEquals", BindingFlags.Public | BindingFlags.Static, [typeof(object), typeof(object),]),
    };

    private static readonly MethodInfo GetComponentMethod = typeof(Entity).GetMethod("GetComponent");
    private readonly Dictionary<string, MethodInfo> _genericMethodCache = new Dictionary<string, MethodInfo>();
    private readonly Dictionary<string, object> _debugObjects = new Dictionary<string, object>();
    private readonly Dictionary<string, object> _dynamicTabCache = new Dictionary<string, object>();
    private readonly Dictionary<string, int> _collectionSkipValues = new Dictionary<string, int>();
    private readonly Dictionary<string, string> _collectionSearchValues = new Dictionary<string, string>();
    private readonly ConditionalWeakTable<object, string> _objectSearchValues = new ConditionalWeakTable<object, string>();
    private readonly ConditionalWeakTable<object, Dictionary<MethodInfo, ParamsAndResult>> _methodParameterInvokeValues = new();
    private List<Entity> _debugEntities = [];
    private string _inputFilter = "";
    private string _guiObjAddr = "";
    private MonsterRarity? _selectedRarity;
    private bool _windowState;
    private object _lastHoveredMenuItem = null;
    private bool _showExtendedInfo = false;
    public Func<List<PluginWrapper>> Plugins;
    private Element UIHoverWithFallback => GameController.IngameState.UIHover switch { null or { Address: 0 } => GameController.IngameState.UIHoverElement, var s => s };

    public override void OnLoad()
    {
        Plugins = () => [];
    }

    public override bool Initialise()
    {
        GameController.SoundController.PreloadSound("alert.wav");
        Force = true;
        IgnoredProperties = Settings.ExclusionSettings.GetExcludedMemberInfos();
        try
        {
            InitObjects();
        }
        catch (Exception e)
        {
            LogError($"{e}");
        }

        Input.RegisterKey(Settings.ToggleWindowKey.Value);
        Input.RegisterKey(Settings.DebugUIHoverItemKey.Value);
        Settings.DebugUIHoverItemKey.OnValueChanged += () => { Input.RegisterKey(Settings.DebugUIHoverItemKey.Value); };
        Settings.ToggleWindowKey.OnValueChanged += () => { Input.RegisterKey(Settings.ToggleWindowKey.Value); };
        Name = "DevTree";
        return true;
    }

    public override void OnPluginDestroyForHotReload()
    {
        ClearCollections();
        base.OnPluginDestroyForHotReload();
    }

    private void ClearCollections()
    {
        _debugObjects.Clear();
        _collectionSkipValues.Clear();
        _collectionSearchValues.Clear();
        _objectSearchValues.Clear();
        _dynamicTabCache.Clear();
        _methodParameterInvokeValues.Clear();
        _debugEntities.Clear();
    }

    public override void AreaChange(AreaInstance area)
    {
        ClearCollections();
        InitObjects();
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
        AddObjects(GameController.IngameState.Data.ServerData.PlayerInventories.FirstOrDefault()?.Inventory, "IngameState.Data.ServerData.PlayerInventories[0].Inventory");
        AddObjects(GameController.IngameState.Data.ServerData.PlayerInventories.FirstOrDefault()?.Inventory.Items, "-> Items");
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

    private void InspectObject(object obj, string name)
    {
        if (ImGui.Begin($"Inspect {name}"))
        {
            Debug(obj, name: name);
            ImGui.End();
        }
    }

    public override void Render()
    {
        if (Settings.RegisterInspector)
        {
            GameController.RegisterInspector(InspectObject);
        }

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
                    clipboardText = clipboardText[2..];
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

        ImGui.InputTextWithHint("##entityFilter", "Entity filter", ref _inputFilter, 300);

        ImGui.PopItemWidth();
        ImGui.SameLine();
        ImGui.PushItemWidth(128);

        if (ImGui.BeginCombo("Rarity", _selectedRarity?.ToString() ?? "All"))
        {
            foreach (var rarity in Enum.GetValues<MonsterRarity>().Cast<MonsterRarity?>().Append(null))
            {
                var isSelected = _selectedRarity == rarity;

                if (ImGui.Selectable(rarity?.ToString() ?? "All", isSelected))
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

        ImGui.Checkbox("Extended info", ref _showExtendedInfo);

        foreach (var o in _debugObjects)
        {
            if (TreeNode($"{o.Key}##0", o.Value))
            {
                ImGui.Indent();

                try
                {
                    Debug(o.Value, name: o.Key);
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
                Debug(UIHoverWithFallback.AsObject<HoverItemIcon>());
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
                    if (TreeNode($"{el.GetAddress(Settings.HideAddresses):X} - {el.X}:{el.Y},{el.Width}:{el.Height}##{el.GetHashCode()}", el))
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

        if (_debugEntities.Count > 0)
        {
            var camera = GameController.IngameState.Camera;
            var hoverIndex = -1;
            DebugCollection(_debugEntities, "Entities", "Entities", "Entities", true, (i, e) => { hoverIndex = i; });

            for (var index = 0; index < _debugEntities.Count; index++)
            {
                var borderColor = hoverIndex == index ? Color.DarkSlateGray : Color.Black;
                var screenPos = camera.WorldToScreen(_debugEntities[index].Pos);
                Graphics.DrawBox(screenPos.Translate(-9, -9), screenPos.Translate(18, 18), borderColor);
                Graphics.DrawText($"{index}", screenPos);
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

    public void Debug(object obj, Type type = null, string name = null)
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
                        if (methodInfo.Invoke(obj, null) is string toString)
                        {
                            if (Settings.HideAddresses && obj is RemoteMemoryObject rmo)
                            {
                                toString = toString.Replace($"{rmo.Address:X}", $"{rmo.GetAddress(Settings.HideAddresses):X}");
                            }

                            ImGui.TextColored(Color.Orange.ToImguiVec4(), toString);
                        }
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
                var collection = enumerable as ICollection ?? (enumerable as IReadOnlyCollection<object>)?.ToList();
                if (collection == null)
                    return;

                var strId = $"{name} ##{type.FullName}";
                var collectionKey = $"{strId} {obj.GetHashCode()}";
                DebugCollection(collection, name ?? "object", strId, collectionKey, false);

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

            if (ImGui.BeginTabItem("Methods"))
            {
                DebugObjectMethods(obj, type, objectFilter);

                ImGui.EndTabItem();
            }

            if (isMemoryObject != null && ImGui.BeginTabItem("Dynamic"))
            {
                var remoteMemoryObject = (RemoteMemoryObject)obj;
                ImGui.TextColored(Color.GreenYellow.ToImguiVec4(), "Address: ");
                if (ImGui.IsItemClicked()) ImGui.SetClipboardText(remoteMemoryObject.Address.ToString());

                ImGui.SameLine();
                CopyableTextButton($"{remoteMemoryObject.GetAddress(Settings.HideAddresses):X}", $"{remoteMemoryObject.Address:X}");
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
                CopyableTextButton($"{fieldValue}");
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
            CopyableTextButton($"{asMemoryObject.GetAddress(Settings.HideAddresses):X}", $"{asMemoryObject.Address:X}");

            if (asMemoryObject is Component asComponent)
            {
                ImGui.Text("OwnerAddress: ");
                ImGui.SameLine();
                CopyableTextButton($"{asComponent.OwnerAddress:X}");
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
                                CopyableTextButton($"{component.Value:X}");

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

        foreach (var property in properties.ExceptBy(IgnoredProperties, x => (x.DeclaringType, x.Name)))
        {
            var propertyName = property.Name;
            try
            {
                if (_showExtendedInfo)
                {
                    ImGui.Text(property.DeclaringType?.FullName ?? "");
                    ImGui.SameLine();
                }

                var propertyValue = property.GetValue(obj);

                if (propertyValue == null)
                {
                    ImGui.Text($"{propertyName}: ");
                    ImGui.SameLine();
                    ImGui.TextColored(Settings.ErrorColor.Value.ToImguiVec4(), "Null");
                    continue;
                }

                var propertyType = property.PropertyType;
                //Draw primitives
                if (IsSimpleType(propertyType))
                {
                    ImGui.Text($"{propertyName}: ");
                    ImGui.SameLine();
                    var propertyVal = propertyName == "Address" ? ((long)propertyValue).ToString("X") : propertyValue.ToString();
                    CopyableTextButton(propertyVal);
                }
                else
                {
                    //Draw enumrable 
                    var isEnumerable = IsEnumerable(propertyType);

                    if (isEnumerable)
                    {
                        if (propertyValue is not ICollection collection)
                            continue;

                        var strId = $"{propertyName} ##{property.DeclaringType.FullName}";
                        var collectionKey = $"{strId} {obj.GetHashCode()}";

                        DebugCollection(collection, propertyName, strId, collectionKey, true);
                    }

                    //Debug others objects
                    else
                    {
                        if (propertyName.Equals("Value"))
                            Debug(propertyValue);
                        else
                        {
                            string name;
                            if (propertyValue is RemoteMemoryObject rmo)
                                name = $"{propertyName} [{rmo.GetAddress(Settings.HideAddresses):X}]###{propertyName} {property.DeclaringType.FullName}";
                            else
                                name = $"{propertyName} ###{propertyName} {type.FullName}";
                            if (ColoredTreeNode(name, propertyValue switch
                                {
                                    Element { IsValid: false } => Color.DarkRed,
                                    Element { IsVisible: true } => Color.Green,
                                    _ => Color.White
                                }, propertyValue, out var isHovered))
                            {
                                Debug(propertyValue);
                                ImGui.TreePop();
                            }

                            if (propertyValue is Element { Width: > 0, Height: > 0 } e && isHovered)
                            {
                                Graphics.DrawFrame(e.GetClientRectCache, Settings.FrameColor, 2);
                            }
                        }
                    }
                }
            }
            catch (Exception e)
            {
                ImGui.Text($"{propertyName}: ");
                ImGui.SameLine();
                ImGui.TextColored(Settings.ErrorColor.Value.ToImguiVec4(), "<exception thrown>");
                LogError($"{propertyName} -> {e}");
            }
        }
    }

    private void DebugCollection(ICollection collection, string propertyName, string structId, string referenceId, bool treeNode, Action<int, object> hoverAction = null)
    {
        var type = collection.GetType();
        if (collection.Count > 0)
        {
            ImGui.TextColored(Color.OrangeRed.ToImguiVec4(), $"[{collection.Count}]");

            var isElementEnumerable = type.GenericTypeArguments.Length == 1 &&
                                      (type.GenericTypeArguments[0] == typeof(Element) ||
                                       type.GenericTypeArguments[0].IsSubclassOf(typeof(Element)));

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

            if (treeNode)
            {
                ImGui.SameLine();
            }

            if (!treeNode || ImGui.TreeNodeEx(structId))
            {
                var skip = _collectionSkipValues.GetValueOrDefault(referenceId);
                if (ImGui.InputInt("Skip", ref skip, 1, 100))
                {
                    _collectionSkipValues[referenceId] = skip;
                }

                var search = _collectionSearchValues.GetValueOrDefault(referenceId) ?? "";
                ImGui.SameLine();
                if (ImGui.InputTextWithHint("##filter", "Filter", ref search, 200))
                {
                    _collectionSearchValues[referenceId] = search;
                }

                foreach (var (item, index) in collection
                             .Cast<object>()
                             .Select((x, i) => (x, i))
                             .Where(x => string.IsNullOrEmpty(search) ||
                                         ToStringSafe(x)?.Contains(search, StringComparison.InvariantCultureIgnoreCase) == true)
                             .Skip(skip)
                             .Take(Settings.LimitForCollections))
                {
                    if (item == null)
                    {
                        ImGui.TextColored(Settings.ErrorColor.Value.ToImguiVec4(), "Null");
                        continue;
                    }

                    var colType = item.GetType();
                    var colName = item switch
                    {
                        Entity e => e.Path,
                        Inventory e => $"{e.InvType} Count: ({e.ItemCount}) Box:{e.TotalBoxesInInventoryRow}",
                        Element { Text.Length: > 0 } e => $"{e.Text}",
                        Element => $"{colType.Name}",
                        _ => $"{colType.Name}"
                    };

                    if (IsSimpleType(colType))
                        CopyableTextButton(item.ToString());
                    else
                    {
                        Element element = null;

                        if (isElementEnumerable)
                        {
                            element = item as Element;

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
                                try
                                {
                                    if (methodInfo?.Invoke(item, null) is string toString)
                                    {
                                        if (Settings.HideAddresses && item is RemoteMemoryObject itemRmo)
                                        {
                                            toString = toString.Replace($"{itemRmo.Address:X}", $"{itemRmo.GetAddress(Settings.HideAddresses):X}");
                                        }

                                        colName = toString;
                                    }
                                }
                                catch (Exception ex)
                                {
                                    LogError($"ToString() -> {ex}");
                                    colName = $"{colName}: ToString(): <exception thrown>";
                                }
                            }
                        }

                        if (item is RemoteMemoryObject rmo && !colName.Contains($"{rmo.GetAddress(Settings.HideAddresses):X}"))
                        {
                            colName += $" [{rmo.GetAddress(Settings.HideAddresses):X}]";
                        }

                        if (ColoredTreeNode($"[{index}] {colName} ###{index},{item.GetType().Name}", item switch
                            {
                                Element { IsValid: false } => Color.DarkRed,
                                Element { IsVisible: true } => Color.Green,
                                _ => Color.White
                            }, item, out var isHovered))
                        {
                            Debug(item, colType);
                            ImGui.TreePop();
                        }

                        if (isHovered)
                        {
                            if (element is { Width: > 0, Height: > 0 })
                            {
                                Graphics.DrawFrame(element.GetClientRectCache, Settings.FrameColor, 2);
                            }

                            hoverAction?.Invoke(index, item);
                        }
                    }
                }

                if (treeNode)
                {
                    ImGui.TreePop();
                }
            }
        }
        else
        {
            ImGui.Indent();
            ImGui.TextColored(Color.Red.ToImguiVec4(), $"{propertyName} [Empty]");
            ImGui.Unindent();
        }
    }

    private void DebugObjectMethods(object obj, Type type, string filter)
    {
        var methods = type.GetAllMethods()
            .Where(x => !x.IsGenericMethodDefinition && !x.IsSpecialName && !x.Name.Contains('<'))
            .Where(x => string.IsNullOrEmpty(filter) || x.Name.Contains(filter, StringComparison.OrdinalIgnoreCase))
            .Except(ExcludedMethods)
            .OrderBy(x => x.Name);

        foreach (var method in methods)
        {
            try
            {
                ImGui.PushID(
                    $"{method.Name} {method.DeclaringType.AssemblyQualifiedName} {string.Join(";", method.GetParameters().Select(x => x.ParameterType.AssemblyQualifiedName))}");

                ImGui.Text($"{method.DeclaringType.FullName}:{method.Name}(");
                ImGui.SameLine(0, 0);
                var canCall = true;
                foreach (var parameter in method.GetParameters())
                {
                    var text = $"{parameter.ParameterType.Name} {parameter.Name}, ";
                    if (!(parameter.ParameterType == typeof(string) ||
                          parameter.ParameterType == typeof(float) ||
                          parameter.ParameterType == typeof(double) ||
                          parameter.ParameterType == typeof(int) ||
                          parameter.ParameterType == typeof(uint) ||
                          parameter.ParameterType == typeof(long) ||
                          parameter.ParameterType == typeof(ulong) ||
                          parameter.ParameterType == typeof(byte) ||
                          parameter.ParameterType == typeof(short)))
                    {
                        canCall = false;
                        ImGui.TextColored(Settings.ErrorColor.Value.ToImguiVec4(), text);
                    }
                    else
                    {
                        ImGui.Text(text);
                    }

                    ImGui.SameLine(0, 0);
                }

                ImGui.Text($") -> {method.ReturnType.Name}");
                if (canCall)
                {
                    _methodParameterInvokeValues.TryGetValue(obj, out var paramDict);
                    paramDict ??= new Dictionary<MethodInfo, ParamsAndResult>();
                    _methodParameterInvokeValues.AddOrUpdate(obj, paramDict);
                    if (!paramDict.TryGetValue(method, out var paramList))
                    {
                        paramDict[method] = paramList = new ParamsAndResult([], null, false);
                    }

                    paramList.Params.Resize(method.GetParameters().Length, "");
                    for (var i = 0; i < method.GetParameters().Length; i++)
                    {
                        var parameter = method.GetParameters()[i];
                        var str = paramList.Params[i];
                        if (ImGui.InputText(parameter.Name, ref str, 200))
                        {
                            paramList.Params[i] = str;
                        }
                    }

                    object[] converted = null;
                    try
                    {
                        converted = paramList.Params.Zip(method.GetParameters(), (s, param) => Convert.ChangeType(s, param.ParameterType)).ToArray();
                    }
                    catch
                    {
                    }

                    ImGui.BeginDisabled(converted == null);
                    if (!method.GetParameters().Any())
                    {
                        ImGui.SameLine();
                    }

                    if (ImGui.Button("Invoke"))
                    {
                        try
                        {
                            paramList = paramList with { Result = method.Invoke(obj, converted), WasCalled = true };
                            paramDict[method] = paramList;
                        }
                        catch (Exception ex)
                        {
                            LogError($"Invoke() -> {ex}");
                        }
                    }

                    ImGui.EndDisabled();
                    if (paramList.WasCalled && TreeNode("Result", paramList.Result))
                    {
                        Debug(paramList.Result);
                        ImGui.TreePop();
                    }
                }

                ImGui.PopID();
            }
            catch (Exception e)
            {
                ImGui.Text($"{method.Name}: ");
                ImGui.SameLine();
                ImGui.TextColored(Settings.ErrorColor.Value.ToImguiVec4(), "<exception thrown>");
                LogError($"{method.Name} -> {e}");
            }
        }
    }

    private static void CopyableTextButton(string text, string copyText = null)
    {
        ImGui.PushStyleColor(ImGuiCol.Text, new ImGuiVector4(1, 0.647f, 0, 1));
        ImGui.PushStyleColor(ImGuiCol.Button, new ImGuiVector4(0, 0, 0, 0));
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new ImGuiVector4(0.25f, 0.25f, 0.25f, 1));
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, new ImGuiVector4(1, 1, 1, 1));

        if (ImGui.SmallButton(text))
        {
            ImGui.SetClipboardText(copyText ?? text);
        }

        ImGui.PopStyleColor(4);
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
                    Graphics.DrawFrame(toScreen,
                        toScreen + new Vector2(renderComponentBounds.X, -renderComponentBounds.Y),
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