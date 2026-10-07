using System;
using System.Collections.Generic;
using System.IO;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Mesen.Config;
using Mesen.Interop;
using Mesen.Logic;
using Mesen.ViewModels;
using Mesen.Views;
using Mesen.Windows;
using Xunit;

namespace Mesen.HeadlessTests;

//#953: the hand-offs whose surface reaches the core (MainWindow, EmuApi) - W-P6's
//Show Pack in Finder, the Enhancement Packs window's Open Folder, the classic
//no-feed dialog's release page, and the tool sheet's release page (its view
//reaches EmuApi.InputBarcode) - pass the folder or URL to the injected
//launcher. Stubs record what they were handed, so nothing opens. The cases
//that call the core skip without one; PlayFolderHandoffTests is the core-free half.
[Collection(NativeCoreCollection.Name)]
public class PlayFolderHandoffCoreTests : IDisposable
{
	//Container, Name, Version, Author, License, Sections, Enabled, Origin(0=folder),
	//PackId, ContentId - see MepPackListParser.
	private const string OnePack = "aaa\tAaa Pack\t1.2\tTastic\tCC BY-NC 4.0\ttextures\t1\t0\tissue-1\tc1\n";
	private const string Sha1 = "0000000000000000000000000000000000000000";

	private readonly string _folder = Path.Combine(Path.GetTempPath(), "mesen-953-" + Guid.NewGuid().ToString("N"));
	private readonly List<string> _launched = new();
	private readonly UiMode _uiMode = ConfigManager.Config.Preferences.UiMode;
	private readonly Workspace _workspace = ConfigManager.Config.Preferences.Workspace;
	private readonly bool _packFolderExisted = Directory.Exists(ConfigManager.EnhancementPackFolder);
	private MainWindow? _window;
	private Window? _sheetWindow;

	public void Dispose()
	{
		if(_window != null) {
			//Closing must not tear down the process-global core the next case shares.
			_window.ReleaseCore = () => { };
			_window.Close();
		}
		_sheetWindow?.Close();
		ConfigManager.Config.Preferences.UiMode = _uiMode;
		ConfigManager.Config.Preferences.Workspace = _workspace;
		try {
			Directory.Delete(_folder, true);
		} catch {
			//The temp folder going is not what the case was proving.
		}
		if(!_packFolderExisted && Directory.Exists(ConfigManager.EnhancementPackFolder)
			&& Directory.GetFileSystemEntries(ConfigManager.EnhancementPackFolder).Length == 0) {
			Directory.Delete(ConfigManager.EnhancementPackFolder);
		}
	}

	private MainWindowViewModel ShowPlay()
	{
		ConfigManager.Config.Preferences.UiMode = UiMode.Player;
		ConfigManager.Config.Preferences.Workspace = Workspace.Play;
		_window = new MainWindow();
		_window.ShowStarted();
		Dispatcher.UIThread.RunJobs();
		return Assert.IsType<MainWindowViewModel>(_window.DataContext);
	}

	[AvaloniaFact]
	public void Show_pack_in_finder_hands_over_the_rendered_packs_folder()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		string packFolder = Path.Combine(_folder, "aaa");
		Directory.CreateDirectory(packFolder);
		MainWindowViewModel model = ShowPlay();
		PlayerPackDetailSheetView sheet = _window!.FindNamed<PlayerPackDetailSheetView>("PlayerPackDetailHost");
		sheet.FolderLauncher = _launched.Add;
		model.RomInfo = new RomInfo() { ConsoleType = ConsoleType.Nes, Format = RomFormat.iNes };
		model.OpenPauseOverlay();
		Dispatcher.UIThread.RunJobs();
		Assert.False(model.OpenPackFromOverlay(OnePack, Sha1, _folder, "", installedSourceSha256: null));
		Dispatcher.UIThread.RunJobs();
		Assert.Empty(_launched);

		_window.FindNamed<Button>("PackDetailFolderButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

		Assert.Equal(new[] { packFolder }, _launched);
	}

	[AvaloniaFact]
	public void Open_folder_in_enhancement_packs_hands_over_the_packs_folder()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		Directory.CreateDirectory(ConfigManager.EnhancementPackFolder);
		EnhancementPacksViewModel packs = new() { FolderLauncher = _launched.Add };

		packs.OpenFolder();

		Assert.Equal(new[] { Path.Combine(ConfigManager.HomeFolder, "EnhancementPacks") }, _launched);
	}

	//#987: the artist's workspace beside the ROM (ADR-0049), made on demand.
	[AvaloniaFact]
	public void Open_sibling_folder_in_enhancement_packs_hands_over_the_games_sibling_folder()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		string sibling = Path.Combine(_folder, "Contra");
		EnhancementPacksViewModel packs = new() { FolderLauncher = _launched.Add };
		packs.SiblingFolder = sibling;

		packs.OpenSiblingFolder();

		Assert.Equal(new[] { sibling }, _launched);
		Assert.True(Directory.Exists(Path.Combine(sibling, "textures")));
	}

	[AvaloniaFact]
	public void The_no_feed_dialog_opens_the_release_page_only_on_ok()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		MainMenuViewModel menu = ShowPlay().MainMenu;
		menu.ReleasePageLauncher = _launched.Add;

		menu.AnswerReleasePageOffer(DialogResult.Cancel);
		Assert.Empty(_launched);

		menu.AnswerReleasePageOffer(DialogResult.OK);
		Assert.Equal(new[] { "https://github.com/sbihaiko/MesenAI/releases" }, _launched);
	}

	[AvaloniaFact]
	public void Open_releases_on_the_tool_sheet_hands_over_the_forks_release_page()
	{
		PlayerToolSheetViewModel sheet = new() { ReleasePageLauncher = _launched.Add };
		sheet.OpenCheckForUpdates();
		_sheetWindow = new Window { Content = new PlayerToolSheetView { DataContext = sheet }, Width = 1000, Height = 700 };
		_sheetWindow.Show();
		Dispatcher.UIThread.RunJobs();
		Assert.Empty(_launched);

		_sheetWindow.FindNamed<Button>("ToolSheetOpenReleases").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
		Dispatcher.UIThread.RunJobs();

		Assert.Equal(new[] { "https://github.com/sbihaiko/MesenAI/releases" }, _launched);
		Assert.False(sheet.IsVisible);
	}
}
