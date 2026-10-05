using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace Mesen.Config.Shortcuts
{
	public partial class ShortcutKeyInfo : ObservableObject
	{
		[ObservableProperty] public partial EmulatorShortcut Shortcut { get; set; }
		[ObservableProperty] public partial KeyCombination KeyCombination { get; set; } = new KeyCombination();
		[ObservableProperty] public partial KeyCombination KeyCombination2 { get; set; } = new KeyCombination();

		//ADR-0255 slice 4: the shortcut's spare pad control, when it has one.
		//Null - not an empty KeyCombination - is "none", and the key is dropped
		//when writing (WhenWritingNull), so a settings.json written before this
		//slot existed loads with it null and is written back byte for byte:
		//Configuration.Serialize only writes the file when the text changed.
		[ObservableProperty] [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
		public partial PadShortcutBinding? PadBinding { get; set; }
	}
}
