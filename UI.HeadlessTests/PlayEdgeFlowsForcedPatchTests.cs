using System;
using System.Collections.Generic;
using System.IO;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Mesen.Config;
using Mesen.Interop;
using Mesen.Logic;
using Mesen.ViewModels;
using Mesen.Windows;
using Xunit;

namespace Mesen.HeadlessTests;

//#732: with "Apply ROM patches even when the hash does not match" on, a pack's
//<patch> made for another revision of the game is applied anyway and can
//freeze this one. Player mode tells the player in place - the shared warning
//banner docked above the game (W-X2 shape, InterruptionBar; the native
//renderer draws over anything laid on the game) - with a way out: reload
//without the patch, for this ROM, this session, leaving the setting alone.
//The rules are host-free (UI.Tests/Play/PlayForcedPatchTests); this checks the
//realized banner and, against the real core, the whole path from a forced
//<patch> to the reload that skips it.
public partial class PlayEdgeFlowsTests
{
	private const string ForcedPatchText = "This game's HD pack patch, Castlevania_PRG1.ips, was made for another version of the game and may break it. If the game freezes or looks wrong, reload it without the patch.";

	[AvaloniaFact]
	public void A_forced_pack_patch_is_told_in_place_with_a_reload_without_it()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		(MainWindow window, MainWindowViewModel model) = ShowPlay();
		Action savedReload = model.ReloadWithoutForcedPatch;
		int reloads = 0;
		model.ReloadWithoutForcedPatch = () => reloads++;
		try {
			Panel host = window.FindNamed<Panel>("InterruptionBarHost");
			Assert.False(host.IsOnScreen());

			model.OnForcedPackPatch("/Mesen/HdPacks/Castlevania (1987) (Konami)/Castlevania_PRG1.ips");
			Dispatcher.UIThread.RunJobs();
			Assert.True(host.IsOnScreen(), "a forced patch left no banner above the game");
			Assert.Equal(ForcedPatchText, window.FindNamed<TextBlock>("InterruptionText").Text);
			Assert.Equal("Keep Playing", window.FindNamed<Button>("InterruptionKeepButton").Content);
			Assert.Equal("Reload Without Patch", window.FindNamed<Button>("InterruptionGoButton").Content);
			Border banner = window.FindNamed<Border>("InterruptionBarBorder");
			Assert.Contains("banner", banner.Classes);
			Assert.DoesNotContain("stop", banner.Classes);
			Assert.Contains("player", host.Classes);

			Click(window, "InterruptionGoButton");
			Assert.Equal(1, reloads);
			Assert.False(host.IsOnScreen());

			//Keep Playing only dismisses it.
			model.OnForcedPackPatch("Castlevania_PRG1.ips");
			Dispatcher.UIThread.RunJobs();
			Click(window, "InterruptionKeepButton");
			Assert.Equal(1, reloads);
			Assert.False(host.IsOnScreen());

			//A load that forces nothing takes a banner left up away with it.
			model.OnForcedPackPatch("Castlevania_PRG1.ips");
			Dispatcher.UIThread.RunJobs();
			model.OnForcedPackPatch("");
			Dispatcher.UIThread.RunJobs();
			Assert.False(host.IsOnScreen());
			Assert.Equal(1, reloads);
		} finally {
			model.ReloadWithoutForcedPatch = savedReload;
		}
	}

	//The real core: a loose HdPacks/<rom>/ pack whose only <patch> names another
	//ROM's hash. The override forces it, the banner names it, stopping the game
	//takes the banner away, and Reload Without Patch power-cycles the game with
	//the patch skipped - the setting still on.
	[AvaloniaFact]
	public void A_rom_patched_by_force_reloads_without_the_patch_and_keeps_the_setting()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		EnhancementPackConfig packs = ConfigManager.Config.EnhancementPacks;
		(bool mismatch, bool autoInstall, bool bootstrap) saved = (packs.ApplyPatchOnHashMismatch, packs.AutoInstallCommunityPacks, packs.BootstrapEnhancementFolder);
		packs.ApplyPatchOnHashMismatch = true;
		packs.AutoInstallCommunityPacks = false;
		packs.BootstrapEnhancementFolder = false;
		packs.ApplyConfig();

		//A ROM of its own (the suppression lasts the process), with its pack.
		string name = "forced-patch-" + Guid.NewGuid().ToString("N").Substring(0, 8);
		byte[] rom = SyntheticNrom.Build();
		Guid.NewGuid().ToByteArray().CopyTo(rom, 16 + 0x200);
		string romPath = Path.Combine(_folder, name + ".nes");
		File.WriteAllBytes(romPath, rom);
		string pack = Path.Combine(ConfigManager.HdPackFolder, name);
		Directory.CreateDirectory(pack);
		File.WriteAllText(Path.Combine(pack, "hires.txt"), "<ver>106\n<patch>Castlevania_PRG1.ips," + new string('0', 40) + "\n");
		File.WriteAllBytes(Path.Combine(pack, "Castlevania_PRG1.ips"), Ips(16 + 0x100, 0x60));

		(MainWindow window, MainWindowViewModel model) = ShowPlay();
		try {
			Assert.True(EmuApi.LoadRom(romPath, string.Empty), $"the core refused to load {romPath}");
			WaitFor(() => model.Interruption.IsVisible && model.Interruption.Kind == InterruptionKind.ForcedPatch, "the forced <patch> never reached the banner");
			Assert.EndsWith("Castlevania_PRG1.ips", EmuApi.GetForcedPackPatch());
			Assert.Equal(ForcedPatchText, window.FindNamed<TextBlock>("InterruptionText").Text);

			//The banner goes with the game.
			EmuApi.Stop();
			WaitFor(() => model.RomInfo.Format == RomFormat.Unknown, "the game never stopped");
			Assert.False(model.Interruption.IsVisible, "the banner outlived its game");

			Assert.True(EmuApi.LoadRom(romPath, string.Empty), $"the core refused to load {romPath} again");
			WaitFor(() => model.Interruption.IsVisible && model.Interruption.Kind == InterruptionKind.ForcedPatch, "the second load's forced <patch> never reached the banner");

			Click(window, "InterruptionGoButton");
			WaitFor(() => EmuApi.GetLog().Contains("the forced patch is off for this ROM"), "Reload Without Patch never reloaded the game without the patch");
			WaitFor(() => EmuApi.IsRunning() && model.RomInfo.GetRomName() == name, "the game did not come back after the reload");
			Assert.Equal("", EmuApi.GetForcedPackPatch());
			Assert.False(model.Interruption.IsVisible);
			Assert.True(packs.ApplyPatchOnHashMismatch, "the way out changed the player's setting");
		} finally {
			EmuApi.Stop();
			packs.ApplyPatchOnHashMismatch = saved.mismatch;
			packs.AutoInstallCommunityPacks = saved.autoInstall;
			packs.BootstrapEnhancementFolder = saved.bootstrap;
			packs.ApplyConfig();
			try {
				Directory.Delete(pack, true);
			} catch(IOException) {
			}
		}
	}

	//One IPS record writing `value` at `offset`.
	private static byte[] Ips(int offset, byte value)
	{
		List<byte> ips = new() { (byte)'P', (byte)'A', (byte)'T', (byte)'C', (byte)'H' };
		ips.AddRange(new[] { (byte)(offset >> 16), (byte)(offset >> 8), (byte)offset, (byte)0, (byte)1, value });
		ips.AddRange(new[] { (byte)'E', (byte)'O', (byte)'F' });
		return ips.ToArray();
	}
}
