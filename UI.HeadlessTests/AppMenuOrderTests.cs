using System;
using System.Linq;
using System.Reflection;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Xunit;

namespace Mesen.HeadlessTests;

//#1018: InitAppMenu reorders the macOS app menu; About first, Settings… next. Quit is Avalonia's own
//item, added only when the platform exports the menu; the headless platform
//never does, so Quit-last is not observable here (see the PR body). Runs the real private InitAppMenu on a real App.
//It drives InitAppMenu on a throwaway `new App()` and does NOT cover the OnFrameworkInitializationCompleted call
//under OperatingSystem.IsMacOS(), which is not drivable headless.
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
		//Relative order only: the platform or Avalonia may append items after these.
		Assert.True(headers.Length >= 3, "expected at least About, separator and Settings…");
		Assert.Equal(Message("DoorMenuAbout"), headers[0]);
		Assert.Equal("-", headers[1]);
		Assert.Equal(Message("DoorMenuSettings"), headers[2]);
	}

	//ResourceHelper is internal to the UI assembly, so it is reached by reflection, like InitAppMenu above.
	private static string Message(string id)
	{
		Type helper = typeof(App).Assembly.GetType("Mesen.Localization.ResourceHelper")!;
		return (string)helper.GetMethod("GetMessage", BindingFlags.Public | BindingFlags.Static)!.Invoke(null, new object[] { id, Array.Empty<object>() })!;
	}
}
