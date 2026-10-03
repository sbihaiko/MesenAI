using System;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using Mesen.Config;
using Mesen.Interop;
using Mesen.Logic;
using Mesen.Services;
using Xunit;

namespace Mesen.HeadlessTests;

//ADR-0240 Option 1 / PRD F6.9 stop condition: a headless run drives the REAL
//CommunityPackInstallCoordinator.Install (no mock of the decision) against a
//fixture hd-legacy pack zip and a synthetic ROM, and the notice shows up on
//the outcome AND in the core log. The rule itself (wired patch, M-convention,
//...) is pinned host-free in UI.Tests/CommunityPacks/PackAudioNoticeTests.cs;
//this only proves the wiring (coordinator -> outcome -> log) plus the
//"texture install stays intact" half. Skips when the core is not built.
[Collection(NativeCoreCollection.Name)]
public class PackAudioNoticeInstallTests : IDisposable
{
	//TestAppBuilder turns auto-install off for the whole test home (#751);
	//these drive the auto-install path, so they turn it on and put it back.
	private readonly bool _autoInstall = ConfigManager.Config.EnhancementPacks.AutoInstallCommunityPacks;

	public PackAudioNoticeInstallTests() => ConfigManager.Config.EnhancementPacks.AutoInstallCommunityPacks = true;

	public void Dispose() => ConfigManager.Config.EnhancementPacks.AutoInstallCommunityPacks = _autoInstall;

	private const string Notice = "audio not generated: 2 of 3 tracks unresolved; supply the `.ogg` files";
	private const string Sha = "0123456789ABCDEF0123456789ABCDEF01234567";

	[Fact]
	public void Install_with_a_wired_patch_and_missing_ogg_stays_Installed_and_logs_one_notice()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		(CommunityPackInstallOutcome outcome, string log, string folder) = RunInstall(withPatch: true);
		Assert.Equal(CommunityPackInstallStatus.Installed, outcome.Status);
		Assert.Equal(new[] { Notice }, outcome.Notices);
		Assert.Contains("[CommunityPackInstall] " + Notice, log);
		//The pack lands in a "mep" folder (beside the ROM, or under the temp home's
		//EnhancementPacks), textures extracted to <mep>/textures/ (ADR-0147).
		string[] tiles = Directory.GetFiles(folder, "tile.png", SearchOption.AllDirectories);
		Assert.Single(tiles);
		Assert.EndsWith(Path.Combine("textures", "tile.png"), tiles[0]);
	}

	[Fact]
	public void Install_without_a_wired_patch_reports_no_notice()
	{
		Assert.SkipWhen(!NativeCore.IsAvailable, NativeCore.SkipReason ?? "");
		(CommunityPackInstallOutcome outcome, string log, _) = RunInstall(withPatch: false);
		Assert.Equal(CommunityPackInstallStatus.Installed, outcome.Status);
		Assert.Empty(outcome.Notices);
		Assert.DoesNotContain("audio not generated", log);
	}

	private static (CommunityPackInstallOutcome Outcome, string Log, string Folder) RunInstall(bool withPatch)
	{
		string folder = Path.Combine(Path.GetTempPath(), "mesen-f69-" + Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(folder);
		//Keep the install registry / EnhancementPacks cache out of the user's real home.
		typeof(ConfigManager).GetField("_homeFolder", BindingFlags.NonPublic | BindingFlags.Static)!.SetValue(null, Path.Combine(folder, "home"));
		Directory.CreateDirectory(Path.Combine(folder, "home"));

		string rom = Path.Combine(folder, "fixture.nes");
		File.WriteAllBytes(rom, BuildNrom());
		string zipPath = Path.Combine(folder, "pack.zip");
		using(ZipArchive zip = ZipFile.Open(zipPath, ZipArchiveMode.Create)) {
			AddEntry(zip, "hires.txt", (withPatch ? "<patch>Music.ips," + Sha + "\n" : "") +
				"<bgm>1,1,BGM/a.ogg\n<bgm>1,2,BGM/b.ogg\n<bgm>1,3,BGM/c.ogg\n<bgm>1,4,BGM/c.ogg\n");
			AddEntry(zip, "tile.png", "png");
			AddEntry(zip, "BGM/c.ogg", "ogg");
			if(withPatch) {
				AddEntry(zip, "Music.ips", "PATCH");
			}
		}

		EmuApi.InitDll();
		EmuApi.InitializeEmu(ConfigManager.HomeFolder, IntPtr.Zero, IntPtr.Zero, true, true, true, true);
		try {
			ConfigApi.SetEmulationFlag(EmulationFlags.ConsoleMode, true);
			Assert.True(EmuApi.LoadRom(rom, string.Empty), $"the core refused to load {rom}");
			CommunityPackCatalogEntry entry = new() {
				Kind = "hd-legacy", Name = "F69 fixture", Game = "fixture", System = "nes", Sha256 = new string('a', 64), PackId = "f69-fixture"
			};
			//The core log is process-global and never cleared, so read only what
			//this install wrote: everything after a per-run marker.
			string marker = "[PackAudioNoticeInstallTests] " + Guid.NewGuid().ToString("N");
			EmuApi.WriteLogEntry(marker);
			CommunityPackInstallOutcome outcome = CommunityPackInstallCoordinator.Install(entry, zipPath, new System.Collections.Generic.Dictionary<string, string>(), CommunityPackInstallCoordinator.CaptureLoad());
			string log = EmuApi.GetLog();
			int start = log.LastIndexOf(marker, StringComparison.Ordinal);
			return (outcome, start < 0 ? log : log.Substring(start + marker.Length), folder);
		} finally {
			//Stop unloads the ROM; Release is NOT called (see CopyAsMepSheetCellTests).
			EmuApi.Stop();
			ConfigApi.SetEmulationFlag(EmulationFlags.ConsoleMode, false);
		}
	}

	private static void AddEntry(ZipArchive zip, string name, string text)
	{
		using StreamWriter writer = new(zip.CreateEntry(name).Open());
		writer.Write(text);
	}

	//Same 32 KB NROM CopyAfterStateLoadTests builds: infinite loop, blank CHR.
	private static byte[] BuildNrom()
	{
		byte[] rom = new byte[16 + 32 * 1024 + 8 * 1024];
		rom[0] = (byte)'N'; rom[1] = (byte)'E'; rom[2] = (byte)'S'; rom[3] = 0x1A;
		rom[4] = 2; rom[5] = 1;
		for(int i = 0; i < 32 * 1024; i++) {
			rom[16 + i] = 0xEA;
		}
		rom[16] = 0x4C; rom[17] = 0x00; rom[18] = 0x80;
		rom[16 + 0x7FFC] = 0x00; rom[16 + 0x7FFD] = 0x80;
		return rom;
	}
}
