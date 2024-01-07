using System.Windows.Forms;
using ExileCore.Shared.Attributes;
using ExileCore.Shared.Interfaces;
using ExileCore.Shared.Nodes;
using SharpDX;

namespace DevTree
{
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
        public ToggleNode Enable { get; set; } = new(false);

        public bool ToggleWindowState;//Just save the state
    }
}
