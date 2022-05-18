using System.Windows.Forms;
using ExileCore.Shared.Attributes;
using ExileCore.Shared.Interfaces;
using ExileCore.Shared.Nodes;

namespace DevTree
{
    public class DevSetting : ISettings
    {
        [Menu("Toggle window", 0)]
        public ToggleNode ToggleWindow { get; set; } = new ToggleNode(false);

        [Menu("Toggle window key")]
        public HotkeyNode ToggleWindowKey { get; set; } = new HotkeyNode(Keys.NumPad9);

        [Menu("Debug Hover Item")]
        public HotkeyNode DebugHoverItem { get; set; } = Keys.NumPad5;

        [Menu("Save hovered DevTree node")]
        public HotkeyNode SaveDevTreeNode { get; set; } = Keys.NumPad8;

        [Menu("Nearest Ents Range")]
        public RangeNode<int> NearestEntsRange { get; set; } = new(300, 1, 2000);

        [Menu("Limit for collections")]
        public RangeNode<int> LimitForCollection { get; set; } = new(500, 2, 5000);

        public ToggleNode Enable { get; set; } = new(false);

        public bool ToggleWindowState;//Just save the state
    }
}
