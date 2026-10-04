using Mesen.Logic;
using Xunit;

namespace Mesen.Tests.Shell
{
	//G.1 (W-S1) and ADR-0249: the status line reads like the renders,
	//"Contra (USA) · pack Contra 80s 1.2". It never repeats the game's name as
	//a pack name: a local pack named after its ROM is said for what it is.
	public class ShellStatusLineTests
	{
		[Fact]
		public void A_game_without_a_pack_is_its_name_alone()
		{
			Assert.Equal(ShellPackKind.None, ShellStatusLine.PackKind("Contra (USA)", "", false));
			Assert.Equal(ShellPackKind.None, ShellStatusLine.PackKind("Contra (USA)", "   ", false));
			Assert.Equal("Contra (USA)", ShellStatusLine.Compose("Contra (USA)", ""));
		}

		[Fact]
		public void A_named_pack_follows_the_game_after_a_dot()
		{
			Assert.Equal(ShellPackKind.Named, ShellStatusLine.PackKind("Contra (USA)", "Contra 80s", false));
			Assert.Equal("Contra (USA) · pack Contra 80s 1.2", ShellStatusLine.Compose("Contra (USA)", "pack " + ShellStatusLine.PackLabel("Contra 80s", "1.2")));
		}

		//W-R1/W-R5: Remaster at rest ends the line with the painted count; an
		//unknown count (ADR-0252) adds no words.
		[Fact]
		public void Remaster_at_rest_ends_with_the_painted_cells_when_known()
		{
			Assert.Equal("Contra (USA) · 412 cells painted", ShellStatusLine.ComposeRemaster("Contra (USA)", "", "412 cells painted"));
			Assert.Equal("Contra (USA) · playing your project · 412 cells painted", ShellStatusLine.ComposeRemaster("Contra (USA)", "playing your project", "412 cells painted"));
			Assert.Equal("Contra (USA) · pack Contra 80s 1.2", ShellStatusLine.ComposeRemaster("Contra (USA)", "pack Contra 80s 1.2", ""));
			Assert.Equal("Contra (USA)", ShellStatusLine.ComposeRemaster("Contra (USA)", "", "  "));
		}

		[Theory]
		[InlineData("Contra 80s", "", "Contra 80s")]
		[InlineData("Contra 80s", "0.0.0", "Contra 80s")]
		[InlineData("Contra 80s", " 1.2 ", "Contra 80s 1.2")]
		public void The_label_shows_a_real_version_only(string name, string version, string expected)
		{
			Assert.Equal(expected, ShellStatusLine.PackLabel(name, version));
		}

		//2026-10-03: "The Legend of Zelda (1987) (Nintendo) is paused, with The
		//Legend of Zelda (1987) (Nintendo)" - the sibling pack had no name of its
		//own, so the folder's (the ROM's) was shown as if it were one.
		[Theory]
		[InlineData("The Legend of Zelda (1987) (Nintendo)", "The Legend of Zelda (1987) (Nintendo)")]
		[InlineData("The Legend of Zelda (1987) (Nintendo)", "the legend of zelda (1987) (nintendo) ")]
		public void A_pack_named_after_the_game_is_the_players_own_project(string game, string pack)
		{
			Assert.Equal(ShellPackKind.Project, ShellStatusLine.PackKind(game, pack, false));
		}

		[Fact]
		public void A_machine_only_pack_is_the_automatic_upscale()
		{
			Assert.Equal(ShellPackKind.AutoUpscale, ShellStatusLine.PackKind("Contra (1988) (Konami)", "Contra (1988) (Konami)", true));
		}
	}
}
