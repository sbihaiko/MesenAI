using System;
using System.IO;
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Headless;

[assembly: AvaloniaTestApplication(typeof(Mesen.HeadlessTests.TestAppBuilder))]

namespace Mesen.HeadlessTests;

//ADR-0150: the headless Avalonia host. `AvaloniaTestApplication` makes every
//[AvaloniaFact] run on a real Avalonia UI thread with the null windowing/render
//backend, so controls are instantiated, styles applied, bindings evaluated and
//the visual tree walked with no display server.
//
//The app object is Mesen's own `App`, on purpose: its Application.Resources /
//Styles (FluentTheme + MesenStyles) are what the windows under test resolve
//their StaticResources and implicit styles against. Rebuilding a stand-in
//Application here would test a theme the app never runs with.
public static class TestAppBuilder
{
	public static AppBuilder BuildAvaloniaApp()
	{
		UsePortableHomeFolder();
		//#619: every MainWindow's startup checks for updates on a thread-pool
		//thread and posts its answer to Dispatcher.UIThread whenever the network
		//replies - after its test ended, possibly while Avalonia is resetting the
		//dispatcher for the next one. A test never needs the network.
		Mesen.Config.ConfigManager.Config.Preferences.AutomaticallyCheckForUpdates = false;
		//ADR-0249 Decision 5: Skia renders real frames (UseHeadlessDrawing =
		//false), so the render gate can write PNGs of the Player screens and
		//read their pixels back. WithInterFont registers the bundled Inter the
		//Player theme uses, exactly as Program.BuildAvaloniaApp does.
		return AppBuilder.Configure<Mesen.App>()
			.UseSkia()
			.WithInterFont()
			.UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
	}

	//Mesen resolves ConfigManager.HomeFolder to the executable's own folder as
	//soon as a settings.json sits next to the binary ("portable" mode), and only
	//falls back to the user's Documents/AppData folder otherwise. Seeding an
	//empty config in the test output folder therefore keeps every config read
	//AND WRITE (e.g. a workspace switch calls Configuration.Save()) inside
	//bin/, instead of mutating the developer's real MesenCE settings.
	//Runs from a module initializer as well as from BuildAvaloniaApp, so it
	//cannot lose the race against the first ConfigManager.HomeFolder read.
	[ModuleInitializer]
	internal static void UsePortableHomeFolder()
	{
		try {
			string settings = Path.Combine(AppContext.BaseDirectory, "settings.json");
			if(!File.Exists(settings)) {
				File.WriteAllText(settings, "{}");
			}
		} catch(Exception) {
			//A read-only output folder would fall back to the real home folder;
			//the tests that write config assert on the in-memory value anyway.
		}
	}
}
