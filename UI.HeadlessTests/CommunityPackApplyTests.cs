using System;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Mesen.Config;
using Mesen.Interop;
using Mesen.Logic;
using Mesen.Services;
using Xunit;

namespace Mesen.HeadlessTests;

//#675: the power cycle that applies a freshly installed pack follows the W-P16
//rule (open generation + SHA-1, CommunityPackLoadTarget.IsStillLoaded), so a
//game the player reopened meanwhile - e.g. Continue to resume a save - is not
//power-cycled under them. #681 (2): a load capture that throws releases the
//install gate. Both rules are host-free (UI.Tests/CommunityPacks); this checks
//the service wiring with the real core for the log calls (skips without one).
[Collection(NativeCoreCollection.Name)]
public class CommunityPackApplyTests : IDisposable
{
	private const string ShaA = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";

	private readonly Func<CommunityPackLoadTarget> _savedProbe = CommunityPackInstallCoordinator.ReadCurrentLoad;
	private readonly Action _savedPowerCycle = CommunityPackInstallService.PowerCycleGame;
	private readonly bool _autoInstall = ConfigManager.Config.EnhancementPacks.AutoInstallCommunityPacks;
	private readonly GameSelectionMode _selectionMode = ConfigManager.Config.Preferences.GameSelectionScreenMode;

	public void Dispose()
	{
		CommunityPackInstallCoordinator.ReadCurrentLoad = _savedProbe;
		CommunityPackInstallService.PowerCycleGame = _savedPowerCycle;
		ConfigManager.Config.EnhancementPacks.AutoInstallCommunityPacks = _autoInstall;
		ConfigManager.Config.Preferences.GameSelectionScreenMode = _selectionMode;
	}

	//The log calls need an initialized core (its home folder), as on a real run.
	private static void InitCore()
	{
		EmuApi.InitDll();
		EmuApi.InitializeEmu(ConfigManager.HomeFolder, IntPtr.Zero, IntPtr.Zero, true, true, true, true);
	}

	[AvaloniaFact]
	public void A_pack_installed_for_a_game_the_player_reopened_does_not_power_cycle_it()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		InitCore();
		//The "reload the game" notice goes straight to the core's OSD, with no
		//home-screen timer posting back after the test.
		ConfigManager.Config.Preferences.GameSelectionScreenMode = GameSelectionMode.Disabled;
		int powerCycles = 0;
		CommunityPackInstallService.PowerCycleGame = () => powerCycles++;
		CommunityPackLoadTarget installedFor = new(ShaA, ShaA, "", "GameA", 1);

		//Same ROM, opened again (open #2) while the install ran.
		CommunityPackInstallCoordinator.ReadCurrentLoad = () => installedFor with { OpenGeneration = 2 };
		CommunityPackInstallService.ApplyInstalledPack(installedFor);
		Dispatcher.UIThread.RunJobs();
		Assert.Equal(0, powerCycles);

		//The load the install was for is still the one running.
		CommunityPackInstallCoordinator.ReadCurrentLoad = () => installedFor;
		CommunityPackInstallService.ApplyInstalledPack(installedFor);
		Dispatcher.UIThread.RunJobs();
		Assert.Equal(1, powerCycles);
	}

	[Fact]
	public async System.Threading.Tasks.Task A_load_capture_that_throws_does_not_hold_the_install_gate()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		InitCore();
		//A game load in an earlier test may still have its auto-install in
		//flight; this test needs the gate free, or OnGameLoaded only defers.
		DateTime deadline = DateTime.UtcNow.AddSeconds(60);
		while(CommunityPackInstallService.InstallInFlight && DateTime.UtcNow < deadline) {
			await System.Threading.Tasks.Task.Delay(50);
		}
		Assert.False(CommunityPackInstallService.InstallInFlight, "an earlier install still held the gate after 60 s");
		ConfigManager.Config.EnhancementPacks.AutoInstallCommunityPacks = true;
		int captures = 0;
		CommunityPackInstallCoordinator.ReadCurrentLoad = () => {
			captures++;
			throw new InvalidOperationException("the load could not be read");
		};

		Exception? thrown = Record.Exception(() => CommunityPackInstallService.OnGameLoaded(false));
		Assert.Equal(1, captures);

		//A free gate lets Restore reach its own "no game" answer.
		CommunityPackInstallCoordinator.ReadCurrentLoad = () => new CommunityPackLoadTarget("", "", "", "", 0);
		(bool ok, string error) = await CommunityPackInstallService.RestoreInstalledPack();
		Assert.False(ok);
		Assert.Equal("no loaded ROM to restore a pack for", error);
		//Fail-soft: an auto-install never breaks the game load that raised it.
		Assert.Null(thrown);
	}
}
