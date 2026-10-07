using System;
using System.Linq;
using System.Reflection;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Xunit;

namespace Mesen.HeadlessTests;

//#1018: InitAppMenu reorders the macOS app menu: About first, Settings… next, Avalonia's own items
//(Services… Quit) after them with Quit last. Avalonia adds those items when the platform exports the
//menu, which the headless platform never does, so the test seeds the app menu with a fake Quit and a
//separator before invoking the real private InitAppMenu on a throwaway `new App()`.
//It does NOT cover the OnFrameworkInitializationCompleted call under OperatingSystem.IsMacOS(),
//which is not drivable headless.
public class AppMenuOrderTests
{
	[AvaloniaFact]
	public void InitAppMenu_puts_About_and_Settings_above_the_existing_items_with_Quit_last()
	{
		App app = new();
		NativeMenu seeded = new();
		seeded.Items.Add(new NativeMenuItemSeparator());
		seeded.Items.Add(new NativeMenuItem("Quit"));
		NativeMenu.SetMenu(app, seeded);

		typeof(App).GetMethod("InitAppMenu", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(app, null);

		NativeMenu? menu = NativeMenu.GetMenu(app);
		Assert.NotNull(menu);
		//The separator is itself a NativeMenuItem subclass, so the order is read off all items.
		string?[] headers = menu!.Items.OfType<NativeMenuItem>().Select(i => i.Header).ToArray();
		Assert.Equal(new string?[] { Message("DoorMenuAbout"), "-", Message("DoorMenuSettings"), "-", "Quit" }, headers);
	}

	//ResourceHelper is internal to the UI assembly, so it is reached by reflection, like InitAppMenu above.
	private static string Message(string id)
	{
		Type helper = typeof(App).Assembly.GetType("Mesen.Localization.ResourceHelper")!;
		return (string)helper.GetMethod("GetMessage", BindingFlags.Public | BindingFlags.Static)!.Invoke(null, new object[] { id, Array.Empty<object>() })!;
	}
}
