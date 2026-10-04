using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Mesen.Config;
using Mesen.Logic;
using Mesen.ViewModels;
using Mesen.Windows;
using Xunit;

namespace Mesen.HeadlessTests;

//ADR-0250 Decision 3: the big tool windows a task door offers - Enhancement
//Packs and the log window (Remaster), Netplay's Connect and Start Server
//(Share) - keep their window and behaviour but open in the Player look
//(Utilities/PlayerWindowLook): the `player` class, the renders' window
//background, Inter, Player footer buttons. Classic opens the same windows in
//the classic look.
public partial class DoorMenuNoClassicWindowTests
{
	private static T OpenWindow<T>(MainWindow owner) where T : Window
	{
		WaitFor(() => Opened.OfType<T>().Any(w => w.IsVisible), $"no {typeof(T).Name} opened");
		return Opened.OfType<T>().Last(w => w.IsVisible);
	}

	private static void AssertPlayerLook(Window window, string buttonName)
	{
		Assert.Contains("player", window.Classes);
		Assert.Equal(WindowBackground, PlayerRender.SolidColor(window.Background));
		AssertPlayerButton(window.FindNamed<Button>(buttonName));
	}

	[AvaloniaFact]
	public void Remaster_tool_windows_open_in_the_player_look()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		(MainWindow window, MainWindowViewModel model) = Show(UiMode.Player, Workspace.Remaster);
		Assert.Equal(new[] { EntryOpens.PlayerLookWindow }, DoorEntryOpensTable.Of(MenuEntry.EnhancementPacks));
		Assert.Equal(new[] { EntryOpens.PlayerLookWindow }, DoorEntryOpensTable.Of(MenuEntry.LogWindow));

		DoorItem(model, "Enhancement Packs").OnClick();
		EnhancementPacksWindow packs = OpenWindow<EnhancementPacksWindow>(window);
		Dispatcher.UIThread.RunJobs();
		AssertPlayerLook(packs, "btnPacksOk");
		AssertInstallButtonFont(packs);
		SaveRender(packs, "ADR-0250-enhancement-packs-player-look");

		//An install or a restore shows a moving bar and holds the buttons.
		ProgressBar bar = packs.FindNamed<ProgressBar>("PacksBusyBar");
		Assert.False(bar.IsOnScreen());
		EnhancementPacksViewModel packsModel = Assert.IsType<EnhancementPacksViewModel>(packs.DataContext);
		packsModel.IsBusy = true;
		Dispatcher.UIThread.RunJobs();
		Assert.True(bar.IsOnScreen());
		Assert.True(bar.IsIndeterminate);
		Assert.False(packs.FindNamed<StackPanel>("PacksActions").IsEnabled);
		Assert.False(packs.FindNamed<Button>("btnPacksOk").IsEnabled);
		packsModel.IsBusy = false;
		packs.Close();

		DoorItem(model, "Log Window").OnClick();
		LogWindow log = OpenWindow<LogWindow>(window);
		Dispatcher.UIThread.RunJobs();
		AssertPlayerLook(log, "btnLogClose");
		SaveRender(log, "ADR-0250-log-window-player-look");
		log.Close();
	}

	private static void AssertInstallButtonFont(Window window)
	{
		Assert.Equal("Inter", window.FindNamed<Button>("btnPacksInstall").FindAll<TextBlock>().First(t => !string.IsNullOrEmpty(t.Text)).FontFamily.Name);
	}

	[AvaloniaFact]
	public void Share_netplay_forms_open_in_the_player_look()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		(MainWindow window, MainWindowViewModel model) = Show(UiMode.Player, Workspace.Share);
		Assert.DoesNotContain(EntryOpens.ClassicWindow, DoorEntryOpensTable.Of(MenuEntry.NetPlay));

		DoorItem(model, "Netplay", "Connect").OnClick();
		NetplayConnectWindow connect = OpenWindow<NetplayConnectWindow>(window);
		Dispatcher.UIThread.RunJobs();
		AssertPlayerLook(connect, "btnNetplayOk");
		Assert.Equal(SizeToContent.Height, connect.SizeToContent);
		SaveRender(connect, "ADR-0250-netplay-connect-player-look");
		connect.Close();

		DoorItem(model, "Netplay", "StartServer").OnClick();
		NetplayStartServerWindow server = OpenWindow<NetplayStartServerWindow>(window);
		Dispatcher.UIThread.RunJobs();
		AssertPlayerLook(server, "btnNetplayCancel");
		server.Close();
	}

	//The scope test: Classic (Advanced mode) opens the classic windows in the
	//classic look - About is AboutWindow, Enhancement Packs keeps radius 0.
	[AvaloniaFact]
	public void Classic_keeps_the_classic_windows_and_look()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		(MainWindow window, MainWindowViewModel model) = Show(UiMode.Advanced, Workspace.Classic);
		Assert.Equal(Workspace.Classic, model.Shell.Active);

		model.MainMenu.OpenAbout(window);
		AboutWindow about = OpenWindow<AboutWindow>(window);
		Assert.False(model.ToolSheet.IsVisible);
		Assert.DoesNotContain("player", about.Classes);
		about.Close();

		model.MainMenu.OpenCommandLineHelp(window);
		CommandLineHelpWindow help = OpenWindow<CommandLineHelpWindow>(window);
		help.Close();

		EnhancementPacksWindow packs = MainMenuViewModel.OpenEnhancementPacks(window);
		Dispatcher.UIThread.RunJobs();
		Assert.DoesNotContain("player", packs.Classes);
		Button ok = packs.FindNamed<Button>("btnPacksOk");
		Assert.Equal(new CornerRadius(0), ok.CornerRadius);
		Assert.NotEqual("Inter", ok.FindAll<TextBlock>().First(t => !string.IsNullOrEmpty(t.Text)).FontFamily.Name);
		Assert.NotEqual(WindowBackground, PlayerRender.SolidColor(packs.Background));
		SaveRender(packs, "ADR-0250-enhancement-packs-classic");
		packs.Close();
	}
}
