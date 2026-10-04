using System;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Mesen.Config;
using Mesen.Interop;
using Mesen.Logic;
using Mesen.ViewModels;
using Mesen.Windows;
using Xunit;

namespace Mesen.HeadlessTests;

//#736: when an accepted community pack exists for the loaded game but another
//pack renders, W-P4's Pack row says "Community pack available" and W-P6 says
//why in plain words, with Use Community Pack. The decision is pinned host-free
//in UI.Tests/Play/CommunityPackOfferTests; this checks the realized surfaces.
//
//Needs a MainWindow (EmuApi.InitDll in its constructor), so it self-skips on the
//core-less CI runner like the other MainWindow tests.
[Collection(NativeCoreCollection.Name)]
public class CommunityPackOfferViewTests : IDisposable
{
	private const string Sha1 = "979494E7869AC7AB4815FDBD1DC99F893F713FBF";
	//Container, Name, Version, Author, License, Sections, Enabled, Origin
	//(2=sibling), PackId, ContentId, IsAutoOnly - see MepPackListParser.
	//The issue's repro: only the bootstrap's xBRZ auto layer is listed.
	private const string BootstrapOnly = "Contra (1988) (Konami)\tContra (1988) (Konami)\t\t\t\ttextures\t1\t2\t\t\t1\n";
	//The community pack installed, but a local pack the player chose renders.
	private const string ShadowedByLocal =
		"Contra 80s\tContra 80s\t1.0\tTastic\t\ttextures\t1\t0\ttastichacks/contra80s\tc-contra\t0\n" +
		"My xBRZ\tMy xBRZ\t\t\t\ttextures\t1\t0\t\t\t0\n";
	private static readonly CommunityPackOfferContext Contra = new(
		new CommunityPackCatalogPick("Contra (USA) — community submission", "tastichacks/contra80s", "c-contra", "Contra (USA) — community submission"),
		null, false);

	private readonly UiMode _uiMode = ConfigManager.Config.Preferences.UiMode;
	private readonly Workspace _workspace = ConfigManager.Config.Preferences.Workspace;
	private readonly bool _autoInstall = ConfigManager.Config.EnhancementPacks.AutoInstallCommunityPacks;

	public void Dispose()
	{
		ConfigManager.Config.Preferences.UiMode = _uiMode;
		ConfigManager.Config.Preferences.Workspace = _workspace;
		ConfigManager.Config.EnhancementPacks.AutoInstallCommunityPacks = _autoInstall;
		ConfigManager.Config.EnhancementPacks.SetRomPackPreference(Sha1, "");
		ConfigManager.Config.Save();
	}

	private static (MainWindow Window, MainWindowViewModel Model) ShowPlayWithGame(Func<PackRowState?> packRow)
	{
		ConfigManager.Config.Preferences.UiMode = UiMode.Player;
		ConfigManager.Config.Preferences.Workspace = Workspace.Play;
		MainWindow window = new();
		window.ShowStarted();
		Dispatcher.UIThread.RunJobs();
		MainWindowViewModel model = Assert.IsType<MainWindowViewModel>(window.DataContext);
		model.RomInfo = new RomInfo() { ConsoleType = ConsoleType.Nes, Format = RomFormat.iNes };
		model.ReadPackRowState = packRow;
		model.OpenPauseOverlay();
		Dispatcher.UIThread.RunJobs();
		return (window, model);
	}

	[AvaloniaFact]
	public void Auto_install_off_the_pack_row_and_detail_offer_the_community_pack()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		ConfigManager.Config.EnhancementPacks.AutoInstallCommunityPacks = false;
		(MainWindow window, MainWindowViewModel model) = ShowPlayWithGame(() => new PackRowState(BootstrapOnly, Sha1, Contra));

		Assert.Equal("Community pack available", window.FindNamed<TextBlock>("OverlayPackValue").Text);

		Assert.False(model.OpenPackFromOverlay(BootstrapOnly, Sha1, "/packs", "/roms/Contra (1988) (Konami)", installedSourceSha256: null, Contra));
		Dispatcher.UIThread.RunJobs();

		Assert.True(window.FindNamed<Border>("PlayerPackDetailSheet").IsOnScreen());
		Assert.True(window.FindNamed<Border>("PackDetailOffer").IsOnScreen());
		Assert.Equal("A community pack is available", window.FindNamed<TextBlock>("PackDetailOfferTitle").Text);
		Assert.Equal("“Contra (USA) — community submission” is made for this game. It is not installed because automatic installs of community packs are off.",
			window.FindNamed<TextBlock>("PackDetailOfferBody").Text);
		Assert.True(window.FindNamed<Button>("PackDetailUseCommunityButton").IsOnScreen());
		Assert.Equal(CommunityPackOfferAction.Install, model.CommunityOffer.Action);
		//Offered, never forced: the switch is still off.
		Assert.False(ConfigManager.Config.EnhancementPacks.AutoInstallCommunityPacks);
	}

	[AvaloniaFact]
	public void Without_a_community_pack_nothing_is_offered()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		CommunityPackOfferContext none = new(null, null, false);
		(MainWindow window, MainWindowViewModel model) = ShowPlayWithGame(() => new PackRowState(BootstrapOnly, Sha1, none));

		Assert.NotEqual("Community pack available", window.FindNamed<TextBlock>("OverlayPackValue").Text);

		model.OpenPackFromOverlay(BootstrapOnly, Sha1, "/packs", "", installedSourceSha256: null, none);
		Dispatcher.UIThread.RunJobs();
		Assert.True(window.FindNamed<Border>("PlayerPackDetailSheet").IsOnScreen());
		Assert.False(window.FindNamed<Border>("PackDetailOffer").IsOnScreen());
	}

	//Two packs would open the picker; with an offer the row opens W-P6, and
	//Use Community Pack stores it as this game's choice and swaps it in place
	//(NES, P.9) - back to W-P4.
	[AvaloniaFact]
	public void Use_community_pack_chooses_the_installed_pack_and_returns_to_the_overlay()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		ConfigManager.Config.EnhancementPacks.SetRomPackPreference(Sha1, "local:my xbrz");
		(MainWindow window, MainWindowViewModel model) = ShowPlayWithGame(() => new PackRowState(ShadowedByLocal, Sha1, Contra));

		Assert.False(model.OpenPackFromOverlay(ShadowedByLocal, Sha1, "/packs", "", installedSourceSha256: null, Contra));
		Dispatcher.UIThread.RunJobs();
		Assert.False(model.IsPlayerPackPickerVisible);
		Assert.True(window.FindNamed<Border>("PlayerPackDetailSheet").IsOnScreen());
		Assert.Equal("“Contra (USA) — community submission” is made for this game. It is installed, but “My xBRZ” is the pack in use.",
			window.FindNamed<TextBlock>("PackDetailOfferBody").Text);

		window.FindNamed<Button>("PackDetailUseCommunityButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
		Dispatcher.UIThread.RunJobs();

		Assert.Equal("tastichacks/contra80s", ConfigManager.Config.EnhancementPacks.GetRomPackPreference(Sha1));
		Assert.False(window.FindNamed<Border>("PlayerPackDetailSheet").IsOnScreen());
		Assert.True(window.FindNamed<Border>("PlayerOverlay").IsOnScreen());
	}
}
