using System.Linq;
using System.Reflection;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Xunit;

namespace Mesen.HeadlessTests;

//#1018: InitAppMenu reorders the macOS app menu; About first, Settings… next. Quit is Avalonia's own
//item, added only when the platform exports the menu; the headless platform
//never does, so Quit-last is not observable here (see the PR body). Runs the real private InitAppMenu on a real App.
[NativeCoreFree("InitAppMenu only builds NativeMenuItems; no emulator call is made until a Click handler fires.")]
public class AppMenuOrderTests
{
	[AvaloniaFact]
	public void InitAppMenu_puts_About_first_Settings_next()
	{
		App app = new();
		typeof(App).GetMethod("InitAppMenu", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(app, null);

		NativeMenu? menu = NativeMenu.GetMenu(app);
		Assert.NotNull(menu);
		//The separator is itself a NativeMenuItem subclass, so the order is read off all items.
		string?[] headers = menu!.Items.OfType<NativeMenuItem>().Select(i => i.Header).ToArray();
		Assert.Equal(3, headers.Length);
		Assert.StartsWith("About", headers[0]);
		Assert.Equal("-", headers[1]);
		Assert.StartsWith("Settings", headers[2]);
	}
}
