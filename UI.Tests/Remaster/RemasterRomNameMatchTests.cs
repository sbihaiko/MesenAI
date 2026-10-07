using System;
using System.IO;
using Mesen.Logic;
using Xunit;

namespace Mesen.Tests.Remaster
{
	//#969 review: Open the Right Game… matches the project folder's name
	//against real files, so a same-named patch, save or movie beside the ROM
	//must never be the file it loads, and a recent game gone from disk must
	//fall through to the games folder, then to the picker.
	public sealed class RemasterRomNameMatchTests : IDisposable
	{
		private readonly string _root = Path.Combine(Path.GetTempPath(), "remaster-rom-match-" + Guid.NewGuid().ToString("N"));
		private readonly string _games;
		private readonly string _project;

		public RemasterRomNameMatchTests()
		{
			_games = Path.Combine(_root, "games");
			_project = Path.Combine(_games, "Contra (U)");
			Directory.CreateDirectory(_project);
		}

		public void Dispose()
		{
			Directory.Delete(_root, true);
		}

		private string Touch(string name)
		{
			string path = Path.Combine(_games, name);
			File.WriteAllBytes(path, new byte[] { 0 });
			return path;
		}

		[Fact]
		public void A_same_named_patch_or_save_in_the_games_folder_never_wins_over_the_rom()
		{
			Touch("Contra (U).ips");
			Touch("Contra (U).sav");
			string rom = Touch("Contra (U).nes");

			RemasterRightGame? step = RemasterScreen.RightGameStepOnDisk(RemasterReason.NotThisProjectsGame, _project, new string[0], _games);

			Assert.Equal(rom, step!.RomPath);
			Assert.False(step.OpensPicker);
		}

		[Fact]
		public void A_recent_rom_gone_from_disk_falls_through_to_the_games_folder_rom()
		{
			Touch("Contra (U).ips");
			string rom = Touch("Contra (U).nes");

			RemasterRightGame? step = RemasterScreen.RightGameStepOnDisk(RemasterReason.NotThisProjectsGame, _project,
				new[] { Path.Combine(_root, "moved", "Contra (U).nes") }, _games);

			Assert.Equal(rom, step!.RomPath);
		}

		[Fact]
		public void A_recent_rom_gone_from_disk_with_only_a_stray_non_rom_left_opens_the_picker()
		{
			Touch("Contra (U).sav");
			Touch("Contra (U).mss");

			RemasterRightGame? step = RemasterScreen.RightGameStepOnDisk(RemasterReason.NotThisProjectsGame, _project,
				new[] { Path.Combine(_root, "moved", "Contra (U).nes") }, _games);

			Assert.NotNull(step);
			Assert.True(step!.OpensPicker);
		}
	}
}
